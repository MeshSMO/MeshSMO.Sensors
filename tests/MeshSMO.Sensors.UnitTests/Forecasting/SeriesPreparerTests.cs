using MeshSMO.Sensors.Forecasting.Configuration;
using MeshSMO.Sensors.Forecasting.Models;
using MeshSMO.Sensors.Forecasting.Preparation;

namespace MeshSMO.Sensors.UnitTests.Forecasting;

public sealed class SeriesPreparerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 2, 0, TimeSpan.Zero);

    [Fact]
    public void Prepare_CompleteSeries_ReturnsRegularValues()
    {
        var options = Options();
        var observations = Enumerable.Range(0, 48)
            .Select(index => new ForecastObservation(
                Now.AddHours(-48 + index),
                10 + index))
            .ToArray();
        var series = Series(observations, Now.AddMinutes(-2));

        var result = new SeriesPreparer(options).Prepare(series, Now);

        Assert.True(result.IsReady);
        Assert.Equal(48, result.Values.Count);
        Assert.Equal(1, result.ObservedCoverage);
        Assert.Equal(0, result.InterpolatedPoints);
        Assert.Equal(new DateTimeOffset(2026, 9, 7, 13, 0, 0, TimeSpan.Zero), result.ForecastFrom);
    }

    [Fact]
    public void Prepare_SmallInternalAndTrailingGaps_InterpolatesThem()
    {
        var observations = Enumerable.Range(0, 48)
            .Where(index => index is not 10 and not 11 and not 47)
            .Select(index => new ForecastObservation(
                Now.AddHours(-48 + index),
                (double)index))
            .ToArray();
        var series = Series(observations, Now.AddMinutes(-2));

        var result = new SeriesPreparer(Options(minimumCoverage: 0.90)).Prepare(series, Now);

        Assert.True(result.IsReady);
        Assert.Equal(3, result.InterpolatedPoints);
        Assert.Equal(10, result.Values[10]);
        Assert.Equal(11, result.Values[11]);
        Assert.Equal(result.Values[^2], result.Values[^1]);
    }

    [Fact]
    public void Prepare_LongTrailingGap_ReturnsSparseData()
    {
        var observations = Enumerable.Range(0, 44)
            .Select(index => new ForecastObservation(Now.AddHours(-48 + index), index))
            .ToArray();
        var series = Series(observations, Now.AddMinutes(-2));

        var result = new SeriesPreparer(Options()).Prepare(series, Now);

        Assert.Equal(ForecastAvailability.SparseData, result.Availability);
    }

    [Fact]
    public void Prepare_OldLatestObservation_ReturnsStaleData()
    {
        var observations = Enumerable.Range(0, 48)
            .Select(index => new ForecastObservation(Now.AddHours(-48 + index), index))
            .ToArray();
        var series = Series(observations, Now.AddHours(-1));

        var result = new SeriesPreparer(Options()).Prepare(series, Now);

        Assert.Equal(ForecastAvailability.StaleData, result.Availability);
    }

    private static ForecastingOptions Options(double minimumCoverage = 0.85) => new()
    {
        Enabled = true,
        StepMinutes = 60,
        TrainingWindowDays = 2,
        MinimumHistoryDays = 1,
        MinimumCoverage = minimumCoverage,
    };

    private static ForecastSeries Series(
        IReadOnlyList<ForecastObservation> observations,
        DateTimeOffset lastObservationAt) =>
        new(
            Guid.Parse("7dccb8b1-3673-46f8-8500-b14a1a55bfd0"),
            "temperature",
            "°C",
            TimeSpan.FromMinutes(5),
            lastObservationAt,
            observations);
}
