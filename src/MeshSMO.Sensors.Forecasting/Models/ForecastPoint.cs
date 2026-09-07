namespace MeshSMO.Sensors.Forecasting.Models;

public sealed record ForecastPoint(
    DateTimeOffset Timestamp,
    double Predicted,
    double Lower,
    double Upper);
