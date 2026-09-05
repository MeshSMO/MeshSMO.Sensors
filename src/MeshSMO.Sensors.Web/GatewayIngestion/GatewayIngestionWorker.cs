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
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            ackIds = batchIds.ToList();
        }

        // Acknowledge only after the PostgreSQL write committed; already stored
        // snapshots are acknowledged as well so the gateway outbox can shrink.
        await client.AcknowledgeAsync(ackIds, cancellationToken);
        logger.LogInformation("Imported {Count} gateway telemetry snapshots", batch.Snapshots.Count);
    }
}
