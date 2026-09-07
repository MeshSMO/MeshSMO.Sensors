using System.Text.Json;

namespace MeshSMO.Sensors.Gateway.MeshCore;

/// <summary>
/// Acquisition surface shared by both poll channels: the MeshCoreTel repeater
/// (HTTP panel API) and a stock companion radio (companion frame protocol over
/// TCP). Both return the repeater document shape {status, responseHex?, rssi?,
/// snr?, elapsedMs} so the polling pipeline stays channel-agnostic; the
/// companion channel reports no rssi/snr (not available in companion frames).
/// </summary>
public interface IMeshNodeClient
{
    string TransportName { get; }

    /// <summary>
    /// Sends a raw acquisition request to a MeshCore node; the payload is
    /// timestamp(4 LE) + request body. Returns the device response document.
    /// </summary>
    Task<JsonDocument> SendAcquisitionRequestAsync(
        string destinationHex,
        string payloadHex,
        int timeoutMilliseconds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Performs the anonymous login bootstrap that makes the target node accept
    /// subsequent requests from this channel.
    /// </summary>
    Task<JsonDocument> SendAcquisitionLoginAsync(
        string destinationHex,
        string password,
        int timeoutMilliseconds,
        CancellationToken cancellationToken);
}
