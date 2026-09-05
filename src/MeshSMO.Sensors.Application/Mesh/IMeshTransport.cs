namespace MeshSMO.Sensors.Application.Mesh;

public readonly record struct MeshNodeAddress(string Value);

public sealed record SendResult(bool Accepted, string? ErrorCode = null);

public sealed record MeshInboundPacket(
    MeshNodeAddress Source,
    ReadOnlyMemory<byte> Payload,
    DateTimeOffset ReceivedAt,
    float? Rssi = null,
    float? Snr = null);

public interface IMeshTransport
{
    Task StartAsync(CancellationToken cancellationToken);

    Task<SendResult> SendAsync(
        MeshNodeAddress destination,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken);

    IAsyncEnumerable<MeshInboundPacket> ReadPacketsAsync(CancellationToken cancellationToken);
}
