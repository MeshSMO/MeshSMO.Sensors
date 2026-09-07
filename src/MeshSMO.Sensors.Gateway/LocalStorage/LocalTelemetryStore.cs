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
        // Fail fast on malformed payloads instead of queuing them for the
        // main API; the JSON itself is the data — nothing else is derived here.
        using var payload = JsonDocument.Parse(payloadJson);

        var snapshot = new OutboxSnapshot
        {
            CapturedAt = capturedAt.ToUniversalTime(),
            Transport = transport,
            PayloadJson = payloadJson,
            CreatedAt = DateTimeOffset.UtcNow,
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
            .OrderBy(entity => entity.Id)
            .Take(maximumCount)
            .ToListAsync(cancellationToken);

            return snapshots
                .Select(snapshot => new PendingTelemetrySnapshot(
                    snapshot.Id,
                    snapshot.CapturedAt,
                    snapshot.Transport,
                    snapshot.PayloadJson))
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

    public async Task<DateTimeOffset?> ReadLastPollStartedAtAsync(
        string sensorSlug,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sensorSlug);

        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
            return await db.SensorPollStates
                .Where(state => state.SensorSlug == sensorSlug)
                .Select(state => (DateTimeOffset?)state.LastPollStartedAt)
                .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task RecordPollStartedAsync(
        string sensorSlug,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sensorSlug);

        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var state = await db.SensorPollStates.FindAsync([sensorSlug], cancellationToken).ConfigureAwait(false);
            if (state is null)
            {
                db.SensorPollStates.Add(new SensorPollState
                {
                    SensorSlug = sensorSlug,
                    LastPollStartedAt = startedAt.ToUniversalTime(),
                });
            }
            else
            {
                state.LastPollStartedAt = startedAt.ToUniversalTime();
            }

            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
