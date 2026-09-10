using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MeshSMO.Sensors.Domain.Measurements;
using MeshSMO.Sensors.Domain.Sensors;
using MeshSMO.Sensors.Infrastructure;
using MeshSMO.Sensors.Infrastructure.Persistence;
using MeshSMO.Sensors.Web.Api;
using MeshSMO.Sensors.Web.Api.MeasurementHistory;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace MeshSMO.Sensors.UnitTests.Web.Api;

public sealed class MeasurementHistoryEndpointsTests : IDisposable
{
    private static readonly DateTimeOffset From = new(2026, 9, 5, 10, 0, 0, TimeSpan.Zero);

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"measurements-{Guid.NewGuid():N}.db");
    private readonly WebApplication _app;
    private readonly HttpClient _client;

    public MeasurementHistoryEndpointsTests()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["ConnectionStrings:Sensors"] = "Host=localhost;Database=unused",
            ["Registry:Directory"] = Path.Combine(Path.GetTempPath(), $"registry-{Guid.NewGuid():N}"),
        });
        builder.Logging.ClearProviders();
        builder.Services.AddSensorsInfrastructure(builder.Configuration);
        builder.Services.RemoveAll<DbContextOptions<SensorsDbContext>>();
        builder.Services.RemoveAll<IDbContextOptionsConfiguration<SensorsDbContext>>();
        builder.Services.AddDbContext<SensorsDbContext>(options =>
            options.UseSqlite($"Data Source={_databasePath}"));
        builder.Services.AddHealthChecks();
        builder.Services.AddScoped<MeasurementHistoryReader>();
        _app = builder.Build();
        _app.MapSensorApi();
        _app.MapMeasurementHistoryApi();
        _app.StartAsync().GetAwaiter().GetResult();

        using var scope = _app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SensorsDbContext>();
        dbContext.Database.EnsureCreated();

        var sensor = CreateSensor("smolensk-center", "Смоленск — центр", enabled: true, visible: true, "pub-key-1");
        var hidden = CreateSensor("private-node", "Private node", enabled: true, visible: false, "pub-key-2");
        dbContext.Sensors.AddRange(sensor, hidden);
        dbContext.SaveChanges();
        dbContext.SensorMetrics
            .Single(metric => metric.SensorId == sensor.Id && metric.MetricKey == "temperature")
            .Unit = "°C";

        var sample1 = Sample(sensor.Id, 1, Minute(1));
        var sample2 = Sample(sensor.Id, 2, Minute(3));
        var sample3 = Sample(sensor.Id, 3, Minute(7));
        var sample4 = Sample(sensor.Id, 4, Minute(9));
        var nightSample = Sample(sensor.Id, 5, Night);
        dbContext.MeasurementSamples.AddRange(sample1, sample2, sample3, sample4, nightSample);
        dbContext.MeasurementValues.AddRange(
            Value(sample1.Id, sensor.Id, "temperature", 20, Minute(1)),
            Value(sample2.Id, sensor.Id, "temperature", 26, Minute(3)),
            Value(sample3.Id, sensor.Id, "temperature", 24, Minute(7)),
            Value(sample4.Id, sensor.Id, "temperature", 28, Minute(9)),
            Value(sample2.Id, sensor.Id, "humidity", 50, Minute(3)),
            Value(nightSample.Id, sensor.Id, "solar_panel_voltage", 1.07, Night));
        dbContext.SaveChanges();
        _client = (_app.Services.GetRequiredService<IServer>() as TestServer)!.CreateClient();
    }

    [Fact]
    public async Task Measurements_ExplicitFiveMinuteResolution_BucketsWindows()
    {
        var response = await _client.GetAsync(MeasurementsUri("temperature", Minute(0), Minute(15), "5m"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("5m", body.GetProperty("range").GetProperty("resolution").GetString());

        var points = body.GetProperty("points");
        Assert.Equal(2, points.GetArrayLength());
        Assert.Collection(
            points.EnumerateArray(),
            point =>
            {
                AssertTimestamp(Minute(0), point.GetProperty("timestamp").GetString()!);
                Assert.Equal(20, point.GetProperty("min").GetDouble());
                Assert.Equal(23, point.GetProperty("avg").GetDouble());
                Assert.Equal(26, point.GetProperty("max").GetDouble());
            },
            point =>
            {
                AssertTimestamp(Minute(5), point.GetProperty("timestamp").GetString()!);
                Assert.Equal(24, point.GetProperty("min").GetDouble());
                Assert.Equal(26, point.GetProperty("avg").GetDouble());
                Assert.Equal(28, point.GetProperty("max").GetDouble());
            });
    }

    [Fact]
    public void ResolutionPolicy_AutoBands_FollowSpec()
    {
        // §15: <=24h -> raw/5m; <=7d -> 15m; <=31d -> 1h; <=180d -> 6h; >180d -> 1d.
        Assert.Equal(MeasurementResolution.Raw, MeasurementResolutionPolicy.ResolveAuto(TimeSpan.FromMinutes(15)));
        Assert.Equal(MeasurementResolution.Raw, MeasurementResolutionPolicy.ResolveAuto(TimeSpan.FromHours(6)));
        Assert.Equal(MeasurementResolution.FiveMinutes, MeasurementResolutionPolicy.ResolveAuto(TimeSpan.FromHours(12)));
        Assert.Equal(MeasurementResolution.FiveMinutes, MeasurementResolutionPolicy.ResolveAuto(TimeSpan.FromHours(24)));
        Assert.Equal(MeasurementResolution.FifteenMinutes, MeasurementResolutionPolicy.ResolveAuto(TimeSpan.FromDays(7)));
        Assert.Equal(MeasurementResolution.OneHour, MeasurementResolutionPolicy.ResolveAuto(TimeSpan.FromDays(31)));
        Assert.Equal(MeasurementResolution.SixHours, MeasurementResolutionPolicy.ResolveAuto(TimeSpan.FromDays(180)));
        Assert.Equal(MeasurementResolution.OneDay, MeasurementResolutionPolicy.ResolveAuto(TimeSpan.FromDays(365)));
    }

    [Fact]
    public async Task Measurements_ShortAutoRange_UsesRawPoints()
    {
        // 4 hours range -> auto policy keeps raw values.
        var response = await _client.GetAsync(MeasurementsUri("temperature", Minute(0), Minute(240), "auto"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("smolensk-center", body.GetProperty("sensor").GetProperty("slug").GetString());
        Assert.Equal("Смоленск — центр", body.GetProperty("sensor").GetProperty("displayName").GetString());
        Assert.Equal("temperature", body.GetProperty("metric").GetProperty("key").GetString());
        Assert.Equal("°C", body.GetProperty("metric").GetProperty("unit").GetString());
        Assert.Equal("raw", body.GetProperty("range").GetProperty("resolution").GetString());
        var points = body.GetProperty("points");
        Assert.Equal(4, points.GetArrayLength());
        Assert.Collection(
            points.EnumerateArray(),
            point => AssertPoint(point, Minute(1), 20),
            point => AssertPoint(point, Minute(3), 26),
            point => AssertPoint(point, Minute(7), 24),
            point => AssertPoint(point, Minute(9), 28));
    }

    [Fact]
    public async Task Measurements_ExplicitRaw_ReturnsEverySample()
    {
        var response = await _client.GetAsync(MeasurementsUri("temperature", Minute(0), Minute(15), "raw"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("raw", body.GetProperty("range").GetProperty("resolution").GetString());
        Assert.Equal(4, body.GetProperty("points").GetArrayLength());
    }

    [Fact]
    public async Task Measurements_FiltersByMetric()
    {
        var response = await _client.GetAsync(MeasurementsUri("humidity", Minute(0), Minute(15), "raw"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var points = body.GetProperty("points");
        Assert.Equal(1, points.GetArrayLength());
        Assert.Equal(50, points[0].GetProperty("avg").GetDouble());
    }

    [Fact]
    public async Task Measurements_SolarPanelVoltageAtNight_MarksAnomaly()
    {
        var response = await _client.GetAsync(
            MeasurementsUri("solar_panel_voltage", Night.AddMinutes(-1), Night.AddMinutes(1), "raw"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var anomaly = body.GetProperty("points")[0].GetProperty("anomaly");
        Assert.Equal(SolarPanelAnomalyDetector.AnomalyCode, anomaly.GetProperty("code").GetString());
        Assert.Equal("warning", anomaly.GetProperty("severity").GetString());
        Assert.Equal(1.07, anomaly.GetProperty("observedMaximum").GetDouble());
        Assert.Equal(
            SolarPanelAnomalyDetector.NightVoltageThreshold,
            anomaly.GetProperty("expectedMaximum").GetDouble());
        Assert.True(anomaly.GetProperty("solarElevationDegrees").GetDouble() < -6);
    }

    [Fact]
    public void SolarPanelAnomalyDetector_DaylightValue_IsNotAnomaly()
    {
        var point = new MeasurementHistoryPoint(From, 2.5, 2.5, 2.5, 1);

        var anomaly = SolarPanelAnomalyDetector.Detect(
            "solar_panel_voltage",
            54.7818,
            32.0401,
            MeasurementResolution.Raw,
            point);

        Assert.Null(anomaly);
        Assert.True(SolarPanelAnomalyDetector.SolarElevationDegrees(From, 54.7818, 32.0401) > 20);
    }

    [Fact]
    public async Task Measurements_UnknownOrHiddenSensor_Returns404()
    {
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _client.GetAsync(MeasurementsUri("temperature", Minute(0), Minute(15), "auto", slug: "ghost"))).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _client.GetAsync(MeasurementsUri("temperature", Minute(0), Minute(15), "auto", slug: "private-node"))).StatusCode);
    }

    [Fact]
    public async Task Measurements_InvalidQuery_Returns400WithValidationError()
    {
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await _client.GetAsync(MeasurementsUri("temperature", Minute(0), Minute(15), "7m"))).StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await _client.GetAsync($"/api/v1/sensors/smolensk-center/measurements?metric=&from={Uri.EscapeDataString($"{Minute(0):O}")}&to={Uri.EscapeDataString($"{Minute(15):O}")}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await _client.GetAsync(MeasurementsUri("temperature", Minute(15), Minute(0), "auto"))).StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await _client.GetAsync(MeasurementsUri("pressure", Minute(0), Minute(15), "auto"))).StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await _client.GetAsync(MeasurementsUri("temperature", Minute(0), Minute(0).AddDays(500), "auto"))).StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await _client.GetAsync(MeasurementsUri("temperature", Minute(0), Minute(0).AddHours(30), "raw"))).StatusCode);
    }

    [Fact]
    public async Task Measurements_RaisedMaxPoints_AllowsLongFineGrainedRange()
    {
        // 30 days at 5m buckets exceeds the default 5k budget but fits into maxPoints=50000.
        var response = await _client.GetAsync(
            MeasurementsUri("temperature", Minute(0), Minute(0).AddDays(30), "5m") + "&maxPoints=50000");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(50000, body.GetProperty("range").GetProperty("maxPoints").GetInt32());
        Assert.Equal(2, body.GetProperty("points").GetArrayLength());
    }

    [Fact]
    public async Task Measurements_DefaultMaxPoints_StillCapsLongFineGrainedRange()
    {
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await _client.GetAsync(MeasurementsUri("temperature", Minute(0), Minute(0).AddDays(30), "5m"))).StatusCode);
    }

    [Fact]
    public async Task Measurements_InvalidMaxPoints_Returns400()
    {
        foreach (var value in new[] { "0", "99", "50001", "abc", "-500" })
        {
            Assert.Equal(
                HttpStatusCode.BadRequest,
                (await _client.GetAsync(
                    MeasurementsUri("temperature", Minute(0), Minute(15), "5m") + $"&maxPoints={value}")).StatusCode);
        }
    }

    private static Sensor CreateSensor(
        string slug, string displayName, bool enabled, bool visible, string publicKey) => new(
        new(Guid.NewGuid()),
        new(slug),
        displayName,
        null,
        publicKey,
        "meshcore-req-lpp",
        TimeSpan.FromMinutes(5),
        TimeSpan.FromSeconds(30),
        2,
        enabled,
        visible,
        false,
        54.7818,
        32.0401,
        "city",
        ["temperature", "humidity", "solar_panel_voltage"],
        DateTimeOffset.UtcNow);

    private static MeasurementSample Sample(SensorId sensorId, long requestId, DateTimeOffset timestamp) =>
        new(Guid.NewGuid(), sensorId, requestId, timestamp, "meshcore-req-lpp");

    private static MeasurementValue Value(
        Guid sampleId, SensorId sensorId, string metric, double value, DateTimeOffset timestamp) =>
        new(sampleId, sensorId, metric, timestamp)
        {
            NumericValue = value,
        };

    private static DateTimeOffset Minute(int minutes) => From.AddMinutes(minutes);

    private static DateTimeOffset Night => new(2026, 9, 5, 19, 0, 0, TimeSpan.Zero);

    private static string MeasurementsUri(
        string metric, DateTimeOffset from, DateTimeOffset to, string resolution, string slug = "smolensk-center") =>
        $"/api/v1/sensors/{slug}/measurements?metric={Uri.EscapeDataString(metric)}" +
        $"&from={Uri.EscapeDataString($"{from:O}")}&to={Uri.EscapeDataString($"{to:O}")}&resolution={resolution}";

    private static void AssertTimestamp(DateTimeOffset expected, string actual) =>
        Assert.Equal(expected, DateTimeOffset.Parse(actual, System.Globalization.CultureInfo.InvariantCulture));

    private static void AssertPoint(JsonElement point, DateTimeOffset expectedTimestamp, double expectedValue)
    {
        AssertTimestamp(expectedTimestamp, point.GetProperty("timestamp").GetString()!);
        Assert.Equal(expectedValue, point.GetProperty("min").GetDouble());
        Assert.Equal(expectedValue, point.GetProperty("avg").GetDouble());
        Assert.Equal(expectedValue, point.GetProperty("max").GetDouble());
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            try
            {
                File.Delete(_databasePath + suffix);
            }
            catch (IOException)
            {
            }
        }
    }
}
