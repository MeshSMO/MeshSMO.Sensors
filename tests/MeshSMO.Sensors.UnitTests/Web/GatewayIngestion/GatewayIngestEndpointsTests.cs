using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MeshSMO.Sensors.Domain.Sensors;
using MeshSMO.Sensors.Infrastructure;
using MeshSMO.Sensors.Infrastructure.Persistence;
using MeshSMO.Sensors.Web.GatewayIngestion;
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

namespace MeshSMO.Sensors.UnitTests.Web.GatewayIngestion;

public sealed class GatewayIngestEndpointsTests : IDisposable
{
    private const string ApiKey = "test-key";
    private readonly string _databaseName = $"ingest-{Guid.NewGuid():N}";
    private readonly WebApplication _app;
    private readonly HttpClient _client;

    public GatewayIngestEndpointsTests()
    {
        _app = CreateApp(
            ("Gateway:Mode", "Push"),
            ("Gateway:Ingest:ApiKey", ApiKey),
            ("Gateway:Ingest:MaximumBatchSize", "2"));
        _client = (_app.Services.GetRequiredService<IServer>() as TestServer)!.CreateClient();
    }

    [Fact]
    public async Task Ingest_WithoutApiKey_IsUnauthorized()
    {
        using var anonymousClient = (_app.Services.GetRequiredService<IServer>() as TestServer)!.CreateClient();

        var response = await anonymousClient.PostAsJsonAsync("/api/telemetry/ingest", ValidBatch());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Ingest_WithWrongApiKey_IsUnauthorized()
    {
        using var wrongKeyClient = (_app.Services.GetRequiredService<IServer>() as TestServer)!.CreateClient();
        wrongKeyClient.DefaultRequestHeaders.Add("X-Api-Key", "wrong");

        var response = await wrongKeyClient.PostAsJsonAsync("/api/telemetry/ingest", ValidBatch());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Ingest_StoresBatch_AndReturnsAcceptedCount()
    {
        _client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);

        var response = await _client.PostAsJsonAsync("/api/telemetry/ingest", ValidBatch());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("accepted").GetInt32());

        using var scope = _app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SensorsDbContext>();
        var snapshot = Assert.Single(dbContext.GatewayTelemetrySnapshots);
        Assert.Equal(42, snapshot.GatewaySnapshotId);
        Assert.Equal("Http", snapshot.Transport);
        var sample = Assert.Single(dbContext.MeasurementSamples.Include(entity => entity.Values));
        Assert.Equal(7, sample.RequestId);
        Assert.Equal("meshcore-req-lpp", sample.ProtocolId);
        var value = Assert.Single(sample.Values);
        Assert.Equal("temperature", value.MetricKey);
        Assert.Equal(21.5, value.NumericValue);
    }

    [Fact]
    public async Task Ingest_SkipsDuplicateSnapshotIds()
    {
        _client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        var first = await _client.PostAsJsonAsync("/api/telemetry/ingest", ValidBatch());
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await _client.PostAsJsonAsync("/api/telemetry/ingest", ValidBatch());

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var body = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body.GetProperty("accepted").GetInt32());
        using var scope = _app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SensorsDbContext>();
        Assert.Single(dbContext.GatewayTelemetrySnapshots);
        Assert.Single(dbContext.MeasurementSamples);
    }

    [Fact]
    public async Task Ingest_RejectsBatchAboveMaximumBatchSize()
    {
        _client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        var oversized = new
        {
            pendingCount = 3,
            snapshots = new[]
            {
                Snapshot(1), Snapshot(2), Snapshot(3),
            },
        };

        var response = await _client.PostAsJsonAsync("/api/telemetry/ingest", oversized);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = _app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SensorsDbContext>();
        Assert.Empty(dbContext.GatewayTelemetrySnapshots);
    }

    [Fact]
    public async Task Ingest_EmptyBatch_ReturnsZeroWithoutStoreWrites()
    {
        _client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);

        var response = await _client.PostAsJsonAsync("/api/telemetry/ingest", new { pendingCount = 0, snapshots = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body.GetProperty("accepted").GetInt32());
    }

    [Fact]
    public async Task Ingest_NotAvailableInPullMode()
    {
        using var app = CreateApp();
        var client = (app.Services.GetRequiredService<IServer>() as TestServer)!.CreateClient();

        var response = await client.PostAsJsonAsync("/api/telemetry/ingest", new { });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static object ValidBatch() => new
    {
        pendingCount = 1,
        snapshots = new[]
        {
            Snapshot(42),
        },
    };

    private static object Snapshot(long id) => new
    {
        id,
        capturedAt = DateTimeOffset.UtcNow,
        transport = "Http",
        payloadJson = """{"type":"sensor_poll","sensor":"smolensk-center","requestId":7,"protocol":"meshcore-req-lpp","rssi":-92.5,"snr":7.5,"responseHex":"00FF","readings":[{"metric":"temperature","value":21.5,"unit":"°C"}]}""",
        readings = new[]
        {
            new { metricKey = "sensors.temperature", numericValue = 21.5, textValue = (string?)null },
        },
    };

    private WebApplication CreateApp(params (string Key, string Value)[] settings)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var configuration = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["ConnectionStrings:Sensors"] = "Host=localhost;Database=unused",
            ["Registry:Directory"] = Path.Combine(Path.GetTempPath(), $"registry-{Guid.NewGuid():N}"),
        };
        foreach (var (key, value) in settings)
            configuration[key] = value;

        builder.Configuration.AddInMemoryCollection(configuration);
        builder.Logging.ClearProviders();
        builder.Services.AddSensorsInfrastructure(builder.Configuration);
        builder.Services.RemoveAll(typeof(DbContextOptions<SensorsDbContext>));
        builder.Services.RemoveAll(typeof(IDbContextOptionsConfiguration<SensorsDbContext>));
        builder.Services.AddDbContext<SensorsDbContext>(options =>
            options.UseInMemoryDatabase(_databaseName));
        builder.Services.AddHealthChecks();
        builder.Services
            .AddOptions<GatewayIngestionOptions>()
            .Bind(builder.Configuration.GetSection(GatewayIngestionOptions.SectionName));
        builder.Services
            .AddOptions<GatewayIngestOptions>()
            .Bind(builder.Configuration.GetSection(GatewayIngestOptions.SectionName));
        builder.Services.AddScoped<GatewayTelemetryImporter>();
        var app = builder.Build();
        app.MapGatewayIngestApi();
        app.StartAsync().GetAwaiter().GetResult();

        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SensorsDbContext>();
        dbContext.Sensors.Add(new Sensor(
            new SensorId(Guid.NewGuid()),
            new SensorSlug("smolensk-center"),
            "Смоленск — центр",
            null,
            "pub-key-1",
            "meshsmo-weather-v1",
            TimeSpan.FromMinutes(5),
            TimeSpan.FromSeconds(30),
            2,
            enabled: true,
            publicVisible: true,
            publicIndexable: true,
            null,
            null,
            null,
            ["temperature"],
            DateTimeOffset.UtcNow));
        dbContext.SaveChanges();
        return app;
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
