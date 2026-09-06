namespace MeshSMO.Sensors.Gateway.MeshCore;

internal sealed record RepeaterSensorPage(
    int TotalCount,
    IReadOnlyDictionary<string, string> Values,
    int? NextIndex);

internal static class RepeaterSerialResponseParser
{
    private const string ReplyPrefix = "->";
    private const string NextPrefix = "... next:";

    public static bool TryExtractReply(string line, out string reply)
    {
        var prefixIndex = line.IndexOf(ReplyPrefix, StringComparison.Ordinal);
        if (prefixIndex < 0)
        {
            reply = string.Empty;
            return false;
        }

        reply = line[(prefixIndex + ReplyPrefix.Length)..].Trim();
        return true;
    }

    public static bool TryParseSensorCount(string value, out int count)
    {
        const string suffix = " vars";
        if (value.EndsWith(suffix, StringComparison.Ordinal) &&
            int.TryParse(value.AsSpan(0, value.Length - suffix.Length), System.Globalization.CultureInfo.InvariantCulture, out count))
        {
            return true;
        }

        count = 0;
        return false;
    }

    public static RepeaterSensorPage ParseSensorPage(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0 || !TryParseSensorCount(lines[0], out var count))
            return new RepeaterSensorPage(0, new Dictionary<string, string>(StringComparer.Ordinal), null);

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        int? next = null;
        foreach (var line in lines.Skip(1))
        {
            if (line.StartsWith(NextPrefix, StringComparison.Ordinal) &&
                int.TryParse(line.AsSpan(NextPrefix.Length), System.Globalization.CultureInfo.InvariantCulture, out var nextValue))
            {
                next = nextValue;
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator > 0)
                values[line[..separator]] = line[(separator + 1)..];
        }

        return new RepeaterSensorPage(count, values, next);
    }
}
