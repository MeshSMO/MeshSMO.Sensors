using MeshSMO.Sensors.Forecasting.Configuration;
using Microsoft.Extensions.Options;
using Polly;

namespace MeshSMO.Sensors.Web.Resilience;

public static class WebResiliencePipelines
{
    public const string ForecastCalculationKey = "forecast-calculation";

    public static IServiceCollection AddWebResiliencePipelines(this IServiceCollection services)
    {
        services.AddResilienceEnricher();
        services.AddResiliencePipeline(ForecastCalculationKey, static (builder, context) =>
        {
            var options = context.ServiceProvider.GetRequiredService<IOptions<ForecastingOptions>>().Value;
            builder.AddTimeout(TimeSpan.FromSeconds(options.CalculationTimeoutSeconds));
        });
        return services;
    }
}
