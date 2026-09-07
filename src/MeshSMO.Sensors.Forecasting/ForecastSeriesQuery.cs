namespace MeshSMO.Sensors.Forecasting;

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
