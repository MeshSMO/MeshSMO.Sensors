using System.Text.Json;

namespace MeshSMO.Sensors.Gateway.MeshCore;

public interface IMeshCoreTelClient : IRepeaterClient
{
    Task<JsonDocument> GetStatsAsync(string? series, CancellationToken cancellationToken);

    /// <summary>
    /// Sends a raw acquisition request to a MeshCore node via POST /api/request
    /// and returns the device JSON response {status, responseHex, rssi, snr, elapsedMs}.
    /// </summary>
    Task<JsonDocument> SendAcquisitionRequestAsync(
        string destinationHex,
        string payloadHex,
        int timeoutMilliseconds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Performs the anonymous login bootstrap (POST /api/login) that makes the
    /// target node accept subsequent requests from this repeater.
    /// </summary>
    Task<JsonDocument> SendAcquisitionLoginAsync(
        string destinationHex,
        string password,
        int timeoutMilliseconds,
        CancellationToken cancellationToken);
}
