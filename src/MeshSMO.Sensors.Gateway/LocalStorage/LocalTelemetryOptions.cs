namespace MeshSMO.Sensors.Gateway.LocalStorage;

public sealed class LocalTelemetryOptions
{
    public const string SectionName = "LocalTelemetry";

    public string DatabasePath { get; set; } = "data/gateway-telemetry.db";
}
