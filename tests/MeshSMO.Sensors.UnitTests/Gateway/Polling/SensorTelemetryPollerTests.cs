using System.Text.Json;
using MeshSMO.Sensors.Application.Registry;
using MeshSMO.Sensors.Domain.Sensors;
using MeshSMO.Sensors.Gateway.LocalStorage;
using MeshSMO.Sensors.Gateway.MeshCore;
using MeshSMO.Sensors.Gateway.Polling;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MeshSMO.Sensors.UnitTests.Gateway.Polling;

public sealed class SensorTelemetryPollerTests : IDisposable
{
    private readonly string _storePath = Path.Combine(Path.GetTempPath(), $"poller-{Guid.NewGuid():N}.db");
    private readonly List<ServiceProvider> _serviceProviders = [];

    public void Dispose()
    {
        foreach (var provider in _serviceProviders)
            provider.Dispose();

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
            SensorId.New(), new("alpha-node"), "Alpha", null,
            new('a', 64), "meshcore-req-lpp", TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(8), 2,
            Enabled: true, PublicVisible: true, PublicIndexable: false, null, null, null,
            ["temperature", "battery_voltage"], "alpha.yaml",
            [new(1, "voltage", "battery_voltage", "Напряжение батареи", "В")]);
        var bravo = new SensorDefinition(
            SensorId.New(), new("bravo-node"), "Bravo", null,
            new('b', 64), "meshcore-req-lpp", TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(8), 2,
            Enabled: true, PublicVisible: true, PublicIndexable: false, null, null, null,
            ["temperature"], "bravo.yaml", []);

        var client = new FakeMeshCoreTelClient(requestStatuses: ["ok", "timeout"]);
        var store = await CreateStoreAsync();

