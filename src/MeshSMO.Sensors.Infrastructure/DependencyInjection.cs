using MeshSMO.Sensors.Application.Abstractions;
using MeshSMO.Sensors.Application.Registry;
using MeshSMO.Sensors.Infrastructure.Persistence;
using MeshSMO.Sensors.Infrastructure.Registry;
using MeshSMO.Sensors.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MeshSMO.Sensors.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSensorsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Sensors")
            ?? throw new InvalidOperationException("Connection string 'Sensors' is required.");

        services.AddSensorRegistry(configuration);
        services.AddDbContext<SensorsDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<ISensorRegistrySynchronizer, SensorRegistrySynchronizer>();
        services.AddScoped<ISensorRepository, SensorRepository>();
        services.AddScoped<IMeasurementRepository, MeasurementRepository>();
        services.AddScoped<IPollAttemptRepository, PollAttemptRepository>();
        return services;
    }

    public static IServiceCollection AddSensorRegistry(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<SensorRegistryOptions>(configuration.GetSection(SensorRegistryOptions.SectionName));
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<ISensorRegistry, FileSystemSensorRegistry>();
        return services;
    }
}
