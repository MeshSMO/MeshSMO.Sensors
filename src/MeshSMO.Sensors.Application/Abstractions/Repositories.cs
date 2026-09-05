using MeshSMO.Sensors.Domain.Measurements;
using MeshSMO.Sensors.Domain.Polling;
using MeshSMO.Sensors.Domain.Sensors;

namespace MeshSMO.Sensors.Application.Abstractions;

public interface ISensorRepository
{
    Task<IReadOnlyList<Sensor>> ListEnabledAsync(CancellationToken cancellationToken);
    Task<Sensor?> FindBySlugAsync(SensorSlug slug, CancellationToken cancellationToken);
}

public interface IMeasurementRepository
{
    Task AddAsync(MeasurementSample sample, CancellationToken cancellationToken);
}

public interface IPollAttemptRepository
{
    Task AddAsync(PollAttempt attempt, CancellationToken cancellationToken);
}
