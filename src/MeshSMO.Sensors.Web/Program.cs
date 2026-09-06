using MeshSMO.Sensors.Infrastructure;
using MeshSMO.Sensors.Infrastructure.Persistence;
using MeshSMO.Sensors.Web.Api;
using MeshSMO.Sensors.Web.GatewayIngestion;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.Services.AddSensorsInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddDbContextCheck<SensorsDbContext>("database", tags: ["ready"]);

builder.Services
    .AddOptions<GatewayIngestionOptions>()
    .Bind(builder.Configuration.GetSection(GatewayIngestionOptions.SectionName))
    .Validate(
        static options => options.BaseUrl is null || options.BaseUrl.IsAbsoluteUri,
        "Gateway:BaseUrl must be an absolute URI when configured.")
    .Validate(
        static options => options.PollIntervalSeconds > 0,
        "Gateway:PollIntervalSeconds must be greater than zero.")
    .Validate(
        static options => options.BatchSize > 0,
        "Gateway:BatchSize must be greater than zero.")
    .Validate(
        static options => Enum.IsDefined(options.Mode),
        "Gateway:Mode must be either Pull or Push.")
    .Validate<GatewayIngestOptions>(
        static (options, ingest) => options.Mode != GatewayDeliveryMode.Push ||
            !string.IsNullOrWhiteSpace(ingest.ApiKey),
        "Gateway:Ingest:ApiKey is required when Gateway:Mode is Push.")
    .ValidateOnStart();
builder.Services
    .AddOptions<GatewayIngestOptions>()
    .Bind(builder.Configuration.GetSection(GatewayIngestOptions.SectionName))
    .Validate(
        static options => options.MaximumBatchSize > 0,
        "Gateway:Ingest:MaximumBatchSize must be greater than zero.")
    .ValidateOnStart();
builder.Services.AddScoped<GatewayTelemetryImporter>();
builder.Services.AddHttpClient<GatewayTelemetryClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<GatewayIngestionOptions>>().Value;
    if (options.BaseUrl is not null)
    {
        client.BaseAddress = new Uri($"{options.BaseUrl.AbsoluteUri.TrimEnd('/')}/");
    }
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHostedService<GatewayIngestionWorker>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
});

app.MapGet("/api/v1", () => Results.Ok(new
{
    service = "MeshSMO Sensors",
    version = "v1",
}));

app.MapGet("/api/v1/telemetry/snapshots", async Task<IResult> (
    int? limit,
    SensorsDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var pageSize = limit is null or < 1 ? 20 : Math.Min(limit.Value, 200);
    var snapshots = await dbContext.GatewayTelemetrySnapshots
        .AsNoTracking()
        .OrderByDescending(snapshot => snapshot.GatewaySnapshotId)
        .Take(pageSize)
        .Select(snapshot => new
        {
            gatewaySnapshotId = snapshot.GatewaySnapshotId,
            capturedAt = snapshot.CapturedAt,
            importedAt = snapshot.ImportedAt,
            transport = snapshot.Transport,
        })
        .ToListAsync(cancellationToken);
    return Results.Ok(new { snapshots });
});

app.MapSensorApi();

app.MapGatewayIngestApi();

app.MapSitemap();
app.MapSensorFallbacks();

app.Map("/api/{**path}", () => Results.NotFound(new
{
    error = "NotFound",
}));

app.MapFallbackToFile("/", "index.html");
app.MapFallbackToFile("__spa-fallback.html");

app.Run();

public partial class Program;
