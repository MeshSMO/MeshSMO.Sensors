namespace MeshSMO.Sensors.Domain.Sensors;

public static class KnownMetrics
{
    public static readonly MetricDefinition Temperature = new("temperature", "Температура", "°C");
    public static readonly MetricDefinition Humidity = new("humidity", "Влажность", "%");
    public static readonly MetricDefinition Pressure = new("pressure", "Давление", "hPa");
    public static readonly MetricDefinition Battery = new("battery", "Заряд батареи", "%");

    public static IReadOnlyDictionary<string, MetricDefinition> All { get; } =
        new[] { Temperature, Humidity, Pressure, Battery }
            .ToDictionary(metric => metric.Key, StringComparer.Ordinal);
}
