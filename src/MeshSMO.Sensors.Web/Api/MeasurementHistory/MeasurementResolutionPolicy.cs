namespace MeshSMO.Sensors.Web.Api.MeasurementHistory;

/// <summary>
/// Downsampling policy (spec §15): never send more than a few thousand points
/// to the browser; resolution=auto picks the bucket from the requested range.
/// </summary>
public static class MeasurementResolutionPolicy
{
    /// <summary>Upper bound of a single historical query (resolution=1d keeps this well under ~5k points).</summary>
    public static readonly TimeSpan MaxRange = TimeSpan.FromDays(400);

    /// <summary>Point budget per series; ranges are capped so auto resolution stays below it.</summary>
    public const int MaxPoints = 5_000;

    private static readonly (TimeSpan UpTo, MeasurementResolution Resolution)[] AutoBands =
    [
        (TimeSpan.FromHours(6), MeasurementResolution.Raw),
        (TimeSpan.FromHours(24), MeasurementResolution.FiveMinutes),
        (TimeSpan.FromDays(7), MeasurementResolution.FifteenMinutes),
        (TimeSpan.FromDays(31), MeasurementResolution.OneHour),
        (TimeSpan.FromDays(180), MeasurementResolution.SixHours),
    ];

    /// <summary>
    /// Parses the query parameter. <c>null</c> means "caller asked for auto".
    /// Returns false for values outside the supported set.
    /// </summary>
    public static bool TryParse(string? value, out MeasurementResolution? resolution)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case null or "" or "auto":
                resolution = null;
                return true;
            case "raw":
                resolution = MeasurementResolution.Raw;
                return true;
            case "5m":
                resolution = MeasurementResolution.FiveMinutes;
                return true;
            case "15m":
                resolution = MeasurementResolution.FifteenMinutes;
                return true;
            case "1h":
                resolution = MeasurementResolution.OneHour;
                return true;
            case "6h":
                resolution = MeasurementResolution.SixHours;
                return true;
            case "1d":
                resolution = MeasurementResolution.OneDay;
                return true;
            default:
                resolution = null;
                return false;
        }
    }

    /// <summary>Short ranges stay raw; longer ones get the coarsest bucket that still charts well.</summary>
    public static MeasurementResolution ResolveAuto(TimeSpan range)
    {
        foreach (var (upTo, resolution) in AutoBands)
        {
            if (range <= upTo)
                return resolution;
        }

        return MeasurementResolution.OneDay;
    }

    /// <summary>
    /// Largest queryable range for an explicit resolution; keeps every answer
    /// within the MaxPoints budget (spec §15) even when the caller pins the
    /// resolution instead of using auto.
    /// </summary>
    public static TimeSpan AllowedRange(MeasurementResolution resolution) => resolution switch
    {
        MeasurementResolution.Raw => TimeSpan.FromHours(24),
        _ => TimeSpan.FromSeconds(BucketSeconds(resolution) * (double)MaxPoints),
    };

    public static int BucketSeconds(MeasurementResolution resolution) => resolution switch
    {
        MeasurementResolution.FiveMinutes => 300,
        MeasurementResolution.FifteenMinutes => 900,
        MeasurementResolution.OneHour => 3_600,
        MeasurementResolution.SixHours => 21_600,
        MeasurementResolution.OneDay => 86_400,
        _ => 0,
    };

    public static string ToApiString(MeasurementResolution resolution) => resolution switch
    {
        MeasurementResolution.Raw => "raw",
        MeasurementResolution.FiveMinutes => "5m",
        MeasurementResolution.FifteenMinutes => "15m",
        MeasurementResolution.OneHour => "1h",
        MeasurementResolution.SixHours => "6h",
        MeasurementResolution.OneDay => "1d",
        _ => throw new ArgumentOutOfRangeException(nameof(resolution), resolution, null),
    };
}
