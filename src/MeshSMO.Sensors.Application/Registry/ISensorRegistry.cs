namespace MeshSMO.Sensors.Application.Registry;

public interface ISensorRegistry
{
    Task<IReadOnlyList<SensorDefinition>> LoadAsync(CancellationToken cancellationToken);
}

public interface ISensorRegistrySynchronizer
{
    Task<SensorRegistrySyncResult> SynchronizeAsync(CancellationToken cancellationToken);
}

public sealed record SensorRegistrySyncResult(int Added, int Updated, int Total);
