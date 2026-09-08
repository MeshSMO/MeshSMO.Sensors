using Microsoft.Extensions.Options;

namespace MeshSMO.Sensors.Web.GatewayIngestion;

/// <summary>
/// Pull mode: polls the sensor-gateway local SQLite outbox over its telemetry
/// API and persists the batches in PostgreSQL. Snapshots are acknowledged
/// (removed on the gateway) only after they are durably stored, and duplicates
/// are skipped via the unique gateway identity and snapshot id, so failures on
/// either side are safe. Disabled when Gateway:Mode is not Pull or the gateway is not
/// configured.
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
        if (ingestionOptions.Mode != GatewayDeliveryMode.Pull)
        {
            logger.LogInformation(
                "Gateway pull ingestion is disabled: {OptionName} is {Mode}",
                $"{GatewayIngestionOptions.SectionName}:{nameof(GatewayIngestionOptions.Mode)}",
                ingestionOptions.Mode);
            return;
        }

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
                await IngestPendingBatchAsync(ingestionOptions, stoppingToken).ConfigureAwait(false);
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
                await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task IngestPendingBatchAsync(GatewayIngestionOptions ingestionOptions, CancellationToken cancellationToken)
    {
        var batch = await client.FetchPendingAsync(ingestionOptions.BatchSize, cancellationToken).ConfigureAwait(false);
        if (batch is null || batch.Snapshots.Count == 0)
            return;

        List<long> ackIds;
        int imported;
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var importer = scope.ServiceProvider.GetRequiredService<GatewayTelemetryImporter>();
            imported = await importer.ImportBatchAsync(
                batch.GatewayId,
                batch.Snapshots,
                cancellationToken).ConfigureAwait(false);
            ackIds = batch.Snapshots.Select(snapshot => snapshot.Id).ToList();
        }

        // Acknowledge only after the PostgreSQL write committed; already stored
        // snapshots are acknowledged as well so the gateway outbox can shrink.
        await client.AcknowledgeAsync(ackIds, cancellationToken).ConfigureAwait(false);
        logger.LogInformation(
            "Imported {ImportedCount} of {BatchCount} gateway telemetry snapshots",
            imported,
            batch.Snapshots.Count);
    }
}
