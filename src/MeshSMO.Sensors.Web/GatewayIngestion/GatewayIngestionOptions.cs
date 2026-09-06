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
}
