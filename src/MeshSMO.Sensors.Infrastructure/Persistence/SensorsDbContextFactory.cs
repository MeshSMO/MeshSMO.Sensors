using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MeshSMO.Sensors.Infrastructure.Persistence;

public sealed class SensorsDbContextFactory : IDesignTimeDbContextFactory<SensorsDbContext>
{
    public SensorsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Sensors")
            ?? "Host=localhost;Port=5432;Database=meshsmo_sensors;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<SensorsDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new SensorsDbContext(options);
    }
}
