using MeshSMO.Sensors.Gateway.Api;
using MeshSMO.Sensors.Gateway.LocalStorage;
using Microsoft.Extensions.Options;

namespace MeshSMO.Sensors.Gateway.Push;

/// <summary>
/// Push mode: periodically delivers pending telemetry snapshots from the local
/// SQLite outbox to the main API. A batch is acknowledged (removed locally)
/// only after the API responded 2xx, and the API skips already-stored
/// snapshots, so failures or restarts on either side never lose or duplicate
/// data.
/// </summary>
public sealed class TelemetryPushWorker(
    ILocalTelemetryStore store,
    TelemetryPushClient client,
    IOptions<TelemetryPushOptions> options,
    ILogger<TelemetryPushWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pushOptions = options.Value;
        if (pushOptions.ApiUrl is null)
        {
            logger.LogInformation(
                "Telemetry push is disabled: {OptionName} is not configured",
                $"{TelemetryPushOptions.SectionName}:{nameof(TelemetryPushOptions.ApiUrl)}");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(1, pushOptions.IntervalSeconds));
        using var timer = new PeriodicTimer(interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PushPendingBatchAsync(pushOptions, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Telemetry push attempt failed; the batch stays in the local outbox and will be retried in {Interval}",
                    interval);
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

    private async Task PushPendingBatchAsync(TelemetryPushOptions pushOptions, CancellationToken cancellationToken)
    {
        var snapshots = await store.ReadPendingAsync(pushOptions.BatchSize, cancellationToken);
        if (snapshots.Count == 0)
        {
            return;
        }

        var items = new List<TelemetrySnapshotDto>(snapshots.Count);
        foreach (var snapshot in snapshots)
        {
            var readings = await store.ReadReadingsAsync(snapshot.Id, cancellationToken);
            items.Add(new TelemetrySnapshotDto(
                snapshot.Id,
                snapshot.CapturedAt,
                snapshot.Transport,
                snapshot.PayloadJson,
                readings
                    .Select(reading => new TelemetryReadingDto(
                        reading.MetricKey,
                        reading.NumericValue,
                        reading.TextValue))
                    .ToArray()));
        }

        var pendingCount = await store.CountPendingAsync(cancellationToken);
        await client.PushAsync(new TelemetryBatchDto(pendingCount, items), cancellationToken);

        // The API confirmed the batch (idempotently), so the local outbox can drop it.
        await store.AcknowledgeAsync(snapshots.Select(snapshot => snapshot.Id).ToArray(), cancellationToken);
        logger.LogInformation("Pushed {Count} telemetry snapshots to the main API", snapshots.Count);
    }
}
