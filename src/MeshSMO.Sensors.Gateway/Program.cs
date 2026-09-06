using System.Text.Json.Serialization;
using MeshSMO.Sensors.Gateway;
using MeshSMO.Sensors.Infrastructure;
using MeshSMO.Sensors.Gateway.Api;
using MeshSMO.Sensors.Gateway.MeshCore;
using MeshSMO.Sensors.Gateway.Polling;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.Services.AddHealthChecks();
builder.Services.AddSensorRegistry(builder.Configuration);
builder.Services.AddMeshCoreGateway(builder.Configuration);
builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<SensorTelemetryPoller>();

builder.Services
    .AddOptions<SensorPollingOptions>()
    .Bind(builder.Configuration.GetSection(SensorPollingOptions.SectionName))
    .Validate(
        static options => options.RequestTimeoutMs is > 0 and <= 30_000,
        "SensorPolling:RequestTimeoutMs must be between 1 and 30000.")
    .Validate(
        static options => options.LoginTimeoutMs is > 0 and <= 10_000,
        "SensorPolling:LoginTimeoutMs must be between 1 and 10000.")
    .ValidateOnStart();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

builder.Services
    .AddOptions<GatewayApiOptions>()
    .Bind(builder.Configuration.GetSection(GatewayApiOptions.SectionName))
    .Validate(
        static options => options.MaximumBatchSize > 0,
        "Gateway:MaximumBatchSize must be greater than zero.")
    .ValidateOnStart();

var app = builder.Build();

app.MapTelemetryApi();

app.MapFallback(() => Results.NotFound(new { error = "NotFound" }));

app.Run();

public partial class Program;
