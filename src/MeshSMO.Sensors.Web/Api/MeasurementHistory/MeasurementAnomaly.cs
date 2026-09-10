namespace MeshSMO.Sensors.Web.Api.MeasurementHistory;

public sealed record MeasurementAnomaly(
    string Code,
    string Severity,
    double ObservedMaximum,
    double ExpectedMaximum,
    double SolarElevationDegrees);
