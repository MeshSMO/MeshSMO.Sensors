using System.Reflection;
using MeshSMO.Sensors.Application.Registry;
using MeshSMO.Sensors.Infrastructure;
using MeshSMO.Sensors.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Filters;

var validateRegistryOnly = args.Contains("--validate-registry", StringComparer.Ordinal);
var hostArgs = args.Where(argument => !string.Equals(argument, "--validate-registry", StringComparison.Ordinal)).ToArray();
var builder = Host.CreateApplicationBuilder(hostArgs);
builder.Configuration.AddUserSecrets(Assembly.GetExecutingAssembly());

// Serilog: levels live in appsettings ("Serilog:MinimumLevel"). Migration SQL
// stays out of the console but keeps flowing into the rolling file sink. The
// logger is created eagerly (so host-build-phase logs go through Serilog too)
// and disposed with the host, which flushes it on both exit paths.
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Logger(console => console
        .Filter.ByExcluding(Matching.FromSource("Microsoft.EntityFrameworkCore.Database.Command"))
        .WriteTo.Console(outputTemplate:
            "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"))
    .WriteTo.File(
        Path.Combine(AppContext.BaseDirectory, "logs", "dbmigrator-.log"),
        outputTemplate:
            "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}",
        rollingInterval: RollingInterval.Day,
        fileSizeLimitBytes: 134_217_728,
        rollOnFileSizeLimit: true,
        retainedFileCountLimit: 14)
    .CreateLogger();
builder.Logging.ClearProviders();
builder.Logging.AddSerilog(Log.Logger, dispose: true);
if (validateRegistryOnly)
    builder.Services.AddSensorRegistry(builder.Configuration);
else
    builder.Services.AddSensorsInfrastructure(builder.Configuration);

using var host = builder.Build();
var scope = host.Services.CreateAsyncScope();
await using (scope.ConfigureAwait(false))
{
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
}
