using MeshSMO.Sensors.Domain.Sensors;
using MeshSMO.Sensors.Infrastructure.Persistence;
using MeshSMO.Sensors.Web.GatewayIngestion;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeshSMO.Sensors.UnitTests.Web.GatewayIngestion;

public sealed class GatewayTelemetryImporterTests : IDisposable
{
    private readonly SensorsDbContext _dbContext;
    private readonly GatewayTelemetryImporter _importer;
    private readonly Sensor _sensor;

    public GatewayTelemetryImporterTests()
    {
        _dbContext = new SensorsDbContext(
            new DbContextOptionsBuilder<SensorsDbContext>()
                .UseInMemoryDatabase($"importer-{Guid.NewGuid():N}")
                .Options);
        _importer = new GatewayTelemetryImporter(_dbContext, NullLogger<GatewayTelemetryImporter>.Instance);
        var now = DateTimeOffset.UtcNow;
        _sensor = new Sensor(
            new SensorId(Guid.NewGuid()),
            new SensorSlug("smolensk-center"),
            "Смоленск — центр",
            null,
            "pub-key-1",
            "meshsmo-weather-v1",
            TimeSpan.FromMinutes(5),
            TimeSpan.FromSeconds(30),
            2,
            enabled: true,
            publicVisible: true,
            publicIndexable: true,
            null,
            null,
            null,
            ["temperature"],
            now);
        _dbContext.Sensors.Add(_sensor);
        _dbContext.SaveChanges();
    }

    [Fact]
    public async Task ImportBatchAsync_CreatesSnapshotSampleValuesAndStatus()
    {
        var imported = await _importer.ImportBatchAsync([Snapshot(1)], CancellationToken.None);

        Assert.Equal(1, imported);
        var snapshot = Assert.Single(_dbContext.GatewayTelemetrySnapshots);
        Assert.Equal(1, snapshot.GatewaySnapshotId);
        var reading = Assert.Single(snapshot.Readings);
        Assert.Equal("sensors.temperature", reading.MetricKey);

        var sample = Assert.Single(_dbContext.MeasurementSamples);
        Assert.Equal(_sensor.Id, sample.SensorId);
        Assert.Equal(1, sample.RequestId);
        Assert.Equal("meshcore-req-lpp", sample.ProtocolId);
        Assert.Equal(-92.5f, sample.Rssi);
        Assert.Equal(new byte[] { 0x00, 0xFF }, sample.RawPayload);
        var value = Assert.Single(sample.Values);
        Assert.Equal("temperature", value.MetricKey);
        Assert.Equal(21.5, value.NumericValue);
        Assert.Equal("°C", value.Unit);

        var status = Assert.Single(_dbContext.SensorStatuses);
        Assert.Equal(_sensor.Id, status.SensorId);
        Assert.Equal(SensorState.Online, status.State);
        Assert.Equal(0, status.ConsecutiveFailures);
    }

    [Fact]
    public async Task ImportBatchAsync_SkipsAlreadyStoredSnapshotIds()
    {
        await _importer.ImportBatchAsync([Snapshot(1)], CancellationToken.None);

        var imported = await _importer.ImportBatchAsync([Snapshot(1)], CancellationToken.None);

        Assert.Equal(0, imported);
        Assert.Single(_dbContext.GatewayTelemetrySnapshots);
        Assert.Single(_dbContext.MeasurementSamples);
    }

    [Fact]
    public async Task ImportBatchAsync_SkipsDuplicateSensorRequestIds()
    {
        await _importer.ImportBatchAsync([Snapshot(1)], CancellationToken.None);

        var imported = await _importer.ImportBatchAsync([Snapshot(2, requestId: 1)], CancellationToken.None);

        Assert.Equal(1, imported);
        Assert.Equal(2, _dbContext.GatewayTelemetrySnapshots.Count());
        Assert.Single(_dbContext.MeasurementSamples);
    }

    [Fact]
    public async Task ImportBatchAsync_UnknownSlugStoresSnapshotOnly()
    {
        var imported = await _importer.ImportBatchAsync(
            [Snapshot(3, sensorSlug: "ghost-node", requestId: 5)],
            CancellationToken.None);

        Assert.Equal(1, imported);
        Assert.Single(_dbContext.GatewayTelemetrySnapshots);
        Assert.Empty(_dbContext.MeasurementSamples);
        Assert.Empty(_dbContext.SensorStatuses);
    }

    [Fact]
    public async Task ImportBatchAsync_MalformedPayloadStoresRawSnapshotOnly()
    {
        var imported = await _importer.ImportBatchAsync(
            [Snapshot(4, payloadJson: "not-json")],
            CancellationToken.None);

        Assert.Equal(1, imported);
        var snapshot = Assert.Single(_dbContext.GatewayTelemetrySnapshots);
        Assert.Single(snapshot.Readings);
        Assert.Empty(_dbContext.MeasurementSamples);
    }

    private static GatewayTelemetrySnapshotDto Snapshot(
        long id,
        string sensorSlug = "smolensk-center",
        long requestId = 1,
        string? payloadJson = null) =>
        new(
            id,
            DateTimeOffset.UtcNow,
            "Http",
            payloadJson ??
                $$"""{"type":"sensor_poll","sensor":"{{sensorSlug}}","requestId":{{requestId}},"protocol":"meshcore-req-lpp","rssi":-92.5,"snr":7.5,"responseHex":"00FF","readings":[{"metric":"temperature","value":21.5,"unit":"°C"}]}""",
            [new GatewayTelemetryReadingDto("sensors.temperature", 21.5, null)]);

    public void Dispose() => _dbContext.Dispose();
}
