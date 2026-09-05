using System.Text.Json;

namespace MeshSMO.Sensors.Gateway.MeshCore;

public interface IRepeaterClient
{
    string TransportName { get; }

    Task ConnectAsync(CancellationToken cancellationToken);

    Task DisconnectAsync(CancellationToken cancellationToken);

    Task<string> ExecuteCommandAsync(string command, CancellationToken cancellationToken);

    Task<JsonDocument> GetTelemetryAsync(CancellationToken cancellationToken);
}
