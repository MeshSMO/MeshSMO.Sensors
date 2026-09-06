using MeshSMO.Sensors.Domain.Sensors;

namespace MeshSMO.Sensors.UnitTests.Domain;

public sealed class SensorTests
{
    [Fact]
    public void ApplyConfiguration_reconciles_metric_keys()
    {
        var now = DateTimeOffset.Parse("2026-09-05T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var sensor = CreateSensor(["temperature", "humidity"], now);

        sensor.ApplyConfiguration(
            sensor.Slug,
            sensor.DisplayName,
            sensor.Description,
            sensor.MeshPublicKey,
            sensor.ProtocolId,
            TimeSpan.FromMinutes(10),
            TimeSpan.FromSeconds(30),
            2,
            true,
            true,
            false,
            null,
            null,
            null,
            ["temperature", "battery"],
            now.AddMinutes(1));

        Assert.Equal(["battery", "temperature"], sensor.Metrics.Select(metric => metric.MetricKey).Order(StringComparer.Ordinal), StringComparer.Ordinal);
        Assert.Equal(600, sensor.PollIntervalSeconds);
    }

    private static Sensor CreateSensor(string[] metrics, DateTimeOffset now) =>
        new(
            SensorId.New(),
            new("test-sensor"),
            "Test sensor",
            null,
            "public-key",
            "test-v1",
            TimeSpan.FromMinutes(5),
            TimeSpan.FromSeconds(30),
            2,
            true,
            true,
            false,
            null,
            null,
            null,
            metrics,
            now);
}
