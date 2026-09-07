namespace MeshSMO.Sensors.Application.Registry;

/// <summary>
/// A half-open local-time window [<c>Start</c>, <c>End</c>) with its own poll
/// interval. A window whose <c>End</c> is not after <c>Start</c> wraps past
/// midnight (for example 22:00–06:00).
/// </summary>
public sealed record PollingScheduleWindow(TimeOnly Start, TimeOnly End, TimeSpan Interval);

/// <summary>
/// Per-sensor time-of-day polling schedule: when the schedule's local time is
/// inside a window, that window's interval replaces the sensor's base poll
/// interval; outside every window the caller falls back to the base interval.
/// </summary>
public sealed record PollingSchedule(TimeZoneInfo TimeZone, IReadOnlyList<PollingScheduleWindow> Windows)
{
    /// <summary>
    /// Poll interval in effect at the given UTC timestamp, or <c>null</c> when
    /// the schedule's local time is outside every window.
    /// </summary>
    public TimeSpan? ResolveInterval(DateTimeOffset timestampUtc)
    {
        var localTime = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(timestampUtc, TimeZone).DateTime);
        foreach (var window in Windows)
        {
            if (Contains(window, localTime))
                return window.Interval;
        }

        return null;
    }

    private static bool Contains(PollingScheduleWindow window, TimeOnly localTime)
    {
        if (window.End > window.Start)
            return localTime >= window.Start && localTime < window.End;

        return localTime >= window.Start || localTime < window.End;
    }
}
