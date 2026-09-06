namespace MeshSMO.Sensors.Web.GatewayIngestion;

/// <summary>How telemetry reaches the main API from the sensor-gateway.</summary>
public enum GatewayDeliveryMode
{
    /// <summary>The main API polls the gateway telemetry API (default).</summary>
    Pull,

    /// <summary>The gateway pushes telemetry batches to the main API ingest endpoint.</summary>
    Push,
}

public sealed class GatewayIngestionOptions
{
    public const string SectionName = "Gateway";

    public GatewayDeliveryMode Mode { get; set; } = GatewayDeliveryMode.Pull;

    /// <summary>Base address of the sensor-gateway telemetry API; used in Pull mode.</summary>
    public Uri? BaseUrl { get; set; }

    /// <summary>Shared secret; must match Gateway:ApiKey when it is configured there.</summary>
    public string? ApiKey { get; set; }

    public int PollIntervalSeconds { get; set; } = 15;

    public int BatchSize { get; set; } = 200;

    /// <summary>
    /// Consecutive failed poll attempts (imported from gateway poll_attempt
    /// snapshots) after which the materialized sensor state becomes Degraded.
    /// </summary>
    public int DegradedAfterFailures { get; set; } = 1;

    /// <summary>
    /// Consecutive failed poll attempts after which the materialized sensor
    /// state becomes Offline. Must be ≥ DegradedAfterFailures.
    /// </summary>
    public int OfflineAfterFailures { get; set; } = 6;
}
