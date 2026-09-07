namespace MeshSMO.Sensors.Forecasting.Models;

public sealed record ForecastSeriesQuery(
    Guid SensorId,
    string MetricKey,
    string? Unit,
    TimeSpan PollInterval,
    DateTimeOffset From,
    DateTimeOffset To,
    TimeSpan Step,
    double? Minimum,
    double? Maximum,
    double? MaximumMae);
