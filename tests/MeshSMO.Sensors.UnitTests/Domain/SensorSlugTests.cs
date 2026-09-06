using MeshSMO.Sensors.Domain.Sensors;

namespace MeshSMO.Sensors.UnitTests.Domain;

public sealed class SensorSlugTests
{
    [Theory]
    [InlineData("smolensk-center")]
    [InlineData("sensor-01")]
    [InlineData("abc")]
    public void Constructor_accepts_canonical_slug(string value)
    {
        var slug = new SensorSlug(value);

        Assert.Equal(value, slug.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("UPPER")]
    [InlineData("ab")]
    [InlineData("-sensor")]
    [InlineData("sensor-")]
    [InlineData("sensor--one")]
    [InlineData("sensor one")]
    public void Constructor_rejects_non_canonical_slug(string value) => Assert.ThrowsAny<ArgumentException>(() => new SensorSlug(value));
}
