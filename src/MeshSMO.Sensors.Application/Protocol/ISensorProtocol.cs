using MeshSMO.Sensors.Domain.Sensors;

namespace MeshSMO.Sensors.Application.Protocol;

public readonly record struct PollRequestId(uint Value);

public sealed record SensorMetricValue(string Key, double? NumericValue, string? TextValue, string? Unit);

public sealed record SensorResponse(
    PollRequestId RequestId,
    DateTimeOffset? MeasuredAt,
    IReadOnlyList<SensorMetricValue> Values);

public interface ISensorProtocol
{
    string ProtocolId { get; }

    ReadOnlyMemory<byte> BuildPollRequest(Sensor sensor, PollRequestId requestId);

    SensorResponse ParseResponse(Sensor sensor, ReadOnlySpan<byte> payload);
}
