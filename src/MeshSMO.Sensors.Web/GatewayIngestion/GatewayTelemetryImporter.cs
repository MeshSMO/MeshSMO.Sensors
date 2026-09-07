using System.Globalization;
using System.Text.Json;
using MeshSMO.Sensors.Domain.Measurements;
using MeshSMO.Sensors.Domain.Polling;
using MeshSMO.Sensors.Domain.Sensors;
using MeshSMO.Sensors.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MeshSMO.Sensors.Web.GatewayIngestion;

/// <summary>
/// Persists a batch of gateway telemetry snapshots in PostgreSQL. Shared by the
/// pull worker and the push ingest endpoint so both delivery modes produce
/// identical rows. Snapshots already stored (by gateway snapshot id) or already
/// mapped (by sensor + request id) are skipped, so redelivery is safe.
/// Two payload kinds arrive from the gateway: <c>sensor_poll</c> (successful
/// poll with readings) and <c>poll_attempt</c> (failed attempt). The former
/// maps to measurement samples, the latter only to poll_attempts and the
/// materialized sensor status; both write a poll_attempts row.
/// </summary>
public sealed class GatewayTelemetryImporter(
    SensorsDbContext dbContext,
    IOptions<GatewayIngestionOptions> ingestionOptions,
    ILogger<GatewayTelemetryImporter> logger)
{
    /// <summary>Imports the batch and returns the number of newly stored snapshots.</summary>
    public async Task<int> ImportBatchAsync(
        IReadOnlyList<GatewayTelemetrySnapshotDto> snapshots,
        CancellationToken cancellationToken)
    {
        var batchIds = snapshots.Select(snapshot => snapshot.Id).ToArray();
        var existingIds = await dbContext.GatewayTelemetrySnapshots
            .Where(snapshot => batchIds.Contains(snapshot.GatewaySnapshotId))
            .Select(snapshot => snapshot.GatewaySnapshotId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var existingSet = existingIds.ToHashSet();
        var importedAt = DateTimeOffset.UtcNow;

        var sensorPollSnapshots = new List<(GatewayTelemetrySnapshot Entity, SensorPollPayload Payload)>();
        var pollAttemptSnapshots = new List<(GatewayTelemetrySnapshot Entity, PollAttemptPayload Payload)>();
        var importedCount = 0;
        foreach (var snapshot in snapshots)
        {
            if (existingSet.Contains(snapshot.Id))
                continue;

            var entity = new GatewayTelemetrySnapshot
            {
                Id = Guid.NewGuid(),
                GatewaySnapshotId = snapshot.Id,
                CapturedAt = snapshot.CapturedAt,
                ImportedAt = importedAt,
                Transport = snapshot.Transport,
                PayloadJson = snapshot.PayloadJson,
            };
            foreach (var reading in snapshot.Readings ?? [])
            {
                entity.Readings.Add(new()
                {
                    SnapshotId = entity.Id,
                    MetricKey = reading.MetricKey,
                    NumericValue = reading.NumericValue,
                    TextValue = reading.TextValue,
                });
            }

            dbContext.GatewayTelemetrySnapshots.Add(entity);
            existingSet.Add(snapshot.Id);
            importedCount++;

            var pollPayload = SensorPollPayload.TryParse(snapshot.PayloadJson);
            if (pollPayload is not null)
            {
                sensorPollSnapshots.Add((entity, pollPayload));
                continue;
            }

            var attemptPayload = PollAttemptPayload.TryParse(snapshot.PayloadJson);
            if (attemptPayload is not null)
                pollAttemptSnapshots.Add((entity, attemptPayload));
        }

        // Failures first so a batch containing both attempts of one cycle ends
        // with the success (Online, zero failures), not the failure.
        var sensors = await ResolveSensorsAsync(
            sensorPollSnapshots.Select(item => item.Payload.Sensor)
                .Concat(pollAttemptSnapshots.Select(item => item.Payload.Sensor)),
            cancellationToken).ConfigureAwait(false);
        // One materialized status per sensor for the whole batch: several
        // snapshots of the same sensor must mutate the same instance.
        var statuses = await dbContext.SensorStatuses.ToDictionaryAsync(
            snapshot => snapshot.SensorId.Value,
            snapshot => snapshot,
            cancellationToken).ConfigureAwait(false);
        await AppendPollAttemptsAsync(sensorPollSnapshots, pollAttemptSnapshots, sensors, statuses, cancellationToken).ConfigureAwait(false);
        await AppendSensorMeasurementsAsync(sensorPollSnapshots, sensors, statuses, cancellationToken).ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (importedCount > 0)
            logger.LogInformation("Imported {Count} gateway telemetry snapshots", importedCount);

        return importedCount;
    }

    /// <summary>
    /// Maps gateway poll attempts to poll_attempts rows (idempotent by the
    /// unique (sensor_id, request_id, attempt_number) index) and records
    /// failures in the materialized sensor status.
    /// </summary>
    private async Task AppendPollAttemptsAsync(
        IReadOnlyList<(GatewayTelemetrySnapshot Entity, SensorPollPayload Payload)> sensorPollSnapshots,
        IReadOnlyList<(GatewayTelemetrySnapshot Entity, PollAttemptPayload Payload)> pollAttemptSnapshots,
        Dictionary<string, Sensor> sensors,
        Dictionary<Guid, SensorStatusSnapshot> statuses,
        CancellationToken cancellationToken)
    {

        var pending = new List<(Sensor Sensor, PollAttempt Attempt, bool Succeeded)>(
            sensorPollSnapshots.Count + pollAttemptSnapshots.Count);
        foreach (var (entity, payload) in sensorPollSnapshots)
        {
            if (!sensors.TryGetValue(payload.Sensor, out var sensor))
            {
                logger.LogWarning("Poll attempt snapshot for unknown sensor {Slug} stored as telemetry only", payload.Sensor);
                continue;
            }

            var attempt = new PollAttempt(
                Guid.NewGuid(),
                sensor.Id,
                payload.RequestId,
                ResolveStartedAt(payload, entity.CapturedAt),
                payload.AttemptNumber)
            {
                CompletedAt = entity.CapturedAt,
                Status = PollAttemptStatus.Succeeded,
                RoundTripMilliseconds = payload.ElapsedMs,
            };
            pending.Add((sensor, attempt, Succeeded: true));
        }
        foreach (var (entity, payload) in pollAttemptSnapshots)
        {
            if (!sensors.TryGetValue(payload.Sensor, out var sensor))
            {
                logger.LogWarning("Poll attempt snapshot for unknown sensor {Slug} stored as telemetry only", payload.Sensor);
                continue;
            }

            var attempt = new PollAttempt(
                Guid.NewGuid(),
                sensor.Id,
                payload.RequestId,
                payload.StartedAt,
                payload.AttemptNumber)
            {
                CompletedAt = payload.CompletedAt ?? entity.CapturedAt,
                Status = payload.Status,
                ErrorCode = payload.ErrorCode,
                ErrorMessage = payload.ErrorMessage,
                RoundTripMilliseconds = payload.RoundTripMs,
            };
            pending.Add((sensor, attempt, Succeeded: false));
        }

        if (pending.Count == 0)
            return;

        var existingAttemptKeys = new HashSet<(Guid SensorId, long RequestId, int AttemptNumber)>();
        foreach (var group in pending.GroupBy(item => item.Sensor.Id))
        {
            var requestIds = group.Select(item => item.Attempt.RequestId).Distinct().ToArray();
            var known = await dbContext.PollAttempts
                .Where(attempt => attempt.SensorId == group.Key && requestIds.Contains(attempt.RequestId))
                .Select(attempt => new { attempt.RequestId, attempt.AttemptNumber })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var attempt in known)
                existingAttemptKeys.Add((group.Key.Value, attempt.RequestId, attempt.AttemptNumber));
        }

        foreach (var (sensor, attempt, succeeded) in pending)
        {
            if (!existingAttemptKeys.Add((sensor.Id.Value, attempt.RequestId, attempt.AttemptNumber)))
                continue;

            dbContext.PollAttempts.Add(attempt);

            if (succeeded)
                continue;

            var status = GetOrAddStatus(statuses, sensor, DateTimeOffset.UtcNow);
            status.ConsecutiveFailures++;
            status.LastPollAt = attempt.StartedAt;
            status.State = status.ConsecutiveFailures >= ingestionOptions.Value.OfflineAfterFailures
                ? SensorState.Offline
                : SensorState.Degraded;
            status.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// Maps gateway sensor_poll snapshots to domain measurement samples and
    /// refreshes the materialized sensor status. Idempotency comes from the
    /// unique (sensor_id, request_id) index on measurement_samples.
    /// </summary>
    private async Task AppendSensorMeasurementsAsync(
        IReadOnlyList<(GatewayTelemetrySnapshot Entity, SensorPollPayload Payload)> sensorPollSnapshots,
        Dictionary<string, Sensor> sensors,
        Dictionary<Guid, SensorStatusSnapshot> statuses,
        CancellationToken cancellationToken)
    {
        if (sensorPollSnapshots.Count == 0)
            return;

        var requestIdBySensor = sensorPollSnapshots
            .Where(item => sensors.ContainsKey(item.Payload.Sensor))
            .GroupBy(item => item.Payload.Sensor, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Payload.RequestId).ToArray(), StringComparer.Ordinal);
        var existingSampleKeys = new HashSet<(Guid, long)>();
        foreach (var (sensor, requestIds) in requestIdBySensor)
        {
            var sensorId = sensors[sensor].Id;
            var known = await dbContext.MeasurementSamples
                .Where(sample => sample.SensorId == sensorId && requestIds.Contains(sample.RequestId!.Value))
                .Select(sample => sample.RequestId)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var requestId in known)
                existingSampleKeys.Add((sensorId.Value, requestId!.Value));
        }

        foreach (var (entity, payload) in sensorPollSnapshots)
        {
            if (!sensors.TryGetValue(payload.Sensor, out var sensor))
            {
                logger.LogWarning(
                    "Sensor poll snapshot for unknown sensor {Slug} stored as telemetry only",
                    payload.Sensor);
                continue;
            }

            if (existingSampleKeys.Contains((sensor.Id.Value, payload.RequestId)))
                continue;

            var sample = new MeasurementSample(
                Guid.NewGuid(),
                sensor.Id,
                payload.RequestId,
                entity.CapturedAt,
                payload.Protocol)
            {
                Rssi = payload.Rssi is not null ? (float)payload.Rssi : null,
                Snr = payload.Snr is not null ? (float)payload.Snr : null,
                RoundTripMilliseconds = payload.ElapsedMs,
                RawPayload = payload.ResponseHex is null ? null : Convert.FromHexString(payload.ResponseHex),
            };
            foreach (var reading in payload.Readings)
            {
                sample.Values.Add(new(
                    sample.Id,
                    sensor.Id,
                    reading.Metric,
                    entity.CapturedAt)
                {
                    NumericValue = reading.Value,
                    Unit = reading.Unit,
                });
            }

            dbContext.MeasurementSamples.Add(sample);
            existingSampleKeys.Add((sensor.Id.Value, payload.RequestId));

            var status = GetOrAddStatus(statuses, sensor, entity.CapturedAt);
            status.State = SensorState.Online;
            status.LastPollAt = entity.CapturedAt;
            status.LastSuccessAt = entity.CapturedAt;
            status.ConsecutiveFailures = 0;
            status.LastRssi = payload.Rssi is not null ? (float)payload.Rssi : null;
            status.LastSnr = payload.Snr is not null ? (float)payload.Snr : null;
            status.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>Returns the sensor's materialized status from the batch cache, creating (and tracking) it once.</summary>
    private SensorStatusSnapshot GetOrAddStatus(
        Dictionary<Guid, SensorStatusSnapshot> statuses,
        Sensor sensor,
        DateTimeOffset now)
    {
        if (statuses.TryGetValue(sensor.Id.Value, out var status))
            return status;

        status = new(sensor.Id, now);
        dbContext.SensorStatuses.Add(status);
        statuses[sensor.Id.Value] = status;
        return status;
    }

    private async Task<Dictionary<string, Sensor>> ResolveSensorsAsync(
        IEnumerable<string> slugs,
        CancellationToken cancellationToken)
    {
        var slugValues = new List<SensorSlug>();
        foreach (var slug in slugs.Distinct(StringComparer.Ordinal))
        {
            try
            {
                slugValues.Add(new(slug));
            }
            catch (ArgumentException)
            {
                logger.LogWarning("Skipping gateway snapshot with malformed slug {Slug}", slug);
            }
        }

        return await dbContext.Sensors
            .Where(sensor => slugValues.Contains(sensor.Slug))
            .ToDictionaryAsync(sensor => sensor.Slug.Value, sensor => sensor, StringComparer.Ordinal, cancellationToken).ConfigureAwait(false);
    }

    private static DateTimeOffset ResolveStartedAt(SensorPollPayload payload, DateTimeOffset capturedAt) =>
        payload.StartedAt
            ?? (payload.ElapsedMs is { } elapsed ? capturedAt.AddMilliseconds(-elapsed) : capturedAt);

    private sealed record SensorPollPayload(
        string Sensor,
        long RequestId,
        string Protocol,
        double? Rssi,
        double? Snr,
        string? ResponseHex,
        IReadOnlyList<SensorPollPayload.MetricReading> Readings,
        int AttemptNumber,
        DateTimeOffset? StartedAt,
        int? ElapsedMs)
    {
        public sealed record MetricReading(string Metric, double Value, string? Unit);

        public static SensorPollPayload? TryParse(string payloadJson)
        {
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(payloadJson);
            }
            catch (JsonException)
            {
                return null;
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.TryGetProperty("type", out var type) &&
                    type.ValueKind == JsonValueKind.String &&
!string.Equals(type.GetString(), "sensor_poll", StringComparison.Ordinal))
                {
                    return null;
                }

                if (root.TryGetProperty("sensor", out var sensorElement) &&
                    sensorElement.ValueKind == JsonValueKind.String &&
                    root.TryGetProperty("requestId", out var requestIdElement) &&
                    requestIdElement.TryGetInt64(out var requestId) &&
                    root.TryGetProperty("readings", out var readingsElement) &&
                    readingsElement.ValueKind == JsonValueKind.Array)
                {
                    var readings = new List<MetricReading>();
                    foreach (var reading in readingsElement.EnumerateArray())
                    {
                        if (reading.TryGetProperty("metric", out var metricElement) &&
                            metricElement.ValueKind == JsonValueKind.String &&
                            reading.TryGetProperty("value", out var valueElement) &&
                            valueElement.ValueKind == JsonValueKind.Number &&
                            valueElement.TryGetDouble(out var value))
                        {
                            var unit = reading.TryGetProperty("unit", out var unitElement) &&
                                unitElement.ValueKind == JsonValueKind.String
                                    ? unitElement.GetString()
                                    : null;
                            readings.Add(new(metricElement.GetString()!, value, unit));
                        }
                    }

                    string? responseHex = null;
                    if (root.TryGetProperty("responseHex", out var responseElement) &&
                        responseElement.ValueKind == JsonValueKind.String)
                    {
                        responseHex = responseElement.GetString();
                    }

                    return new(
                        sensorElement.GetString()!,
                        requestId,
                        root.TryGetProperty("protocol", out var protocolElement) &&
                            protocolElement.ValueKind == JsonValueKind.String
                            ? protocolElement.GetString()!
                            : "unknown",
                        ReadNumber(root, "rssi"),
                        ReadNumber(root, "snr"),
                        responseHex,
                        readings,
                        ReadInt(root, "attemptNumber") ?? 1,
                        ReadTimestamp(root, "startedAt"),
                        ReadInt(root, "elapsedMs"));
                }

                return null;
            }
        }
    }

    private sealed record PollAttemptPayload(
        string Sensor,
        long RequestId,
        int AttemptNumber,
        DateTimeOffset StartedAt,
        DateTimeOffset? CompletedAt,
        PollAttemptStatus Status,
        string? ErrorCode,
        string? ErrorMessage,
        int? RoundTripMs)
    {
        public static PollAttemptPayload? TryParse(string payloadJson)
        {
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(payloadJson);
            }
            catch (JsonException)
            {
                return null;
            }

            using (document)
            {
                var root = document.RootElement;
                if (!root.TryGetProperty("type", out var type) ||
                    type.ValueKind != JsonValueKind.String ||
!string.Equals(type.GetString(), "poll_attempt", StringComparison.Ordinal) ||
                    !root.TryGetProperty("sensor", out var sensorElement) ||
                    sensorElement.ValueKind != JsonValueKind.String ||
                    !root.TryGetProperty("requestId", out var requestIdElement) ||
                    !requestIdElement.TryGetInt64(out var requestId) ||
                    !root.TryGetProperty("startedAt", out var startedAtElement) ||
                    !TryGetTimestamp(startedAtElement, out var startedAt))
                {
                    return null;
                }

                var statusText = root.TryGetProperty("status", out var statusElement) &&
                    statusElement.ValueKind == JsonValueKind.String
                        ? statusElement.GetString()
                        : null;
                if (!Enum.TryParse<PollAttemptStatus>(statusText, out var status) ||
                    status is PollAttemptStatus.Started or PollAttemptStatus.Cancelled)
                {
                    return null;
                }

                return new(
                    sensorElement.GetString()!,
                    requestId,
                    ReadInt(root, "attemptNumber") ?? 1,
                    startedAt,
                    ReadTimestamp(root, "completedAt"),
                    status,
                    root.TryGetProperty("errorCode", out var errorCodeElement) &&
                        errorCodeElement.ValueKind == JsonValueKind.String
                            ? errorCodeElement.GetString()
                            : null,
                    root.TryGetProperty("errorMessage", out var errorMessageElement) &&
                        errorMessageElement.ValueKind == JsonValueKind.String
                            ? errorMessageElement.GetString()
                            : null,
                    ReadInt(root, "roundTripMs"));
            }
        }
    }

    private static double? ReadNumber(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    private static int? ReadInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var intValue)
                ? intValue
                : null;

    private static DateTimeOffset? ReadTimestamp(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && TryGetTimestamp(value, out var timestamp)
            ? timestamp
            : null;

    private static bool TryGetTimestamp(JsonElement element, out DateTimeOffset timestamp)
    {
        timestamp = default;
        return element.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(
                element.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out timestamp);
    }
}
