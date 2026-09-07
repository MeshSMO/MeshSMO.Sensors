using System.Globalization;
using System.Text.RegularExpressions;
using MeshSMO.Sensors.Application.Registry;
using MeshSMO.Sensors.Domain.Sensors;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MeshSMO.Sensors.Infrastructure.Registry;

public sealed partial class FileSystemSensorRegistry(
    IOptions<SensorRegistryOptions> options,
    IHostEnvironment hostEnvironment,
    Func<string, string?>? environmentVariableLookup = null) : ISensorRegistry
{
    /// <summary>Wire limit: the node login password travels inside ANON_REQ as
    /// timestamp(4) + password, 15 bytes max (docs/repeater-firmware-acquisition-spec.md §8.2).</summary>
    private const int MaximumLoginPasswordBytes = 15;

    private readonly Func<string, string?> _environmentVariableLookup =
        environmentVariableLookup ?? Environment.GetEnvironmentVariable;

    private readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public async Task<IReadOnlyList<SensorDefinition>> LoadAsync(CancellationToken cancellationToken)
    {
        var directory = options.Value.Directory;
        var fullPath = Path.GetFullPath(
            Path.IsPathRooted(directory)
                ? directory
                : Path.Combine(hostEnvironment.ContentRootPath, directory));

        if (!System.IO.Directory.Exists(fullPath))
            throw new DirectoryNotFoundException($"Sensor registry directory was not found: {fullPath}");

        var files = System.IO.Directory
            .EnumerateFiles(fullPath, "*.y*ml", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (files.Length == 0)
            throw new SensorRegistryValidationException(["The sensor registry contains no YAML files."]);

        var definitions = new List<SensorDefinition>(files.Length);
        var errors = new List<string>();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var stream = File.OpenRead(file);
                await using (stream.ConfigureAwait(false))
                {
                    using var reader = new StreamReader(stream);
                    var yaml = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                    var document = _deserializer.Deserialize<SensorYaml>(yaml);
                    definitions.Add(Parse(document, Path.GetFileName(file), errors));
                }
            }
            catch (YamlException exception)
            {
                errors.Add($"{Path.GetFileName(file)}: invalid YAML at line {exception.Start.Line}, column {exception.Start.Column}: {exception.Message}");
            }
        }

        AddDuplicateErrors(definitions, errors);
        if (errors.Count > 0)
            throw new SensorRegistryValidationException(errors);

        return definitions;
    }

    private SensorDefinition Parse(SensorYaml yaml, string source, ICollection<string> errors)
    {
        var startErrorCount = errors.Count;

        if (!Guid.TryParse(yaml.Id, out var id) || id == Guid.Empty)
        {
            errors.Add($"{source}: id must be a non-empty UUID.");
            id = Guid.NewGuid();
        }

        SensorSlug slug;
        try
        {
            slug = new SensorSlug(yaml.Slug ?? string.Empty);
        }
        catch (ArgumentException exception)
        {
            errors.Add($"{source}: {exception.Message}");
            slug = new SensorSlug("invalid-placeholder");
        }

        Require(yaml.DisplayName, source, "displayName", errors);
        Require(yaml.Mesh?.PublicKey, source, "mesh.publicKey", errors);
        Require(yaml.Mesh?.Protocol, source, "mesh.protocol", errors);

        var interval = ParseDuration(yaml.Polling?.Interval, source, "polling.interval", errors);
        var timeout = ParseDuration(yaml.Polling?.Timeout, source, "polling.timeout", errors);

        if (interval > TimeSpan.Zero && timeout > interval)
            errors.Add($"{source}: polling.timeout cannot exceed polling.interval.");

        var maxAttempts = yaml.Polling?.MaxAttempts ?? 0;
        if (maxAttempts is < 1 or > 10)
            errors.Add($"{source}: polling.maxAttempts must be between 1 and 10.");

        var schedule = ParsePollingSchedule(yaml.Polling?.Schedule, timeout, source, errors);

        var latitude = yaml.Location?.Latitude;
        var longitude = yaml.Location?.Longitude;
        if (latitude is < -90 or > 90)
            errors.Add($"{source}: location.latitude must be between -90 and 90.");

        if (longitude is < -180 or > 180)
            errors.Add($"{source}: location.longitude must be between -180 and 180.");

        if (latitude.HasValue != longitude.HasValue)
            errors.Add($"{source}: location.latitude and location.longitude must be specified together.");

        var metrics = (yaml.Metrics ?? [])
            .Where(metric => !string.IsNullOrWhiteSpace(metric))
            .Select(metric => metric.Trim())
            .ToArray();

        if (metrics.Length == 0)
            errors.Add($"{source}: at least one metric is required.");

        foreach (var metric in metrics.Where(metric => !MetricKeyPattern().IsMatch(metric)))
            errors.Add($"{source}: metric '{metric}' has an invalid key.");

        foreach (var duplicate in metrics.GroupBy(metric => metric, StringComparer.Ordinal).Where(group => group.Count() > 1))
            errors.Add($"{source}: metric '{duplicate.Key}' is listed more than once.");

        var channels = ParseTelemetryChannels(yaml.Telemetry?.Channels, source, errors);
        var loginPassword = ResolveLoginPassword(yaml.Mesh?.LoginPassword, source, errors);

        var visible = yaml.Public?.Visible ?? false;
        var indexable = yaml.Public?.Indexable ?? false;
        if (indexable && !visible)
            errors.Add($"{source}: an indexable sensor must also be publicly visible.");

        if (errors.Count > startErrorCount)
        {
            return new SensorDefinition(
                new SensorId(id), slug, yaml.DisplayName ?? "Invalid", yaml.Description,
                yaml.Mesh?.PublicKey ?? "invalid", yaml.Mesh?.Protocol ?? "invalid",
                interval == TimeSpan.Zero ? TimeSpan.FromMinutes(5) : interval,
                timeout == TimeSpan.Zero ? TimeSpan.FromSeconds(30) : timeout,
                Math.Clamp(maxAttempts, 1, 10), yaml.Polling?.Enabled ?? false,
                visible, indexable, latitude, longitude, yaml.Location?.Precision,
                metrics.Length == 0 ? ["invalid"] : metrics, source, channels);
        }

        return new SensorDefinition(
            new SensorId(id), slug, yaml.DisplayName!.Trim(), yaml.Description,
            yaml.Mesh!.PublicKey!.Trim(), yaml.Mesh.Protocol!.Trim(), interval, timeout,
            maxAttempts, yaml.Polling!.Enabled, visible, indexable, latitude, longitude,
            yaml.Location?.Precision, metrics, source, channels, loginPassword, schedule);
    }

    /// <summary>
    /// Resolves <c>mesh.loginPassword</c>: <c>null</c> stays <c>null</c> (the poller
    /// falls back to the global <c>SensorPolling:LoginPassword</c>), <c>${VAR}</c> and
    /// <c>${VAR:-default}</c> references are substituted from the environment, anything
    /// else is used verbatim as the node password.
    /// </summary>
    private string? ResolveLoginPassword(string? raw, string source, ICollection<string> errors)
    {
        if (raw is null)
            return null;

        var resolved = raw.Contains('$')
            ? EnvironmentReferenceRegex().Replace(raw, match =>
            {
                var name = match.Groups["name"].Value;
                var value = _environmentVariableLookup(name);
                if (value is not null)
                    return value;

                if (match.Groups["default"].Success)
                    return match.Groups["default"].Value;

                errors.Add(
                    $"{source}: mesh.loginPassword references environment variable '{name}' that is not set. " +
                    $"Set it for the gateway/dbmigrator processes (e.g. in deploy/.env) or use ${{{name}:-default}}.");
                return string.Empty;
            })
            : raw;

        if (resolved.Contains("${", StringComparison.Ordinal))
        {
            errors.Add(
                $"{source}: mesh.loginPassword contains a malformed or unresolved '${{...}}' reference. " +
                "Expected format: ${VARIABLE_NAME} or ${VARIABLE_NAME:-default}.");
        }

        if (System.Text.Encoding.UTF8.GetByteCount(resolved) > MaximumLoginPasswordBytes)
        {
            errors.Add(
                $"{source}: mesh.loginPassword must be at most {MaximumLoginPasswordBytes} bytes of UTF-8 " +
                "(the node login wire limit).");
        }

        return resolved;
    }

    private static List<TelemetryChannelMapping> ParseTelemetryChannels(
        List<TelemetryChannelYaml>? channels,
        string source,
        ICollection<string> errors)
    {
        if (channels is null || channels.Count == 0)
            return [];

        var parsed = new List<TelemetryChannelMapping>(channels.Count);
        foreach (var channel in channels)
        {
            if (channel.Channel is null or < 0 or > 255)
            {
                errors.Add($"{source}: telemetry channel number must be between 0 and 255.");
                continue;
            }

            var metric = channel.Metric?.Trim();
            if (string.IsNullOrWhiteSpace(metric))
            {
                errors.Add($"{source}: telemetry channel {channel.Channel}: metric is required.");
                continue;
            }

            if (!MetricKeyPattern().IsMatch(metric))
            {
                errors.Add($"{source}: telemetry channel {channel.Channel}: metric '{metric}' has an invalid key.");
                continue;
            }

            var type = channel.Type?.Trim();
            if (!string.IsNullOrEmpty(type) && !TelemetryTypes.KnownTypes.Contains(type))
            {
                errors.Add(
                    $"{source}: telemetry channel {channel.Channel}: unknown type '{type}'. " +
                    "Use an LPP type key such as 'voltage' or 'temperature', or '*' for any type.");
                continue;
            }

            parsed.Add(new TelemetryChannelMapping(
                channel.Channel.Value,
                string.IsNullOrEmpty(type) ? null : type,
                metric,
                string.IsNullOrWhiteSpace(channel.DisplayName) ? null : channel.DisplayName.Trim(),
                string.IsNullOrWhiteSpace(channel.Unit) ? null : channel.Unit.Trim()));
        }

        foreach (var duplicate in parsed.GroupBy(mapping => (mapping.Channel, mapping.Type ?? "*")).Where(group => group.Count() > 1))
            errors.Add($"{source}: telemetry channel {duplicate.Key.Channel} (type '{duplicate.Key.Item2}') is mapped more than once.");

        foreach (var duplicate in parsed.GroupBy(mapping => mapping.Metric, StringComparer.Ordinal).Where(group => group.Count() > 1))
            errors.Add($"{source}: telemetry metric '{duplicate.Key}' is mapped more than once.");

        return parsed;
    }

    private static TimeSpan ParseDuration(string? value, string source, string field, ICollection<string> errors)
    {
        if (!RegistryDuration.TryParse(value, out var duration) || duration.TotalSeconds < 1 || duration.TotalSeconds > int.MaxValue || duration.TotalSeconds % 1 != 0)
        {
            errors.Add($"{source}: {field} must be a positive whole-second duration such as '30s' or '5m'.");
            return TimeSpan.Zero;
        }

        return duration;
    }

    private const int MinutesPerDay = 24 * 60;

    /// <summary>
    /// Parses the optional <c>polling.schedule</c> section: the IANA time zone
    /// the window times are interpreted in and non-overlapping half-open
    /// windows whose interval replaces the base poll interval inside the
    /// window. Errors leave placeholder values behind — the accumulated errors
    /// make <see cref="LoadAsync"/> reject the file anyway.
    /// </summary>
    private static PollingSchedule? ParsePollingSchedule(
        ScheduleYaml? yaml,
        TimeSpan timeout,
        string source,
        ICollection<string> errors)
    {
        if (yaml is null)
            return null;

        var timeZone = ParseTimeZone(yaml.TimeZone, source, errors);
        var windows = new List<PollingScheduleWindow>();

        if (yaml.Windows is not { Count: > 0 })
        {
            errors.Add($"{source}: polling.schedule.windows must contain at least one window.");
        }
        else
        {
            foreach (var windowYaml in yaml.Windows)
            {
                var from = ParseTimeOfDay(windowYaml.From, source, "polling.schedule.windows[].from", errors);
                var to = ParseTimeOfDay(windowYaml.To, source, "polling.schedule.windows[].to", errors);
                var interval = ParseDuration(windowYaml.Interval, source, "polling.schedule.windows[].interval", errors);

                if (from is null || to is null || interval == TimeSpan.Zero)
                    continue;

                if (from.Value == to.Value)
                {
                    errors.Add($"{source}: polling.schedule window must not be zero-length (from and to are equal).");
                    continue;
                }

                if (timeout > interval)
                    errors.Add($"{source}: polling.timeout cannot exceed a polling.schedule window interval.");

                windows.Add(new PollingScheduleWindow(from.Value, to.Value, interval));
            }
        }

        ReportScheduleOverlaps(windows, source, errors);
        return new PollingSchedule(timeZone, windows);
    }

    private static TimeZoneInfo ParseTimeZone(string? timeZoneId, string source, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            errors.Add($"{source}: polling.schedule.timeZone is required (IANA id such as 'Europe/Moscow').");
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
        }
        catch (TimeZoneNotFoundException)
        {
            errors.Add($"{source}: polling.schedule.timeZone '{timeZoneId}' was not found on this machine.");
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            errors.Add($"{source}: polling.schedule.timezone '{timeZoneId}' is invalid.");
            return TimeZoneInfo.Utc;
        }
    }

    private static TimeOnly? ParseTimeOfDay(string? value, string source, string field, ICollection<string> errors)
    {
        if (TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            return time;

        errors.Add($"{source}: {field} must be a local time in 'HH:mm' format, for example '08:30'.");
        return null;
    }

    /// <summary>
    /// Windows must not overlap (half-open windows may touch: 08:00–12:00 and
    /// 12:00–18:00 are fine). Windows wrapping past midnight are split at
    /// midnight into two ranges before comparison; two windows starting at the
    /// same minute always overlap since both contain that instant.
    /// </summary>
    private static void ReportScheduleOverlaps(IReadOnlyList<PollingScheduleWindow> windows, string source, ICollection<string> errors)
    {
        var ranges = new List<(int Start, int End, PollingScheduleWindow Window)>(windows.Count * 2);
        foreach (var window in windows)
        {
            var start = window.Start.Hour * 60 + window.Start.Minute;
            var end = window.End.Hour * 60 + window.End.Minute;
            if (window.End > window.Start)
            {
                ranges.Add((start, end, window));
                continue;
            }

            ranges.Add((start, MinutesPerDay, window));
            if (end > 0)
                ranges.Add((0, end, window));
        }

        ranges.Sort((left, right) => left.Start.CompareTo(right.Start));
        for (var index = 1; index < ranges.Count; index++)
        {
            if (ranges[index].Start < ranges[index - 1].End || ranges[index].Start == ranges[index - 1].Start)
            {
                errors.Add(
                    $"{source}: polling.schedule windows overlap: {DescribeWindow(ranges[index - 1].Window)} " +
                    $"and {DescribeWindow(ranges[index].Window)}.");
            }
        }
    }

    private static string DescribeWindow(PollingScheduleWindow window) =>
        $"{window.Start.ToString("HH:mm", CultureInfo.InvariantCulture)}-{window.End.ToString("HH:mm", CultureInfo.InvariantCulture)}";

    private static void Require(string? value, string source, string field, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
            errors.Add($"{source}: {field} is required.");
    }

    private static void AddDuplicateErrors(IReadOnlyCollection<SensorDefinition> definitions, ICollection<string> errors)
    {
        AddDuplicates(definitions, definition => definition.Id.ToString(), "id", StringComparer.Ordinal, errors);
        AddDuplicates(definitions, definition => definition.Slug.Value, "slug", StringComparer.Ordinal, errors);
        AddDuplicates(definitions, definition => definition.MeshPublicKey, "mesh.publicKey", StringComparer.OrdinalIgnoreCase, errors);
    }

    private static void AddDuplicates(
        IEnumerable<SensorDefinition> definitions,
        Func<SensorDefinition, string> selector,
        string field,
        StringComparer comparer,
        ICollection<string> errors)
    {
        foreach (var group in definitions.GroupBy(selector, comparer).Where(group => group.Count() > 1))
            errors.Add($"Duplicate {field} '{group.Key}' in {string.Join(", ", group.Select(definition => definition.Source))}.");
    }

#pragma warning disable MA0009
    [GeneratedRegex("^[a-z][a-z0-9_-]{0,63}$", RegexOptions.CultureInvariant)]
#pragma warning restore MA0009
    private static partial Regex MetricKeyPattern();

#pragma warning disable MA0009
    [GeneratedRegex(@"\$\{(?<name>[A-Za-z_][A-Za-z0-9_]*)(?::-(?<default>[^}]*))?\}", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
#pragma warning restore MA0009
    private static partial Regex EnvironmentReferenceRegex();

    private sealed class SensorYaml
    {
        public string? Id { get; set; }
        public string? Slug { get; set; }
        public string? DisplayName { get; set; }
        public string? Description { get; set; }
        public MeshYaml? Mesh { get; set; }
        public PollingYaml? Polling { get; set; }
        public PublicYaml? Public { get; set; }
        public LocationYaml? Location { get; set; }
        public List<string>? Metrics { get; set; }
        public TelemetryYaml? Telemetry { get; set; }
    }

    private sealed class TelemetryYaml
    {
        public List<TelemetryChannelYaml>? Channels { get; set; }
    }

    private sealed class TelemetryChannelYaml
    {
        public int? Channel { get; set; }
        public string? Type { get; set; }
        public string? Metric { get; set; }
        public string? DisplayName { get; set; }
        public string? Unit { get; set; }
    }

    private sealed class MeshYaml
    {
        public string? PublicKey { get; set; }
        public string? Protocol { get; set; }
        public string? LoginPassword { get; set; }
    }

    private sealed class PollingYaml
    {
        public string? Interval { get; set; }
        public string? Timeout { get; set; }
        public int MaxAttempts { get; set; }
        public bool Enabled { get; set; }
        public ScheduleYaml? Schedule { get; set; }
    }

    private sealed class ScheduleYaml
    {
        public string? TimeZone { get; set; }
        public List<ScheduleWindowYaml>? Windows { get; set; }
    }

    private sealed class ScheduleWindowYaml
    {
        public string? From { get; set; }
        public string? To { get; set; }
        public string? Interval { get; set; }
    }

    private sealed class PublicYaml
    {
        public bool Visible { get; set; }
        public bool Indexable { get; set; }
    }

    private sealed class LocationYaml
    {
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string? Precision { get; set; }
    }
}
