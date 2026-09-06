using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using MeshSMO.Sensors.Application.Registry;
using MeshSMO.Sensors.Gateway.LocalStorage;
using MeshSMO.Sensors.Gateway.MeshCore;
using Microsoft.Extensions.Options;

namespace MeshSMO.Sensors.Gateway.Polling;

/// <summary>
/// Sequentially polls pull-only MeshCore sensor nodes through the repeater
/// acquisition API and appends every response to the local telemetry outbox.
/// Request payload: timestamp(4 LE) + 0x03 (GET_TELEMETRY_DATA) + inverse
/// permission mask 0x00; the reply body after the reflected timestamp is
/// Cayenne LPP.
/// </summary>
public sealed class SensorTelemetryPoller(
    IMeshCoreTelClient client,
    ISensorRegistry registry,
    ILocalTelemetryStore store,
    IOptions<MeshCoreOptions> meshOptions,
    IOptions<SensorPollingOptions> pollingOptions,
    ILogger<SensorTelemetryPoller> logger) : BackgroundService
{
    private long _requestIdSeed = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (meshOptions.Value.Mode == MeshCoreConnectionMode.Disabled || !pollingOptions.Value.Enabled)
        {
            logger.LogInformation("Sensor polling is disabled");
            return;
        }

        var sensors = (await registry.LoadAsync(stoppingToken))
            .Where(sensor => sensor.Enabled)
            .Where(sensor => IsHexPublicKey(sensor.MeshPublicKey))
            .ToArray();
        if (sensors.Length == 0)
        {
            logger.LogInformation("No enabled sensors in the registry; sensor polling idles");
            return;
        }

        logger.LogInformation(
            "Sensor polling started for {Count} sensor(s): {Slugs}",
            sensors.Length,
            string.Join(", ", sensors.Select(sensor => sensor.Slug.Value)));

        var nextDue = sensors.ToDictionary(
            sensor => sensor.Slug.Value,
            sensor => DateTimeOffset.UtcNow + NextStartDelay(sensor));
        var loginAttempted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (!stoppingToken.IsCancellationRequested)
        {
            var (slug, due) = nextDue.MinBy(pair => pair.Value);
            var now = DateTimeOffset.UtcNow;
            if (due > now)
            {
                await Task.Delay(TimeSpan.FromTicks(Math.Min((due - now).Ticks, TimeSpan.FromSeconds(5).Ticks)), stoppingToken);
                continue;
            }

            var sensor = sensors.Single(candidate => candidate.Slug.Value == slug);
            try
            {
                await PollSensorAsync(sensor, loginAttempted, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Poll of sensor {Slug} failed; scheduling retry",
                    slug);
            }

            nextDue[slug] = DateTimeOffset.UtcNow + PollInterval(sensor);
        }
    }

    private async Task PollSensorAsync(
        SensorDefinition sensor,
        HashSet<string> loginAttempted,
        CancellationToken cancellationToken)
    {
        var options = pollingOptions.Value;
        var requestId = Interlocked.Increment(ref _requestIdSeed);
        var payload = ToRequestHex(BuildTelemetryRequestPayload(requestId));
        var capturedAt = DateTimeOffset.UtcNow;

        using var response = await client.SendAcquisitionRequestAsync(
            sensor.MeshPublicKey,
            payload,
            options.RequestTimeoutMs,
            cancellationToken);
        var status = response.RootElement.TryGetProperty("status", out var statusElement)
            ? statusElement.GetString()
            : null;

        if (!string.Equals(status, "ok", StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Sensor {Slug} did not answer within {TimeoutMs} ms (request {RequestId})",
                sensor.Slug.Value,
                options.RequestTimeoutMs,
                requestId);
            await TryLoginOnceAsync(sensor, loginAttempted, cancellationToken);
            return;
        }

        var responseHex = response.RootElement.TryGetProperty("responseHex", out var hexElement) &&
            hexElement.ValueKind == JsonValueKind.String
                ? hexElement.GetString()
                : null;
        if (string.IsNullOrEmpty(responseHex))
        {
            logger.LogWarning("Sensor {Slug} replied without a response body (request {RequestId})", sensor.Slug.Value, requestId);
            return;
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
            return;
        }

        var readings = telemetry
            .GroupBy(value => value.MetricKey)
            .ToDictionary(group => group.Key, group => group.First().Value);
        var payloadJson = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["type"] = "sensor_poll",
            ["sensor"] = sensor.Slug.Value,
            ["requestId"] = requestId,
            ["protocol"] = "meshcore-req-lpp",
            ["rssi"] = ReadNullableDouble(response.RootElement, "rssi"),
            ["snr"] = ReadNullableDouble(response.RootElement, "snr"),
            ["elapsedMs"] = ReadNullableDouble(response.RootElement, "elapsedMs"),
            ["responseHex"] = responseHex,
            ["readings"] = readings,
        });

        var snapshotId = await store.AppendAsync(capturedAt, client.TransportName, payloadJson, cancellationToken);
        logger.LogInformation(
            "Sensor {Slug} answered request {RequestId}: {Metrics}; outbox snapshot {SnapshotId}",
            sensor.Slug.Value,
            requestId,
            string.Join(", ", telemetry.Select(value => $"{value.MetricKey}={value.Value.ToString(CultureInfo.InvariantCulture)}{value.Unit}")),
            snapshotId);
    }

    private async Task TryLoginOnceAsync(SensorDefinition sensor, HashSet<string> loginAttempted, CancellationToken cancellationToken)
    {
        var options = pollingOptions.Value;
        if (!options.LoginOnTimeout || !loginAttempted.Add(sensor.Slug.Value))
        {
            return;
        }

        logger.LogInformation(
            "Attempting ANON login bootstrap for sensor {Slug} (password {Configured})",
            sensor.Slug.Value,
            options.LoginPassword.Length > 0 ? "configured" : "empty");
        try
        {
            using var login = await client.SendAcquisitionLoginAsync(
                sensor.MeshPublicKey,
                options.LoginPassword,
                options.LoginTimeoutMs,
                cancellationToken);
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

    private TimeSpan PollInterval(SensorDefinition sensor)
    {
        var overrideSeconds = pollingOptions.Value.IntervalOverrideSeconds;
        if (overrideSeconds >= 30 && overrideSeconds < sensor.PollInterval.TotalSeconds)
        {
            return TimeSpan.FromSeconds(overrideSeconds);
        }

        return sensor.PollInterval;
    }

    private static TimeSpan NextStartDelay(SensorDefinition sensor)
    {
        // Deterministic jitter so restarts do not synchronise all sensors.
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sensor.Slug.Value));
        var jitterSeconds = BitConverter.ToUInt32(hash, 0) % 20;
        return TimeSpan.FromSeconds(jitterSeconds);
    }

    private static bool IsHexPublicKey(string value) =>
        value.Length == 64 && System.Text.RegularExpressions.Regex.IsMatch(value, "^[0-9a-fA-F]{64}$");

    private static double? ReadNullableDouble(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;
}
