using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MeshSMO.Sensors.Application.Registry;
using MeshSMO.Sensors.Domain.Polling;
using MeshSMO.Sensors.Gateway.LocalStorage;
using MeshSMO.Sensors.Gateway.MeshCore;
using MeshSMO.Sensors.Gateway.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Polly;

namespace MeshSMO.Sensors.Gateway.Polling;

/// <summary>
/// Sequentially polls pull-only MeshCore sensor nodes through the configured
/// channel (the MeshCoreTel repeater acquisition API, or a stock companion
/// radio in Companion mode — both via <see cref="IMeshNodeClient"/>) and
/// appends every response to the local telemetry outbox.
/// Request payload: timestamp(4 LE) + 0x03 (GET_TELEMETRY_DATA) + inverse
/// permission mask 0x00; the reply body after the reflected timestamp is
/// Cayenne LPP.
/// Scheduling follows the spec: deterministic jitter on start, a due-time
/// priority queue with MaxConcurrentPolls = 1, and a bounded retry policy —
/// a failed poll cycle retries up to the registry's pollMaxAttempts with a
/// small randomized backoff; LoRa airtime is never hammered (spec §8.4).
/// When a sensor defines polling.schedule, the between-cycles interval is
/// taken from the schedule window covering the sensor's local time, falling
/// back to the base interval outside the windows; the regime switch lands on
/// the next poll queued after a completed cycle.
/// Every attempt (success or failure) is appended to the outbox so the main
/// API can persist poll_attempts.
/// </summary>
public sealed class SensorTelemetryPoller(
    IMeshNodeClient client,
    IServiceScopeFactory scopeFactory,
    ILocalTelemetryStore store,
    IOptions<MeshCoreOptions> meshOptions,
    IOptions<SensorPollingOptions> pollingOptions,
    ILogger<SensorTelemetryPoller> logger,
    [FromKeyedServices(GatewayResiliencePipelines.SensorPollingKey)] ResiliencePipeline? retryPipeline = null) : BackgroundService
{
    /// <summary>Airtime guard: registry allows up to 10, we never fire more than 3 requests per cycle.</summary>
    private const int MaxAttemptsPerCycle = 3;

    private readonly ResiliencePipeline _retryPipeline = retryPipeline ??
        GatewayResiliencePipelines.CreateSensorPollingPipeline(pollingOptions.Value);
    private long _requestIdSeed = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (meshOptions.Value.Mode == MeshCoreConnectionMode.Disabled || !pollingOptions.Value.Enabled)
        {
            logger.LogInformation("Sensor polling is disabled");
            return;
        }

        if (meshOptions.Value.Mode == MeshCoreConnectionMode.Companion)
        {
            logger.LogInformation(
                "Sensor polling runs through the companion radio at {Host}:{Port}",
                meshOptions.Value.Companion.Host,
                meshOptions.Value.Companion.Port);
        }

        // ISensorRegistry is scoped; hosted services are singletons, so the
        // registry must be resolved from a scope (Development validates this
        // at startup, production does not — never take it via the constructor).
        SensorDefinition[] sensors;
        using (var scope = scopeFactory.CreateScope())
        {
            var registry = scope.ServiceProvider.GetRequiredService<ISensorRegistry>();
            sensors = (await registry.LoadAsync(stoppingToken).ConfigureAwait(false))
                .Where(sensor => sensor.Enabled && IsHexPublicKey(sensor.MeshPublicKey))
                .ToArray();
        }
        if (sensors.Length == 0)
        {
            logger.LogInformation("No enabled sensors in the registry; sensor polling idles");
            return;
        }

        logger.LogInformation(
            "Sensor polling started for {Count} sensor(s): {Slugs}",
            sensors.Length,
            string.Join(", ", sensors.Select(sensor => sensor.Slug.Value)));

        var queue = new PriorityQueue<(SensorDefinition Sensor, DateTimeOffset Due), DateTimeOffset>();
        var now = DateTimeOffset.UtcNow;
        foreach (var sensor in sensors)
        {
            var lastPollStartedAt = await store.ReadLastPollStartedAtAsync(
                sensor.Slug.Value,
                stoppingToken).ConfigureAwait(false);
            var due = lastPollStartedAt is null
                ? now + NextStartDelay(sensor)
                : lastPollStartedAt.Value + EffectivePollInterval(sensor, pollingOptions.Value, now);
            queue.Enqueue((sensor, due), due);
        }
        var loginAttempted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (!stoppingToken.IsCancellationRequested)
        {
            var due = queue.Peek().Due;
            now = DateTimeOffset.UtcNow;
            if (due > now)
            {
                await Task.Delay(TimeSpan.FromTicks(Math.Min((due - now).Ticks, TimeSpan.FromSeconds(5).Ticks)), stoppingToken).ConfigureAwait(false);
                continue;
            }

            var (sensor, _) = queue.Dequeue();
            var pollStartedAt = DateTimeOffset.UtcNow;
            try
            {
                // Persist before touching the radio. If the process stops during
                // the cycle, a restart still observes the interval guard.
                await store.RecordPollStartedAsync(
                    sensor.Slug.Value,
                    pollStartedAt,
                    stoppingToken).ConfigureAwait(false);
                await PollCycleAsync(sensor, loginAttempted, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Poll cycle of sensor {Slug} failed unexpectedly; scheduling retry on the next interval",
                    sensor.Slug.Value);
            }

            var queuedAt = DateTimeOffset.UtcNow;
            var nextDue = pollStartedAt + EffectivePollInterval(sensor, pollingOptions.Value, queuedAt);
            queue.Enqueue((sensor, nextDue), nextDue);
        }
    }

    private async Task PollCycleAsync(SensorDefinition sensor, HashSet<string> loginAttempted, CancellationToken cancellationToken)
    {
        var options = pollingOptions.Value;
        var timeoutMs = ResolveRequestTimeoutMs(sensor, options);
        var maxAttempts = Math.Clamp(sensor.PollMaxAttempts, 1, MaxAttemptsPerCycle);
        var attempt = 0;

        await _retryPipeline.ExecuteAsync(async token =>
        {
            attempt++;
            var requestId = Interlocked.Increment(ref _requestIdSeed);
            var startedAt = DateTimeOffset.UtcNow;
            PollAttemptOutcome outcome;
            try
            {
                outcome = await PollOnceAsync(sensor, requestId, startedAt, timeoutMs, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Request {RequestId} to sensor {Slug} failed (attempt {Attempt}/{MaxAttempts})",
                    requestId,
                    sensor.Slug.Value,
                    attempt,
                    maxAttempts);
                outcome = PollAttemptOutcome.Failure(
                    PollAttemptStatus.Failed,
                    "transport_error",
                    exception.Message,
                    (int)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);
            }

            await RecordAttemptAsync(sensor, requestId, attempt, startedAt, outcome, token).ConfigureAwait(false);

            if (outcome.Telemetry is not null)
                return;

            if (attempt >= maxAttempts)
                return;

            // An empty or undecodable body is a deterministic answer; retrying
            // would only burn LoRa airtime. Timeouts and transport errors are
            // transient and are retried.
            if (outcome.ErrorCode is "empty_response" or "undecodable_response")
                return;

            if (outcome.Status == PollAttemptStatus.TimedOut)
            {
                // The node most likely has not added this repeater to its ACL
                // yet; bootstrap the ANON login once before the retry.
                await TryLoginOnceAsync(sensor, loginAttempted, token).ConfigureAwait(false);
            }

            throw new SensorPollingRetryException();
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<PollAttemptOutcome> PollOnceAsync(
        SensorDefinition sensor,
        long requestId,
        DateTimeOffset startedAt,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var payload = ToRequestHex(BuildTelemetryRequestPayload(requestId));

        using var response = await client.SendAcquisitionRequestAsync(
            sensor.MeshPublicKey,
            payload,
            timeoutMs,
            cancellationToken).ConfigureAwait(false);
        var status = response.RootElement.TryGetProperty("status", out var statusElement)
            ? statusElement.GetString()
            : null;

        if (!string.Equals(status, "ok", StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Sensor {Slug} did not answer within {TimeoutMs} ms (request {RequestId})",
                sensor.Slug.Value,
                timeoutMs,
                requestId);
            return PollAttemptOutcome.Failure(
                PollAttemptStatus.TimedOut,
                "timeout",
                null,
                ReadNullableInt(response.RootElement, "elapsedMs"));
        }

        var responseHex = response.RootElement.TryGetProperty("responseHex", out var hexElement) &&
            hexElement.ValueKind == JsonValueKind.String
                ? hexElement.GetString()
                : null;
        if (string.IsNullOrEmpty(responseHex))
        {
            logger.LogWarning("Sensor {Slug} replied without a response body (request {RequestId})", sensor.Slug.Value, requestId);
            return PollAttemptOutcome.Failure("empty_response", "The node answered without a response body.");
        }

        var lpp = Convert.FromHexString(responseHex);
        var telemetry = lpp.Length > 4 ? CayenneLppDecoder.Decode(lpp.AsSpan(4)) : [];
        if (telemetry.Count == 0)
        {
            logger.LogWarning(
                "Sensor {Slug} replied with an undecodable body (request {RequestId}, {Hex})",
                sensor.Slug.Value,
                requestId,
                responseHex[..Math.Min(responseHex.Length, 64)]);
            return PollAttemptOutcome.Failure(
                "undecodable_response",
                $"The node reply could not be decoded as Cayenne LPP: {responseHex[..Math.Min(responseHex.Length, 64)]}");
        }

        var readings = ResolveReadings(sensor, telemetry);
        logger.LogInformation(
            "Sensor {Slug} answered request {RequestId}: {Metrics}",
            sensor.Slug.Value,
            requestId,
            string.Join(", ", readings.Select(reading => $"{reading.metric}={reading.value.ToString(CultureInfo.InvariantCulture)}{reading.unit}")));
        return PollAttemptOutcome.Success(
            readings,
            responseHex,
            ReadNullableDouble(response.RootElement, "rssi"),
            ReadNullableDouble(response.RootElement, "snr"),
            ReadNullableInt(response.RootElement, "elapsedMs"));
    }

    /// <summary>
    /// Appends the attempt to the local outbox: successful polls as
    /// <c>sensor_poll</c> snapshots (extended with attempt metadata), failures
    /// as <c>poll_attempt</c> records. The main API maps both to poll_attempts.
    /// </summary>
    private async Task RecordAttemptAsync(
        SensorDefinition sensor,
        long requestId,
        int attemptNumber,
        DateTimeOffset startedAt,
        PollAttemptOutcome outcome,
        CancellationToken cancellationToken)
    {
        var completedAt = DateTimeOffset.UtcNow;
        string payloadJson;
        if (outcome.Telemetry is { } readings)
        {
            payloadJson = JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["type"] = "sensor_poll",
                ["sensor"] = sensor.Slug.Value,
                ["requestId"] = requestId,
                ["protocol"] = "meshcore-req-lpp",
                ["attemptNumber"] = attemptNumber,
                ["startedAt"] = startedAt,
                ["rssi"] = outcome.Rssi,
                ["snr"] = outcome.Snr,
                ["elapsedMs"] = outcome.RoundTripMilliseconds,
                ["responseHex"] = outcome.ResponseHex,
                ["readings"] = readings.Select(reading => new { metric = reading.metric, value = reading.value, unit = reading.unit }).ToArray(),
            });
        }
        else
        {
            payloadJson = JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["type"] = "poll_attempt",
                ["sensor"] = sensor.Slug.Value,
                ["requestId"] = requestId,
                ["protocol"] = "meshcore-req-lpp",
                ["attemptNumber"] = attemptNumber,
                ["startedAt"] = startedAt,
                ["completedAt"] = completedAt,
                ["status"] = outcome.Status.ToString(),
                ["errorCode"] = outcome.ErrorCode,
                ["errorMessage"] = outcome.ErrorMessage,
                ["roundTripMs"] = outcome.RoundTripMilliseconds,
            });
        }

        var snapshotId = await store.AppendAsync(startedAt, client.TransportName, payloadJson, cancellationToken).ConfigureAwait(false);
        logger.LogInformation(
            "Poll attempt {Attempt} of sensor {Slug} (request {RequestId}) recorded as outbox snapshot {SnapshotId}: {Status}",
            attemptNumber,
            sensor.Slug.Value,
            requestId,
            snapshotId,
            outcome.Telemetry is null ? outcome.Status.ToString() : "Succeeded");
    }

    private async Task TryLoginOnceAsync(SensorDefinition sensor, HashSet<string> loginAttempted, CancellationToken cancellationToken)
    {
        var options = pollingOptions.Value;
        if (!options.LoginOnTimeout || !loginAttempted.Add(sensor.Slug.Value))
            return;

        var password = ResolveLoginPassword(sensor, options);
        logger.LogInformation(
            "Attempting ANON login bootstrap for sensor {Slug} ({Source} password, {Configured})",
            sensor.Slug.Value,
            sensor.LoginPassword is null ? "global" : "per-sensor",
            password.Length > 0 ? "configured" : "empty");
        try
        {
            using var login = await client.SendAcquisitionLoginAsync(
                sensor.MeshPublicKey,
                password,
                options.LoginTimeoutMs,
                cancellationToken).ConfigureAwait(false);
            var loginStatus = login.RootElement.TryGetProperty("status", out var statusElement)
                ? statusElement.GetString()
                : null;
            logger.LogInformation(
                "ANON login for sensor {Slug}: {Status}",
                sensor.Slug.Value,
                loginStatus ?? "unknown");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "ANON login attempt for sensor {Slug} failed", sensor.Slug.Value);
        }
    }

    /// <summary>
    /// The node password for the ANON login bootstrap: the per-sensor
    /// <c>mesh.loginPassword</c> when set in the registry (an empty string counts as
    /// "node has no password"), otherwise the global <c>SensorPolling:LoginPassword</c>.
    /// </summary>
    internal static string ResolveLoginPassword(SensorDefinition sensor, SensorPollingOptions options) =>
        sensor.LoginPassword ?? options.LoginPassword;

    internal static int ResolveRequestTimeoutMs(SensorDefinition sensor, SensorPollingOptions options)
    {
        var timeoutMs = (int)sensor.PollTimeout.TotalMilliseconds;
        return timeoutMs > 0 ? timeoutMs : options.RequestTimeoutMs;
    }

    internal static byte[] BuildTelemetryRequestPayload(long requestId)
    {
        var timestamp = (uint)requestId;
        var payload = new byte[6];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, timestamp);
        payload[4] = 0x03; // REQ_TYPE_GET_TELEMETRY_DATA
        payload[5] = 0x00; // inverse permission mask: allow all
        return payload;
    }

    internal static string ToRequestHex(byte[] payload) => Convert.ToHexString(payload).ToLowerInvariant();

    internal static IReadOnlyList<(string metric, double value, string unit)> ResolveReadings(
        SensorDefinition sensor,
        IReadOnlyList<LppValue> telemetry)
    {
        var mappings = sensor.Channels
            .GroupBy(channel => (channel.Channel, channel.Type ?? "*"))
            .ToDictionary(group => group.Key, group => group.First().Metric);
        var repeatedTypes = telemetry
            .GroupBy(value => value.TypeKey, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);

        return telemetry
            .Select(value => (
                Metric: CayenneLppDecoder.ResolveMetricKey(value, mappings, repeatedTypes),
                value.Value,
                value.Unit))
            .GroupBy(resolved => resolved.Metric, StringComparer.Ordinal)
            .Select(group => (group.Key, group.First().Value, group.First().Unit))
            .OrderBy(resolved => resolved.Item1, StringComparer.Ordinal)
            .Select(resolved => (resolved.Item1, resolved.Item2, resolved.Item3))
            .ToArray();
    }

    /// <summary>
    /// Between-cycles interval: the <c>polling.schedule</c> window covering the
    /// sensor's local time when a schedule is configured, otherwise the base
    /// registry interval. The global <c>SensorPolling:IntervalOverrideSeconds</c>
    /// still shortens either of them.
    /// </summary>
    internal static TimeSpan EffectivePollInterval(SensorDefinition sensor, SensorPollingOptions options, DateTimeOffset now)
    {
        var interval = sensor.PollingSchedule?.ResolveInterval(now) ?? sensor.PollInterval;
        var overrideSeconds = options.IntervalOverrideSeconds;
        if (overrideSeconds >= 30 && overrideSeconds < interval.TotalSeconds)
            return TimeSpan.FromSeconds(overrideSeconds);

        return interval;
    }

    private static TimeSpan NextStartDelay(SensorDefinition sensor)
    {
        // Deterministic jitter so restarts do not synchronise all sensors.
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sensor.Slug.Value));
        var jitterSeconds = BitConverter.ToUInt32(hash, 0) % 5 + 1;
        return TimeSpan.FromSeconds(jitterSeconds);
    }

    private static bool IsHexPublicKey(string value) =>
