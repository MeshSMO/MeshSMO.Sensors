using MeshSMO.Sensors.Application.Abstractions;
using MeshSMO.Sensors.Domain.Measurements;
using MeshSMO.Sensors.Domain.Polling;
using MeshSMO.Sensors.Domain.Sensors;
using Microsoft.EntityFrameworkCore;

namespace MeshSMO.Sensors.Infrastructure.Persistence;

public sealed class SensorRepository(SensorsDbContext dbContext) : ISensorRepository
{
    public async Task<IReadOnlyList<Sensor>> ListEnabledAsync(CancellationToken cancellationToken) =>
        await dbContext.Sensors
            .AsNoTracking()
            .Include(sensor => sensor.Metrics)
            .Where(sensor => sensor.Enabled)
            .OrderBy(sensor => sensor.Slug)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<Sensor?> FindBySlugAsync(SensorSlug slug, CancellationToken cancellationToken) =>
        dbContext.Sensors
            .AsNoTracking()
            .Include(sensor => sensor.Metrics)
            .SingleOrDefaultAsync(sensor => sensor.Slug == slug, cancellationToken);
}

public sealed class MeasurementRepository(SensorsDbContext dbContext) : IMeasurementRepository
{
    public async Task AddAsync(MeasurementSample sample, CancellationToken cancellationToken)
    {
        dbContext.MeasurementSamples.Add(sample);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class PollAttemptRepository(SensorsDbContext dbContext) : IPollAttemptRepository
{
    public async Task AddAsync(PollAttempt attempt, CancellationToken cancellationToken)
    {
        dbContext.PollAttempts.Add(attempt);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
