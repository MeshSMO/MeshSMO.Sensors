namespace MeshSMO.Sensors.Gateway.Push;

/// <summary>
/// Push delivery of telemetry to the main API: the gateway sends batches from
/// its local outbox itself, so it never needs to be reachable from outside.
/// Enabled by configuring Push:ApiUrl.
/// </summary>
public sealed class TelemetryPushOptions
{
    public const string SectionName = "Push";

    /// <summary>Base address of the main API; push is disabled when not configured.</summary>
    public Uri? ApiUrl { get; set; }

    /// <summary>Shared secret; must match Gateway:Ingest:ApiKey on the main API.</summary>
    public string? ApiKey { get; set; }

    public int BatchSize { get; set; } = 200;

    public int IntervalSeconds { get; set; } = 15;

    /// <summary>Permits plain HTTP towards the main API (for isolated networks); HTTPS is enforced otherwise.</summary>
    public bool AllowInsecureHttp { get; set; }
}
