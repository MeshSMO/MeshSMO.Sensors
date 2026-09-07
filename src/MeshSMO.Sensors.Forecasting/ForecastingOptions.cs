namespace MeshSMO.Sensors.Forecasting;

public sealed class ForecastingOptions
{
    public const string SectionName = "Forecasting";

    public bool Enabled { get; set; }
    public int StepMinutes { get; set; } = 5;
    public int TrainingWindowDays { get; set; } = 28;
    public int MinimumHistoryDays { get; set; } = 14;
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
}
