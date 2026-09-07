using System.Threading.RateLimiting;
using MeshSMO.Sensors.Infrastructure;
using MeshSMO.Sensors.Infrastructure.Persistence;
using MeshSMO.Sensors.Web.Api;
using MeshSMO.Sensors.Web.Api.Forecasting;
using MeshSMO.Sensors.Web.Api.MeasurementHistory;
using MeshSMO.Sensors.Web.GatewayIngestion;
using MeshSMO.Sensors.Forecasting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
});
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
        static options => options.TrainingWindowDays > 0 &&
            options.MinimumHistoryDays > 0 &&
            options.MinimumHistoryDays <= options.TrainingWindowDays,
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
    client.Timeout = TimeSpan.FromSeconds(30);
});
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
        .OrderByDescending(snapshot => snapshot.GatewaySnapshotId)
        .Take(pageSize)
        .Select(snapshot => new
        {
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
app.MapFallbackToFile("__spa-fallback.html");

app.Run();

public partial class Program;
