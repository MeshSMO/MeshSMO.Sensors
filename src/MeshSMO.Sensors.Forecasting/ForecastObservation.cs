namespace MeshSMO.Sensors.Forecasting;

public sealed record ForecastObservation(DateTimeOffset Timestamp, double Value);
