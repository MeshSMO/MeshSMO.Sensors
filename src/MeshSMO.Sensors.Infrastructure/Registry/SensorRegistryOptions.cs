namespace MeshSMO.Sensors.Infrastructure.Registry;

public sealed class SensorRegistryOptions
{
    public const string SectionName = "Registry";

    public string Directory { get; set; } = "config/sensors";
}
