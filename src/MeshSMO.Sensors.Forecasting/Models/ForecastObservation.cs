namespace MeshSMO.Sensors.Forecasting.Models;

public sealed record ForecastObservation(DateTimeOffset Timestamp, double Value);
