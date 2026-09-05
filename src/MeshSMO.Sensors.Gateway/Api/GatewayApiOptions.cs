namespace MeshSMO.Sensors.Gateway.Api;

public sealed class GatewayApiOptions
{
    public const string SectionName = "Gateway";

    /// <summary>
    /// Optional shared secret. When set, telemetry API requests must carry
    /// the same value in the X-Api-Key header.
    /// </summary>
    public string? ApiKey { get; set; }

    public int MaximumBatchSize { get; set; } = 500;
}
