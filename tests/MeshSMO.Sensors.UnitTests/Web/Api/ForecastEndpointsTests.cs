using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MeshSMO.Sensors.Application.Abstractions;
using MeshSMO.Sensors.Domain.Sensors;
using MeshSMO.Sensors.Forecasting.Abstractions;
using MeshSMO.Sensors.Forecasting.Configuration;
using MeshSMO.Sensors.Forecasting.Models;
using MeshSMO.Sensors.Infrastructure;
using MeshSMO.Sensors.Infrastructure.Persistence;
using MeshSMO.Sensors.Web.Api.Forecasting;
using MeshSMO.Sensors.Web.Resilience;
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

public sealed class ForecastEndpointsTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"forecast-{Guid.NewGuid():N}.db");
    private readonly WebApplication _app;
    private readonly HttpClient _client;
    private readonly FakeForecastService _forecastService = new();

    public ForecastEndpointsTests()
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
        builder.Services.RemoveAll<IClock>();
        builder.Services.AddSingleton<IClock>(new TestClock(Now));
        builder.Services.AddDbContext<SensorsDbContext>(options =>
            options.UseSqlite($"Data Source={_databasePath}"));
        builder.Services.Configure<ForecastingOptions>(options =>
        {
            options.Enabled = true;
            options.StepMinutes = 5;
            options.TrainingWindowDays = 28;
            options.MinimumHistoryDays = 14;
            options.MaximumConcurrentTrainings = 1;
            options.MaximumCacheEntries = 8;
            options.CalculationTimeoutSeconds = 1;
        });
        builder.Services.AddScoped<IForecastSeriesSource, FakeForecastSeriesSource>();
        builder.Services.AddSingleton<IForecastService>(_forecastService);
        builder.Services.AddWebResiliencePipelines();
        builder.Services.AddSingleton<ForecastCoordinator>();
        _app = builder.Build();
        _app.MapForecastApi();
        _app.StartAsync().GetAwaiter().GetResult();

        using var scope = _app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SensorsDbContext>();
        dbContext.Database.EnsureCreated();
        dbContext.Sensors.Add(CreateSensor("forecast-node", visible: true, "forecast-public-key"));
        dbContext.Sensors.Add(CreateSensor("hidden-node", visible: false, "hidden-public-key"));
        dbContext.SaveChanges();
        _client = (_app.Services.GetRequiredService<IServer>() as TestServer)!.CreateClient();
    }

    [Fact]
    public async Task Forecast_ValidRequest_ReturnsReadyContractAndUsesCache()
    {
        var first = await _client.GetAsync("/api/v1/sensors/forecast-node/forecast?metric=temperature&horizon=1h");
        var second = await _client.GetAsync("/api/v1/sensors/forecast-node/forecast?metric=temperature&horizon=1h");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var body = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ready", body.GetProperty("availability").GetString());
        Assert.Equal("temperature", body.GetProperty("metric").GetProperty("key").GetString());
        Assert.Equal("1h", body.GetProperty("range").GetProperty("horizon").GetString());
        Assert.Single(body.GetProperty("points").EnumerateArray());
        Assert.Equal(1, _forecastService.CallCount);
    }

    [Fact]
    public async Task Forecast_ConcurrentIdenticalRequests_RunOneCalculation()
    {
        _forecastService.Delay = TimeSpan.FromMilliseconds(100);

        var responses = await Task.WhenAll(
            _client.GetAsync("/api/v1/sensors/forecast-node/forecast?metric=temperature&horizon=6h"),
            _client.GetAsync("/api/v1/sensors/forecast-node/forecast?metric=temperature&horizon=6h"));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Equal(1, _forecastService.CallCount);
    }

    [Fact]
    public async Task Forecast_InvalidRequest_ReturnsValidationOrNotFound()
    {
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await _client.GetAsync("/api/v1/sensors/forecast-node/forecast?metric=temperature&horizon=2h")).StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await _client.GetAsync("/api/v1/sensors/forecast-node/forecast?metric=pressure&horizon=1h")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _client.GetAsync("/api/v1/sensors/hidden-node/forecast?metric=temperature&horizon=1h")).StatusCode);
    }

    [Fact]
    public async Task Forecast_WhenCalculationTimesOut_ReturnsServiceUnavailable()
    {
        _forecastService.Delay = TimeSpan.FromSeconds(2);

        var response = await _client.GetAsync(
            "/api/v1/sensors/forecast-node/forecast?metric=temperature&horizon=12h");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("5", response.Headers.RetryAfter?.ToString());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ForecastUnavailable", body.GetProperty("error").GetString());
        Assert.Equal("Forecast calculation timed out.", body.GetProperty("message").GetString());
    }

    private static Sensor CreateSensor(string slug, bool visible, string publicKey) => new(
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
        visible,
        false,
        null,
        null,
        null,
        ["temperature"],
        Now);

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

    private sealed class TestClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class FakeForecastSeriesSource : IForecastSeriesSource
    {
        public Task<ForecastSeries> ReadAsync(
            ForecastSeriesQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ForecastSeries(
                query.SensorId,
                query.MetricKey,
                query.Unit,
                query.PollInterval,
                Now,
                [new(Now.AddMinutes(-5), 20)]));
    }

    private sealed class FakeForecastService : IForecastService
    {
        public int CallCount { get; private set; }
        public TimeSpan Delay { get; set; }

        public ForecastResult Forecast(
            ForecastSeries series,
            TimeSpan horizon,
            DateTimeOffset generatedAt,
            CancellationToken cancellationToken = default)
        {
            if (Delay > TimeSpan.Zero)
                Task.Delay(Delay, cancellationToken).GetAwaiter().GetResult();
            CallCount++;
            var diagnostics = new ForecastDiagnostics(
                "ssa",
                12,
                100,
                generatedAt.AddDays(-14),
                generatedAt.AddMinutes(-5),
                1,
                0,
                0.5,
                0.7,
                0.8,
                0.9,
                0.9);
            return new(
                ForecastAvailability.Ready,
                null,
                generatedAt,
                series.LastObservationAt,
                TimeSpan.FromMinutes(5),
                diagnostics,
                [new(generatedAt.AddMinutes(5), 21, 20, 22)]);
        }
    }
}
