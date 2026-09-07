namespace MeshSMO.Sensors.Forecasting;

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
}
