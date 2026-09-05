using System.Text.Json.Serialization;
using MeshSMO.Sensors.Gateway;
using MeshSMO.Sensors.Gateway.Api;
using MeshSMO.Sensors.Gateway.MeshCore;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.Services.AddHealthChecks();
builder.Services.AddMeshCoreGateway(builder.Configuration);
builder.Services.AddHostedService<Worker>();

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
