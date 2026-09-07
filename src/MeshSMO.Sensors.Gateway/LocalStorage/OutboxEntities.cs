namespace MeshSMO.Sensors.Gateway.LocalStorage;

/// <summary>
/// A queued telemetry snapshot: the raw payload JSON the gateway received
/// (repeater telemetry or a sensor poll attempt) plus its flattened readings.
/// Rows live until the main API acknowledges them.
/// </summary>
public sealed class OutboxSnapshot
{
    public long Id { get; set; }

    /// <summary>When the payload was received (UTC).</summary>
    public DateTimeOffset CapturedAt { get; set; }

    /// <summary>Transport the payload arrived over (e.g. "http", "serial").</summary>
    public string Transport { get; set; } = string.Empty;

    /// <summary>The payload exactly as documented in docs/protocol.md.</summary>
    public string PayloadJson { get; set; } = string.Empty;

    /// <summary>When the row was appended to the outbox (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<OutboxReading> Readings { get; set; } = new List<OutboxReading>();
}

/// <summary>Flattened scalar from <see cref="OutboxSnapshot.PayloadJson"/> (key path like "sensors.temperature").</summary>
public sealed class OutboxReading
{
    public long SnapshotId { get; set; }

    public string MetricKey { get; set; } = string.Empty;

    public double? NumericValue { get; set; }

    public string? TextValue { get; set; }
}

/// <summary>Writability probe row; the health check inserts and deletes it, never touching queued snapshots.</summary>
public sealed class OutboxHealthProbe
{
    public long Id { get; set; }

    public DateTimeOffset CheckedAt { get; set; }
}
