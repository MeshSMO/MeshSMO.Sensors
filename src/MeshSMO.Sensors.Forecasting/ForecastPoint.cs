namespace MeshSMO.Sensors.Forecasting;

public sealed record ForecastPoint(
    DateTimeOffset Timestamp,
    double Predicted,
    double Lower,
    double Upper);
