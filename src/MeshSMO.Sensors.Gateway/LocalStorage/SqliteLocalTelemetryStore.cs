using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace MeshSMO.Sensors.Gateway.LocalStorage;

public sealed class SqliteLocalTelemetryStore(IOptions<LocalTelemetryOptions> options) : ILocalTelemetryStore
{
    private readonly string _databasePath = Path.GetFullPath(options.Value.DatabasePath);

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;

            CREATE TABLE IF NOT EXISTS telemetry_snapshots (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                captured_at TEXT NOT NULL,
                transport TEXT NOT NULL,
                payload_json TEXT NOT NULL,
                created_at TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_telemetry_snapshots_captured_at
                ON telemetry_snapshots(captured_at, id);

            CREATE TABLE IF NOT EXISTS telemetry_readings (
                snapshot_id INTEGER NOT NULL,
                metric_key TEXT NOT NULL,
                numeric_value REAL NULL,
                text_value TEXT NULL,
                PRIMARY KEY(snapshot_id, metric_key),
                FOREIGN KEY(snapshot_id) REFERENCES telemetry_snapshots(id) ON DELETE CASCADE
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<long> AppendAsync(
        DateTimeOffset capturedAt,
        string transport,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transport);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);
        using var payload = JsonDocument.Parse(payloadJson);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT INTO telemetry_snapshots(captured_at, transport, payload_json, created_at)
            VALUES ($capturedAt, $transport, $payloadJson, $createdAt);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$capturedAt", capturedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$transport", transport);
        command.Parameters.AddWithValue("$payloadJson", payloadJson);
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        var result = await command.ExecuteScalarAsync(cancellationToken);
        var snapshotId = Convert.ToInt64(result, CultureInfo.InvariantCulture);

        foreach (var reading in FlattenReadings(payload.RootElement))
        {
            await using var readingCommand = connection.CreateCommand();
            readingCommand.Transaction = (SqliteTransaction)transaction;
            readingCommand.CommandText = """
                INSERT INTO telemetry_readings(snapshot_id, metric_key, numeric_value, text_value)
                VALUES ($snapshotId, $metricKey, $numericValue, $textValue);
                """;
            readingCommand.Parameters.AddWithValue("$snapshotId", snapshotId);
            readingCommand.Parameters.AddWithValue("$metricKey", reading.MetricKey);
            readingCommand.Parameters.AddWithValue(
                "$numericValue",
                reading.NumericValue is null ? DBNull.Value : reading.NumericValue.Value);
            readingCommand.Parameters.AddWithValue(
                "$textValue",
                reading.TextValue is null ? DBNull.Value : reading.TextValue);
            await readingCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return snapshotId;
    }

    public async Task<IReadOnlyList<PendingTelemetrySnapshot>> ReadPendingAsync(
        int maximumCount,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCount);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, captured_at, transport, payload_json
            FROM telemetry_snapshots
            ORDER BY id
            LIMIT $maximumCount;
            """;
        command.Parameters.AddWithValue("$maximumCount", maximumCount);

        var result = new List<PendingTelemetrySnapshot>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new PendingTelemetrySnapshot(
                reader.GetInt64(0),
                DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.GetString(2),
                reader.GetString(3)));
        }

        return result;
    }

    public async Task<IReadOnlyList<LocalTelemetryReading>> ReadReadingsAsync(
        long snapshotId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT snapshot_id, metric_key, numeric_value, text_value
            FROM telemetry_readings
            WHERE snapshot_id = $snapshotId
            ORDER BY metric_key;
            """;
        command.Parameters.AddWithValue("$snapshotId", snapshotId);

        var result = new List<LocalTelemetryReading>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new LocalTelemetryReading(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetDouble(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        }

        return result;
    }

    public async Task AcknowledgeAsync(
        IReadOnlyCollection<long> ids,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return;
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var id in ids)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = "DELETE FROM telemetry_snapshots WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<long> CountPendingAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM telemetry_snapshots;";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
        await command.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    private static IEnumerable<FlattenedReading> FlattenReadings(JsonElement root)
    {
        return FlattenReadings(root, string.Empty);
    }

    private static IEnumerable<FlattenedReading> FlattenReadings(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var childPath = string.IsNullOrEmpty(path) ? property.Name : $"{path}.{property.Name}";
                foreach (var reading in FlattenReadings(property.Value, childPath))
                {
                    yield return reading;
                }
            }

            yield break;
        }

        if (string.IsNullOrEmpty(path) || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            yield break;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var numericValue))
        {
            yield return new FlattenedReading(path, numericValue, null);
            yield break;
        }

        var textValue = element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => element.GetRawText(),
        };
        yield return new FlattenedReading(path, null, textValue);
    }

    private sealed record FlattenedReading(string MetricKey, double? NumericValue, string? TextValue);
}
