using MeshSMO.Sensors.Forecasting;

namespace MeshSMO.Sensors.UnitTests.Forecasting;

public sealed class ForecastMetricsTests
{
    [Fact]
    public void Evaluate_ReturnsExpectedErrorsAndCoverage()
    {
        var result = ForecastMetrics.Evaluate(
            [1, 2, 3],
            [2, 2, 1],
            [0, 1, 0],
            [2, 3, 2]);

        Assert.Equal(1, result.Mae);
        Assert.Equal(Math.Sqrt(5d / 3), result.Rmse, 12);
        Assert.Equal(2d / 3, result.IntervalCoverage, 12);
    }

    [Fact]
    public void RelativeMae_ConstantExactBaseline_DoesNotClaimImprovement()
    {
        Assert.Equal(1, ForecastMetrics.RelativeMae(0, 0));
        Assert.Equal(double.PositiveInfinity, ForecastMetrics.RelativeMae(1, 0));
    }

    [Fact]
    public void NaiveForecasters_RepeatLastValueAndSeason()
    {
        Assert.Equal([4f, 4f, 4f], NaiveForecasters.LastValue([1, 2, 3, 4], 3));
        Assert.Equal([3f, 4f, 3f, 4f, 3f], NaiveForecasters.Seasonal([1, 2, 3, 4], 5, 2));
    }
}
