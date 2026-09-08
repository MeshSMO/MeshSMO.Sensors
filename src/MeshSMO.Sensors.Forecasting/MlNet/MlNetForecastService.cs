using Microsoft.ML;
using Microsoft.ML.Transforms.TimeSeries;
using MeshSMO.Sensors.Forecasting.Abstractions;
using MeshSMO.Sensors.Forecasting.Configuration;
using MeshSMO.Sensors.Forecasting.Evaluation;
using MeshSMO.Sensors.Forecasting.Models;
using MeshSMO.Sensors.Forecasting.Preparation;

namespace MeshSMO.Sensors.Forecasting.MlNet;

public sealed class MlNetForecastService : IForecastService
{
    private static readonly TimeSpan[] CandidateWindows =
    [
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(6),
        TimeSpan.FromHours(12),
        TimeSpan.FromHours(24),
    ];

    private readonly ForecastingOptions _options;
    private readonly SeriesPreparer _preparer;

    public MlNetForecastService(ForecastingOptions options)
    {
        ValidateOptions(options);
        _options = options;
        _preparer = new(options);
    }

    public ForecastResult Forecast(
        ForecastSeries series,
        TimeSpan horizon,
        DateTimeOffset generatedAt,
        CancellationToken cancellationToken = default)
    {
        var utcNow = generatedAt.ToUniversalTime();
        var step = TimeSpan.FromMinutes(_options.StepMinutes);
        if (horizon <= TimeSpan.Zero || horizon > TimeSpan.FromHours(24) || horizon.Ticks % step.Ticks != 0)
            throw new ArgumentOutOfRangeException(nameof(horizon), "Horizon must be a positive multiple of the forecast step and no longer than 24 hours.");

        var minimumHistoryDays = _options.MinimumHistoryDaysFor(horizon);
        var prepared = _preparer.Prepare(series, utcNow, minimumHistoryDays);
        if (!prepared.IsReady)
        {
            return ForecastResult.Unavailable(
                prepared.Availability,
                prepared.Reason ?? "The series is unavailable.",
                utcNow,
                series.LastObservationAt,
                step);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var horizonPoints = checked((int)(horizon.Ticks / step.Ticks));
        var initialTrainSize = prepared.Values.Count - (_options.BacktestFolds * horizonPoints);
        var seasonLength = checked((int)(TimeSpan.FromDays(1).Ticks / step.Ticks));
        if (initialTrainSize < seasonLength * 2)
        {
            return ForecastResult.Unavailable(
                ForecastAvailability.InsufficientData,
                "The series does not contain enough history for rolling backtest at this horizon.",
                utcNow,
                series.LastObservationAt,
                step);
        }

        var windowSizes = CandidateWindows
            .Select(window => checked((int)(window.Ticks / step.Ticks)))
            .Where(windowSize => windowSize >= 2 && windowSize * 2 < initialTrainSize)
            .Distinct()
            .ToArray();
        if (windowSizes.Length == 0)
        {
            return ForecastResult.Unavailable(
                ForecastAvailability.InsufficientData,
                "The series is too short for the configured SSA windows.",
                utcNow,
                series.LastObservationAt,
                step);
        }

        var evaluations = new List<SsaCandidateEvaluation>(windowSizes.Length);
        foreach (var windowSize in windowSizes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            evaluations.Add(EvaluateCandidate(
                prepared.Values,
                horizonPoints,
                seasonLength,
                windowSize,
                cancellationToken));
        }

        var best = evaluations
            .OrderBy(static candidate => candidate.Mase)
            .ThenBy(static candidate => candidate.Metrics.Rmse)
            .ThenBy(static candidate => candidate.WindowSize)
            .First();
        var diagnostics = new ForecastDiagnostics(
            "ssa",
            best.WindowSize,
            prepared.Values.Count,
            prepared.TrainingFrom!.Value,
            prepared.TrainingTo!.Value,
            prepared.ObservedCoverage,
            prepared.InterpolatedPoints,
            best.Metrics.Mae,
            best.Metrics.Rmse,
            best.Mase,
            best.Metrics.IntervalCoverage,
            _options.ConfidenceLevel);

        var qualityReason = QualityReason(best, series);
        if (qualityReason is not null)
        {
            return new(
                ForecastAvailability.LowQuality,
                qualityReason,
                utcNow,
                series.LastObservationAt,
                step,
                diagnostics,
                []);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var outputHorizon = checked(horizonPoints + prepared.ForecastOffsetSteps);
        var output = ForecastBySsa(prepared.Values, outputHorizon, best.WindowSize);
        if (!HasValidOutput(output, outputHorizon) || HasMaterialBoundViolation(
            output.Forecast.Skip(prepared.ForecastOffsetSteps).Take(horizonPoints),
            series.Minimum,
            series.Maximum))
        {
            return new(
                ForecastAvailability.LowQuality,
                "The final forecast failed numeric or physical-bound checks.",
                utcNow,
                series.LastObservationAt,
                step,
                diagnostics,
                []);
        }

        var points = new ForecastPoint[horizonPoints];
        for (var index = 0; index < horizonPoints; index++)
        {
            var outputIndex = index + prepared.ForecastOffsetSteps;
            var predicted = Clamp(output.Forecast[outputIndex], series.Minimum, series.Maximum);
            var lower = Clamp(output.Lower[outputIndex], series.Minimum, series.Maximum);
            var upper = Clamp(output.Upper[outputIndex], series.Minimum, series.Maximum);
            points[index] = new(
                prepared.ForecastFrom + TimeSpan.FromTicks(step.Ticks * index),
                predicted,
                Math.Min(lower, predicted),
                Math.Max(upper, predicted));
        }

        return new(
            ForecastAvailability.Ready,
            null,
            utcNow,
            series.LastObservationAt,
            step,
            diagnostics,
            points);
    }

    private SsaCandidateEvaluation EvaluateCandidate(
        IReadOnlyList<float> values,
        int horizon,
        int seasonLength,
        int windowSize,
        CancellationToken cancellationToken)
    {
        var actual = new List<float>(_options.BacktestFolds * horizon);
        var predicted = new List<float>(_options.BacktestFolds * horizon);
        var lower = new List<float>(_options.BacktestFolds * horizon);
        var upper = new List<float>(_options.BacktestFolds * horizon);
        var lastValuePredicted = new List<float>(_options.BacktestFolds * horizon);
        var seasonalPredicted = new List<float>(_options.BacktestFolds * horizon);

        for (var fold = 0; fold < _options.BacktestFolds; fold++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var trainSize = values.Count - ((_options.BacktestFolds - fold) * horizon);
            var training = values.Take(trainSize).ToArray();
            var validation = values.Skip(trainSize).Take(horizon).ToArray();
            var output = ForecastBySsa(training, horizon, windowSize);
            actual.AddRange(validation);
            predicted.AddRange(output.Forecast);
            lower.AddRange(output.Lower);
            upper.AddRange(output.Upper);
            lastValuePredicted.AddRange(NaiveForecasters.LastValue(training, horizon));
            seasonalPredicted.AddRange(NaiveForecasters.Seasonal(training, horizon, seasonLength));
        }

        var metrics = ForecastMetrics.Evaluate(actual, predicted, lower, upper);
        var lastValueMetrics = ForecastMetrics.Evaluate(actual, lastValuePredicted);
        var seasonalMetrics = ForecastMetrics.Evaluate(actual, seasonalPredicted);
        var baselineMae = Math.Min(lastValueMetrics.Mae, seasonalMetrics.Mae);
        return new(windowSize, metrics, ForecastMetrics.RelativeMae(metrics.Mae, baselineMae));
    }

    private SsaModelOutput ForecastBySsa(IReadOnlyList<float> values, int horizon, int windowSize)
    {
        var mlContext = new MLContext(seed: 1);
        var data = mlContext.Data.LoadFromEnumerable(values.Select(static value => new SsaModelInput { Value = value }));
        var seriesLength = Math.Min(values.Count, Math.Max(windowSize * 4, windowSize + 1));
        var estimator = mlContext.Forecasting.ForecastBySsa(
            nameof(SsaModelOutput.Forecast),
            nameof(SsaModelInput.Value),
            windowSize,
            seriesLength,
            values.Count,
            horizon,
            confidenceLowerBoundColumn: nameof(SsaModelOutput.Lower),
            confidenceUpperBoundColumn: nameof(SsaModelOutput.Upper),
            confidenceLevel: _options.ConfidenceLevel,
            shouldStabilize: true);
        var transformer = estimator.Fit(data);
        var engine = transformer.CreateTimeSeriesEngine<SsaModelInput, SsaModelOutput>(mlContext);
        return engine.Predict();
    }

    private string? QualityReason(SsaCandidateEvaluation evaluation, ForecastSeries series)
    {
        if (!double.IsFinite(evaluation.Mase) || evaluation.Mase > _options.MaximumMase)
            return "The SSA forecast did not improve sufficiently on the naive baseline.";
        if (series.MaximumMae is not null && evaluation.Metrics.Mae > series.MaximumMae.Value)
            return "The forecast error exceeds the configured limit for this series.";
        if (!double.IsFinite(evaluation.Metrics.IntervalCoverage) ||
            evaluation.Metrics.IntervalCoverage < _options.MinimumIntervalCoverage)
        {
            return "The forecast interval coverage is below the configured quality threshold.";
        }

        return null;
    }

    private static double Clamp(double value, double? minimum, double? maximum)
    {
        var result = value;
        if (minimum is not null)
            result = Math.Max(result, minimum.Value);
        if (maximum is not null)
            result = Math.Min(result, maximum.Value);
        return result;
    }

    private static bool HasValidOutput(SsaModelOutput output, int horizon) =>
        output.Forecast.Length == horizon &&
        output.Lower.Length == horizon &&
        output.Upper.Length == horizon &&
        output.Forecast.All(static value => float.IsFinite(value)) &&
        output.Lower.All(static value => float.IsFinite(value)) &&
        output.Upper.All(static value => float.IsFinite(value));

    private static bool HasMaterialBoundViolation(
        IEnumerable<float> values,
        double? minimum,
        double? maximum)
    {
        if (minimum is null && maximum is null)
            return false;

        var tolerance = minimum is not null && maximum is not null
            ? Math.Max(1e-6, (maximum.Value - minimum.Value) * 0.05)
            : Math.Max(1e-6, Math.Abs(minimum ?? maximum!.Value) * 0.05);
        return values.Any(value =>
            minimum is not null && value < minimum.Value - tolerance ||
            maximum is not null && value > maximum.Value + tolerance);
    }

    private static void ValidateOptions(ForecastingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.StepMinutes <= 0 || 60 % options.StepMinutes != 0)
            throw new ArgumentOutOfRangeException(nameof(options), "StepMinutes must be a positive divisor of one hour.");
        if (!options.HasValidHistoryConfiguration())
            throw new ArgumentOutOfRangeException(nameof(options), "Training and minimum history windows are invalid.");
        if (!double.IsFinite(options.MinimumCoverage) || options.MinimumCoverage is <= 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(options), "MinimumCoverage must be in (0, 1].");
        if (!float.IsFinite(options.ConfidenceLevel) || options.ConfidenceLevel is <= 0 or >= 1)
            throw new ArgumentOutOfRangeException(nameof(options), "ConfidenceLevel must be in (0, 1).");
        if (options.BacktestFolds < 3)
            throw new ArgumentOutOfRangeException(nameof(options), "At least three backtest folds are required.");
        if (!double.IsFinite(options.MaximumMase) ||
            options.MaximumMase <= 0 ||
            !double.IsFinite(options.MinimumIntervalCoverage) ||
            options.MinimumIntervalCoverage is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(options), "Quality thresholds are invalid.");
    }
}
