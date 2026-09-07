namespace MeshSMO.Sensors.Domain.Polling;

public enum PollAttemptStatus
{
    Started,
    Succeeded,
    TimedOut,
    Failed,
    Cancelled,
}
