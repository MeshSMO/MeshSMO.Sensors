using System.Text.Json;
using MeshSMO.Sensors.Application.Registry;
using MeshSMO.Sensors.Domain.Sensors;
using Microsoft.Data.Sqlite;
using MeshSMO.Sensors.Gateway.LocalStorage;
using MeshSMO.Sensors.Gateway.MeshCore;
using MeshSMO.Sensors.Gateway.Polling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MeshSMO.Sensors.UnitTests.Gateway.Polling;

public sealed class SensorTelemetryPollerTests : IDisposable
{
    private readonly string _storePath = Path.Combine(Path.GetTempPath(), $"poller-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            try
            {
                File.Delete(_storePath + suffix);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public async Task Poller_PollsEveryRegistrySensor_AppliesChannelMapping_AndBootstrapsLogin()
    {
        var alpha = new SensorDefinition(
            SensorId.New(), new SensorSlug("alpha-node"), "Alpha", null,
            new string('a', 64), "meshcore-req-lpp", TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(8), 2,
            Enabled: true, PublicVisible: true, PublicIndexable: false, null, null, null,
            ["temperature", "battery_voltage"], "alpha.yaml",
            [new TelemetryChannelMapping(1, "voltage", "battery_voltage", "Напряжение батареи", "В")]);
        var bravo = new SensorDefinition(
            SensorId.New(), new SensorSlug("bravo-node"), "Bravo", null,
            new string('b', 64), "meshcore-req-lpp", TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(8), 2,
            Enabled: true, PublicVisible: true, PublicIndexable: false, null, null, null,
            ["temperature"], "bravo.yaml", []);

        var client = new FakeMeshCoreTelClient(requestStatuses: ["ok", "timeout"]);
        var store = new SqliteLocalTelemetryStore(Options.Create(new LocalTelemetryOptions { DatabasePath = _storePath }));
        await store.InitializeAsync(CancellationToken.None);

        var poller = new SensorTelemetryPoller(
            client,
            new FakeRegistry([alpha, bravo]),
            store,
            Options.Create(new MeshCoreOptions { Mode = MeshCoreConnectionMode.Http }),
            Options.Create(new SensorPollingOptions
            {
                Enabled = true,
                RequestTimeoutMs = 1000,
                LoginPassword = "hello",
                IntervalOverrideSeconds = 3600, // one poll per sensor within the test window
            }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SensorTelemetryPoller>.Instance);

        using var source = new CancellationTokenSource();
        var run = poller.StartAsync(source.Token);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while ((await store.CountPendingAsync(CancellationToken.None) < 1 || client.LoginAttempts.Count < 1) &&
                   DateTime.UtcNow < deadline)
            {
                await Task.Delay(100);
            }
        }
        finally
        {
            source.Cancel();
            await poller.StopAsync(CancellationToken.None);
            await run;
        }

        var pending = await store.ReadPendingAsync(10, CancellationToken.None);
        var pollSnapshot = pending.Single(snapshot => snapshot.PayloadJson.Contains("alpha-node"));
        var payload = JsonDocument.Parse(pollSnapshot.PayloadJson).RootElement;

        Assert.Equal("sensor_poll", payload.GetProperty("type").GetString());
        Assert.Equal("alpha-node", payload.GetProperty("sensor").GetString());
        var readings = payload.GetProperty("readings");
        Assert.Collection(
            readings.EnumerateArray(),
            reading =>
            {
                Assert.Equal("battery_voltage", reading.GetProperty("metric").GetString());
                Assert.Equal(3.96, reading.GetProperty("value").GetDouble(), 2);
                Assert.Equal("V", reading.GetProperty("unit").GetString());
            },
            reading =>
            {
                Assert.Equal("temperature", reading.GetProperty("metric").GetString());
                Assert.Equal(26.5, reading.GetProperty("value").GetDouble(), 1);
                Assert.Equal("°C", reading.GetProperty("unit").GetString());
            });

        // The sensor that did not answer triggered exactly one ANON login bootstrap with the global password.
        Assert.Equal([(new string('b', 8), "hello")], client.LoginAttempts);
        Assert.Equal(2, client.Requests.Count);
        Assert.All(client.Requests, request => Assert.EndsWith("0300", request.PayloadHex));
    }

    [Fact]
    public async Task Poller_PerSensorEmptyLoginPassword_OverridesGlobal()
    {
        var node = new SensorDefinition(
            SensorId.New(), new SensorSlug("nopass-node"), "NoPass", null,
            new string('d', 64), "meshcore-req-lpp", TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(8), 2,
            Enabled: true, PublicVisible: true, PublicIndexable: false, null, null, null,
            ["temperature"], "nopass.yaml", [], LoginPassword: "");

        var client = new FakeMeshCoreTelClient(requestStatuses: ["timeout"]);
        var store = new SqliteLocalTelemetryStore(Options.Create(new LocalTelemetryOptions { DatabasePath = _storePath }));
        await store.InitializeAsync(CancellationToken.None);

        var poller = new SensorTelemetryPoller(
            client,
            new FakeRegistry([node]),
            store,
            Options.Create(new MeshCoreOptions { Mode = MeshCoreConnectionMode.Http }),
            Options.Create(new SensorPollingOptions
            {
                Enabled = true,
                RequestTimeoutMs = 1000,
                LoginPassword = "hello",
                IntervalOverrideSeconds = 3600,
            }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SensorTelemetryPoller>.Instance);

        using var source = new CancellationTokenSource();
        var run = poller.StartAsync(source.Token);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (client.LoginAttempts.Count < 1 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(100);
            }
        }
        finally
        {
            source.Cancel();
            await poller.StopAsync(CancellationToken.None);
            await run;
        }

        // The registry explicitly says "node has no password"; the global one must not be used.
        Assert.Equal([(new string('d', 8), "")], client.LoginAttempts);
    }

    [Fact]
    public async Task Poller_DisabledSensor_IsSkipped()
    {
        var disabled = new SensorDefinition(
            SensorId.New(), new SensorSlug("hidden-node"), "Hidden", null,
            new string('c', 64), "meshcore-req-lpp", TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(8), 2,
            Enabled: false, PublicVisible: true, PublicIndexable: false, null, null, null,
            ["temperature"], "hidden.yaml", []);

        var client = new FakeMeshCoreTelClient(requestStatuses: []);
        var store = new SqliteLocalTelemetryStore(Options.Create(new LocalTelemetryOptions { DatabasePath = _storePath }));
        await store.InitializeAsync(CancellationToken.None);

        var poller = new SensorTelemetryPoller(
            client,
            new FakeRegistry([disabled]),
            store,
            Options.Create(new MeshCoreOptions { Mode = MeshCoreConnectionMode.Http }),
            Options.Create(new SensorPollingOptions { Enabled = true }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SensorTelemetryPoller>.Instance);

        using var source = new CancellationTokenSource(TimeSpan.FromMilliseconds(1500));
        await poller.StartAsync(source.Token);
        try
        {
            await Task.Delay(500);
        }
        finally
        {
            await poller.StopAsync(CancellationToken.None);
        }

        Assert.Empty(client.Requests);
        Assert.Equal(0, await store.CountPendingAsync(CancellationToken.None));
    }

    private sealed class FakeRegistry(IReadOnlyList<SensorDefinition> sensors) : ISensorRegistry
    {
        public Task<IReadOnlyList<SensorDefinition>> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(sensors);
    }

    private sealed class FakeMeshCoreTelClient(string[] requestStatuses) : IMeshCoreTelClient
    {
        private int _requestIndex;
        public List<(string DestinationHex, string PayloadHex)> Requests { get; } = [];
        public List<(string DestinationHex, string Password)> LoginAttempts { get; } = [];

        public Task<JsonDocument> SendAcquisitionRequestAsync(
            string destinationHex, string payloadHex, int timeoutMilliseconds, CancellationToken cancellationToken)
        {
            var status = _requestIndex < requestStatuses.Length ? requestStatuses[_requestIndex++] : "timeout";
            Requests.Add((destinationHex, payloadHex));
            var response = status == "ok"
                ? """{"status":"ok","responseHex":"000000000174018C03670109","rssi":-30.0,"snr":12.0,"elapsedMs":900}"""
                : """{"status":"timeout","responseHex":null,"rssi":null,"snr":null,"elapsedMs":1000}""";
            return Task.FromResult(JsonDocument.Parse(response));
        }

        public Task<JsonDocument> SendAcquisitionLoginAsync(
            string destinationHex, string password, int timeoutMilliseconds, CancellationToken cancellationToken)
        {
            LoginAttempts.Add((destinationHex[..8], password));
            return Task.FromResult(JsonDocument.Parse(
                """{"status":"timeout","responseHex":null,"rssi":null,"snr":null,"elapsedMs":1000}"""));
        }

        public string TransportName => "fake";
        public Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<string> ExecuteCommandAsync(string command, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<JsonDocument> GetStatsAsync(string? series, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<JsonDocument> GetTelemetryAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}

