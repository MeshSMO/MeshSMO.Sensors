using System.Buffers.Binary;
using System.Globalization;

namespace MeshSMO.Sensors.Gateway.Polling;

public sealed record LppValue(int Channel, string TypeKey, double Value, string Unit);

/// <summary>
/// Decoder for Cayenne LPP payloads returned by MeshCore sensor nodes
/// (the telemetry reply body after the reflected 4-byte timestamp).
/// Sizes, scale factors and type ids follow the ElectronicCats CayenneLPP
/// 1.6.1 table used by MeshCore (see docs "MeshCore telemetry via CayenneLPP").
/// </summary>
public static class CayenneLppDecoder
{
    public static IReadOnlyList<LppValue> Decode(ReadOnlySpan<byte> payload)
    {
        var values = new List<LppValue>();
        var offset = 0;
        while (offset + 2 <= payload.Length)
        {
            // MeshCore telemetry replies are zero-padded to a fixed buffer size;
            // a tail of zero bytes is padding, not a stream of zero records.
            if (IsAllZeros(payload.Slice(offset)))
                break;

            var channel = payload[offset];
            var type = payload[offset + 1];
            if (type == 0xF0)
                break; // polyline: variable length, not used by MeshCore telemetry replies

            var dataLength = DataSizeOf(type);
            if (dataLength is null || offset + 2 + dataLength.Value > payload.Length)
                break; // trailing padding or unknown type

            var data = payload.Slice(offset + 2, dataLength.Value);
            AppendValues(values, channel, type, data);
            offset += 2 + dataLength.Value;
        }

        return values;
    }

    private static bool IsAllZeros(ReadOnlySpan<byte> span)
    {
        foreach (var b in span)
        {
            if (b != 0)
                return false;
        }

        return true;
    }

    private static int? DataSizeOf(byte type) => type switch
    {
        0x00 or 0x01 or 0x66 or 0x8E => 1,   // digital in/out, presence, switch
        0x02 or 0x03 => 2,                    // analog in/out, 0.01 signed
        0x64 => 4,                            // generic sensor, unsigned
        0x65 => 2,                            // luminosity, lux
        0x67 => 2,                            // temperature, 0.1 °C signed
        0x68 => 1,                            // relative humidity, 0.5 %
        0x71 => 6,                            // accelerometer, 3 × 0.001 G signed
        0x73 => 2,                            // barometric pressure, 0.1 hPa
        0x74 => 2,                            // voltage, 0.01 V
        0x75 => 2,                            // current, 0.001 A
        0x76 => 4,                            // frequency, 1 Hz
        0x78 => 1,                            // percentage, 1 %
        0x79 => 2,                            // altitude, 1 m signed
        0x7D => 2,                            // concentration, 1 ppm
        0x80 => 2,                            // power, 1 W
        0x82 => 4,                            // distance, 0.001 m
        0x83 => 4,                            // energy, 0.001 kWh
        0x84 => 2,                            // direction, 1 °
        0x85 => 4,                            // unix time, s
        0x86 => 6,                            // gyrometer, 3 × 0.01 °/s signed
        0x87 => 3,                            // colour, r/g/b
        0x88 => 9,                            // gps, lat/lon 0.0001°, altitude 0.01 m (24-bit fields)
        _ => null,
    };

