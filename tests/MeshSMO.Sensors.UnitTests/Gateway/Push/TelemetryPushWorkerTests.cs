using MeshSMO.Sensors.Gateway.LocalStorage;
using MeshSMO.Sensors.Gateway.Push;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MeshSMO.Sensors.UnitTests.Gateway.Push;

public sealed class TelemetryPushWorkerTests
{
    [Fact]
    public async Task PendingSnapshotsArePushedAndAcknowledged()
    {
        var store = new FakeTelemetryStore(
        [
            new PendingTelemetrySnapshot(9, DateTimeOffset.UnixEpoch, "Serial", """{"type":"sensor_poll"}""", []),
        ]);
        string? apiKey = null;
        string? body = null;
        var handler = new StubHttpMessageHandler(async request =>
        {
            apiKey = request.Headers.TryGetValues("X-Api-Key", out var values) ? values.SingleOrDefault() : null;
            body = await request.Content!.ReadAsStringAsync().ConfigureAwait(false);
            return (System.Net.HttpStatusCode.OK, """{"accepted":1}""");
        });
        var client = CreateClient(handler, "secret", intervalSeconds: 1);
        using var worker = new TelemetryPushWorker(
            store,
            client,
            Options.Create(new TelemetryPushOptions
            {
                ApiUrl = new Uri("http://localhost/"),
                ApiKey = "secret",
                BatchSize = 50,
                IntervalSeconds = 1,
            }),
            NullLogger<TelemetryPushWorker>.Instance);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await worker.StartAsync(cancellation.Token);
        var acknowledged = await store.Acknowledged.Task.WaitAsync(cancellation.Token);
        await worker.StopAsync(CancellationToken.None);

        Assert.True(store.ReadPendingCalled);
        Assert.Equal([9L], acknowledged);
        Assert.Equal("secret", apiKey);
        using var json = System.Text.Json.JsonDocument.Parse(body!);
        Assert.Equal(1, json.RootElement.GetProperty("pendingCount").GetInt64());
        Assert.Equal(9, json.RootElement.GetProperty("snapshots").EnumerateArray().Single()
            .GetProperty("id").GetInt64());
    }

    [Fact]
    public async Task FailedPushKeepsSnapshotsPending()
    {
        var store = new FakeTelemetryStore(
        [
            new PendingTelemetrySnapshot(9, DateTimeOffset.UnixEpoch, "Serial", """{"type":"sensor_poll"}""", []),
        ]);
        var pushed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHttpMessageHandler(_ =>
        {
            pushed.TrySetResult();
            return (System.Net.HttpStatusCode.InternalServerError, "{}");
        });
        var client = CreateClient(handler, "secret", intervalSeconds: 3600);
        using var worker = new TelemetryPushWorker(
            store,
            client,
            Options.Create(new TelemetryPushOptions
            {
                ApiUrl = new Uri("http://localhost/"),
                ApiKey = "secret",
                BatchSize = 50,
                IntervalSeconds = 3600,
            }),
            NullLogger<TelemetryPushWorker>.Instance);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await worker.StartAsync(cancellation.Token);
        await pushed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        Assert.False(store.Acknowledged.Task.IsCompleted);
    }

    [Fact]
    public async Task EmptyOutboxSendsNoRequests()
    {
        var store = new FakeTelemetryStore([]);
        var handler = new StubHttpMessageHandler(_ => (System.Net.HttpStatusCode.OK, """{"accepted":0}"""));
        var client = CreateClient(handler, "secret", intervalSeconds: 1);
        using var worker = new TelemetryPushWorker(
            store,
            client,
            Options.Create(new TelemetryPushOptions
            {
                ApiUrl = new Uri("http://localhost/"),
                ApiKey = "secret",
                IntervalSeconds = 1,
            }),
            NullLogger<TelemetryPushWorker>.Instance);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await worker.StartAsync(cancellation.Token);
        await Task.Delay(300);
        await worker.StopAsync(CancellationToken.None);

        Assert.True(store.ReadPendingCalled);
        Assert.False(store.Acknowledged.Task.IsCompleted);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task DisabledWhenApiUrlIsNotConfigured()
    {
        var store = new FakeTelemetryStore([]);
        var handler = new StubHttpMessageHandler(_ => (System.Net.HttpStatusCode.OK, """{"accepted":0}"""));
        var client = CreateClient(handler, "secret", intervalSeconds: 1);
        using var worker = new TelemetryPushWorker(
            store,
            client,
            Options.Create(new TelemetryPushOptions()),
            NullLogger<TelemetryPushWorker>.Instance);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await worker.StartAsync(cancellation.Token);
        await worker.StopAsync(CancellationToken.None);

        Assert.False(store.ReadPendingCalled);
        Assert.Equal(0, handler.RequestCount);
    }

    private static TelemetryPushClient CreateClient(StubHttpMessageHandler handler, string apiKey, int intervalSeconds) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") },
            Options.Create(new TelemetryPushOptions { ApiUrl = new Uri("http://localhost/"), ApiKey = apiKey }));

    private sealed class FakeTelemetryStore(IReadOnlyList<PendingTelemetrySnapshot> pending) : ILocalTelemetryStore
    {
        public bool ReadPendingCalled { get; private set; }

        public TaskCompletionSource<IReadOnlyList<long>> Acknowledged { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<long> AppendAsync(
            DateTimeOffset capturedAt,
            string transport,
            string payloadJson,
            CancellationToken cancellationToken) => Task.FromResult(0L);

        public Task<IReadOnlyList<PendingTelemetrySnapshot>> ReadPendingAsync(
            int maximumCount,
            CancellationToken cancellationToken)
        {
            ReadPendingCalled = true;
            return Task.FromResult(pending);
        }

        public Task AcknowledgeAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken)
        {
            Acknowledged.TrySetResult(ids.ToArray());
            return Task.CompletedTask;
        }

        public Task<long> CountPendingAsync(CancellationToken cancellationToken) =>
            Task.FromResult((long)pending.Count);
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, Task<(System.Net.HttpStatusCode, string)>> responder) : HttpMessageHandler
    {
        public StubHttpMessageHandler(Func<HttpRequestMessage, (System.Net.HttpStatusCode, string)> responder)
            : this(request => Task.FromResult(responder(request)))
        {
        }

        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            var (statusCode, content) = await responder(request).ConfigureAwait(false);
            return new HttpResponseMessage(statusCode) { Content = new StringContent(content) };
        }
    }
}
