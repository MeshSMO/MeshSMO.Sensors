using MeshSMO.Sensors.Gateway;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
