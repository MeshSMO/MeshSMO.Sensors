using MeshSMO.Sensors.Forecasting.Configuration;
using MeshSMO.Sensors.Forecasting.MlNet;
using MeshSMO.Sensors.Forecasting.Models;

namespace MeshSMO.Sensors.UnitTests.Forecasting;

public sealed class MlNetForecastServiceTests
{
    [Fact]
    public void Forecast_SeasonalTrend_ReturnsRequestedFuturePoints()
    {
        var now = new DateTimeOffset(2026, 9, 7, 12, 2, 0, TimeSpan.Zero);
        var observations = Enumerable.Range(0, 24 * 16)
            .Select(index => new ForecastObservation(
                now.AddHours((-24 * 16) + index),
                20 + (index * 0.02) + (3 * Math.Sin(index * 2 * Math.PI / 24))))
            .ToArray();
        var options = new ForecastingOptions
        {
            Enabled = true,
            StepMinutes = 60,
            TrainingWindowDays = 16,
            MinimumHistoryDays = 14,
            MinimumCoverage = 0.85,
            BacktestFolds = 3,
            MaximumMase = 10,
            MinimumIntervalCoverage = 0,
        };
        var series = new ForecastSeries(
            Guid.NewGuid(),
            "temperature",
            "°C",
            TimeSpan.FromMinutes(5),
            now.AddMinutes(-2),
            observations,
            -50,
            60);

        var result = new MlNetForecastService(options).Forecast(series, TimeSpan.FromHours(6), now);

        Assert.Equal(ForecastAvailability.Ready, result.Availability);
        Assert.Equal(6, result.Points.Count);
        Assert.All(result.Points, point => Assert.True(double.IsFinite(point.Predicted)));
        Assert.Equal(new DateTimeOffset(2026, 9, 7, 13, 0, 0, TimeSpan.Zero), result.Points[0].Timestamp);
        Assert.NotNull(result.Diagnostics);
        Assert.True(result.Diagnostics.Mae >= 0);
    }

    [Fact]
    public void Forecast_Disabled_ReturnsUnavailableWithoutTraining()
    {
        var now = DateTimeOffset.UtcNow;
        var series = new ForecastSeries(
            Guid.NewGuid(),
            "temperature",
            "°C",
            TimeSpan.FromMinutes(5),
            now,
            [new(now, 20)]);

        var result = new MlNetForecastService(new()).Forecast(series, TimeSpan.FromHours(1), now);

        Assert.Equal(ForecastAvailability.Disabled, result.Availability);
        Assert.Empty(result.Points);
    }

    [Fact]
    public void Forecast_LenientMode_BuildsForecastFromStaleSparseShortSeries()
    {
        var now = new DateTimeOffset(2026, 9, 7, 12, 2, 0, TimeSpan.Zero);
        var start = now.AddHours(-64);
        var observations = Enumerable.Range(0, 40)
            .Where(index => index is < 12 or > 23)
            .Select(index => new ForecastObservation(
                new DateTimeOffset(start.Ticks, TimeSpan.Zero).AddHours(index),
                20 + (index % 5)))
            .ToArray();
        var series = new ForecastSeries(
            Guid.NewGuid(),
            "temperature",
            "°C",
            TimeSpan.FromMinutes(5),
            observations[^1].Timestamp,
            observations,
            -50,
            60);
        var strictOptions = new ForecastingOptions
        {
            Enabled = true,
            StepMinutes = 60,
            TrainingWindowDays = 7,
            MinimumHistoryDays = 3,
            MinimumCoverage = 0.85,
            BacktestFolds = 3,
        };
        var lenientOptions = new ForecastingOptions
        {
            Enabled = true,
            LenientMode = true,
            StepMinutes = 60,
            TrainingWindowDays = 7,
            MinimumHistoryDays = 3,
            MinimumCoverage = 0.85,
            BacktestFolds = 3,
        };

        var strictResult = new MlNetForecastService(strictOptions).Forecast(series, TimeSpan.FromHours(6), now);
        var lenientResult = new MlNetForecastService(lenientOptions).Forecast(series, TimeSpan.FromHours(6), now);

        Assert.Equal(ForecastAvailability.StaleData, strictResult.Availability);
        Assert.Equal(ForecastAvailability.Ready, lenientResult.Availability);
        Assert.Equal(6, lenientResult.Points.Count);
        Assert.All(lenientResult.Points, point => Assert.True(double.IsFinite(point.Predicted)));
        Assert.NotNull(lenientResult.Diagnostics);
        Assert.True(lenientResult.Diagnostics.InterpolatedPoints > 0);
        Assert.True(lenientResult.Diagnostics.ObservedCoverage < strictOptions.MinimumCoverage);
    }

