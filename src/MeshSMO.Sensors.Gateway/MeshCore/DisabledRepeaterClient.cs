using System.Text.Json;

namespace MeshSMO.Sensors.Gateway.MeshCore;

public sealed class DisabledRepeaterClient : IRepeaterClient
{
    public string TransportName => "disabled";

    public Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<string> ExecuteCommandAsync(string command, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Repeater communication is disabled.");

    public Task<JsonDocument> GetTelemetryAsync(CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Repeater communication is disabled.");
}
