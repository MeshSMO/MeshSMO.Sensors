namespace MeshSMO.Sensors.Web.GatewayIngestion;

public sealed class GatewayIngestionOptions
{
    public const string SectionName = "Gateway";

    /// <summary>Base address of the sensor-gateway telemetry API.</summary>
    public Uri? BaseUrl { get; set; }

    /// <summary>Shared secret; must match Gateway:ApiKey when it is configured there.</summary>
    public string? ApiKey { get; set; }

    public int PollIntervalSeconds { get; set; } = 15;

    public int BatchSize { get; set; } = 200;
}
