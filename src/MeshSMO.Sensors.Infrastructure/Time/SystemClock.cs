using MeshSMO.Sensors.Application.Abstractions;

namespace MeshSMO.Sensors.Infrastructure.Time;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
