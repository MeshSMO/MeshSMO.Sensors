namespace MeshSMO.Sensors.Web.Api.MeasurementHistory;

public static class SolarPanelAnomalyDetector
{
    public const string AnomalyCode = "unexpected_night_voltage";
    public const double NightVoltageThreshold = 0.1;

    private const double NightSolarElevation = -6;
    private const double JulianDateAtUnixEpoch = 2_440_587.5;
    private const double JulianDateAtJ2000 = 2_451_545;

    public static MeasurementAnomaly? Detect(
        string metricKey,
        double? latitude,
        double? longitude,
        MeasurementResolution resolution,
        MeasurementHistoryPoint point)
    {
        if (!string.Equals(metricKey, "solar_panel_voltage", StringComparison.Ordinal) ||
            latitude is null ||
            longitude is null ||
            !SupportsResolution(resolution) ||
            point.Max <= NightVoltageThreshold)
        {
            return null;
        }

        var timestamp = BucketMidpoint(point.Timestamp, resolution);
        var solarElevation = SolarElevationDegrees(timestamp, latitude.Value, longitude.Value);
        return solarElevation <= NightSolarElevation
            ? new(
                AnomalyCode,
                "warning",
                point.Max,
                NightVoltageThreshold,
                solarElevation)
            : null;
    }

    public static double SolarElevationDegrees(
        DateTimeOffset timestamp,
        double latitude,
        double longitude)
    {
        var julianDate = timestamp.ToUnixTimeMilliseconds() / 86_400_000d + JulianDateAtUnixEpoch;
        var daysSinceJ2000 = julianDate - JulianDateAtJ2000;
        var meanLongitude = NormalizeDegrees(280.46 + 0.9856474 * daysSinceJ2000);
        var meanAnomaly = DegreesToRadians(NormalizeDegrees(357.528 + 0.9856003 * daysSinceJ2000));
        var eclipticLongitude = DegreesToRadians(
            meanLongitude + 1.915 * Math.Sin(meanAnomaly) + 0.02 * Math.Sin(2 * meanAnomaly));
        var obliquity = DegreesToRadians(23.439 - 0.0000004 * daysSinceJ2000);
        var rightAscension = Math.Atan2(
            Math.Cos(obliquity) * Math.Sin(eclipticLongitude),
            Math.Cos(eclipticLongitude));
        var declination = Math.Asin(Math.Sin(obliquity) * Math.Sin(eclipticLongitude));
        var siderealTime = DegreesToRadians(NormalizeDegrees(
            280.46061837 + 360.98564736629 * daysSinceJ2000 + longitude));
        var hourAngle = NormalizeRadians(siderealTime - rightAscension);
        var latitudeRadians = DegreesToRadians(latitude);
        var elevation = Math.Asin(
            Math.Sin(latitudeRadians) * Math.Sin(declination) +
            Math.Cos(latitudeRadians) * Math.Cos(declination) * Math.Cos(hourAngle));
        return RadiansToDegrees(elevation);
    }

    private static bool SupportsResolution(MeasurementResolution resolution) =>
        resolution is MeasurementResolution.Raw or
            MeasurementResolution.FiveMinutes or
            MeasurementResolution.FifteenMinutes;

    private static DateTimeOffset BucketMidpoint(
        DateTimeOffset timestamp,
        MeasurementResolution resolution)
    {
        var bucketSeconds = MeasurementResolutionPolicy.BucketSeconds(resolution);
        return bucketSeconds == 0 ? timestamp : timestamp.AddSeconds(bucketSeconds / 2d);
    }

    private static double NormalizeDegrees(double value)
    {
        var normalized = value % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

    private static double NormalizeRadians(double value)
    {
        var normalized = value % (2 * Math.PI);
        if (normalized > Math.PI)
            normalized -= 2 * Math.PI;
        else if (normalized < -Math.PI)
            normalized += 2 * Math.PI;

        return normalized;
    }

    private static double DegreesToRadians(double value) => value * Math.PI / 180;

    private static double RadiansToDegrees(double value) => value * 180 / Math.PI;
}
