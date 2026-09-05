using MeshSMO.Sensors.Domain.Sensors;

namespace MeshSMO.Sensors.Domain.Polling;

public enum PollAttemptStatus
{
    Started,
    Succeeded,
    TimedOut,
    Failed,
    Cancelled,
}

public sealed class PollAttempt
{
    private PollAttempt()
    {
    }

    public PollAttempt(Guid id, SensorId sensorId, long requestId, DateTimeOffset startedAt, int attemptNumber)
    {
        Id = id;
        SensorId = sensorId;
        RequestId = requestId;
        StartedAt = startedAt;
        AttemptNumber = attemptNumber;
        Status = PollAttemptStatus.Started;
    }

    public Guid Id { get; private set; }
    public SensorId SensorId { get; private set; }
    public long RequestId { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int AttemptNumber { get; private set; }
    public PollAttemptStatus Status { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public int? RoundTripMilliseconds { get; set; }
}
