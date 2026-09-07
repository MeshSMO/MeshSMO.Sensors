using MeshSMO.Sensors.Application.Registry;

namespace MeshSMO.Sensors.UnitTests.Application.Registry;

public sealed class PollingScheduleTests
{
    private static readonly TimeZoneInfo Moscow = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");

    private static PollingSchedule CreateSchedule() => new(
        Moscow,
        [
            new PollingScheduleWindow(new TimeOnly(8, 0), new TimeOnly(12, 0), TimeSpan.FromMinutes(15)),
            new PollingScheduleWindow(new TimeOnly(22, 0), new TimeOnly(6, 0), TimeSpan.FromHours(1)),
        ]);

    [Fact]
    public void ResolveInterval_returns_window_interval_inside_window()
    {
        // 05:30 UTC = 08:30 Moscow, inside the 08:00–12:00 window.
        Assert.Equal(TimeSpan.FromMinutes(15), CreateSchedule().ResolveInterval(Utc(5, 30)));
    }

    [Fact]
    public void ResolveInterval_returns_null_outside_every_window()
    {
        // 12:00 UTC = 15:00 Moscow: after the 08:00–12:00 window, before 22:00.
        Assert.Null(CreateSchedule().ResolveInterval(Utc(12, 0)));
    }

    [Fact]
    public void ResolveInterval_window_start_is_inclusive_and_end_exclusive()
    {
        // 05:00 UTC = 08:00 Moscow (window start); 09:00 UTC = 12:00 Moscow (window end).
        Assert.Equal(TimeSpan.FromMinutes(15), CreateSchedule().ResolveInterval(Utc(5, 0)));
        Assert.Null(CreateSchedule().ResolveInterval(Utc(9, 0)));
    }

    [Fact]
    public void ResolveInterval_supports_window_wrapping_past_midnight()
    {
        // 19:00 UTC = 22:00 Moscow (wrap-window start); 20:30 UTC = 23:30 Moscow;
        // 23:00 UTC the previous day = 02:00 Moscow (past midnight); 03:00 UTC = 06:00 (window end).
        Assert.Equal(TimeSpan.FromHours(1), CreateSchedule().ResolveInterval(Utc(19, 0)));
        Assert.Equal(TimeSpan.FromHours(1), CreateSchedule().ResolveInterval(Utc(20, 30)));
        Assert.Equal(
            TimeSpan.FromHours(1),
            CreateSchedule().ResolveInterval(new DateTimeOffset(2026, 6, 14, 23, 0, 0, TimeSpan.Zero)));
        Assert.Null(CreateSchedule().ResolveInterval(Utc(3, 0)));
    }

    private static DateTimeOffset Utc(int hour, int minute) => new(2026, 6, 15, hour, minute, 0, TimeSpan.Zero);
}
