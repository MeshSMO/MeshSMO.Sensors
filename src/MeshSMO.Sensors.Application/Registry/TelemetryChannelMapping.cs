namespace MeshSMO.Sensors.Application.Registry;

/// <summary>
/// Maps a decoded telemetry value (LPP channel + type) to a public metric key
/// with presentation metadata. Configured in the sensor registry YAML:
/// <code>
/// telemetry:
///   channels:
///     - channel: 2
///       type: voltage
///       metric: solar_panel_voltage
///       displayName: "Напряжение солнечной панели"
///       unit: "В"
/// </code>
/// </summary>
public sealed record TelemetryChannelMapping(int Channel, string? Type, string Metric, string? DisplayName, string? Unit);

public static class TelemetryTypes
{
    /// <summary>All LPP type keys the decoder can emit (including multi-value components).</summary>
    public static readonly IReadOnlySet<string> KnownTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "digital_input", "digital_output", "analog_input", "analog_output",
        "generic", "luminosity", "presence",
        "temperature", "humidity", "pressure", "altitude",
        "voltage", "current", "power", "frequency", "percentage",
        "concentration", "distance", "energy", "direction", "unixtime",
        "accel_x", "accel_y", "accel_z",
        "gyro_x", "gyro_y", "gyro_z",
        "colour_r", "colour_g", "colour_b",
        "gps_lat", "gps_lon", "gps_alt",
        "switch", "*",
    };
}
