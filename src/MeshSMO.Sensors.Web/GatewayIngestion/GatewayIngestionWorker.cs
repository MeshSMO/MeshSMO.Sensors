using System.Text.Json;
using MeshSMO.Sensors.Domain.Measurements;
using MeshSMO.Sensors.Domain.Sensors;
using MeshSMO.Sensors.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MeshSMO.Sensors.Web.GatewayIngestion;

/// <summary>
/// Pulls telemetry snapshots from the sensor-gateway local SQLite outbox and
/// persists them in PostgreSQL. Snapshots are acknowledged (removed on the
/// gateway) only after they are durably stored, and duplicates are skipped via
/// the unique gateway snapshot id, so failures on either side are safe.
/// </summary>
public sealed class GatewayIngestionWorker(
    GatewayTelemetryClient client,
    IServiceScopeFactory scopeFactory,
    IOptions<GatewayIngestionOptions> options,
    ILogger<GatewayIngestionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var ingestionOptions = options.Value;
        if (ingestionOptions.BaseUrl is null)
        {
            logger.LogInformation(
                "Gateway ingestion is disabled: {OptionName} is not configured",
                $"{GatewayIngestionOptions.SectionName}:{nameof(GatewayIngestionOptions.BaseUrl)}");
            return;
        }

        var pollInterval = TimeSpan.FromSeconds(Math.Max(1, ingestionOptions.PollIntervalSeconds));
        using var timer = new PeriodicTimer(pollInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await IngestPendingBatchAsync(ingestionOptions, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Gateway telemetry ingestion attempt failed; retrying in {PollInterval}",
                    pollInterval);
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task IngestPendingBatchAsync(GatewayIngestionOptions ingestionOptions, CancellationToken cancellationToken)
    {
        var batch = await client.FetchPendingAsync(ingestionOptions.BatchSize, cancellationToken);
        if (batch is null || batch.Snapshots.Count == 0)
        {
            return;
        }

        List<long> ackIds;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<SensorsDbContext>();
            var batchIds = batch.Snapshots.Select(snapshot => snapshot.Id).ToArray();
            var existingIds = await dbContext.GatewayTelemetrySnapshots
                .Where(snapshot => batchIds.Contains(snapshot.GatewaySnapshotId))
                .Select(snapshot => snapshot.GatewaySnapshotId)
                .ToListAsync(cancellationToken);
            var existingSet = existingIds.ToHashSet();
            var importedAt = DateTimeOffset.UtcNow;

            var sensorPollSnapshots = new List<(GatewayTelemetrySnapshot Entity, SensorPollPayload Payload)>();
            foreach (var snapshot in batch.Snapshots)
            {
                if (existingSet.Contains(snapshot.Id))
                {
                    continue;
                }

                var entity = new GatewayTelemetrySnapshot
                {
                    Id = Guid.NewGuid(),
                    GatewaySnapshotId = snapshot.Id,
                    CapturedAt = snapshot.CapturedAt,
                    ImportedAt = importedAt,
                    Transport = snapshot.Transport,
                    PayloadJson = snapshot.PayloadJson,
                };
                foreach (var reading in snapshot.Readings)
                {
                    entity.Readings.Add(new GatewayTelemetryReading
                    {
                        SnapshotId = entity.Id,
                        MetricKey = reading.MetricKey,
                        NumericValue = reading.NumericValue,
                        TextValue = reading.TextValue,
                    });
                }

                dbContext.GatewayTelemetrySnapshots.Add(entity);
                existingSet.Add(snapshot.Id);

                var pollPayload = SensorPollPayload.TryParse(snapshot.PayloadJson);
                if (pollPayload is not null)
                {
                    sensorPollSnapshots.Add((entity, pollPayload));
                }
            }

            await AppendSensorMeasurementsAsync(dbContext, sensorPollSnapshots, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            ackIds = batchIds.ToList();
        }

        // Acknowledge only after the PostgreSQL write committed; already stored
        // snapshots are acknowledged as well so the gateway outbox can shrink.
        await client.AcknowledgeAsync(ackIds, cancellationToken);
        logger.LogInformation("Imported {Count} gateway telemetry snapshots", batch.Snapshots.Count);
    }

    /// <summary>
    /// Maps gateway sensor_poll snapshots to domain measurement samples and
    /// refreshes the materialized sensor status. Idempotency comes from the
    /// unique (sensor_id, request_id) index on measurement_samples.
    /// </summary>
    private async Task AppendSensorMeasurementsAsync(
        SensorsDbContext dbContext,
        IReadOnlyList<(GatewayTelemetrySnapshot Entity, SensorPollPayload Payload)> sensorPollSnapshots,
        CancellationToken cancellationToken)
    {
        if (sensorPollSnapshots.Count == 0)
        {
            return;
        }

        var slugs = sensorPollSnapshots
            .Select(item => item.Payload.Sensor)
            .Distinct()
            .ToArray();
        var slugValues = new List<SensorSlug>(slugs.Length);
        foreach (var slug in slugs)
        {
            try
            {
                slugValues.Add(new SensorSlug(slug));
            }
            catch (ArgumentException)
            {
                logger.LogWarning("Skipping sensor poll snapshot with malformed slug {Slug}", slug);
            }
        }

        var sensors = await dbContext.Sensors
            .Where(sensor => slugValues.Contains(sensor.Slug))
            .ToDictionaryAsync(sensor => sensor.Slug.Value, sensor => sensor, cancellationToken);
        var requestIdBySensor = sensorPollSnapshots
            .Where(item => sensors.ContainsKey(item.Payload.Sensor))
            .GroupBy(item => item.Payload.Sensor)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Payload.RequestId).ToArray());
        var existingSampleKeys = new HashSet<(Guid, long)>();
        foreach (var (sensor, requestIds) in requestIdBySensor)
        {
            var sensorId = sensors[sensor].Id;
            var known = await dbContext.MeasurementSamples
                .Where(sample => sample.SensorId == sensorId && requestIds.Contains(sample.RequestId!.Value))
                .Select(sample => sample.RequestId)
                .ToListAsync(cancellationToken);
            foreach (var requestId in known)
            {
                existingSampleKeys.Add((sensorId.Value, requestId!.Value));
            }
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
            {
                continue;
            }

            var sample = new MeasurementSample(
                Guid.NewGuid(),
                sensor.Id,
                payload.RequestId,
                entity.CapturedAt,
                payload.Protocol)
            {
                Rssi = payload.Rssi is not null ? (float)payload.Rssi : null,
                Snr = payload.Snr is not null ? (float)payload.Snr : null,
                RawPayload = payload.ResponseHex is null ? null : Convert.FromHexString(payload.ResponseHex),
            };
            foreach (var reading in payload.Readings)
            {
                sample.Values.Add(new MeasurementValue(
                    sample.Id,
                    sensor.Id,
                    reading.Key,
                    entity.CapturedAt)
                {
                    NumericValue = reading.Value,
                });
            }

            dbContext.MeasurementSamples.Add(sample);
            existingSampleKeys.Add((sensor.Id.Value, payload.RequestId));

            var status = await dbContext.SensorStatuses
                .SingleOrDefaultAsync(snapshot => snapshot.SensorId == sensor.Id, cancellationToken);
            if (status is null)
            {
                status = new SensorStatusSnapshot(sensor.Id, entity.CapturedAt);
                dbContext.SensorStatuses.Add(status);
            }

            status.State = SensorState.Online;
            status.LastPollAt = entity.CapturedAt;
            status.LastSuccessAt = entity.CapturedAt;
            status.ConsecutiveFailures = 0;
            status.LastRssi = payload.Rssi is not null ? (float)payload.Rssi : null;
            status.LastSnr = payload.Snr is not null ? (float)payload.Snr : null;
            status.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    private sealed record SensorPollPayload(
        string Sensor,
        long RequestId,
        string Protocol,
        double? Rssi,
        double? Snr,
        string? ResponseHex,
        IReadOnlyDictionary<string, double> Readings)
    {
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
                    type.GetString() != "sensor_poll")
                {
                    return null;
                }

                if (root.TryGetProperty("sensor", out var sensorElement) &&
                    sensorElement.ValueKind == JsonValueKind.String &&
                    root.TryGetProperty("requestId", out var requestIdElement) &&
                    requestIdElement.TryGetInt64(out var requestId) &&
                    root.TryGetProperty("readings", out var readingsElement) &&
                    readingsElement.ValueKind == JsonValueKind.Object)
                {
                    var readings = new Dictionary<string, double>();
                    foreach (var reading in readingsElement.EnumerateObject())
                    {
                        if (reading.Value.ValueKind == JsonValueKind.Number && reading.Value.TryGetDouble(out var value))
                        {
                            readings[reading.Name] = value;
                        }
                    }

                    string? responseHex = null;
                    if (root.TryGetProperty("responseHex", out var responseElement) &&
                        responseElement.ValueKind == JsonValueKind.String)
                    {
                        responseHex = responseElement.GetString();
                    }

                    return new SensorPollPayload(
                        sensorElement.GetString()!,
                        requestId,
                        root.TryGetProperty("protocol", out var protocolElement) &&
                            protocolElement.ValueKind == JsonValueKind.String
                            ? protocolElement.GetString()!
                            : "unknown",
                        ReadNumber(root, "rssi"),
                        ReadNumber(root, "snr"),
                        responseHex,
                        readings);
                }

                return null;
            }
        }

        private static double? ReadNumber(JsonElement element, string property) =>
            element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
                ? value.GetDouble()
                : null;
    }
}
