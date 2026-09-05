namespace MeshSMO.Sensors.Domain.Sensors;

public sealed class SensorStatusSnapshot
{
    private SensorStatusSnapshot()
    {
    }

    public SensorStatusSnapshot(SensorId sensorId, DateTimeOffset updatedAt)
    {
        SensorId = sensorId;
        State = SensorState.Unknown;
        UpdatedAt = updatedAt;
    }

    public SensorId SensorId { get; private set; }
    public SensorState State { get; set; }
    public DateTimeOffset? LastPollAt { get; set; }
    public DateTimeOffset? LastSuccessAt { get; set; }
    public int ConsecutiveFailures { get; set; }
    public float? LastRssi { get; set; }
    public float? LastSnr { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
