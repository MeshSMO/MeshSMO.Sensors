using System.Text.Json.Serialization;
using MeshSMO.Sensors.Gateway;
using MeshSMO.Sensors.Gateway.Api;
using MeshSMO.Sensors.Gateway.Health;
using MeshSMO.Sensors.Gateway.LocalStorage;
using MeshSMO.Sensors.Gateway.MeshCore;
using MeshSMO.Sensors.Gateway.Polling;
using MeshSMO.Sensors.Gateway.Push;
using MeshSMO.Sensors.Gateway.Resilience;
using MeshSMO.Sensors.Infrastructure;
using Serilog;
using Serilog.Filters;

var builder = WebApplication.CreateBuilder(args);

// Serilog: levels live in appsettings ("Serilog:MinimumLevel", overridable via
// Serilog__* env). EF SQL (SQLite outbox) stays out of the console but keeps
// flowing into the rolling file sink.
builder.Host.UseSerilog((context, loggerConfiguration) => loggerConfiguration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Logger(console => console
        .Filter.ByExcluding(Matching.FromSource("Microsoft.EntityFrameworkCore.Database.Command"))
        .WriteTo.Console(outputTemplate:
            "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"))
    .WriteTo.File(
        Path.Combine(AppContext.BaseDirectory, "logs", "gateway-.log"),
        outputTemplate:
            "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}",
        rollingInterval: RollingInterval.Day,
        fileSizeLimitBytes: 134_217_728,
        rollOnFileSizeLimit: true,
        retainedFileCountLimit: 14));
builder.Services.AddHealthChecks()
    .AddCheck<LocalOutboxHealthCheck>("local-outbox", tags: ["ready"])
    .AddCheck<SensorRegistryHealthCheck>("sensor-registry", tags: ["ready"]);
builder.Services.AddSensorRegistry(builder.Configuration);
builder.Services.AddMeshCoreGateway(builder.Configuration);

builder.Services
    .AddOptions<SensorPollingOptions>()
    .Bind(builder.Configuration.GetSection(SensorPollingOptions.SectionName))
    .Validate(
        static options => options.RequestTimeoutMs is > 0 and <= 30_000,
        "SensorPolling:RequestTimeoutMs must be between 1 and 30000.")
    .Validate(
        static options => options.LoginTimeoutMs is > 0 and <= 10_000,
        "SensorPolling:LoginTimeoutMs must be between 1 and 10000.")
    .Validate(
        static options => options.RetryBackoffMinMs is >= 0 and <= 60_000
            && options.RetryBackoffMaxMs is >= 0 and <= 60_000
            && options.RetryBackoffMinMs <= options.RetryBackoffMaxMs,
        "SensorPolling:RetryBackoffMinMs/MaxMs must be between 0 and 60000, MinMs <= MaxMs.")
    .ValidateOnStart();

builder.Services.AddGatewayResiliencePipelines();
builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<SensorTelemetryPoller>();
builder.Services.AddHostedService<TelemetryPushWorker>();

builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);

builder.Services
    .AddOptions<GatewayApiOptions>()
    .Bind(builder.Configuration.GetSection(GatewayApiOptions.SectionName))
    .Validate(
        static options => options.MaximumBatchSize > 0,
        "Gateway:MaximumBatchSize must be greater than zero.")
    .ValidateOnStart();

builder.Services
    .AddOptions<TelemetryPushOptions>()
    .Bind(builder.Configuration.GetSection(TelemetryPushOptions.SectionName))
    .Validate(
        static options => options.ApiUrl is null || options.ApiUrl.IsAbsoluteUri,
        "Push:ApiUrl must be an absolute URI when configured.")
    .Validate(
        static options => options.ApiUrl is null || !string.IsNullOrWhiteSpace(options.ApiKey),
        "Push:ApiKey is required when Push:ApiUrl is configured.")
    .Validate(
        static options => options.ApiUrl is null || options.AllowInsecureHttp ||
string.Equals(options.ApiUrl.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal),
        "Push:ApiUrl must use HTTPS unless Push:AllowInsecureHttp is enabled.")
    .Validate(
        static options => options.BatchSize > 0,
        "Push:BatchSize must be greater than zero.")
    .Validate(
        static options => options.IntervalSeconds > 0,
        "Push:IntervalSeconds must be greater than zero.")
    .ValidateOnStart();

builder.Services.AddHttpClient<TelemetryPushClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<TelemetryPushOptions>>().Value;
    if (options.ApiUrl is not null)
        client.BaseAddress = new($"{options.ApiUrl.AbsoluteUri.TrimEnd('/')}/");
    client.Timeout = Timeout.InfiniteTimeSpan;
})
// Ingest is idempotent by gateway identity plus snapshot id, so the standard
// handler may safely retry POST together with applying its timeout, limiter,
// and circuit breaker.
.AddStandardResilienceHandler();

var app = builder.Build();

// Fail fast if the local outbox cannot be created/migrated: every hosted
// service and the telemetry API depend on it.
await LocalOutboxDatabase.MigrateAsync(app.Services).ConfigureAwait(false);

app.MapTelemetryApi();

app.MapFallback(() => Results.NotFound(new { error = "NotFound" }));

app.Run();
Log.CloseAndFlush();

public partial class Program;