        var poller = new SensorTelemetryPoller(
            client,
            ScopeFactory(new FakeRegistry([alpha, bravo])),
            store,
            Options.Create(new MeshCoreOptions { Mode = MeshCoreConnectionMode.Http }),
            Options.Create(new SensorPollingOptions
            {
                Enabled = true,
                RequestTimeoutMs = 1000,
                LoginPassword = "hello",
                IntervalOverrideSeconds = 3600, // one poll cycle per sensor within the test window
                RetryBackoffMinMs = 10,
                RetryBackoffMaxMs = 50,
            }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SensorTelemetryPoller>.Instance);

        using var source = new CancellationTokenSource();
        var run = poller.StartAsync(source.Token);
        try
        {
            // Alpha answers immediately; bravo times out, bootstraps the ANON
            // login and retries once (request 3).
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while ((client.Requests.Count < 3 || client.LoginAttempts.Count < 1) &&
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
        var pollSnapshot = pending.Single(snapshot => snapshot.PayloadJson.Contains("alpha-node", StringComparison.Ordinal));
        var payload = JsonDocument.Parse(pollSnapshot.PayloadJson).RootElement;

        Assert.Equal("sensor_poll", payload.GetProperty("type").GetString());
        Assert.Equal("alpha-node", payload.GetProperty("sensor").GetString());
        Assert.Equal(1, payload.GetProperty("attemptNumber").GetInt32());
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

        // The sensor that did not answer triggered exactly one ANON login bootstrap
        // with the global password, then one retry that timed out again.
        Assert.Equal([(new('b', 8), "hello")], client.LoginAttempts);
        Assert.Equal(3, client.Requests.Count);
        Assert.All(client.Requests, request => Assert.EndsWith("0300", request.PayloadHex, StringComparison.Ordinal));
        var attemptSnapshots = pending
            .Where(snapshot => snapshot.PayloadJson.Contains("\"poll_attempt\"", StringComparison.Ordinal))
            .ToArray();
        Assert.True(attemptSnapshots.Length == 2, $"expected 2 attempt snapshots, got {attemptSnapshots.Length}");
    }

    [Fact]
    public async Task Poller_PerSensorEmptyLoginPassword_OverridesGlobal()
    {
        var node = new SensorDefinition(
            SensorId.New(), new("nopass-node"), "NoPass", null,
            new('d', 64), "meshcore-req-lpp", TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(8), 2,
            Enabled: true, PublicVisible: true, PublicIndexable: false, null, null, null,
            ["temperature"], "nopass.yaml", [], LoginPassword: "");

        var client = new FakeMeshCoreTelClient(requestStatuses: ["timeout"]);
        var store = await CreateStoreAsync();

        var poller = new SensorTelemetryPoller(
            client,
            ScopeFactory(new FakeRegistry([node])),
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
                await Task.Delay(100);
        }
        finally
        {
            source.Cancel();
            await poller.StopAsync(CancellationToken.None);
            await run;
        }

        // The registry explicitly says "node has no password"; the global one must not be used.
        Assert.Equal([(new('d', 8), "")], client.LoginAttempts);
    }

    [Fact]
    public async Task Poller_RetriesAfterTimeout_AndRecordsEveryAttempt()
    {
        var node = new SensorDefinition(
            SensorId.New(), new("retry-node"), "Retry", null,
            new('e', 64), "meshcore-req-lpp", TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(8), 2,
            Enabled: true, PublicVisible: true, PublicIndexable: false, null, null, null,
            ["temperature"], "retry.yaml", []);

        var client = new FakeMeshCoreTelClient(requestStatuses: ["timeout", "ok"]);
        var store = await CreateStoreAsync();

        var poller = new SensorTelemetryPoller(
            client,
            ScopeFactory(new FakeRegistry([node])),
            store,
            Options.Create(new MeshCoreOptions { Mode = MeshCoreConnectionMode.Http }),
            Options.Create(new SensorPollingOptions
            {
                Enabled = true,
                LoginPassword = "hello",
                IntervalOverrideSeconds = 3600,
                RetryBackoffMinMs = 10,
                RetryBackoffMaxMs = 50,
            }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SensorTelemetryPoller>.Instance);

        using var source = new CancellationTokenSource();
        var run = poller.StartAsync(source.Token);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (await store.CountPendingAsync(CancellationToken.None) < 2 && DateTime.UtcNow < deadline)
                await Task.Delay(50);
        }
        finally
        {
            source.Cancel();
            await poller.StopAsync(CancellationToken.None);
            await run;
        }

        Assert.Equal(2, client.Requests.Count);
        var pending = await store.ReadPendingAsync(10, CancellationToken.None);
        var attemptSnapshot = pending.Single(snapshot => snapshot.PayloadJson.Contains("\"poll_attempt\"", StringComparison.Ordinal));
        var attemptPayload = JsonDocument.Parse(attemptSnapshot.PayloadJson).RootElement;
        Assert.Equal("TimedOut", attemptPayload.GetProperty("status").GetString());
        Assert.Equal(1, attemptPayload.GetProperty("attemptNumber").GetInt32());

        var pollSnapshot = pending.Single(snapshot => snapshot.PayloadJson.Contains("\"sensor_poll\"", StringComparison.Ordinal));
        var pollPayload = JsonDocument.Parse(pollSnapshot.PayloadJson).RootElement;
        Assert.Equal(2, pollPayload.GetProperty("attemptNumber").GetInt32());
        Assert.NotEqual(
            attemptPayload.GetProperty("requestId").GetInt64(),
            pollPayload.GetProperty("requestId").GetInt64());
    }

    [Fact]
    public async Task Poller_ExhaustsRetries_LeavesOnlyAttemptRecords()
    {
        var node = new SensorDefinition(
            SensorId.New(), new("quiet-node"), "Quiet", null,
            new('f', 64), "meshcore-req-lpp", TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(8), 2,
            Enabled: true, PublicVisible: true, PublicIndexable: false, null, null, null,
            ["temperature"], "quiet.yaml", []);

        var client = new FakeMeshCoreTelClient(requestStatuses: ["timeout"]);
        var store = await CreateStoreAsync();

        var poller = new SensorTelemetryPoller(
            client,
            ScopeFactory(new FakeRegistry([node])),
            store,
            Options.Create(new MeshCoreOptions { Mode = MeshCoreConnectionMode.Http }),
            Options.Create(new SensorPollingOptions
            {
                Enabled = true,
                IntervalOverrideSeconds = 3600,
                RetryBackoffMinMs = 10,
                RetryBackoffMaxMs = 50,
            }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SensorTelemetryPoller>.Instance);

        using var source = new CancellationTokenSource();
        var run = poller.StartAsync(source.Token);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (await store.CountPendingAsync(CancellationToken.None) < 2 && DateTime.UtcNow < deadline)
                await Task.Delay(50);
        }
        finally
        {
            source.Cancel();
            await poller.StopAsync(CancellationToken.None);
            await run;
        }

        Assert.Equal(2, client.Requests.Count);
        var pending = await store.ReadPendingAsync(10, CancellationToken.None);
        Assert.Equal(2, pending.Count);
        Assert.All(pending, snapshot => Assert.Contains("\"poll_attempt\"", snapshot.PayloadJson, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Poller_UndecodableBody_IsRecordedWithoutRetry()
    {
        var node = new SensorDefinition(
            SensorId.New(), new("garbled-node"), "Garbled", null,
            new('1', 64), "meshcore-req-lpp", TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(8), 2,
            Enabled: true, PublicVisible: true, PublicIndexable: false, null, null, null,
            ["temperature"], "garbled.yaml", []);

        var client = new FakeMeshCoreTelClient(requestStatuses: ["garbage"]);
        var store = await CreateStoreAsync();

        var poller = new SensorTelemetryPoller(
            client,
            ScopeFactory(new FakeRegistry([node])),
            store,
            Options.Create(new MeshCoreOptions { Mode = MeshCoreConnectionMode.Http }),
            Options.Create(new SensorPollingOptions
            {
                Enabled = true,
                IntervalOverrideSeconds = 3600,
                RetryBackoffMinMs = 10,
                RetryBackoffMaxMs = 50,
            }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SensorTelemetryPoller>.Instance);

        using var source = new CancellationTokenSource();
        var run = poller.StartAsync(source.Token);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (await store.CountPendingAsync(CancellationToken.None) < 1 && DateTime.UtcNow < deadline)
                await Task.Delay(50);

            await Task.Delay(300); // give a (wrongful) retry a chance to surface
        }
        finally
        {
            source.Cancel();
            await poller.StopAsync(CancellationToken.None);
            await run;
        }

        Assert.Single(client.Requests);
        var pending = await store.ReadPendingAsync(10, CancellationToken.None);
        var snapshot = Assert.Single(pending);
        var payload = JsonDocument.Parse(snapshot.PayloadJson).RootElement;
        Assert.Equal("poll_attempt", payload.GetProperty("type").GetString());
        Assert.Equal("undecodable_response", payload.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Poller_DisabledSensor_IsSkipped()
    {
        var disabled = new SensorDefinition(
            SensorId.New(), new("hidden-node"), "Hidden", null,
            new('c', 64), "meshcore-req-lpp", TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(8), 2,
            Enabled: false, PublicVisible: true, PublicIndexable: false, null, null, null,
            ["temperature"], "hidden.yaml", []);

        var client = new FakeMeshCoreTelClient(requestStatuses: []);
        var store = await CreateStoreAsync();

        var poller = new SensorTelemetryPoller(
            client,
            ScopeFactory(new FakeRegistry([disabled])),
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

    [Fact]
    public async Task Poller_WithRecentPersistedPoll_DoesNotPollImmediatelyAfterRestart()
    {
        var node = new SensorDefinition(
            SensorId.New(), new("restart-node"), "Restart", null,
            new('2', 64), "meshcore-req-lpp", TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(8), 1,
            Enabled: true, PublicVisible: true, PublicIndexable: false, null, null, null,
            ["temperature"], "restart.yaml", []);
        var client = new FakeMeshCoreTelClient(requestStatuses: ["ok"]);
        var store = await CreateStoreAsync();
        await store.RecordPollStartedAsync("restart-node", DateTimeOffset.UtcNow, CancellationToken.None);
        var poller = new SensorTelemetryPoller(
            client,
            ScopeFactory(new FakeRegistry([node])),
            store,
            Options.Create(new MeshCoreOptions { Mode = MeshCoreConnectionMode.Http }),
            Options.Create(new SensorPollingOptions { Enabled = true }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SensorTelemetryPoller>.Instance);

        using var source = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await poller.StartAsync(source.Token);
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
        }
        finally
        {
            await poller.StopAsync(CancellationToken.None);
        }

        Assert.Empty(client.Requests);
        Assert.Equal(0, await store.CountPendingAsync(CancellationToken.None));
    }

    [Fact]
    public void EffectivePollInterval_uses_schedule_window_and_falls_back_to_base_interval()
    {
        var schedule = new PollingSchedule(
            TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow"),
            [new PollingScheduleWindow(new TimeOnly(8, 0), new TimeOnly(12, 0), TimeSpan.FromMinutes(15))]);
        var sensor = new SensorDefinition(
            SensorId.New(), new("scheduled-node"), "Scheduled", null,
            new('5', 64), "meshcore-req-lpp", TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(8), 2,
            Enabled: true, PublicVisible: true, PublicIndexable: false, null, null, null,
            ["temperature"], "scheduled.yaml", [], PollingSchedule: schedule);

        // 05:30 UTC = 08:30 in Moscow: inside the window; 09:30 UTC = 12:30: outside.
        var morning = new DateTimeOffset(2026, 6, 15, 5, 30, 0, TimeSpan.Zero);
        var afternoon = new DateTimeOffset(2026, 6, 15, 9, 30, 0, TimeSpan.Zero);

        Assert.Equal(
            TimeSpan.FromMinutes(15),
            SensorTelemetryPoller.EffectivePollInterval(sensor, new SensorPollingOptions(), morning));
        Assert.Equal(
            TimeSpan.FromMinutes(5),
            SensorTelemetryPoller.EffectivePollInterval(sensor, new SensorPollingOptions(), afternoon));
    }

    [Fact]
    public void EffectivePollInterval_interval_override_still_shortens_scheduled_interval()
    {
        var schedule = new PollingSchedule(
            TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow"),
            [new PollingScheduleWindow(new TimeOnly(8, 0), new TimeOnly(12, 0), TimeSpan.FromMinutes(15))]);
        var sensor = new SensorDefinition(
            SensorId.New(), new("overridden-node"), "Overridden", null,
            new('6', 64), "meshcore-req-lpp", TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(8), 2,
            Enabled: true, PublicVisible: true, PublicIndexable: false, null, null, null,
            ["temperature"], "overridden.yaml", [], PollingSchedule: schedule);
        var options = new SensorPollingOptions { IntervalOverrideSeconds = 300 };

        // Inside the window the override shortens 15m to 5m; outside it never
        // lengthens the base 5m interval.
        var morning = new DateTimeOffset(2026, 6, 15, 5, 30, 0, TimeSpan.Zero);
        var afternoon = new DateTimeOffset(2026, 6, 15, 9, 30, 0, TimeSpan.Zero);

        Assert.Equal(TimeSpan.FromMinutes(5), SensorTelemetryPoller.EffectivePollInterval(sensor, options, morning));
        Assert.Equal(TimeSpan.FromMinutes(5), SensorTelemetryPoller.EffectivePollInterval(sensor, options, afternoon));
    }

    private async Task<ILocalTelemetryStore> CreateStoreAsync()
    {
        var provider = new ServiceCollection()
            .AddDbContextFactory<LocalOutboxDbContext>(options => options.UseSqlite($"Data Source={_storePath}"))
            .BuildServiceProvider();
        _serviceProviders.Add(provider);
        var factory = provider.GetRequiredService<IDbContextFactory<LocalOutboxDbContext>>();
        await LocalOutboxDatabase.MigrateAsync(factory, _storePath, CancellationToken.None).ConfigureAwait(false);
        return new LocalTelemetryStore(factory);
    }

    private static IServiceScopeFactory ScopeFactory(ISensorRegistry registry)
    {
        var services = new ServiceCollection();
        services.AddSingleton(registry);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
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
            var response = status switch
            {
                "ok" => """{"status":"ok","responseHex":"000000000174018C03670109","rssi":-30.0,"snr":12.0,"elapsedMs":900}""",
                "garbage" => """{"status":"ok","responseHex":"00000000","rssi":-30.0,"snr":12.0,"elapsedMs":50}""",
                _ => """{"status":"timeout","responseHex":null,"rssi":null,"snr":null,"elapsedMs":1000}""",
            };
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
