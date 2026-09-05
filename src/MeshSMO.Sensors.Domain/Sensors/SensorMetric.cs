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
}
