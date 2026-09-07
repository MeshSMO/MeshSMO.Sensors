namespace MeshSMO.Sensors.Forecasting.Models;

public sealed record ForecastResult(
    ForecastAvailability Availability,
    string? Reason,
    DateTimeOffset GeneratedAt,
    DateTimeOffset? LastObservationAt,
    TimeSpan Step,
    ForecastDiagnostics? Diagnostics,
    IReadOnlyList<ForecastPoint> Points)
{
    public static ForecastResult Unavailable(
        ForecastAvailability availability,
        string reason,
        DateTimeOffset generatedAt,
        DateTimeOffset? lastObservationAt,
        TimeSpan step) =>
        new(availability, reason, generatedAt, lastObservationAt, step, null, []);
}
