namespace MeshSMO.Sensors.Forecasting.Evaluation;

public static class NaiveForecasters
{
    public static float[] LastValue(IReadOnlyList<float> training, int horizon)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(horizon);
        if (training.Count == 0)
            throw new ArgumentException("Training data is required.", nameof(training));

        return Enumerable.Repeat(training[^1], horizon).ToArray();
    }

    public static float[] Seasonal(IReadOnlyList<float> training, int horizon, int seasonLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(horizon);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seasonLength);
        if (training.Count < seasonLength)
            throw new ArgumentException("Training data must contain at least one full season.", nameof(training));

        var result = new float[horizon];
        var seasonStart = training.Count - seasonLength;
        for (var index = 0; index < horizon; index++)
            result[index] = training[seasonStart + (index % seasonLength)];

        return result;
    }

    public static float[] SeasonalMedian(
        IReadOnlyList<float> training,
        int horizon,
        int seasonLength,
        int maximumSeasons = 7)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(horizon);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seasonLength);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumSeasons);
        if (training.Count < seasonLength)
            throw new ArgumentException("Training data must contain at least one full season.", nameof(training));

        var result = new float[horizon];
        for (var index = 0; index < horizon; index++)
        {
            var targetIndex = training.Count + index;
            var samples = new List<float>(maximumSeasons);
            for (var season = 1; season <= maximumSeasons; season++)
            {
                var sourceIndex = targetIndex - (season * seasonLength);
                if (sourceIndex >= training.Count)
                    continue;
                if (sourceIndex < 0)
                    break;

                samples.Add(training[sourceIndex]);
            }

            samples.Sort();
            var middle = samples.Count / 2;
            result[index] = samples.Count % 2 == 0
                ? (samples[middle - 1] + samples[middle]) / 2
                : samples[middle];
        }

        return result;
    }
}
