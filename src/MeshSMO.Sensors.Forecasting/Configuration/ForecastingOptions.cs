using System.Globalization;

namespace MeshSMO.Sensors.Forecasting.Configuration;

public sealed class ForecastingOptions
{
    public const string SectionName = "Forecasting";

    public bool Enabled { get; set; }
    public int StepMinutes { get; set; } = 5;
    public int TrainingWindowDays { get; set; } = 28;
    public int MinimumHistoryDays { get; set; } = 14;
    public IDictionary<string, int> MinimumHistoryDaysByHorizon { get; set; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["1h"] = 3,
            ["6h"] = 3,
            ["12h"] = 5,
            ["24h"] = 7,
        };
    public double MinimumCoverage { get; set; } = 0.85;
    public float ConfidenceLevel { get; set; } = 0.90f;
    public int BacktestFolds { get; set; } = 3;
    public double MaximumMase { get; set; } = 0.90;
    public double MinimumIntervalCoverage { get; set; } = 0.80;
    public int ResultCacheMinutes { get; set; } = 5;
    public int MaximumCacheEntries { get; set; } = 64;
    public int MaximumConcurrentTrainings { get; set; } = 1;
    public int CalculationTimeoutSeconds { get; set; } = 10;
    public IDictionary<string, ForecastSeriesOptions> Series { get; set; } =
        new Dictionary<string, ForecastSeriesOptions>(StringComparer.OrdinalIgnoreCase);

    public int MinimumHistoryDaysFor(TimeSpan horizon)
    {
        var key = HorizonKey(horizon);
        if (key is null || MinimumHistoryDaysByHorizon is null)
            return MinimumHistoryDays;
        if (MinimumHistoryDaysByHorizon.TryGetValue(key, out var configuredDays))
            return configuredDays;

        foreach (var (configuredKey, days) in MinimumHistoryDaysByHorizon)
        {
            if (string.Equals(configuredKey, key, StringComparison.OrdinalIgnoreCase))
                return days;
        }

        return MinimumHistoryDays;
    }

    public bool HasValidHistoryConfiguration()
    {
        if (TrainingWindowDays <= 0 ||
            MinimumHistoryDays <= 0 ||
            MinimumHistoryDays > TrainingWindowDays ||
            MinimumHistoryDaysByHorizon is null)
        {
            return false;
        }

        foreach (var (key, days) in MinimumHistoryDaysByHorizon)
        {
            if (!TryParseHorizonKey(key, out var horizon) ||
                days <= 0 ||
                days > TrainingWindowDays ||
                days < MinimumDaysForRollingBacktest(horizon))
            {
                return false;
            }
        }

        return true;
    }

    private double MinimumDaysForRollingBacktest(TimeSpan horizon) =>
        Math.Ceiling(2 + (BacktestFolds * horizon.TotalDays));

    private static string? HorizonKey(TimeSpan horizon)
    {
        if (horizon <= TimeSpan.Zero || horizon.Ticks % TimeSpan.TicksPerHour != 0)
            return null;

        var hours = horizon.Ticks / TimeSpan.TicksPerHour;
        return string.Create(CultureInfo.InvariantCulture, $"{hours}h");
    }

    private static bool TryParseHorizonKey(string key, out TimeSpan horizon)
    {
        horizon = default;
        if (string.IsNullOrWhiteSpace(key) ||
            key.Length < 2 ||
            key[^1] is not ('h' or 'H') ||
            !int.TryParse(key.AsSpan(0, key.Length - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var hours) ||
            hours is <= 0 or > 24)
        {
            return false;
        }

        horizon = TimeSpan.FromHours(hours);
        return true;
    }
}
