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
    private const string LastValueModel = "last_value";
    private const string SeasonalNaiveModel = "seasonal_naive";
    private const string SeasonalMedianModel = "seasonal_median";
    private const string SsaModel = "ssa";

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
        var minimumTrainSeasons = _options.LenientMode ? 1 : 2;
        if (initialTrainSize < seasonLength * minimumTrainSeasons)
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
        var evaluations = EvaluateCandidates(
            prepared.Values,
            horizonPoints,
            seasonLength,
            windowSizes,
            cancellationToken);
        var eligibleEvaluations = _options.LenientMode
            ? evaluations
            : evaluations.Where(candidate =>
                !string.Equals(candidate.ModelKind, SsaModel, StringComparison.Ordinal) ||
                candidate.Mase <= _options.MaximumMase);
        var best = eligibleEvaluations
            .OrderBy(static candidate => candidate.Metrics.Mae)
            .ThenBy(static candidate => candidate.Metrics.Rmse)
            .ThenBy(static candidate => ModelPreference(candidate.ModelKind))
            .ThenBy(static candidate => candidate.WindowSize)
            .First();
        var diagnostics = new ForecastDiagnostics(
            best.ModelKind,
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

        var qualityReason = _options.LenientMode ? null : QualityReason(best, series);
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
        var output = ForecastCandidate(prepared.Values, outputHorizon, seasonLength, best);
        if (output is null ||
            output.Length != outputHorizon ||
            output.Any(static value => !float.IsFinite(value)) ||
            (!_options.LenientMode && HasMaterialBoundViolation(
                output.Skip(prepared.ForecastOffsetSteps).Take(horizonPoints),
                series.Minimum,
                series.Maximum)))
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
            var predicted = Clamp(output[outputIndex], series.Minimum, series.Maximum);
            var lower = Clamp(output[outputIndex] + best.LowerResiduals[index], series.Minimum, series.Maximum);
            var upper = Clamp(output[outputIndex] + best.UpperResiduals[index], series.Minimum, series.Maximum);
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

    private IReadOnlyList<ForecastCandidateEvaluation> EvaluateCandidates(
        IReadOnlyList<float> values,
        int horizon,
        int seasonLength,
        IReadOnlyList<int> windowSizes,
        CancellationToken cancellationToken)
    {
        var actual = new List<float>(_options.BacktestFolds * horizon);
        var lastValuePredicted = new List<float>(_options.BacktestFolds * horizon);
        var seasonalPredicted = new List<float>(_options.BacktestFolds * horizon);
        var seasonalMedianPredicted = new List<float>(_options.BacktestFolds * horizon);
        for (var fold = 0; fold < _options.BacktestFolds; fold++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var trainSize = values.Count - ((_options.BacktestFolds - fold) * horizon);
            var training = values.Take(trainSize).ToArray();
            actual.AddRange(values.Skip(trainSize).Take(horizon));
            lastValuePredicted.AddRange(NaiveForecasters.LastValue(training, horizon));
            seasonalPredicted.AddRange(NaiveForecasters.Seasonal(training, horizon, seasonLength));
            seasonalMedianPredicted.AddRange(NaiveForecasters.SeasonalMedian(training, horizon, seasonLength));
        }

        var lastValueMetrics = ForecastMetrics.Evaluate(actual, lastValuePredicted);
        var seasonalMetrics = ForecastMetrics.Evaluate(actual, seasonalPredicted);
        var seasonalMedianMetrics = ForecastMetrics.Evaluate(actual, seasonalMedianPredicted);
        var baselineMae = Math.Min(lastValueMetrics.Mae, Math.Min(seasonalMetrics.Mae, seasonalMedianMetrics.Mae));
        var evaluations = new List<ForecastCandidateEvaluation>(windowSizes.Count + 3)
        {
            CalibrateCandidate(LastValueModel, null, actual, lastValuePredicted, baselineMae, horizon),
            CalibrateCandidate(SeasonalNaiveModel, null, actual, seasonalPredicted, baselineMae, horizon),
            CalibrateCandidate(SeasonalMedianModel, null, actual, seasonalMedianPredicted, baselineMae, horizon),
        };

        foreach (var windowSize in windowSizes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var evaluation = EvaluateSsaCandidate(
                values,
                horizon,
                windowSize,
                baselineMae,
                cancellationToken);
            if (evaluation is not null)
                evaluations.Add(evaluation);
        }

        return evaluations;
    }

    private ForecastCandidateEvaluation? EvaluateSsaCandidate(
        IReadOnlyList<float> values,
        int horizon,
        int windowSize,
        double baselineMae,
        CancellationToken cancellationToken)
    {
        var actual = new List<float>(_options.BacktestFolds * horizon);
        var predicted = new List<float>(_options.BacktestFolds * horizon);
        for (var fold = 0; fold < _options.BacktestFolds; fold++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var trainSize = values.Count - ((_options.BacktestFolds - fold) * horizon);
            var training = values.Take(trainSize).ToArray();
            var output = ForecastBySsa(training, horizon, windowSize);
            if (!HasValidOutput(output, horizon))
                return null;

            actual.AddRange(values.Skip(trainSize).Take(horizon));
            predicted.AddRange(output.Forecast);
        }

        return CalibrateCandidate(SsaModel, windowSize, actual, predicted, baselineMae, horizon);
    }

    private ForecastCandidateEvaluation CalibrateCandidate(
        string modelKind,
        int? windowSize,
        IReadOnlyList<float> actual,
        IReadOnlyList<float> predicted,
        double baselineMae,
        int horizon)
    {
        var (lowerResiduals, upperResiduals) = CalibrateResiduals(actual, predicted, horizon);
        var lower = new float[predicted.Count];
        var upper = new float[predicted.Count];
        for (var index = 0; index < predicted.Count; index++)
        {
            var lead = index % horizon;
            lower[index] = ToFiniteFloat(predicted[index] + lowerResiduals[lead]);
            upper[index] = ToFiniteFloat(predicted[index] + upperResiduals[lead]);
        }

        var metrics = ForecastMetrics.Evaluate(actual, predicted, lower, upper);
        return new(
            modelKind,
            windowSize,
            metrics,
            ForecastMetrics.RelativeMae(metrics.Mae, baselineMae),
            lowerResiduals,
            upperResiduals);
    }

    private (IReadOnlyList<double> Lower, IReadOnlyList<double> Upper) CalibrateResiduals(
        IReadOnlyList<float> actual,
        IReadOnlyList<float> predicted,
        int horizon)
    {
        var foldCount = actual.Count / horizon;
        var neighborhoodRadius = Math.Clamp(horizon / 48, 1, 6);
        var lower = new double[horizon];
        var upper = new double[horizon];
        var tailProbability = (1 - _options.ConfidenceLevel) / 2;
        for (var lead = 0; lead < horizon; lead++)
        {
            var firstNeighbor = Math.Max(0, lead - neighborhoodRadius);
            var lastNeighbor = Math.Min(horizon - 1, lead + neighborhoodRadius);
            var residuals = new List<double>(foldCount * ((lastNeighbor - firstNeighbor) + 1));
            for (var fold = 0; fold < foldCount; fold++)
            {
                for (var neighbor = firstNeighbor; neighbor <= lastNeighbor; neighbor++)
                {
                    var index = (fold * horizon) + neighbor;
                    residuals.Add(actual[index] - predicted[index]);
                }
            }

            residuals.Sort();
            lower[lead] = Quantile(residuals, tailProbability);
            upper[lead] = Quantile(residuals, 1 - tailProbability);
        }

        return (lower, upper);
    }

    private float[]? ForecastCandidate(
        IReadOnlyList<float> values,
        int horizon,
        int seasonLength,
        ForecastCandidateEvaluation candidate) => candidate.ModelKind switch
        {
            LastValueModel => NaiveForecasters.LastValue(values, horizon),
            SeasonalNaiveModel => NaiveForecasters.Seasonal(values, horizon, seasonLength),
            SeasonalMedianModel => NaiveForecasters.SeasonalMedian(values, horizon, seasonLength),
            SsaModel => ForecastSsaValues(values, horizon, candidate.WindowSize!.Value),
            _ => throw new ArgumentOutOfRangeException(nameof(candidate), candidate.ModelKind, null),
        };

    private float[]? ForecastSsaValues(IReadOnlyList<float> values, int horizon, int windowSize)
    {
        var output = ForecastBySsa(values, horizon, windowSize);
        return HasValidOutput(output, horizon) ? output.Forecast : null;
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

    private string? QualityReason(ForecastCandidateEvaluation evaluation, ForecastSeries series)
    {
        if (!double.IsFinite(evaluation.Mase))
            return "The forecast could not be compared with the naive baseline.";
        if (series.MaximumMae is not null && evaluation.Metrics.Mae > series.MaximumMae.Value)
            return "The forecast error exceeds the configured limit for this series.";
        if (!double.IsFinite(evaluation.Metrics.IntervalCoverage) ||
            evaluation.Metrics.IntervalCoverage < _options.MinimumIntervalCoverage)
        {
            return "The forecast interval coverage is below the configured quality threshold.";
        }

        return null;
    }

    private static int ModelPreference(string modelKind) => modelKind switch
    {
        SeasonalMedianModel => 0,
        SeasonalNaiveModel => 1,
        LastValueModel => 2,
        SsaModel => 3,
        _ => int.MaxValue,
    };

    private static double Quantile(IReadOnlyList<double> sortedValues, double probability)
    {
        var position = (sortedValues.Count - 1) * probability;
        var lowerIndex = (int)Math.Floor(position);
        var upperIndex = (int)Math.Ceiling(position);
        if (lowerIndex == upperIndex)
            return sortedValues[lowerIndex];

        var fraction = position - lowerIndex;
        return sortedValues[lowerIndex] + ((sortedValues[upperIndex] - sortedValues[lowerIndex]) * fraction);
    }

    private static float ToFiniteFloat(double value) =>
        (float)Math.Clamp(value, -float.MaxValue, float.MaxValue);

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
