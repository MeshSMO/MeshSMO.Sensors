using MeshSMO.Sensors.Forecasting.Configuration;
using MeshSMO.Sensors.Forecasting.Models;

namespace MeshSMO.Sensors.Forecasting.Preparation;

public sealed class SeriesPreparer(ForecastingOptions options)
{
    private const int MaximumInterpolatedGap = 2;

    public PreparedSeries Prepare(
        ForecastSeries series,
        DateTimeOffset generatedAt,
        int minimumHistoryDays)
    {
        var step = TimeSpan.FromMinutes(options.StepMinutes);
        if (!options.Enabled)
            return Unavailable(ForecastAvailability.Disabled, "Forecasting is disabled.", generatedAt, step);
        if (series.LastObservationAt is null || series.Observations.Count == 0)
            return Unavailable(ForecastAvailability.InsufficientData, "The series has no numeric observations.", generatedAt, step);

        var staleAfter = TimeSpan.FromTicks(Math.Max(
            TimeSpan.FromMinutes(15).Ticks,
            series.PollInterval.Ticks * 3));
        if (generatedAt - series.LastObservationAt.Value > staleAfter)
        {
            return Unavailable(
                ForecastAvailability.StaleData,
                "The latest observation is too old for a current forecast.",
                generatedAt,
                step);
        }

        var currentBucket = Floor(generatedAt, step);
        var lastCompleteBucket = currentBucket - step;
        var windowStart = currentBucket - TimeSpan.FromDays(options.TrainingWindowDays);
        var buckets = series.Observations
            .Where(observation => observation.Timestamp >= windowStart && observation.Timestamp < currentBucket)
            .Where(static observation => double.IsFinite(observation.Value))
            .Where(static observation => Math.Abs(observation.Value) <= float.MaxValue)
            .Where(observation => IsInsideBounds(observation.Value, series.Minimum, series.Maximum))
            .GroupBy(observation => Floor(observation.Timestamp, step))
            .ToDictionary(
                static group => group.Key,
                static group => group.Average(observation => observation.Value));
        if (buckets.Count == 0)
            return Unavailable(ForecastAvailability.InsufficientData, "The training window has no usable values.", generatedAt, step);

        var observedBuckets = buckets.Keys.Order().ToArray();
        var segmentStart = observedBuckets[0];
        for (var index = 1; index < observedBuckets.Length; index++)
        {
            var missing = StepsBetween(observedBuckets[index - 1], observedBuckets[index], step) - 1;
            if (missing > MaximumInterpolatedGap)
                segmentStart = observedBuckets[index];
        }

        var trailingMissing = StepsBetween(observedBuckets[^1], lastCompleteBucket, step);
        if (trailingMissing > MaximumInterpolatedGap)
        {
            return Unavailable(
                ForecastAvailability.SparseData,
                "The latest complete portion of the series contains a long gap.",
                generatedAt,
                step);
        }

        var expectedCount = StepsBetween(segmentStart, lastCompleteBucket, step) + 1;
        var minimumCount = checked((int)(TimeSpan.FromDays(minimumHistoryDays).Ticks / step.Ticks));
        if (expectedCount < minimumCount)
        {
            return Unavailable(
                ForecastAvailability.InsufficientData,
                $"At least {minimumHistoryDays} days of history are required for this forecast horizon.",
                generatedAt,
                step);
        }

        var values = new double?[expectedCount];
        var observedCount = 0;
        for (var index = 0; index < expectedCount; index++)
        {
            var timestamp = segmentStart + TimeSpan.FromTicks(step.Ticks * index);
            if (!buckets.TryGetValue(timestamp, out var value))
                continue;

            values[index] = value;
            observedCount++;
        }

        var coverage = (double)observedCount / expectedCount;
        if (coverage < options.MinimumCoverage)
        {
            return Unavailable(
                ForecastAvailability.SparseData,
                $"Observed coverage {coverage:P0} is below the required {options.MinimumCoverage:P0}.",
                generatedAt,
                step);
        }

        var interpolated = FillSmallGaps(values);
        if (values.Any(static value => value is null))
        {
            return Unavailable(
                ForecastAvailability.SparseData,
                "The latest complete portion of the series contains an unfillable gap.",
                generatedAt,
                step);
        }

        var preparedValues = values
            .Select(static value => checked((float)value!.Value))
            .ToArray();
        var forecastFrom = Ceiling(generatedAt, step);
        var offsetSteps = StepsBetween(lastCompleteBucket, forecastFrom, step) - 1;
        return new(
            ForecastAvailability.Ready,
            null,
            preparedValues,
            segmentStart,
            lastCompleteBucket,
            forecastFrom,
            Math.Max(0, offsetSteps),
            coverage,
            interpolated);
    }

    private static int FillSmallGaps(double?[] values)
    {
        var interpolated = 0;
        var index = 0;
        while (index < values.Length)
        {
            if (values[index] is not null)
            {
                index++;
                continue;
            }

            var start = index;
            while (index < values.Length && values[index] is null)
                index++;

            var length = index - start;
            if (length > MaximumInterpolatedGap || start == 0)
                continue;

            var left = values[start - 1]!.Value;
            if (index == values.Length)
            {
                for (var gapIndex = start; gapIndex < index; gapIndex++)
                    values[gapIndex] = left;
            }
            else
            {
                var right = values[index]!.Value;
                for (var gapIndex = 0; gapIndex < length; gapIndex++)
                {
                    var fraction = (double)(gapIndex + 1) / (length + 1);
                    values[start + gapIndex] = left + ((right - left) * fraction);
                }
            }

            interpolated += length;
        }

        return interpolated;
    }

    private static bool IsInsideBounds(double value, double? minimum, double? maximum) =>
        (minimum is null || value >= minimum.Value) &&
        (maximum is null || value <= maximum.Value);

    private static DateTimeOffset Floor(DateTimeOffset value, TimeSpan step)
    {
        var utcTicks = value.ToUniversalTime().Ticks;
        return new(utcTicks - (utcTicks % step.Ticks), TimeSpan.Zero);
    }

    private static DateTimeOffset Ceiling(DateTimeOffset value, TimeSpan step)
    {
        var floor = Floor(value, step);
        return floor + step;
    }

    private static int StepsBetween(DateTimeOffset left, DateTimeOffset right, TimeSpan step) =>
        checked((int)((right - left).Ticks / step.Ticks));

    private static PreparedSeries Unavailable(
        ForecastAvailability availability,
        string reason,
        DateTimeOffset generatedAt,
        TimeSpan step) =>
        new(availability, reason, [], null, null, Ceiling(generatedAt, step), 0, 0, 0);
}
