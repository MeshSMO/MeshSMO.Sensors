namespace MeshSMO.Sensors.Application.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
