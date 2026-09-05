using MeshSMO.Sensors.Gateway.LocalStorage;
using MeshSMO.Sensors.Gateway.MeshCore;
using Microsoft.Extensions.Options;

namespace MeshSMO.Sensors.Gateway;

public sealed class Worker(
    IRepeaterClient repeaterClient,
    ILocalTelemetryStore localTelemetryStore,
    IOptions<MeshCoreOptions> options,
    ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await localTelemetryStore.InitializeAsync(stoppingToken);

        if (options.Value.Mode == MeshCoreConnectionMode.Disabled)
        {
            logger.LogInformation("MeshSMO Sensors gateway started with MeshCore communication disabled");
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
            return;
        }

        var retryDelay = TimeSpan.FromSeconds(options.Value.ReconnectDelaySeconds);
        var collectionInterval = TimeSpan.FromSeconds(options.Value.TelemetryCollectionIntervalSeconds);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await repeaterClient.ConnectAsync(stoppingToken);
                    var version = await repeaterClient.ExecuteCommandAsync("ver", stoppingToken);
                    logger.LogInformation(
                        "Connected to MeshCoreTel repeater over {Transport}; firmware: {FirmwareVersion}",
                        repeaterClient.TransportName,
                        version);

                    while (!stoppingToken.IsCancellationRequested)
                    {
                        using var telemetry = await repeaterClient.GetTelemetryAsync(stoppingToken);
                        var snapshotId = await localTelemetryStore.AppendAsync(
                            DateTimeOffset.UtcNow,
                            repeaterClient.TransportName,
                            telemetry.RootElement.GetRawText(),
                            stoppingToken);
                        logger.LogDebug(
                            "Stored repeater telemetry snapshot {SnapshotId} in the local outbox",
                            snapshotId);
                        await Task.Delay(collectionInterval, stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception) when (
                    exception is HttpRequestException or TaskCanceledException or MeshCoreTelApiException or
                        IOException or UnauthorizedAccessException or TimeoutException or InvalidOperationException)
                {
                    logger.LogWarning(
                        exception,
                        "MeshCoreTel repeater communication over {Transport} failed; retrying in {RetryDelay}",
                        repeaterClient.TransportName,
                        retryDelay);
                    await repeaterClient.DisconnectAsync(CancellationToken.None);
                    await Task.Delay(retryDelay, stoppingToken);
                }
            }
        }
        finally
        {
            try
            {
                await repeaterClient.DisconnectAsync(CancellationToken.None);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                logger.LogWarning(exception, "Failed to close the {Transport} repeater connection", repeaterClient.TransportName);
            }
        }
    }
}
