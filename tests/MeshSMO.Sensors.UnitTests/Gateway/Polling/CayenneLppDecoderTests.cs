using MeshSMO.Sensors.Application.Registry;
using MeshSMO.Sensors.Gateway.Polling;

namespace MeshSMO.Sensors.UnitTests.Gateway.Polling;

public sealed class CayenneLppDecoderTests
{
    private static IReadOnlyDictionary<(int, string), string> NoMappings { get; } =
        new Dictionary<(int, string), string>();

    [Fact]
    public void Decode_ParsesRealNodeReply()
    {
        // Captured from the physical sensor node: reflected timestamp stripped,
        // body = voltage ch1, voltage ch2, temperature ch3, humidity ch3.
        var payload = Convert.FromHexString("0174018C027401A00367010903685F0000000000000000000000");

        var values = CayenneLppDecoder.Decode(payload);

        Assert.Collection(
            values,
            value => { Assert.Equal(1, value.Channel); Assert.Equal("voltage", value.TypeKey); Assert.Equal(3.96, value.Value, 2); Assert.Equal("V", value.Unit); },
            value => { Assert.Equal(2, value.Channel); Assert.Equal("voltage", value.TypeKey); Assert.Equal(4.16, value.Value, 2); },
            value => { Assert.Equal(3, value.Channel); Assert.Equal("temperature", value.TypeKey); Assert.Equal(26.5, value.Value, 1); Assert.Equal("°C", value.Unit); },
            value => { Assert.Equal(3, value.Channel); Assert.Equal("humidity", value.TypeKey); Assert.Equal(47.5, value.Value, 1); Assert.Equal("%", value.Unit); });
    }

    [Fact]
    public void ResolveMetricKey_ChannelMapping_RenamesVoltageToSolarPanel()
    {
        var payload = Convert.FromHexString("0174018C027401A0");
        var mappings = new Dictionary<(int, string), string>
        {
            [(1, "voltage")] = "battery_voltage",
            [(2, "voltage")] = "solar_panel_voltage",
        };

        var values = CayenneLppDecoder.Decode(payload);
        var keys = values.Select(value => CayenneLppDecoder.ResolveMetricKey(value, mappings, new HashSet<string>(StringComparer.Ordinal))).ToArray();

        Assert.Equal(["battery_voltage", "solar_panel_voltage"], keys);
    }

    [Fact]
    public void ResolveMetricKey_WildcardMapping_MatchesAnyType()
    {
        var mappings = new Dictionary<(int, string), string> { [(5, "*")] = "custom_channel" };

        var temperature = CayenneLppDecoder.ResolveMetricKey(new(5, "temperature", 20, "°C"), mappings, new HashSet<string>(StringComparer.Ordinal));
        var unmapped = CayenneLppDecoder.ResolveMetricKey(new(6, "temperature", 20, "°C"), mappings, new HashSet<string>(StringComparer.Ordinal));

        Assert.Equal("custom_channel", temperature);
        Assert.Equal("temperature", unmapped);
    }

    [Fact]
    public void ResolveMetricKey_RepeatedType_GetsChannelSuffix()
    {
        var repeated = new HashSet<string>(StringComparer.Ordinal) { "voltage" };

        var key = CayenneLppDecoder.ResolveMetricKey(new(2, "voltage", 4.16, "V"), NoMappings, repeated);

        Assert.Equal("voltage_2", key);
    }

    [Fact]
    public void Decode_CoversMeshCoreTelemetryTable()
    {
        // One record per LPP type used by MeshCore, concatenated; channel 9 for all.
        var payload = Convert.FromHexString(string.Concat(
            "0900", "01",              // digital input = 1
            "0901", "00",              // digital output = 0
            "0902", "FFF6",            // analog input = -0.10
            "0903", "0064",            // analog output = 1.00
            "0964", "00000064",        // generic = 100
            "0965", "0BB8",            // luminosity = 3000 lux
            "0966", "01",              // presence = 1
            "0967", "0109",            // temperature = 26.5 °C
            "0968", "5F",              // humidity = 47.5 %
            "0973", "2703",            // pressure = 998.7 hPa
            "0974", "018C",            // voltage = 3.96 V
            "0975", "0064",            // current = 0.1 A
            "0976", "00002710",        // frequency = 10000 Hz
            "0978", "50",              // percentage = 80 %
            "0979", "00A0",            // altitude = 160 m
            "097D", "01F4",            // concentration = 500 ppm
            "0980", "03E8",            // power = 1000 W
            "0982", "000003E8",        // distance = 1.000 m
            "0983", "00000064",        // energy = 0.100 kWh
            "0984", "010E",            // direction = 270°
            "0985", "6A9D3DF8",        // unixtime = 1788697080
            "098E", "01"));            // switch = 1

        var values = CayenneLppDecoder.Decode(payload);

        var byKey = values.ToDictionary(value => value.TypeKey, value => value.Value, StringComparer.Ordinal);
        Assert.Equal(22, values.Count);
        Assert.All(values, value => Assert.Equal(9, value.Channel));
        Assert.Equal(1, byKey["digital_input"]);
        Assert.Equal(0, byKey["digital_output"]);
        Assert.Equal(-0.10, byKey["analog_input"], 2);
        Assert.Equal(1.00, byKey["analog_output"], 2);
        Assert.Equal(100, byKey["generic"]);
        Assert.Equal(3000, byKey["luminosity"]);
        Assert.Equal(1, byKey["presence"]);
        Assert.Equal(26.5, byKey["temperature"], 1);
        Assert.Equal(47.5, byKey["humidity"], 1);
        Assert.Equal(998.7, byKey["pressure"], 1);
        Assert.Equal(3.96, byKey["voltage"], 2);
        Assert.Equal(0.1, byKey["current"], 3);
        Assert.Equal(10000, byKey["frequency"]);
        Assert.Equal(80, byKey["percentage"]);
        Assert.Equal(160, byKey["altitude"]);
        Assert.Equal(500, byKey["concentration"]);
        Assert.Equal(1000, byKey["power"]);
        Assert.Equal(1.0, byKey["distance"], 3);
        Assert.Equal(0.1, byKey["energy"], 3);
        Assert.Equal(270, byKey["direction"]);
        Assert.Equal(1788689912, byKey["unixtime"]);
        Assert.Equal(1, byKey["switch"]);
    }

