using System.Globalization;
using MeshSMO.Sensors.Gateway.LocalStorage;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace MeshSMO.Sensors.Gateway.Health;

/// <summary>
/// Ready check for the local SQLite outbox: the database file must be openable
/// and writable (disk full or a locked file means telemetry would be lost).
/// Uses its own probe table so it never touches queued snapshots.
/// </summary>
public sealed class LocalOutboxHealthCheck(IOptions<LocalTelemetryOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var databasePath = Path.GetFullPath(options.Value.DatabasePath);
            var directory = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
                new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
                {
                    DataSource = databasePath,
                    Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWriteCreate,
                }.ToString());
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS health_probe (checked_at TEXT NOT NULL);
                INSERT INTO health_probe(checked_at) VALUES ($checkedAt);
                DELETE FROM health_probe;
                """;
            command.Parameters.AddWithValue(
                "$checkedAt",
                DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            await command.ExecuteNonQueryAsync(cancellationToken);

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
