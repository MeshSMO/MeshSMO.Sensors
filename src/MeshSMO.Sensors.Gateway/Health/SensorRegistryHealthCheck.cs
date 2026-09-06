using MeshSMO.Sensors.Application.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MeshSMO.Sensors.Gateway.Health;

/// <summary>
/// Ready check for the GitOps sensor registry: the YAML files must parse and
/// validate. Intentionally does NOT check repeater or LoRa reachability — a
/// temporarily offline node must not flip readiness (spec §36).
/// </summary>
public sealed class SensorRegistryHealthCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var registry = scope.ServiceProvider.GetRequiredService<ISensorRegistry>();
            var sensors = await registry.LoadAsync(cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy(
                $"Registry loaded with {sensors.Count} sensor definition(s).");
        }
        catch (Exception exception)
        {
            return new(
                context.Registration.FailureStatus,
                "Sensor registry could not be loaded.",
                exception);
        }
    }
}
