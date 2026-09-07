namespace MeshSMO.Sensors.Forecasting.Evaluation;

public sealed record ForecastMetricValues(
    double Mae,
    double Rmse,
    double IntervalCoverage,
    int Count);
