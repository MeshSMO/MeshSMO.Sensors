using MeshSMO.Sensors.Gateway.LocalStorage;
using MeshSMO.Sensors.Gateway.MeshCore;
using MeshSMO.Sensors.Gateway.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Timeout;

namespace MeshSMO.Sensors.Gateway;

public sealed class Worker(
    IRepeaterClient repeaterClient,
    ILocalTelemetryStore localTelemetryStore,
    IOptions<MeshCoreOptions> options,
    ILogger<Worker> logger,
    [FromKeyedServices(GatewayResiliencePipelines.RepeaterSessionKey)] ResiliencePipeline? reconnectPipeline = null) : BackgroundService
{
    private readonly ResiliencePipeline _reconnectPipeline = reconnectPipeline ??
        GatewayResiliencePipelines.CreateRepeaterSessionPipeline(options.Value);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The outbox schema is migrated in Program.cs before hosted services start.
        if (options.Value.Mode == MeshCoreConnectionMode.Disabled)
        {
            logger.LogInformation("MeshSMO Sensors gateway started with MeshCore communication disabled");
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken).ConfigureAwait(false);
            return;
        }

        // Panel telemetry is opt-in: it duplicates repeater internals into the
        // outbox and the main API does not consume it. Sensor polling is
        // unaffected — the HTTP client logs its panel session in lazily.
        if (!options.Value.TelemetryCollectionEnabled)
        {
            logger.LogInformation("Repeater telemetry collection is disabled (MeshCore:TelemetryCollectionEnabled)");
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken).ConfigureAwait(false);
            return;
        }

        var retryDelay = TimeSpan.FromSeconds(options.Value.ReconnectDelaySeconds);
        var collectionInterval = TimeSpan.FromSeconds(options.Value.TelemetryCollectionIntervalSeconds);
        try
        {
            await _reconnectPipeline.ExecuteAsync(async token =>
            {
                try
                {
                    await repeaterClient.ConnectAsync(token).ConfigureAwait(false);
                    var version = await repeaterClient.ExecuteCommandAsync("ver", token).ConfigureAwait(false);
                    logger.LogInformation(
                        "Connected to MeshCoreTel repeater over {Transport}; firmware: {FirmwareVersion}",
                        repeaterClient.TransportName,
                        version);

                    while (!token.IsCancellationRequested)
                    {
                        using var telemetry = await repeaterClient.GetTelemetryAsync(token).ConfigureAwait(false);
                        var snapshotId = await localTelemetryStore.AppendAsync(
                            DateTimeOffset.UtcNow,
                            repeaterClient.TransportName,
                            telemetry.RootElement.GetRawText(),
                            token).ConfigureAwait(false);
                        logger.LogDebug(
                            "Stored repeater telemetry snapshot {SnapshotId} in the local outbox",
                            snapshotId);
                        await Task.Delay(collectionInterval, token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (
                    exception is HttpRequestException or TaskCanceledException or MeshCoreTelApiException or
                        IOException or UnauthorizedAccessException or TimeoutException or TimeoutRejectedException or
                        InvalidOperationException)
                {
                    logger.LogWarning(
                        exception,
                        "MeshCoreTel repeater communication over {Transport} failed; retrying in {RetryDelay}",
                        repeaterClient.TransportName,
                        retryDelay);
                    await repeaterClient.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
            }, stoppingToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await repeaterClient.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                logger.LogWarning(exception, "Failed to close the {Transport} repeater connection", repeaterClient.TransportName);
            }
        }
    }
}
