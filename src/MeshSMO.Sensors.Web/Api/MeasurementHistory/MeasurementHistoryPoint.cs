namespace MeshSMO.Sensors.Web.Api.MeasurementHistory;

public sealed record MeasurementHistoryPoint(DateTimeOffset Timestamp, double Min, double Avg, double Max, int Count);
