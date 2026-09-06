namespace MeshSMO.Sensors.Domain.Sensors;

public sealed class SensorMetric
{
    private SensorMetric()
    {
    }

    public SensorMetric(SensorId sensorId, string metricKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metricKey);
        SensorId = sensorId;
        MetricKey = metricKey;
    }

    public SensorId SensorId { get; private set; }
    public string MetricKey { get; private set; } = string.Empty;

    /// <summary>Human-readable label from the registry (e.g. "Напряжение солнечной панели").</summary>
    public string? DisplayName { get; set; }

    /// <summary>Unit of measurement configured in the registry (e.g. "В", "°C").</summary>
    public string? Unit { get; set; }
}