    private static void AppendValues(List<LppValue> values, int channel, byte type, ReadOnlySpan<byte> data)
    {
        switch (type)
        {
            case 0x00:
                Add(values, channel, "digital_input", data[0]);
                break;
            case 0x01:
                Add(values, channel, "digital_output", data[0]);
                break;
            case 0x02:
                Add(values, channel, "analog_input", BinaryPrimitives.ReadInt16BigEndian(data) / 100.0, "");
                break;
            case 0x03:
                Add(values, channel, "analog_output", BinaryPrimitives.ReadInt16BigEndian(data) / 100.0, "");
                break;
            case 0x64:
                Add(values, channel, "generic", BinaryPrimitives.ReadUInt32BigEndian(data), "");
                break;
            case 0x65:
                Add(values, channel, "luminosity", BinaryPrimitives.ReadUInt16BigEndian(data), "lux");
                break;
            case 0x66:
                Add(values, channel, "presence", data[0]);
                break;
            case 0x67:
                Add(values, channel, "temperature", BinaryPrimitives.ReadInt16BigEndian(data) / 10.0, "°C");
                break;
            case 0x68:
                Add(values, channel, "humidity", data[0] / 2.0, "%");
                break;
            case 0x71:
                Add(values, channel, "accel_x", BinaryPrimitives.ReadInt16BigEndian(data) / 1000.0, "G");
                Add(values, channel, "accel_y", BinaryPrimitives.ReadInt16BigEndian(data.Slice(2)) / 1000.0, "G");
                Add(values, channel, "accel_z", BinaryPrimitives.ReadInt16BigEndian(data.Slice(4)) / 1000.0, "G");
                break;
            case 0x73:
                Add(values, channel, "pressure", BinaryPrimitives.ReadUInt16BigEndian(data) / 10.0, "hPa");
                break;
            case 0x74:
                Add(values, channel, "voltage", BinaryPrimitives.ReadUInt16BigEndian(data) / 100.0, "V");
                break;
            case 0x75:
                Add(values, channel, "current", BinaryPrimitives.ReadUInt16BigEndian(data) / 1000.0, "A");
                break;
            case 0x76:
                Add(values, channel, "frequency", BinaryPrimitives.ReadUInt32BigEndian(data), "Hz");
                break;
            case 0x78:
                Add(values, channel, "percentage", data[0], "%");
                break;
            case 0x79:
                Add(values, channel, "altitude", BinaryPrimitives.ReadInt16BigEndian(data), "m");
                break;
            case 0x7D:
                Add(values, channel, "concentration", BinaryPrimitives.ReadUInt16BigEndian(data), "ppm");
                break;
            case 0x80:
                Add(values, channel, "power", BinaryPrimitives.ReadUInt16BigEndian(data), "W");
                break;
            case 0x82:
                Add(values, channel, "distance", BinaryPrimitives.ReadUInt32BigEndian(data) / 1000.0, "m");
                break;
            case 0x83:
                Add(values, channel, "energy", BinaryPrimitives.ReadUInt32BigEndian(data) / 1000.0, "kWh");
                break;
            case 0x84:
                Add(values, channel, "direction", BinaryPrimitives.ReadUInt16BigEndian(data), "°");
                break;
            case 0x85:
                Add(values, channel, "unixtime", BinaryPrimitives.ReadUInt32BigEndian(data), "s");
                break;
            case 0x86:
                Add(values, channel, "gyro_x", BinaryPrimitives.ReadInt16BigEndian(data) / 100.0, "°/s");
                Add(values, channel, "gyro_y", BinaryPrimitives.ReadInt16BigEndian(data.Slice(2)) / 100.0, "°/s");
                Add(values, channel, "gyro_z", BinaryPrimitives.ReadInt16BigEndian(data.Slice(4)) / 100.0, "°/s");
                break;
            case 0x87:
                Add(values, channel, "colour_r", data[0], "");
                Add(values, channel, "colour_g", data[1], "");
                Add(values, channel, "colour_b", data[2], "");
                break;
            case 0x88:
                Add(values, channel, "gps_lat", ReadInt24(data) / 10000.0, "°");
                Add(values, channel, "gps_lon", ReadInt24(data.Slice(3)) / 10000.0, "°");
                Add(values, channel, "gps_alt", ReadInt24(data.Slice(6)) / 100.0, "m");
                break;
            case 0x8E:
                Add(values, channel, "switch", data[0]);
                break;
        }
    }

    private static int ReadInt24(ReadOnlySpan<byte> data) =>
        (data[0] << 16) | (data[1] << 8) | data[2] | ((data[0] & 0x80) == 0 ? 0 : unchecked((int)0xFF000000));

    private static void Add(List<LppValue> values, int channel, string typeKey, double value, string unit = "") =>
        values.Add(new LppValue(channel, typeKey, value, unit));

    /// <summary>
    /// Resolves the metric key for a decoded value: an explicit channel mapping
    /// (from the sensor registry) wins, otherwise the LPP type key is used with
    /// a channel suffix when the same type appears on several channels.
    /// </summary>
    public static string ResolveMetricKey(
        LppValue value,
        IReadOnlyDictionary<(int Channel, string TypeKey), string> mappings,
        IReadOnlySet<string> typesSeenMoreThanOnce)
    {
        if (mappings.TryGetValue((value.Channel, value.TypeKey), out var mapped))
            return mapped;

        if (mappings.TryGetValue((value.Channel, "*"), out var channelWide))
            return channelWide;

        return typesSeenMoreThanOnce.Contains(value.TypeKey)
            ? $"{value.TypeKey}_{value.Channel.ToString(CultureInfo.InvariantCulture)}"
            : value.TypeKey;
    }
}
