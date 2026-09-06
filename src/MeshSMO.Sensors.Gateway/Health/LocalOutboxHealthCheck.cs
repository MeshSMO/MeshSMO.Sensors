using MeshSMO.Sensors.Gateway.LocalStorage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MeshSMO.Sensors.Gateway.Health;

/// <summary>
/// Ready check for the local SQLite outbox: the database must be openable and
/// writable (disk full or a locked file means telemetry would be lost). Writes
/// a probe row via EF and deletes it again, never touching queued snapshots.
/// </summary>
public sealed class LocalOutboxHealthCheck(IDbContextFactory<LocalOutboxDbContext> contextFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
            var probe = new OutboxHealthProbe { CheckedAt = DateTimeOffset.UtcNow };
            db.HealthProbes.Add(probe);
            await db.SaveChangesAsync(cancellationToken);
            db.HealthProbes.Remove(probe);
            await db.SaveChangesAsync(cancellationToken);

            return HealthCheckResult.Healthy("Local telemetry outbox is writable.");
        }
        catch (Exception exception)
        {
            return new HealthCheckResult(
                context.Registration.FailureStatus,
                "Local telemetry outbox is not writable.",
                exception);
        }
    }
}
