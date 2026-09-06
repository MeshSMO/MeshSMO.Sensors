using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MeshSMO.Sensors.Gateway.LocalStorage;

/// <summary>
/// Per-connection SQLite tuning for the outbox: busy_timeout so the poller
/// writing while push/pull workers read never fails with SQLITE_BUSY, and
/// synchronous=NORMAL, the recommended level for WAL (journal mode is set once
/// at migration time). These pragmas are per-connection state; everything else
/// about the schema is EF's business.
/// </summary>
public sealed class SqliteOutboxConnectionInterceptor : DbConnectionInterceptor
{
    public static readonly SqliteOutboxConnectionInterceptor Instance = new();

    private const string Pragmas = "PRAGMA busy_timeout = 5000; PRAGMA synchronous = NORMAL;";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = connection.CreateCommand();
        command.CommandText = Pragmas;
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        var command = connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.CommandText = Pragmas;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
