namespace MeshSMO.Sensors.Web.Api.Forecasting;

public sealed record ForecastDescriptor(
    Guid SensorId,
    string SensorSlug,
    string MetricKey,
    string? Unit,
    TimeSpan PollInterval,
    double? Minimum,
    double? Maximum,
    double? MaximumMae);
