namespace MeshSMO.Sensors.Web.Api.MeasurementHistory;

public static class RobustSeriesAnomalyDetector
{
    public const string TemperatureAnomalyCode = "temperature_outlier";
    public const string HumidityAnomalyCode = "humidity_outlier";

    private const int WindowRadius = 6;
    private const int MinimumNeighborCount = 6;
    private const double MadScaleFactor = 1.4826;
    private const double StandardDeviationThreshold = 3.5;
    private const double TemperatureMinimumDeviation = 3;
    private const double HumidityMinimumDeviation = 10;

    public static bool Supports(string metricKey, MeasurementResolution resolution) =>
        MetricParameters(metricKey) is not null &&
        resolution is MeasurementResolution.Raw or
            MeasurementResolution.FiveMinutes or
            MeasurementResolution.FifteenMinutes;

    public static TimeSpan ContextLookback(
        MeasurementResolution resolution,
        int pollIntervalSeconds)
    {
        var intervalSeconds = resolution == MeasurementResolution.Raw
            ? Math.Max(pollIntervalSeconds, 60)
            : MeasurementResolutionPolicy.BucketSeconds(resolution);
        return TimeSpan.FromSeconds(intervalSeconds * WindowRadius * 2d);
    }

    public static IReadOnlyDictionary<DateTimeOffset, MeasurementAnomaly> Detect(
        string metricKey,
        MeasurementResolution resolution,
        IReadOnlyList<MeasurementHistoryPoint> points)
    {
        var parameters = MetricParameters(metricKey);
        if (parameters is null || !Supports(metricKey, resolution))
            return new Dictionary<DateTimeOffset, MeasurementAnomaly>();

        var anomalies = new Dictionary<DateTimeOffset, MeasurementAnomaly>();
        for (var index = 0; index < points.Count; index++)
        {
            var point = points[index];
            var neighbors = NeighborValues(points, index);
            if (neighbors.Length < MinimumNeighborCount)
                continue;

            var expected = Median(neighbors);
            var mad = Median(neighbors.Select(value => Math.Abs(value - expected)));
            var threshold = Math.Max(
                parameters.Value.MinimumDeviation,
                StandardDeviationThreshold * MadScaleFactor * mad);
            var deviation = Math.Abs(point.Avg - expected);
            if (deviation > threshold)
            {
                anomalies.Add(
                    point.Timestamp,
                    new(
                        parameters.Value.Code,
                        "warning",
                        point.Avg,
                        expected,
                        threshold,
                        null));
            }
        }

        return anomalies;
    }

    private static (string Code, double MinimumDeviation)? MetricParameters(string metricKey) =>
        metricKey switch
        {
            "temperature" => (TemperatureAnomalyCode, TemperatureMinimumDeviation),
            "humidity" => (HumidityAnomalyCode, HumidityMinimumDeviation),
            _ => null,
        };

    private static double[] NeighborValues(
        IReadOnlyList<MeasurementHistoryPoint> points,
        int index)
    {
        var from = Math.Max(0, index - WindowRadius);
        var to = Math.Min(points.Count - 1, index + WindowRadius);
        var values = new List<double>(to - from);
        for (var neighborIndex = from; neighborIndex <= to; neighborIndex++)
        {
            if (neighborIndex != index)
                values.Add(points[neighborIndex].Avg);
        }

        return [.. values];
    }

    private static double Median(IEnumerable<double> source)
    {
        var values = source.Order().ToArray();
        var midpoint = values.Length / 2;
        return values.Length % 2 == 0
            ? (values[midpoint - 1] + values[midpoint]) / 2
            : values[midpoint];
    }
}
