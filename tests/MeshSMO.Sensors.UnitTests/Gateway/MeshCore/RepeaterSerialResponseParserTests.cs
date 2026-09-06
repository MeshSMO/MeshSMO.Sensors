using MeshSMO.Sensors.Gateway.MeshCore;

namespace MeshSMO.Sensors.UnitTests.Gateway.MeshCore;

public sealed class RepeaterSerialResponseParserTests
{
    [Theory]
    [InlineData("  -> MeshCore Repeater 1.0", "MeshCore Repeater 1.0")]
    [InlineData("noise before -> > enabled:on", "> enabled:on")]
    public void ExtractsReplyAfterFirmwarePrompt(string line, string expected)
    {
        var parsed = RepeaterSerialResponseParser.TryExtractReply(line, out var reply);

        Assert.True(parsed);
        Assert.Equal(expected, reply);
    }

    [Fact]
    public void ParsesPaginatedSensorSettings()
    {
        string[] lines = [
            "5 vars",
            "gps=1",
            "temperature=21.75",
            "... next:2",
        ];

        var page = RepeaterSerialResponseParser.ParseSensorPage(lines);

        Assert.Equal(5, page.TotalCount);
        Assert.Equal("1", page.Values["gps"]);
        Assert.Equal("21.75", page.Values["temperature"]);
        Assert.Equal(2, page.NextIndex);
    }

    [Fact]
    public void UnknownSensorCommandProducesEmptyPage()
    {
        var page = RepeaterSerialResponseParser.ParseSensorPage(["Unknown command"]);

        Assert.Equal(0, page.TotalCount);
        Assert.Empty(page.Values);
        Assert.Null(page.NextIndex);
    }
}