#pragma warning disable MA0009
        value.Length == 64 && Regex.IsMatch(value, "^[0-9a-fA-F]{64}$");
#pragma warning restore MA0009

    private static double? ReadNullableDouble(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    private static int? ReadNullableInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var intValue)
                ? intValue
                : null;

    private sealed record PollAttemptOutcome(
        PollAttemptStatus Status,
        string? ErrorCode,
        string? ErrorMessage,
        int? RoundTripMilliseconds,
        IReadOnlyList<(string metric, double value, string unit)>? Telemetry,
        string? ResponseHex,
        double? Rssi,
        double? Snr)
    {
        public static PollAttemptOutcome Success(
            IReadOnlyList<(string metric, double value, string unit)> readings,
            string responseHex,
            double? rssi,
            double? snr,
            int? roundTripMs) => new(
                PollAttemptStatus.Succeeded, null, null, roundTripMs, readings, responseHex, rssi, snr);

        public static PollAttemptOutcome Failure(string errorCode, string errorMessage) => new(
            PollAttemptStatus.Failed, errorCode, errorMessage, null, null, null, null, null);

        public static PollAttemptOutcome Failure(
            PollAttemptStatus status,
            string? errorCode,
            string? errorMessage,
            int? roundTripMs) => new(status, errorCode, errorMessage, roundTripMs, null, null, null, null);
    }
}
