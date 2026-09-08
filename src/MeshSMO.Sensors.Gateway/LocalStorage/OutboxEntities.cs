namespace MeshSMO.Sensors.Gateway.LocalStorage;

/// <summary>
/// A queued telemetry snapshot: the raw payload JSON the gateway received
/// (repeater telemetry or a sensor poll attempt). The payload is the single
/// source of truth — readings are parsed from it by the main API. Rows live
/// until the main API acknowledges them.
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
}

/// <summary>
/// Stable identity of this gateway outbox. It namespaces the local autoincrement
/// snapshot ids when telemetry is delivered to the main API.
/// </summary>
public sealed class GatewayIdentity
{
    public int Id { get; set; }

    public Guid InstanceId { get; set; }
}

/// <summary>Writability probe row; the health check inserts and deletes it, never touching queued snapshots.</summary>
public sealed class OutboxHealthProbe
{
    public long Id { get; set; }

    public DateTimeOffset CheckedAt { get; set; }
}

/// <summary>Persistent scheduler state used to preserve polling intervals across gateway restarts.</summary>
public sealed class SensorPollState
{
    /// <summary>Stable sensor slug from the registry.</summary>
    public string SensorSlug { get; set; } = string.Empty;

    /// <summary>When the most recent poll cycle started (UTC).</summary>
    public DateTimeOffset LastPollStartedAt { get; set; }
}
