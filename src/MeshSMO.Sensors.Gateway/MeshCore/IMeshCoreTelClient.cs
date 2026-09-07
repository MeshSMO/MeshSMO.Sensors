using System.Text.Json;

namespace MeshSMO.Sensors.Gateway.MeshCore;

public interface IMeshCoreTelClient : IRepeaterClient, IMeshNodeClient
{
    Task<JsonDocument> GetStatsAsync(string? series, CancellationToken cancellationToken);
}
