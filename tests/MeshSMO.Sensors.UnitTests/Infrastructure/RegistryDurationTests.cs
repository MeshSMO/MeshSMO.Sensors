using MeshSMO.Sensors.Infrastructure.Registry;

namespace MeshSMO.Sensors.UnitTests.Infrastructure;

public sealed class RegistryDurationTests
{
    [Theory]
    [InlineData("30s", 30)]
    [InlineData("5m", 300)]
    [InlineData("2h", 7200)]
    [InlineData("1d", 86400)]
    public void TryParse_accepts_supported_units(string value, int expectedSeconds)
    {
        var parsed = RegistryDuration.TryParse(value, out var duration);

        Assert.True(parsed);
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), duration);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("5")]
    [InlineData("-1s")]
    [InlineData("1w")]
    public void TryParse_rejects_invalid_values(string? value)
    {
        Assert.False(RegistryDuration.TryParse(value, out _));
    }
}
