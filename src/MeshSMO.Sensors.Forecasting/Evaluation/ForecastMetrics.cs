namespace MeshSMO.Sensors.Forecasting.Evaluation;

public static class ForecastMetrics
{
    public static ForecastMetricValues Evaluate(
        IReadOnlyList<float> actual,
        IReadOnlyList<float> predicted,
        IReadOnlyList<float>? lower = null,
        IReadOnlyList<float>? upper = null)
    {
        if (actual.Count == 0 || predicted.Count != actual.Count)
            throw new ArgumentException("Actual and predicted values must have the same non-zero length.", nameof(predicted));
        if ((lower is null) != (upper is null) || lower is not null && lower.Count != actual.Count || upper is not null && upper.Count != actual.Count)
            throw new ArgumentException("Interval bounds must both match the actual values length.", nameof(lower));

        var absoluteError = 0d;
        var squaredError = 0d;
        var insideInterval = 0;
        for (var index = 0; index < actual.Count; index++)
        {
            var error = actual[index] - predicted[index];
            absoluteError += Math.Abs(error);
            squaredError += error * error;
            if (lower is not null && upper is not null && actual[index] >= lower[index] && actual[index] <= upper[index])
                insideInterval++;
        }

        return new(
            absoluteError / actual.Count,
            Math.Sqrt(squaredError / actual.Count),
            lower is null ? double.NaN : (double)insideInterval / actual.Count,
            actual.Count);
    }

    public static double RelativeMae(double modelMae, double baselineMae)
    {
        const double epsilon = 1e-9;
        if (baselineMae <= epsilon)
            return modelMae <= epsilon ? 1 : double.PositiveInfinity;

        return modelMae / baselineMae;
    }
}
