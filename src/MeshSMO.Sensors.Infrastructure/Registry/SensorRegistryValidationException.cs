namespace MeshSMO.Sensors.Infrastructure.Registry;

public sealed class SensorRegistryValidationException(IReadOnlyList<string> errors)
    : Exception($"Sensor registry validation failed:{Environment.NewLine}- {string.Join($"{Environment.NewLine}- ", errors)}")
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
