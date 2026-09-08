using MeshSMO.Sensors.Forecasting.Configuration;

namespace MeshSMO.Sensors.UnitTests.Forecasting;

public sealed class ForecastingOptionsTests
{
    [Theory]
    [InlineData(1, 3)]
    [InlineData(6, 3)]
    [InlineData(12, 5)]
    [InlineData(24, 7)]
    public void MinimumHistoryDaysFor_KnownHorizon_ReturnsConfiguredMinimum(
        int horizonHours,
        int expectedDays)
    {
        var options = new ForecastingOptions();

        var result = options.MinimumHistoryDaysFor(TimeSpan.FromHours(horizonHours));

        Assert.Equal(expectedDays, result);
    }

    [Fact]
    public void MinimumHistoryDaysFor_UnconfiguredHorizon_ReturnsFallback()
    {
        var options = new ForecastingOptions { MinimumHistoryDays = 11 };

        var result = options.MinimumHistoryDaysFor(TimeSpan.FromHours(2));

        Assert.Equal(11, result);
    }

    [Fact]
    public void MinimumHistoryDaysFor_CaseSensitiveConfiguration_MatchesHorizonIgnoringCase()
    {
        var options = new ForecastingOptions
        {
            MinimumHistoryDaysByHorizon = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["6H"] = 4,
            },
        };

        var result = options.MinimumHistoryDaysFor(TimeSpan.FromHours(6));

        Assert.Equal(4, result);
    }

    [Fact]
    public void HasValidHistoryConfiguration_MinimumCannotUndercutRollingBacktest()
    {
        var options = new ForecastingOptions();
        options.MinimumHistoryDaysByHorizon["24h"] = 4;

        Assert.False(options.HasValidHistoryConfiguration());
    }

    [Fact]
    public void HasValidHistoryConfiguration_UnknownHorizonKey_ReturnsFalse()
    {
        var options = new ForecastingOptions();
        options.MinimumHistoryDaysByHorizon["tomorrow"] = 7;

        Assert.False(options.HasValidHistoryConfiguration());
    }
}
