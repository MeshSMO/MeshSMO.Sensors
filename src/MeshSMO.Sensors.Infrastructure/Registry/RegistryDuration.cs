using System.Globalization;

namespace MeshSMO.Sensors.Infrastructure.Registry;

public static class RegistryDuration
{
    public static bool TryParse(string? value, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var unitLength = value.EndsWith("ms", StringComparison.Ordinal) ? 2 : 1;
        if (value.Length <= unitLength || !double.TryParse(value[..^unitLength], NumberStyles.None, CultureInfo.InvariantCulture, out var amount))
            return false;

        try
        {
            duration = value[^unitLength..] switch
            {
                "ms" => TimeSpan.FromMilliseconds(amount),
                "s" => TimeSpan.FromSeconds(amount),
                "m" => TimeSpan.FromMinutes(amount),
                "h" => TimeSpan.FromHours(amount),
                "d" => TimeSpan.FromDays(amount),
                _ => TimeSpan.Zero,
            };
        }
        catch (OverflowException)
        {
            return false;
        }

        return duration > TimeSpan.Zero;
    }
}
