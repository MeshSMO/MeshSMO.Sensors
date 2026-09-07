using MeshSMO.Sensors.Forecasting;

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
}
