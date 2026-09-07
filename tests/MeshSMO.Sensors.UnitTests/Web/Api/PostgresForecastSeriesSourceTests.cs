using MeshSMO.Sensors.Domain.Measurements;
using MeshSMO.Sensors.Domain.Sensors;
using MeshSMO.Sensors.Forecasting.Models;
using MeshSMO.Sensors.Infrastructure.Persistence;
using MeshSMO.Sensors.Web.Api.Forecasting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MeshSMO.Sensors.UnitTests.Web.Api;

public sealed class PostgresForecastSeriesSourceTests : IDisposable
{
    private static readonly DateTimeOffset From = new(2026, 9, 7, 10, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly SensorsDbContext _dbContext;

    public PostgresForecastSeriesSourceTests()
    {
        _connection.Open();
        var options = new DbContextOptionsBuilder<SensorsDbContext>()
            .UseSqlite(_connection)
            .Options;
        _dbContext = new(options);
        _dbContext.Database.EnsureCreated();
    }

    [Fact]
    public async Task ReadAsync_BucketsOnlyRequestedSensorAndMetric()
    {
        var sensor = CreateSensor("source-one", "source-key-one");
        var other = CreateSensor("source-two", "source-key-two");
        _dbContext.Sensors.AddRange(sensor, other);
        var firstSample = Sample(sensor.Id, 1, From.AddMinutes(1));
        var secondSample = Sample(sensor.Id, 2, From.AddMinutes(3));
        var otherSample = Sample(other.Id, 3, From.AddMinutes(2));
        _dbContext.MeasurementSamples.AddRange(firstSample, secondSample, otherSample);
        _dbContext.MeasurementValues.AddRange(
            Value(firstSample.Id, sensor.Id, "temperature", 20, From.AddMinutes(1)),
            Value(secondSample.Id, sensor.Id, "temperature", 26, From.AddMinutes(3)),
            Value(secondSample.Id, sensor.Id, "humidity", 70, From.AddMinutes(3)),
            Value(otherSample.Id, other.Id, "temperature", 99, From.AddMinutes(2)));
        await _dbContext.SaveChangesAsync();
        var query = new ForecastSeriesQuery(
            sensor.Id.Value,
            "temperature",
            "°C",
            TimeSpan.FromMinutes(5),
            From,
            From.AddHours(1),
            TimeSpan.FromMinutes(5),
            null,
            null,
            null);

        var result = await new PostgresForecastSeriesSource(_dbContext).ReadAsync(query);

        Assert.Equal(From.AddMinutes(3), result.LastObservationAt);
        var observation = Assert.Single(result.Observations);
        Assert.Equal(From, observation.Timestamp);
        Assert.Equal(23, observation.Value);
    }

    private static Sensor CreateSensor(string slug, string publicKey) => new(
        new(Guid.NewGuid()),
        new(slug),
        slug,
        null,
        publicKey,
        "meshcore-req-lpp",
        TimeSpan.FromMinutes(5),
        TimeSpan.FromSeconds(30),
        2,
        true,
        true,
        false,
        null,
        null,
        null,
        ["temperature", "humidity"],
        From);

    private static MeasurementSample Sample(SensorId sensorId, long requestId, DateTimeOffset timestamp) =>
        new(Guid.NewGuid(), sensorId, requestId, timestamp, "meshcore-req-lpp");

    private static MeasurementValue Value(
        Guid sampleId,
        SensorId sensorId,
        string metric,
        double value,
        DateTimeOffset timestamp) => new(sampleId, sensorId, metric, timestamp)
        {
            NumericValue = value,
        };

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }
}
