using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MeshSMO.Sensors.Gateway.LocalStorage;

/// <summary>
/// Applies the local outbox EF migrations. Called once at gateway startup
/// (Program.cs, before hosted services and the telemetry API start) and from
/// tests; there is intentionally no separate migrator project for SQLite.
/// </summary>
public static class LocalOutboxDatabase
{
    public static Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var options = services.GetRequiredService<IOptions<LocalTelemetryOptions>>().Value;
        return MigrateAsync(
            services.GetRequiredService<IDbContextFactory<LocalOutboxDbContext>>(),
            options.DatabasePath,
            cancellationToken);
    }

    public static async Task MigrateAsync(
        IDbContextFactory<LocalOutboxDbContext> contextFactory,
        string databasePath,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            // WAL lets the poller write while push/pull workers read; the mode is
            // persisted in the database file, so setting it at startup is enough.
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = WAL;", cancellationToken);
            await db.Database.MigrateAsync(cancellationToken);

            if (!await db.GatewayIdentities.AnyAsync(cancellationToken).ConfigureAwait(false))
            {
                db.GatewayIdentities.Add(new GatewayIdentity
                {
                    Id = 1,
                    InstanceId = Guid.NewGuid(),
                });
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
