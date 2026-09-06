namespace MeshSMO.Sensors.Web.GatewayIngestion;

/// <summary>
/// Settings for the push ingest endpoint that accepts telemetry delivered by
/// the gateway itself (Gateway:Mode = Push).
/// </summary>
public sealed class GatewayIngestOptions
{
    public const string SectionName = "Gateway:Ingest";

    /// <summary>Shared secret the gateway must send as X-Api-Key; required in Push mode.</summary>
    public string? ApiKey { get; set; }

    public int MaximumBatchSize { get; set; } = 500;
}
