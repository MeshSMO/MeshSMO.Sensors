using System.Text.RegularExpressions;

namespace MeshSMO.Sensors.Domain.Sensors;

public readonly partial record struct SensorSlug
{
    public SensorSlug(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (!SlugPattern().IsMatch(value))
        {
            throw new ArgumentException(
                "Sensor slug must contain 3-63 lowercase Latin letters, digits, or single hyphens and start/end with a letter or digit.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;

#pragma warning disable MA0009
    [GeneratedRegex("^[a-z0-9](?:[a-z0-9]|-(?=[a-z0-9])){1,61}[a-z0-9]$", RegexOptions.CultureInvariant)]
#pragma warning restore MA0009
    private static partial Regex SlugPattern();
}
