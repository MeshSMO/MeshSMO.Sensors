using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MeshSMO.Sensors.Domain.Sensors;
using MeshSMO.Sensors.Infrastructure;
using MeshSMO.Sensors.Infrastructure.Persistence;
using MeshSMO.Sensors.Web.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace MeshSMO.Sensors.UnitTests.Web.Api;

public sealed class SensorApiEndpointsTests : IDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _client;
    private readonly string _databaseName = $"sensors-api-{Guid.NewGuid():N}";

    public SensorApiEndpointsTests()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["ConnectionStrings:Sensors"] = "Host=localhost;Database=unused",
            ["Registry:Directory"] = Path.Combine(Path.GetTempPath(), $"registry-{Guid.NewGuid():N}"),
        });
        builder.Logging.ClearProviders();
        builder.Services.AddSensorsInfrastructure(builder.Configuration);
        builder.Services.RemoveAll<DbContextOptions<SensorsDbContext>>();
        builder.Services.RemoveAll<IDbContextOptionsConfiguration<SensorsDbContext>>();
        builder.Services.AddDbContext<SensorsDbContext>(options =>
            options.UseInMemoryDatabase(_databaseName));
        builder.Services.AddHealthChecks();
        _app = builder.Build();
        _app.MapSensorApi();
        _app.StartAsync().GetAwaiter().GetResult();

        using var scope = _app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SensorsDbContext>();
        var now = DateTimeOffset.UtcNow;
        var publicSensor = new Sensor(
            new(Guid.NewGuid()),
            new("smolensk-center"),
            "Смоленск — центр",
            "Метеодатчик MeshSMO в центральной части Смоленска.",
            "pub-key-1",
            "meshsmo-weather-v1",
            TimeSpan.FromMinutes(5),
            TimeSpan.FromSeconds(30),
            2,
            enabled: true,
            publicVisible: true,
            publicIndexable: true,
            55.0000,
            33.0000,
            "approximate",
            ["temperature", "humidity"],
            now);
        var hiddenSensor = new Sensor(
            new(Guid.NewGuid()),
            new("private-node"),
            "Private node",
            null,
            "pub-key-2",
            "meshsmo-weather-v1",
            TimeSpan.FromMinutes(5),
            TimeSpan.FromSeconds(30),
            2,
            enabled: true,
            publicVisible: false,
            publicIndexable: false,
            null,
            null,
            null,
            ["battery"],
            now);
        dbContext.Sensors.AddRange(publicSensor, hiddenSensor);
        dbContext.SensorStatuses.Add(new(publicSensor.Id, now) { State = SensorState.Online });
        dbContext.SaveChanges();
        _client = (_app.Services.GetRequiredService<IServer>() as TestServer)!.CreateClient();
    }

    [Fact]
    public async Task Sensors_ListsOnlyPublicSensors_WithState()
    {
        var response = await _client.GetAsync("/api/v1/sensors");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var slugs = body.GetProperty("sensors").EnumerateArray()
            .Select(sensor => sensor.GetProperty("slug").GetString()!)
            .ToArray();
        Assert.Equal(["smolensk-center"], slugs);

        var sensor = body.GetProperty("sensors").EnumerateArray().Single();
        Assert.Equal("Online", sensor.GetProperty("state").GetString());
        Assert.Equal(
            ["humidity", "temperature"],
            sensor.GetProperty("metrics").EnumerateArray().Select(m => m.GetString()!).ToArray());
    }

    [Fact]
    public async Task SensorDetail_ReturnsPublicSensor_And404ForUnknownOrHidden()
    {
        var response = await _client.GetAsync("/api/v1/sensors/smolensk-center");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Смоленск — центр", body.GetProperty("displayName").GetString());
        Assert.Equal("Online", body.GetProperty("state").GetString());

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/v1/sensors/no-such-sensor")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/v1/sensors/private-node")).StatusCode);
    }

    [Fact]
    public async Task SensorStatus_ReturnsMaterializedState()
    {
        var response = await _client.GetAsync("/api/v1/sensors/smolensk-center/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Online", body.GetProperty("state").GetString());
        Assert.Equal(0, body.GetProperty("consecutiveFailures").GetInt32());
    }

    [Fact]
    public async Task Dashboard_ReturnsAggregatedSummary()
    {
        var response = await _client.GetAsync("/api/v1/dashboard");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var summary = body.GetProperty("summary");
        Assert.Equal(1, summary.GetProperty("total").GetInt32());
        Assert.Equal(1, summary.GetProperty("online").GetInt32());
        Assert.Equal(0, summary.GetProperty("offline").GetInt32());
        Assert.Single(body.GetProperty("sensors").EnumerateArray());
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
