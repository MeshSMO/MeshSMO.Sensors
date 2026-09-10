namespace MeshSMO.Sensors.Web.Api.MeasurementHistory;

public sealed record MeasurementAnomaly(
    string Code,
    string Severity,
    double ObservedValue,
    double ExpectedValue,
    double Threshold,
    double? SolarElevationDegrees);
