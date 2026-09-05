namespace MeshSMO.Sensors.Domain.Sensors;

public readonly record struct SensorId
{
    public SensorId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Sensor id cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static SensorId New() => new(Guid.NewGuid());

    public static SensorId Parse(string value) => new(Guid.Parse(value));

    public override string ToString() => Value.ToString("D");
}
