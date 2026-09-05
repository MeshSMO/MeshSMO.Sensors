namespace MeshSMO.Sensors.Gateway.LocalStorage;

public sealed record PendingTelemetrySnapshot(
    long Id,
    DateTimeOffset CapturedAt,
    string Transport,
    string PayloadJson);

public sealed record LocalTelemetryReading(
    long SnapshotId,
    string MetricKey,
    double? NumericValue,
    string? TextValue);

public interface ILocalTelemetryStore
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task<long> AppendAsync(
        DateTimeOffset capturedAt,
        string transport,
        string payloadJson,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PendingTelemetrySnapshot>> ReadPendingAsync(
        int maximumCount,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<LocalTelemetryReading>> ReadReadingsAsync(
        long snapshotId,
        CancellationToken cancellationToken);

    Task AcknowledgeAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken);

    Task<long> CountPendingAsync(CancellationToken cancellationToken);
}
