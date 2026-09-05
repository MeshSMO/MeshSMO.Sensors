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
    IHostEnvironment hostEnvironment) : ISensorRegistry
{
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

    private static SensorDefinition Parse(SensorYaml yaml, string source, ICollection<string> errors)
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
                metrics.Length == 0 ? ["invalid"] : metrics, source);
        }

        return new SensorDefinition(
            new SensorId(id), slug, yaml.DisplayName!.Trim(), yaml.Description,
            yaml.Mesh!.PublicKey!.Trim(), yaml.Mesh.Protocol!.Trim(), interval, timeout,
            maxAttempts, yaml.Polling!.Enabled, visible, indexable, latitude, longitude,
            yaml.Location?.Precision, metrics, source);
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
    }

    private sealed class MeshYaml
    {
        public string? PublicKey { get; set; }
        public string? Protocol { get; set; }
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
