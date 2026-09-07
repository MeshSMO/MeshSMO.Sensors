using MeshSMO.Sensors.Gateway.MeshCore;
using MeshSMO.Sensors.Gateway.Polling;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using Polly.Timeout;

namespace MeshSMO.Sensors.Gateway.Resilience;

public static class GatewayResiliencePipelines
{
    public const string RepeaterSessionKey = "repeater-session";
    public const string SensorPollingKey = "sensor-polling";

    public static IServiceCollection AddGatewayResiliencePipelines(this IServiceCollection services)
    {
        services.AddResilienceEnricher();
        services.AddResiliencePipeline(RepeaterSessionKey, static (builder, context) =>
        {
            var options = context.ServiceProvider.GetRequiredService<IOptions<MeshCoreOptions>>().Value;
            ConfigureRepeaterSession(builder, options);
        });
        services.AddResiliencePipeline(SensorPollingKey, static (builder, context) =>
        {
            var options = context.ServiceProvider.GetRequiredService<IOptions<SensorPollingOptions>>().Value;
            ConfigureSensorPolling(builder, options);
        });
        return services;
    }

    internal static ResiliencePipeline CreateRepeaterSessionPipeline(MeshCoreOptions options)
    {
        var builder = new ResiliencePipelineBuilder();
        ConfigureRepeaterSession(builder, options);
        return builder.Build();
    }

    internal static ResiliencePipeline CreateSensorPollingPipeline(SensorPollingOptions options)
    {
        var builder = new ResiliencePipelineBuilder();
        ConfigureSensorPolling(builder, options);
        return builder.Build();
    }

    internal static ResiliencePipeline CreateTimeoutPipeline(TimeSpan timeout) =>
        new ResiliencePipelineBuilder()
            .AddTimeout(timeout)
            .Build();

    internal static ResiliencePipeline<HttpResponseMessage> CreateAuthorizationPipeline() =>
        new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .HandleResult(static response => response.StatusCode == System.Net.HttpStatusCode.Unauthorized),
                Delay = TimeSpan.Zero,
                MaxRetryAttempts = 1,
                OnRetry = static arguments =>
                {
                    arguments.Outcome.Result?.Dispose();
                    return default;
                },
            })
            .Build();

    private static void ConfigureRepeaterSession(
        ResiliencePipelineBuilder builder,
        MeshCoreOptions options) =>
        builder.AddRetry(new RetryStrategyOptions
        {
            ShouldHandle = new PredicateBuilder()
                .Handle<HttpRequestException>()
                .Handle<TaskCanceledException>()
                .Handle<MeshCoreTelApiException>()
                .Handle<IOException>()
                .Handle<UnauthorizedAccessException>()
                .Handle<TimeoutException>()
                .Handle<TimeoutRejectedException>()
                .Handle<InvalidOperationException>(),
            Delay = TimeSpan.FromSeconds(options.ReconnectDelaySeconds),
            MaxRetryAttempts = int.MaxValue,
        });

    private static void ConfigureSensorPolling(
        ResiliencePipelineBuilder builder,
        SensorPollingOptions options) =>
        builder.AddRetry(new RetryStrategyOptions
        {
            ShouldHandle = new PredicateBuilder().Handle<SensorPollingRetryException>(),
            MaxRetryAttempts = 2,
            DelayGenerator = _ => new ValueTask<TimeSpan?>(TimeSpan.FromMilliseconds(
                Random.Shared.Next(options.RetryBackoffMinMs, options.RetryBackoffMaxMs + 1))),
        });
}
