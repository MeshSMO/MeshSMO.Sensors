namespace MeshSMO.Sensors.Infrastructure.Persistence;

/// <summary>
/// Telemetry snapshot pulled from the gateway local outbox. The payload JSON is
/// stored as delivered; domain data is mapped to measurement samples, values
/// and poll attempts. Idempotency is guaranteed by the gateway identity plus
/// local snapshot id, so different gateways may reuse the same local ids.
/// </summary>
public sealed class GatewayTelemetrySnapshot
{
    public Guid Id { get; set; }
    public Guid GatewayId { get; set; }
    public long GatewaySnapshotId { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public DateTimeOffset ImportedAt { get; set; }
    public string Transport { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
}
