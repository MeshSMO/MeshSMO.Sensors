using System.Text.Json;
using MeshSMO.Sensors.Gateway;
using MeshSMO.Sensors.Gateway.LocalStorage;
using MeshSMO.Sensors.Gateway.MeshCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MeshSMO.Sensors.UnitTests.Gateway;

public sealed class GatewayWorkerTests
{
    [Fact]
    public async Task ConnectedRepeaterTelemetryIsWrittenToLocalStore()
    {
        var repeater = new FakeRepeaterClient();
        var store = new RecordingTelemetryStore();
        var options = Options.Create(new MeshCoreOptions
        {
            Mode = MeshCoreConnectionMode.Http,
            TelemetryCollectionIntervalSeconds = 60,
        });
        using var worker = new Worker(repeater, store, options, NullLogger<Worker>.Instance);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await worker.StartAsync(cancellation.Token);
        var appended = await store.Appended.Task.WaitAsync(cancellation.Token);
        await worker.StopAsync(CancellationToken.None);

        Assert.True(repeater.WasConnected);
        Assert.False(repeater.Connected);
        Assert.Equal("http", appended.Transport);
        using var payload = JsonDocument.Parse(appended.PayloadJson);
        Assert.Equal(22.25, payload.RootElement.GetProperty("sensors").GetProperty("temperature").GetDouble());
    }

    private sealed class FakeRepeaterClient : IRepeaterClient
    {
        public string TransportName => "http";

        public bool Connected { get; private set; }

        public bool WasConnected { get; private set; }

        public Task ConnectAsync(CancellationToken cancellationToken)
        {
            Connected = true;
            WasConnected = true;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            Connected = false;
            return Task.CompletedTask;
        }

        public Task<string> ExecuteCommandAsync(string command, CancellationToken cancellationToken) =>
            Task.FromResult("MeshCoreTel test firmware");

        public Task<JsonDocument> GetTelemetryAsync(CancellationToken cancellationToken) =>
            Task.FromResult(JsonDocument.Parse("{\"sensors\":{\"temperature\":22.25}}"));
    }

    private sealed class RecordingTelemetryStore : ILocalTelemetryStore
    {
        public TaskCompletionSource<AppendedSnapshot> Appended { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<long> AppendAsync(
            DateTimeOffset capturedAt,
            string transport,
            string payloadJson,
            CancellationToken cancellationToken)
        {
            Appended.TrySetResult(new(transport, payloadJson));
            return Task.FromResult(1L);
        }

        public Task<IReadOnlyList<PendingTelemetrySnapshot>> ReadPendingAsync(
            int maximumCount,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PendingTelemetrySnapshot>>([]);

        public Task AcknowledgeAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<long> CountPendingAsync(CancellationToken cancellationToken) => Task.FromResult(0L);
    }

    private sealed record AppendedSnapshot(string Transport, string PayloadJson);
}
