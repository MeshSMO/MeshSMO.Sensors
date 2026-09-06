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
        {
            throw new DirectoryNotFoundException($"Sensor registry directory was not found: {fullPath}");
        }

        var files = System.IO.Directory
            .EnumerateFiles(fullPath, "*.y*ml", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (files.Length == 0)
        {
            throw new SensorRegistryValidationException(["The sensor registry contains no YAML files."]);
        }

        var definitions = new List<SensorDefinition>(files.Length);
        var errors = new List<string>();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await using var stream = File.OpenRead(file);
                using var reader = new StreamReader(stream);
                var yaml = await reader.ReadToEndAsync(cancellationToken);
                var document = _deserializer.Deserialize<SensorYaml>(yaml);
                definitions.Add(Parse(document, Path.GetFileName(file), errors));
            }
            catch (YamlException exception)
            {
                errors.Add($"{Path.GetFileName(file)}: invalid YAML at line {exception.Start.Line}, column {exception.Start.Column}: {exception.Message}");
            }
        }

        AddDuplicateErrors(definitions, errors);
        if (errors.Count > 0)
        {
            throw new SensorRegistryValidationException(errors);
        }

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
        {
            errors.Add($"{source}: polling.timeout cannot exceed polling.interval.");
        }

        var maxAttempts = yaml.Polling?.MaxAttempts ?? 0;
        if (maxAttempts is < 1 or > 10)
        {
            errors.Add($"{source}: polling.maxAttempts must be between 1 and 10.");
        }

        var latitude = yaml.Location?.Latitude;
        var longitude = yaml.Location?.Longitude;
        if (latitude is < -90 or > 90)
        {
            errors.Add($"{source}: location.latitude must be between -90 and 90.");
        }

        if (longitude is < -180 or > 180)
        {
            errors.Add($"{source}: location.longitude must be between -180 and 180.");
        }

        if (latitude.HasValue != longitude.HasValue)
        {
            errors.Add($"{source}: location.latitude and location.longitude must be specified together.");
        }

        var metrics = (yaml.Metrics ?? [])
            .Where(metric => !string.IsNullOrWhiteSpace(metric))
            .Select(metric => metric.Trim())
            .ToArray();

        if (metrics.Length == 0)
        {
            errors.Add($"{source}: at least one metric is required.");
        }

        foreach (var metric in metrics.Where(metric => !MetricKeyPattern().IsMatch(metric)))
        {
            errors.Add($"{source}: metric '{metric}' has an invalid key.");
        }

        foreach (var duplicate in metrics.GroupBy(metric => metric, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            errors.Add($"{source}: metric '{duplicate.Key}' is listed more than once.");
        }

        var channels = ParseTelemetryChannels(yaml.Telemetry?.Channels, source, errors);
        var loginPassword = ResolveLoginPassword(yaml.Mesh?.LoginPassword, source, errors);

        var visible = yaml.Public?.Visible ?? false;
        var indexable = yaml.Public?.Indexable ?? false;
        if (indexable && !visible)
        {
            errors.Add($"{source}: an indexable sensor must also be publicly visible.");
        }

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
            yaml.Location?.Precision, metrics, source, channels, loginPassword);
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
        {
            return null;
        }

        var resolved = raw.Contains('$')
            ? EnvironmentReferenceRegex().Replace(raw, match =>
            {
                var name = match.Groups[1].Value;
                var value = _environmentVariableLookup(name);
                if (value is not null)
                {
                    return value;
                }

                if (match.Groups[2].Success)
                {
                    return match.Groups[2].Value;
                }

                errors.Add(
                    $"{source}: mesh.loginPassword references environment variable '{name}' that is not set. " +
                    $"Set it for the gateway/dbmigrator processes (e.g. in deploy/.env) or use ${{{name}:-default}}.");
                return string.Empty;
            })
            : raw;

        if (resolved.Contains("${"))
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
        {
            return [];
        }

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

        foreach (var duplicate in parsed
                     .GroupBy(mapping => (mapping.Channel, mapping.Type ?? "*"))
                     .Where(group => group.Count() > 1))
        {
            errors.Add(
                $"{source}: telemetry channel {duplicate.Key.Channel} (type '{duplicate.Key.Item2}') is mapped more than once.");
        }

        foreach (var duplicate in parsed
                     .GroupBy(mapping => mapping.Metric, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            errors.Add($"{source}: telemetry metric '{duplicate.Key}' is mapped more than once.");
        }

        return parsed;
    }

    private static TimeSpan ParseDuration(string? value, string source, string field, ICollection<string> errors)
    {
        if (!RegistryDuration.TryParse(value, out var duration) || duration.TotalSeconds < 1 || duration.TotalSeconds > int.MaxValue || duration.TotalSeconds % 1 != 0)
        {
            errors.Add($"{source}: {field} must be a positive whole-second duration such as '30s' or '5m'.");
            return default;
        }

        return duration;
    }

    private static void Require(string? value, string source, string field, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{source}: {field} is required.");
        }
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
        {
            errors.Add($"Duplicate {field} '{group.Key}' in {string.Join(", ", group.Select(definition => definition.Source))}.");
        }
    }

    [GeneratedRegex("^[a-z][a-z0-9_-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex MetricKeyPattern();

    [GeneratedRegex(@"\$\{([A-Za-z_][A-Za-z0-9_]*)(?::-([^}]*))?\}", RegexOptions.CultureInvariant)]
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