    [Fact]
    public void Decode_MultiValueRecords_SplitIntoComponents()
    {
        var payload = Convert.FromHexString(string.Concat(
            "0A71", "FFF6", "000A", "0064",   // accelerometer: -0.010 G, 0.010 G, 0.100 G
            "0A86", "0064", "FFF6", "000A",   // gyrometer: 1.0, -0.1, 0.1 °/s
            "0A87", "FF", "80", "01"));       // colour 255, 128, 1

        var values = CayenneLppDecoder.Decode(payload);
        var byKey = values.ToDictionary(value => value.TypeKey, value => value.Value, StringComparer.Ordinal);

        Assert.Equal(-0.010, byKey["accel_x"], 3);
        Assert.Equal(0.010, byKey["accel_y"], 3);
        Assert.Equal(0.100, byKey["accel_z"], 3);
        Assert.Equal(1.0, byKey["gyro_x"], 2);
        Assert.Equal(-0.1, byKey["gyro_y"], 2);
        Assert.Equal(0.1, byKey["gyro_z"], 2);
        Assert.Equal(255, byKey["colour_r"]);
        Assert.Equal(128, byKey["colour_g"]);
        Assert.Equal(1, byKey["colour_b"]);
    }

    [Fact]
    public void Decode_GpsCoordinates_AreSignedDecimalDegrees()
    {
        // lat 0x034EC4 = 216772 → 21.6772°; lon 0xFC0BE3 = -259101 → -25.9101°; alt 0x01F400 = 128000 → 1280.00 m
        var payload = Convert.FromHexString("0A88034EC4FC0BE301F400");

        var values = CayenneLppDecoder.Decode(payload);
        var byKey = values.ToDictionary(value => value.TypeKey, value => value.Value, StringComparer.Ordinal);

        Assert.Equal(21.6772, byKey["gps_lat"], 4);
        Assert.Equal(-25.9101, byKey["gps_lon"], 4);
        Assert.Equal(1280.00, byKey["gps_alt"], 2);
    }

    [Fact]
    public void Decode_EmptyOrPaddingOnly_ReturnsEmpty()
    {
        Assert.Empty(CayenneLppDecoder.Decode(ReadOnlySpan<byte>.Empty));
        Assert.Empty(CayenneLppDecoder.Decode(new byte[] { 0x00, 0x00, 0x00, 0x00 }));
    }

    [Fact]
    public void Decode_UnknownType_StopsCleanlyBeforeIt()
    {
        var payload = Convert.FromHexString("01670109029A0102"); // 0x9A is not a known LPP type

        var values = CayenneLppDecoder.Decode(payload);

        var value = Assert.Single(values);
        Assert.Equal("temperature", value.TypeKey);
        Assert.Equal(26.5, value.Value, 1);
    }

    [Fact]
    public void Decode_NegativeTemperature_UsesSignedValue()
    {
        var payload = Convert.FromHexString("0567FE92"); // 0xFE92 = -366 → -36.6 °C

        var value = Assert.Single(CayenneLppDecoder.Decode(payload));

        Assert.Equal("temperature", value.TypeKey);
        Assert.Equal(-36.6, value.Value, 1);
    }

    [Fact]
    public void BuildTelemetryRequestPayload_IsTimestampPlusTypeThreePlusMask()
    {
        var payload = SensorTelemetryPoller.BuildTelemetryRequestPayload(0x6A9D3DF8);

        Assert.Equal(6, payload.Length);
        Assert.Equal(0xF8, payload[0]);
        Assert.Equal(0x3D, payload[1]);
        Assert.Equal(0x9D, payload[2]);
        Assert.Equal(0x6A, payload[3]);
        Assert.Equal(0x03, payload[4]);
        Assert.Equal(0x00, payload[5]);
        Assert.Equal("f83d9d6a0300", SensorTelemetryPoller.ToRequestHex(payload));
    }

    [Fact]
    public void TelemetryTypes_KnownSet_ContainsCommonLppKeys()
    {
        Assert.Contains("voltage", TelemetryTypes.KnownTypes);
        Assert.Contains("temperature", TelemetryTypes.KnownTypes);
        Assert.Contains("gps_lat", TelemetryTypes.KnownTypes);
        Assert.Contains("*", TelemetryTypes.KnownTypes);
        Assert.DoesNotContain("radiation", TelemetryTypes.KnownTypes);
    }
}
