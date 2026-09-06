using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace MeshSMO.Sensors.Gateway.LocalStorage;

public sealed class LocalTelemetryStore(IDbContextFactory<LocalOutboxDbContext> contextFactory) : ILocalTelemetryStore
{
    public async Task<long> AppendAsync(
        DateTimeOffset capturedAt,
        string transport,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transport);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);
        using var payload = JsonDocument.Parse(payloadJson);

        var snapshot = new OutboxSnapshot
        {
            CapturedAt = capturedAt.ToUniversalTime(),
            Transport = transport,
            PayloadJson = payloadJson,
            CreatedAt = DateTimeOffset.UtcNow,
            Readings = FlattenReadings(payload.RootElement)
                .Select(reading => new OutboxReading
                {
                    MetricKey = reading.MetricKey,
                    NumericValue = reading.NumericValue,
                    TextValue = reading.TextValue,
                })
                .ToList(),
        };

        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            db.Snapshots.Add(snapshot);
            await db.SaveChangesAsync(cancellationToken);
            return snapshot.Id;
        }
    }

    public async Task<IReadOnlyList<PendingTelemetrySnapshot>> ReadPendingAsync(
        int maximumCount,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCount);

        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var snapshots = await db.Snapshots
            .Include(entity => entity.Readings)
            .OrderBy(entity => entity.Id)
            .Take(maximumCount)
            .ToListAsync(cancellationToken);

            return snapshots
                .Select(snapshot => new PendingTelemetrySnapshot(
                    snapshot.Id,
                    snapshot.CapturedAt,
                    snapshot.Transport,
                    snapshot.PayloadJson,
                    snapshot.Readings
                        .OrderBy(reading => reading.MetricKey, StringComparer.Ordinal)
                        .Select(reading => new LocalTelemetryReading(
                            reading.SnapshotId,
                            reading.MetricKey,
                            reading.NumericValue,
                            reading.TextValue))
                        .ToArray()))
                .ToArray();
        }
    }

    public async Task AcknowledgeAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
            return;

        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            // The FK cascade removes the readings together with the snapshot.
            await db.Snapshots
            .Where(entity => ids.Contains(entity.Id))
            .ExecuteDeleteAsync(cancellationToken);
        }
    }

    public async Task<long> CountPendingAsync(CancellationToken cancellationToken)
    {
        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
            return await db.Snapshots.LongCountAsync(cancellationToken);
    }

    private static IEnumerable<FlattenedReading> FlattenReadings(JsonElement root) => FlattenReadings(root, string.Empty);

    private static IEnumerable<FlattenedReading> FlattenReadings(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var childPath = string.IsNullOrEmpty(path) ? property.Name : $"{path}.{property.Name}";
                foreach (var reading in FlattenReadings(property.Value, childPath))
                    yield return reading;
            }

            yield break;
        }

        if (string.IsNullOrEmpty(path) || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            yield break;

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
