namespace MeshSMO.Sensors.Gateway.LocalStorage;

public sealed record PendingTelemetrySnapshot(
    long Id,
    DateTimeOffset CapturedAt,
    string Transport,
    string PayloadJson);

/// <summary>
/// The gateway's local telemetry outbox (SQLite via EF Core). Writers append
/// snapshots, the main API reads them (pull) or the push worker delivers them;
/// rows are deleted only after the main API acknowledged the batch — that ack
/// means the data is durably stored in PostgreSQL.
/// </summary>
public interface ILocalTelemetryStore
{
    Task<long> AppendAsync(
        DateTimeOffset capturedAt,
        string transport,
        string payloadJson,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PendingTelemetrySnapshot>> ReadPendingAsync(
        int maximumCount,
        CancellationToken cancellationToken);

    Task AcknowledgeAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken);

    Task<long> CountPendingAsync(CancellationToken cancellationToken);

    Task<DateTimeOffset?> ReadLastPollStartedAtAsync(
        string sensorSlug,
        CancellationToken cancellationToken);

    Task RecordPollStartedAsync(
        string sensorSlug,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken);
}
