using System.Threading.RateLimiting;
using MeshSMO.Sensors.Forecasting.Abstractions;
using MeshSMO.Sensors.Forecasting.Configuration;
using MeshSMO.Sensors.Forecasting.MlNet;
using MeshSMO.Sensors.Infrastructure;
using MeshSMO.Sensors.Infrastructure.Persistence;
using MeshSMO.Sensors.Web.Api;
using MeshSMO.Sensors.Web.Api.Forecasting;
using MeshSMO.Sensors.Web.Api.MeasurementHistory;
using MeshSMO.Sensors.Web.GatewayIngestion;
using MeshSMO.Sensors.Web.Resilience;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Filters;

var builder = WebApplication.CreateBuilder(args);

// Serilog: levels live in appsettings ("Serilog:MinimumLevel", overridable via
// Serilog__* env). EF SQL stays out of the console — one SaveChanges can emit a
// huge multi-insert batch — but keeps flowing into the rolling file sink.
builder.Host.UseSerilog((context, loggerConfiguration) => loggerConfiguration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Logger(console => console
        .Filter.ByExcluding(Matching.FromSource("Microsoft.EntityFrameworkCore.Database.Command"))
        .WriteTo.Console(outputTemplate:
            "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"))
    .WriteTo.File(
        Path.Combine(AppContext.BaseDirectory, "logs", "web-.log"),
        outputTemplate:
            "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}",
        rollingInterval: RollingInterval.Day,
        fileSizeLimitBytes: 134_217_728,
        rollOnFileSizeLimit: true,
        retainedFileCountLimit: 14));
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
    .Validate(
        static options => options.DegradedAfterFailures >= 1 &&
            options.OfflineAfterFailures >= options.DegradedAfterFailures,
        "Gateway:DegradedAfterFailures must be >= 1 and Gateway:OfflineAfterFailures must be >= DegradedAfterFailures.")
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
// Validate<GatewayIngestOptions> above resolves the raw type from DI;
// AddOptions<T> on .NET 10 no longer registers TOptions itself.
builder.Services.AddTransient<GatewayIngestOptions>(
    sp => sp.GetRequiredService<IOptions<GatewayIngestOptions>>().Value);
builder.Services.AddScoped<GatewayTelemetryImporter>();
builder.Services.AddScoped<MeasurementHistoryReader>();
builder.Services
    .AddOptions<ForecastingOptions>()
    .Bind(builder.Configuration.GetSection(ForecastingOptions.SectionName))
    .Validate(
        static options => options.StepMinutes > 0 && 60 % options.StepMinutes == 0,
        "Forecasting:StepMinutes must be a positive divisor of one hour.")
    .Validate(
        static options => options.HasValidHistoryConfiguration(),
        "Forecasting history windows are invalid.")
    .Validate(
        static options => options.MinimumCoverage is > 0 and <= 1 &&
            options.ConfidenceLevel is > 0 and < 1 &&
            options.MinimumIntervalCoverage is >= 0 and <= 1 &&
            options.MaximumMase > 0,
        "Forecasting quality thresholds are invalid.")
    .Validate(
        static options => options.BacktestFolds >= 3 &&
            options.ResultCacheMinutes > 0 &&
            options.MaximumCacheEntries > 0 &&
            options.MaximumConcurrentTrainings > 0 &&
            options.CalculationTimeoutSeconds > 0,
        "Forecasting resource limits are invalid.")
    .Validate(
        static options => options.Series.Values.All(static series =>
            series.MaximumMae is null or > 0 &&
            (series.Minimum is null || double.IsFinite(series.Minimum.Value)) &&
            (series.Maximum is null || double.IsFinite(series.Maximum.Value)) &&
            (series.Minimum is null || series.Maximum is null || series.Minimum.Value < series.Maximum.Value)),
        "Forecasting series overrides are invalid.")
    .ValidateOnStart();
builder.Services.AddWebResiliencePipelines();
builder.Services.AddSingleton<IForecastService>(serviceProvider =>
    new MlNetForecastService(serviceProvider.GetRequiredService<IOptions<ForecastingOptions>>().Value));
builder.Services.AddScoped<IForecastSeriesSource, PostgresForecastSeriesSource>();
builder.Services.AddSingleton<ForecastCoordinator>();

// Public API rate limiting: per-IP fixed window. Only endpoints tagged with
// the "public-api" policy are limited; ingest stays unlimited (gateway→web).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("public-api", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
        _ => new()
        {
            PermitLimit = 120,
            Window = TimeSpan.FromMinutes(1),
        }));
    options.AddPolicy("forecast-api", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
        _ => new()
        {
            PermitLimit = 20,
            Window = TimeSpan.FromMinutes(1),
        }));
});

builder.Services.AddOpenApi();
builder.Services.AddHttpClient<GatewayTelemetryClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<GatewayIngestionOptions>>().Value;
    if (options.BaseUrl is not null)
        client.BaseAddress = new($"{options.BaseUrl.AbsoluteUri.TrimEnd('/')}/");
    client.Timeout = Timeout.InfiniteTimeSpan;
})
// Fetch is read-only and ack is idempotent by snapshot id, so the standard
// handler can safely retry both requests.
.AddStandardResilienceHandler();
builder.Services.AddHostedService<GatewayIngestionWorker>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();

app.MapHealthChecks("/health/live", new()
{
    Predicate = _ => false,
});
app.MapHealthChecks("/health/ready", new()
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
        .OrderByDescending(snapshot => snapshot.ImportedAt)
        .Take(pageSize)
        .Select(snapshot => new
        {
            gatewayId = snapshot.GatewayId,
            gatewaySnapshotId = snapshot.GatewaySnapshotId,
            capturedAt = snapshot.CapturedAt,
            importedAt = snapshot.ImportedAt,
            transport = snapshot.Transport,
        })
        .ToListAsync(cancellationToken).ConfigureAwait(false);
    return Results.Ok(new { snapshots });
});

app.MapSensorApi();

app.MapMeasurementHistoryApi();

app.MapForecastApi();

app.MapOpenApi();

app.MapGatewayIngestApi();

app.MapSitemap();
app.MapSensorFallbacks();

app.Map("/api/{**path}", () => Results.NotFound(new
{
    error = "NotFound",
}));

app.MapFallbackToFile("/", "index.html");

// Unknown URLs get a real 404 (a catch-all 200 would flood the index with
// soft-200 duplicates); the SPA shell is still the body so the client router
// renders its not-found page.
app.MapFallback(async Task<IResult> (
    IWebHostEnvironment environment,
    CancellationToken cancellationToken) =>
{
    var webRoot = environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");
    var spaFallback = Path.Combine(webRoot, "__spa-fallback.html");
    return File.Exists(spaFallback)
        ? Results.Content(await File.ReadAllTextAsync(spaFallback, cancellationToken), "text/html", statusCode: 404)
        : Results.NotFound();
});

app.Run();
Log.CloseAndFlush();

public partial class Program;
