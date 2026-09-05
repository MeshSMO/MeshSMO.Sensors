using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MeshSMO.Sensors.Gateway;
using MeshSMO.Sensors.Gateway.Api;
using MeshSMO.Sensors.Gateway.MeshCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using MeshSMO.Sensors.Gateway.LocalStorage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MeshSMO.Sensors.UnitTests;

public sealed class GatewayTelemetryApiTests : IDisposable
{
    private readonly string _databasePath;
    private readonly WebApplication _app;
    private readonly HttpClient _client;

    public GatewayTelemetryApiTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"gateway-api-{Guid.NewGuid():N}.db");
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LocalTelemetry:DatabasePath"] = _databasePath,
            ["Gateway:ApiKey"] = "test-key",
            ["MeshCore:Mode"] = "Disabled",
        });
        builder.Logging.ClearProviders();
        builder.Services.AddHealthChecks();
        builder.Services.AddMeshCoreGateway(builder.Configuration);
        builder.Services
            .AddOptions<GatewayApiOptions>()
            .Bind(builder.Configuration.GetSection(GatewayApiOptions.SectionName));
        builder.Services.AddHostedService<Worker>();
        _app = builder.Build();
        _app.MapTelemetryApi();
        _app.StartAsync().GetAwaiter().GetResult();
        _app.Services.GetRequiredService<ILocalTelemetryStore>()
            .InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        _client = (_app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>() as TestServer)!.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Api-Key", "test-key");
    }

    [Fact]
    public async Task Pending_ReturnsSnapshotsWithReadings()
    {
        var store = _app.Services.GetRequiredService<ILocalTelemetryStore>();
        await store.AppendAsync(
            DateTimeOffset.UtcNow,
            "Serial",
            """{"core":{"battery_mv":4100},"radio":{"rssi":-92}}""",
            CancellationToken.None);

        var response = await _client.GetAsync("/api/telemetry/pending?maxCount=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var batch = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, batch.GetProperty("pendingCount").GetInt64());
        var snapshot = batch.GetProperty("snapshots").EnumerateArray().Single();
        Assert.Equal("Serial", snapshot.GetProperty("transport").GetString());
        Assert.True(snapshot.GetProperty("id").GetInt64() > 0);

        var metricKeys = snapshot.GetProperty("readings")
            .EnumerateArray()
            .Select(reading => reading.GetProperty("metricKey").GetString()!)
            .ToArray();
        Assert.Equal(["core.battery_mv", "radio.rssi"], metricKeys);
    }

    [Fact]
    public async Task Acknowledge_RemovesSnapshotsFromPending()
    {
        var store = _app.Services.GetRequiredService<ILocalTelemetryStore>();
        var firstId = await store.AppendAsync(DateTimeOffset.UtcNow, "Serial", """{"a":1}""", CancellationToken.None);
        var secondId = await store.AppendAsync(DateTimeOffset.UtcNow, "Serial", """{"a":2}""", CancellationToken.None);

        var response = await _client.PostAsJsonAsync("/api/telemetry/ack", new { ids = new[] { firstId } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Equal(0, await CountPending(firstId));
        Assert.Equal(1, await CountPending(secondId));
    }

    [Fact]
    public async Task Pending_WithoutApiKey_IsUnauthorized()
    {
        using var anonymousClient = (_app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>() as TestServer)!.CreateClient();
        var response = await anonymousClient.GetAsync("/api/telemetry/pending");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<int> CountPending(long snapshotId)
    {
        var batch = await (await _client.GetAsync("/api/telemetry/pending?maxCount=10"))
            .Content.ReadFromJsonAsync<JsonElement>();
        return batch.GetProperty("snapshots")
            .EnumerateArray()
            .Count(snapshot => snapshot.GetProperty("id").GetInt64() == snapshotId);
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            try
            {
                File.Delete(_databasePath + suffix);
            }
            catch (IOException)
            {
            }
        }
    }
}
