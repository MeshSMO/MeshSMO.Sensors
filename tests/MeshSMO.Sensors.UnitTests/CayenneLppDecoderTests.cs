using MeshSMO.Sensors.Gateway.Polling;

namespace MeshSMO.Sensors.UnitTests;

public sealed class CayenneLppDecoderTests
{
    [Fact]
    public void Decode_ParsesRealNodeReply()
    {
        // Captured from the physical sensor node: reflected timestamp stripped,
        // body = voltage ch1, voltage ch2, temperature ch3, humidity ch3.
        var payload = Convert.FromHexString("0174018C027401A00367010903685F0000000000000000000000");

        var values = CayenneLppDecoder.Decode(payload);

        Assert.Collection(
            values,
            value =>
            {
                Assert.Equal("voltage", value.MetricKey);
                Assert.Equal(3.96, value.Value, precision: 2);
                Assert.Equal("V", value.Unit);
            },
            value =>
            {
                Assert.Equal("voltage_2", value.MetricKey);
                Assert.Equal(4.16, value.Value, precision: 2);
            },
            value =>
            {
                Assert.Equal("temperature", value.MetricKey);
                Assert.Equal(26.5, value.Value, precision: 1);
            },
            value =>
            {
                Assert.Equal("humidity", value.MetricKey);
                Assert.Equal(47.5, value.Value, precision: 1);
            });
    }

    [Fact]
    public void Decode_EmptyOrPaddingOnly_ReturnsEmpty()
    {
        Assert.Empty(CayenneLppDecoder.Decode(ReadOnlySpan<byte>.Empty));
        Assert.Empty(CayenneLppDecoder.Decode(new byte[] { 0x00, 0x00, 0x00, 0x00 }));
    }

    [Fact]
    public void Decode_NegativeTemperature_UsesSignedValue()
    {
        var payload = Convert.FromHexString("0567FE92"); // 0xFE92 = -366 → -36.6 °C

        var value = Assert.Single(CayenneLppDecoder.Decode(payload));

        Assert.Equal("temperature", value.MetricKey);
        Assert.Equal(-36.6, value.Value, precision: 1);
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
}
