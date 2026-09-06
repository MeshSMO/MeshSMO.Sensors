using MeshSMO.Sensors.Application.Registry;
using MeshSMO.Sensors.Infrastructure;
using MeshSMO.Sensors.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var validateRegistryOnly = args.Contains("--validate-registry", StringComparer.Ordinal);
var hostArgs = args.Where(argument => !string.Equals(argument, "--validate-registry", StringComparison.Ordinal)).ToArray();
var builder = Host.CreateApplicationBuilder(hostArgs);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
});
if (validateRegistryOnly)
{
    builder.Services.AddSensorRegistry(builder.Configuration);
}
else
{
    builder.Services.AddSensorsInfrastructure(builder.Configuration);
}

using var host = builder.Build();
await using var scope = host.Services.CreateAsyncScope();
var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbMigrator");

if (validateRegistryOnly)
{
    var registry = scope.ServiceProvider.GetRequiredService<ISensorRegistry>();
    var sensors = await registry.LoadAsync(CancellationToken.None);
    logger.LogInformation("Sensor registry is valid: {Total} sensor definitions", sensors.Count);
    return;
}

logger.LogInformation("Applying MeshSMO Sensors database migrations");
var dbContext = scope.ServiceProvider.GetRequiredService<SensorsDbContext>();
await dbContext.Database.MigrateAsync();

logger.LogInformation("Synchronizing GitOps sensor registry");
var synchronizer = scope.ServiceProvider.GetRequiredService<ISensorRegistrySynchronizer>();
var result = await synchronizer.SynchronizeAsync(CancellationToken.None);
logger.LogInformation(
    "Sensor registry synchronized: {Added} added, {Updated} updated, {Total} total",
    result.Added,
    result.Updated,
    result.Total);
