using MeshSMO.Sensors.Domain.Sensors;

namespace MeshSMO.Sensors.Domain.Measurements;

public sealed class MeasurementValue
{
    private MeasurementValue()
    {
    }

    public MeasurementValue(Guid sampleId, SensorId sensorId, string metricKey, DateTimeOffset timestamp)
    {
        SampleId = sampleId;
        SensorId = sensorId;
        MetricKey = metricKey;
        Timestamp = timestamp;
    }

    public Guid SampleId { get; private set; }
    public SensorId SensorId { get; private set; }
    public string MetricKey { get; private set; } = string.Empty;
    public DateTimeOffset Timestamp { get; private set; }
    public double? NumericValue { get; set; }
    public string? TextValue { get; set; }
    public string? Unit { get; set; }
    public string? Quality { get; set; }
}
