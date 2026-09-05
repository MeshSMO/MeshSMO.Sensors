namespace MeshSMO.Sensors.Gateway;

public sealed class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("MeshSMO Sensors gateway started; MeshCore transport is not configured yet");
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }
}