    [Fact]
    public void Forecast_LenientMode_SkipsQualityGates()
    {
        var now = new DateTimeOffset(2026, 9, 7, 12, 2, 0, TimeSpan.Zero);
        var observations = Enumerable.Range(0, 24 * 16)
            .Select(index => new ForecastObservation(
                now.AddHours((-24 * 16) + index),
                20 + (index * 0.02) + (3 * Math.Sin(index * 2 * Math.PI / 24))))
            .ToArray();
        var series = new ForecastSeries(
            Guid.NewGuid(),
            "temperature",
            "°C",
            TimeSpan.FromMinutes(5),
            now.AddMinutes(-2),
            observations,
            -50,
            60,
            0.000001);
        ForecastingOptions Options(bool lenient) => new()
        {
            Enabled = true,
            LenientMode = lenient,
            StepMinutes = 60,
            TrainingWindowDays = 16,
            MinimumHistoryDays = 14,
            MinimumCoverage = 0.85,
            BacktestFolds = 3,
            MaximumMase = 0.000001,
            MinimumIntervalCoverage = 1,
        };

        var strictResult = new MlNetForecastService(Options(lenient: false)).Forecast(series, TimeSpan.FromHours(6), now);
        var lenientResult = new MlNetForecastService(Options(lenient: true)).Forecast(series, TimeSpan.FromHours(6), now);

        Assert.Equal(ForecastAvailability.LowQuality, strictResult.Availability);
        Assert.Equal(ForecastAvailability.Ready, lenientResult.Availability);
        Assert.True(lenientResult.Diagnostics!.Mase <= strictResult.Diagnostics!.Mase);
        Assert.Equal(6, lenientResult.Points.Count);
    }

    [Fact]
    public void Forecast_DiurnalSeries_SelectsRobustProfileAndClampsPhysicalBounds()
    {
        var now = new DateTimeOffset(2026, 9, 10, 0, 2, 0, TimeSpan.Zero);
        var dailyAmplitudes = new[] { 4d, 1d, 4d, 1d, 4d };
        var observations = Enumerable.Range(0, 24 * dailyAmplitudes.Length)
            .Select(index =>
            {
                var hour = index % 24;
                var value = hour is >= 6 and < 18 ? dailyAmplitudes[index / 24] : 0;
                return new ForecastObservation(now.AddHours(index - (24 * dailyAmplitudes.Length)), value);
            })
            .ToArray();
        var options = new ForecastingOptions
        {
            Enabled = true,
            StepMinutes = 60,
            TrainingWindowDays = 5,
            MinimumHistoryDays = 5,
            MinimumHistoryDaysByHorizon = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["24h"] = 5,
            },
            MinimumCoverage = 1,
            BacktestFolds = 3,
            MaximumMase = 0.000001,
            MinimumIntervalCoverage = 0.8,
        };
        var series = new ForecastSeries(
            Guid.NewGuid(),
            "solar_panel_voltage",
            "V",
            TimeSpan.FromHours(1),
            now.AddMinutes(-2),
            observations,
            0,
            5);

        var result = new MlNetForecastService(options).Forecast(series, TimeSpan.FromHours(24), now);

        Assert.Equal(ForecastAvailability.Ready, result.Availability);
        Assert.Equal("seasonal_median", result.Diagnostics!.ModelKind);
        Assert.Null(result.Diagnostics.WindowSize);
        Assert.Equal(24, result.Points.Count);
        Assert.Contains(result.Points, point => point.Predicted > 2);
        Assert.All(result.Points, point =>
        {
            Assert.InRange(point.Lower, 0, 5);
            Assert.InRange(point.Predicted, 0, 5);
            Assert.InRange(point.Upper, 0, 5);
        });
    }
}
