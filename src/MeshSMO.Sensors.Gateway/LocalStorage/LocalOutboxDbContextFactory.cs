using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MeshSMO.Sensors.Gateway.LocalStorage;

/// <summary>
/// Design-time factory so `dotnet ef migrations add` works against this
/// project without booting the gateway host. The path is irrelevant for
/// generating migrations.
/// </summary>
public sealed class LocalOutboxDbContextFactory : IDesignTimeDbContextFactory<LocalOutboxDbContext>
{
    public LocalOutboxDbContext CreateDbContext(string[] args)
    {
        var databasePath = Environment.GetEnvironmentVariable("LocalTelemetry__DatabasePath")
            ?? new LocalTelemetryOptions().DatabasePath;
        var options = new DbContextOptionsBuilder<LocalOutboxDbContext>()
            .UseSqlite($"Data Source={Path.GetFullPath(databasePath)}")
            .Options;
        return new(options);
    }
}
