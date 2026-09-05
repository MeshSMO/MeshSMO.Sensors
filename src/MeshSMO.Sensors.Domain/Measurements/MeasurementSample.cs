using MeshSMO.Sensors.Domain.Sensors;

namespace MeshSMO.Sensors.Domain.Measurements;

public sealed class MeasurementSample
{
    private MeasurementSample()
    {
    }

    public MeasurementSample(Guid id, SensorId sensorId, long? requestId, DateTimeOffset receivedAt, string protocolId)
    {
        Id = id == Guid.Empty ? throw new ArgumentException("Sample id cannot be empty.", nameof(id)) : id;
        SensorId = sensorId;
        RequestId = requestId;
        ReceivedAt = receivedAt;
        ProtocolId = string.IsNullOrWhiteSpace(protocolId)
            ? throw new ArgumentException("Protocol id is required.", nameof(protocolId))
            : protocolId;
    }

    public Guid Id { get; private set; }
    public SensorId SensorId { get; private set; }
    public long? RequestId { get; private set; }
    public DateTimeOffset? MeasuredAt { get; set; }
    public DateTimeOffset ReceivedAt { get; private set; }
    public float? Rssi { get; set; }
    public float? Snr { get; set; }
    public int? RoundTripMilliseconds { get; set; }
    public string ProtocolId { get; private set; } = string.Empty;
    public byte[]? RawPayload { get; set; }
    public string? Extra { get; set; }
    public ICollection<MeasurementValue> Values { get; } = new List<MeasurementValue>();
}
