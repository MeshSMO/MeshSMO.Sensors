namespace MeshSMO.Sensors.Forecasting;

public sealed record PreparedSeries(
    ForecastAvailability Availability,
    string? Reason,
    IReadOnlyList<float> Values,
    DateTimeOffset? TrainingFrom,
    DateTimeOffset? TrainingTo,
    DateTimeOffset ForecastFrom,
    int ForecastOffsetSteps,
    double ObservedCoverage,
    int InterpolatedPoints)
{
    public bool IsReady => Availability == ForecastAvailability.Ready;
}
