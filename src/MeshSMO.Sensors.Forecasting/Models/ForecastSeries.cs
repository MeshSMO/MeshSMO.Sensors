namespace MeshSMO.Sensors.Forecasting.Models;

public sealed record ForecastSeries(
    Guid SensorId,
    string MetricKey,
    string? Unit,
    TimeSpan PollInterval,
    DateTimeOffset? LastObservationAt,
    IReadOnlyList<ForecastObservation> Observations,
    double? Minimum = null,
    double? Maximum = null,
    double? MaximumMae = null);
