using MeshSMO.Sensors.Domain.Polling;
using MeshSMO.Sensors.Domain.Sensors;
using MeshSMO.Sensors.Infrastructure.Persistence;
using MeshSMO.Sensors.Web.GatewayIngestion;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MeshSMO.Sensors.UnitTests.Web.GatewayIngestion;

public sealed class GatewayTelemetryImporterTests : IDisposable
{
    private readonly SensorsDbContext _dbContext;
    private readonly GatewayTelemetryImporter _importer;
    private readonly Sensor _sensor;

    public GatewayTelemetryImporterTests()
    {
        _dbContext = new(
            new DbContextOptionsBuilder<SensorsDbContext>()
                .UseInMemoryDatabase($"importer-{Guid.NewGuid():N}")
                .Options);
        _importer = new(
            _dbContext,
            Options.Create(new GatewayIngestionOptions()),
            NullLogger<GatewayTelemetryImporter>.Instance);
        var now = DateTimeOffset.UtcNow;
        _sensor = new(
            new(Guid.NewGuid()),
            new("smolensk-center"),
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
        Assert.Equal("not-json", snapshot.PayloadJson);
        Assert.Empty(_dbContext.MeasurementSamples);
    }

    [Fact]
    public async Task ImportBatchAsync_SuccessfulPoll_CreatesSucceededAttempt()
    {
        var startedAt = DateTimeOffset.UtcNow.AddMilliseconds(-900);
        var payloadJson =
            $$"""{"type":"sensor_poll","sensor":"smolensk-center","requestId":11,"protocol":"meshcore-req-lpp","attemptNumber":2,"startedAt":"{{startedAt:O}}","rssi":-92.5,"snr":7.5,"elapsedMs":900,"responseHex":"00FF","readings":[{"metric":"temperature","value":21.5,"unit":"°C"}]}""";

        await _importer.ImportBatchAsync([Snapshot(5, payloadJson: payloadJson)], CancellationToken.None);

        var attempt = Assert.Single(_dbContext.PollAttempts);
        Assert.Equal(_sensor.Id, attempt.SensorId);
        Assert.Equal(11, attempt.RequestId);
        Assert.Equal(2, attempt.AttemptNumber);
        Assert.Equal(PollAttemptStatus.Succeeded, attempt.Status);
        Assert.Equal(900, attempt.RoundTripMilliseconds);
        var sample = Assert.Single(_dbContext.MeasurementSamples);
        Assert.Equal(900, sample.RoundTripMilliseconds);
        var status = Assert.Single(_dbContext.SensorStatuses);
        Assert.Equal(SensorState.Online, status.State);
    }

    [Fact]
    public async Task ImportBatchAsync_FailedAttempt_CreatesTimedOutAttemptAndDegradesSensor()
    {
        await _importer.ImportBatchAsync(
            [AttemptSnapshot(6, requestId: 12, attemptNumber: 1, status: "TimedOut")],
            CancellationToken.None);

        var attempt = Assert.Single(_dbContext.PollAttempts);
        Assert.Equal(PollAttemptStatus.TimedOut, attempt.Status);
        Assert.Equal(1, attempt.AttemptNumber);
        Assert.True(attempt.CompletedAt >= attempt.StartedAt);
        var status = Assert.Single(_dbContext.SensorStatuses);
        Assert.Equal(SensorState.Degraded, status.State);
        Assert.Equal(1, status.ConsecutiveFailures);
        Assert.Empty(_dbContext.MeasurementSamples);
    }

    [Fact]
    public async Task ImportBatchAsync_RetrySucceedsInSameCycle_BatchEndsOnline()
    {
        await _importer.ImportBatchAsync(
        [
            AttemptSnapshot(7, requestId: 13, attemptNumber: 1, status: "TimedOut"),
            Snapshot(8, requestId: 13, payloadJson:
                """{"type":"sensor_poll","sensor":"smolensk-center","requestId":13,"protocol":"meshcore-req-lpp","attemptNumber":2,"readings":[{"metric":"temperature","value":21.5,"unit":"°C"}]}"""),
        ], CancellationToken.None);

        Assert.Equal(2, _dbContext.PollAttempts.Count());
        var status = Assert.Single(_dbContext.SensorStatuses);
        Assert.Equal(SensorState.Online, status.State);
        Assert.Equal(0, status.ConsecutiveFailures);
        Assert.Single(_dbContext.MeasurementSamples);
    }

    [Fact]
    public async Task ImportBatchAsync_ConsecutiveFailures_MarkSensorOffline()
    {
        var snapshots = Enumerable.Range(0, 6)
            .Select(index => AttemptSnapshot(100 + index, requestId: 200 + index, attemptNumber: 1, status: "TimedOut"))
            .ToArray();

        await _importer.ImportBatchAsync(snapshots, CancellationToken.None);

        var status = Assert.Single(_dbContext.SensorStatuses);
        Assert.Equal(SensorState.Offline, status.State);
        Assert.Equal(6, status.ConsecutiveFailures);
        Assert.Equal(6, _dbContext.PollAttempts.Count());
    }

    [Fact]
    public async Task ImportBatchAsync_SkipsDuplicateAttemptsAcrossRedelivery()
    {
        await _importer.ImportBatchAsync(
            [AttemptSnapshot(9, requestId: 14, attemptNumber: 1, status: "TimedOut")],
            CancellationToken.None);
        var imported = await _importer.ImportBatchAsync(
            [AttemptSnapshot(10, requestId: 14, attemptNumber: 1, status: "TimedOut")],
            CancellationToken.None);

        Assert.Equal(1, imported);
        Assert.Single(_dbContext.PollAttempts);
    }

    [Fact]
    public async Task ImportBatchAsync_UnknownSensorAttempt_StoresSnapshotOnly()
    {
        var imported = await _importer.ImportBatchAsync(
            [AttemptSnapshot(15, sensorSlug: "ghost-node", requestId: 16, attemptNumber: 1, status: "Failed")],
            CancellationToken.None);

        Assert.Equal(1, imported);
        Assert.Single(_dbContext.GatewayTelemetrySnapshots);
        Assert.Empty(_dbContext.PollAttempts);
        Assert.Empty(_dbContext.SensorStatuses);
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
                $$"""{"type":"sensor_poll","sensor":"{{sensorSlug}}","requestId":{{requestId}},"protocol":"meshcore-req-lpp","rssi":-92.5,"snr":7.5,"responseHex":"00FF","readings":[{"metric":"temperature","value":21.5,"unit":"°C"}]}""");

    private static GatewayTelemetrySnapshotDto AttemptSnapshot(
        long id,
        string sensorSlug = "smolensk-center",
        long requestId = 1,
        int attemptNumber = 1,
        string status = "TimedOut") =>
        new(
            id,
            DateTimeOffset.UtcNow,
            "Http",
            $$"""{"type":"poll_attempt","sensor":"{{sensorSlug}}","requestId":{{requestId}},"protocol":"meshcore-req-lpp","attemptNumber":{{attemptNumber}},"startedAt":"{{DateTimeOffset.UtcNow.AddSeconds(-2):O}}","completedAt":"{{DateTimeOffset.UtcNow:O}}","status":"{{status}}","errorCode":"timeout","roundTripMs":8000}""");

    public void Dispose() => _dbContext.Dispose();
}
