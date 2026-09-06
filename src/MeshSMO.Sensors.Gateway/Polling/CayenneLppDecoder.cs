using System.Buffers.Binary;
using System.Globalization;

namespace MeshSMO.Sensors.Gateway.Polling;

public sealed record LppValue(int Channel, string MetricKey, double Value, string Unit);

/// <summary>
/// Decoder for Cayenne LPP payloads returned by MeshCore sensor nodes
/// (the telemetry reply body after the reflected 4-byte timestamp).
/// </summary>
public static class CayenneLppDecoder
{
    public static IReadOnlyList<LppValue> Decode(ReadOnlySpan<byte> payload)
    {
        var values = new List<LppValue>();
        var channelUsage = new Dictionary<string, int>();
        var offset = 0;
        while (offset + 2 <= payload.Length)
        {
            var channel = payload[offset];
            var type = payload[offset + 1];
            var valueSize = SizeOf(type);
            if (valueSize is null || offset + 2 + valueSize.Value > payload.Length)
            {
                break; // trailing padding or unknown type
            }

            var raw = payload.Slice(offset + 2, valueSize.Value);
            var (value, unit) = DecodeValue(type, raw);
            if (value is not null)
            {
                var baseKey = MetricKeyOf(type);
                var usage = channelUsage.TryGetValue(baseKey, out var count) ? count : 0;
                channelUsage[baseKey] = usage + 1;
                var key = usage == 0 ? baseKey : $"{baseKey}_{channel.ToString(CultureInfo.InvariantCulture)}";
                // A second occurrence of the same type gets the channel suffix;
                // later duplicates of the same channel keep incrementing the suffix.
                if (usage > 1)
                {
                    key = $"{baseKey}_{channel}_{usage}";
                }

                values.Add(new LppValue(channel, key, value.Value, unit));
            }

            offset += 2 + valueSize.Value;
        }

        return values;
    }

    private static int? SizeOf(byte type) => type switch
    {
        0x67 => 2, // temperature, 0.1 °C signed
        0x68 => 1, // relative humidity, 0.5 %
        0x73 => 2, // barometric pressure, 0.1 hPa
        0x74 => 2, // voltage, 0.01 V
        0x77 => 2, // current, 1 mA
        0x02 => 2, // generic analog input, 0.01
        _ => null,
    };

    private static (double? Value, string Unit) DecodeValue(byte type, ReadOnlySpan<byte> raw) => type switch
    {
        0x67 => (BinaryPrimitives.ReadInt16BigEndian(raw) / 10.0, "°C"),
        0x68 => (raw[0] / 2.0, "%"),
        0x73 => (BinaryPrimitives.ReadUInt16BigEndian(raw) / 10.0, "hPa"),
        0x74 => (BinaryPrimitives.ReadUInt16BigEndian(raw) / 100.0, "V"),
        0x77 => (BinaryPrimitives.ReadUInt16BigEndian(raw) / 1000.0, "A"),
        0x02 => (BinaryPrimitives.ReadUInt16BigEndian(raw) / 100.0, ""),
        _ => (null, string.Empty),
    };

    private static string MetricKeyOf(byte type) => type switch
    {
        0x67 => "temperature",
        0x68 => "humidity",
        0x73 => "pressure",
        0x74 => "voltage",
        0x77 => "current",
        0x02 => "analog",
        _ => $"lpp_{type:X2}",
    };
}
