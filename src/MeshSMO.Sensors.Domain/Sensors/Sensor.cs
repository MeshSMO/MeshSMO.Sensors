namespace MeshSMO.Sensors.Domain.Sensors;

public sealed class Sensor
{
    private Sensor()
    {
    }

    public Sensor(
        SensorId id,
        SensorSlug slug,
        string displayName,
        string? description,
        string meshPublicKey,
        string protocolId,
        TimeSpan pollInterval,
        TimeSpan pollTimeout,
        int pollMaxAttempts,
        bool enabled,
        bool publicVisible,
        bool publicIndexable,
        double? latitude,
        double? longitude,
        string? locationPrecision,
        IEnumerable<string> metrics,
        DateTimeOffset now)
    {
        Id = id;
        CreatedAt = now;
        ApplyConfiguration(
            slug,
            displayName,
            description,
            meshPublicKey,
            protocolId,
            pollInterval,
            pollTimeout,
            pollMaxAttempts,
            enabled,
            publicVisible,
            publicIndexable,
            latitude,
            longitude,
            locationPrecision,
            metrics,
            now);
    }

    public SensorId Id { get; private set; }
    public SensorSlug Slug { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string MeshPublicKey { get; private set; } = string.Empty;
    public string ProtocolId { get; private set; } = string.Empty;
    public int PollIntervalSeconds { get; private set; }
    public int PollTimeoutSeconds { get; private set; }
    public int PollMaxAttempts { get; private set; }
    public bool Enabled { get; private set; }
    public bool PublicVisible { get; private set; }
    public bool PublicIndexable { get; private set; }
    public double? Latitude { get; private set; }
    public double? Longitude { get; private set; }
    public string? LocationPrecision { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public ICollection<SensorMetric> Metrics { get; } = new List<SensorMetric>();

    public void ApplyConfiguration(
        SensorSlug slug,
        string displayName,
        string? description,
        string meshPublicKey,
        string protocolId,
        TimeSpan pollInterval,
        TimeSpan pollTimeout,
        int pollMaxAttempts,
        bool enabled,
        bool publicVisible,
        bool publicIndexable,
        double? latitude,
        double? longitude,
        string? locationPrecision,
        IEnumerable<string> metrics,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(meshPublicKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(protocolId);

        if (pollInterval <= TimeSpan.Zero || pollInterval.TotalSeconds > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(pollInterval));

        if (pollTimeout <= TimeSpan.Zero || pollTimeout.TotalSeconds > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(pollTimeout));

        if (pollMaxAttempts is < 1 or > 10)
            throw new ArgumentOutOfRangeException(nameof(pollMaxAttempts));

        var metricKeys = metrics
            .Select(metric => metric.Trim())
            .Where(metric => metric.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (metricKeys.Length == 0)
            throw new ArgumentException("A sensor must expose at least one metric.", nameof(metrics));

        Slug = slug;
        DisplayName = displayName.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        MeshPublicKey = meshPublicKey.Trim();
        ProtocolId = protocolId.Trim();
        PollIntervalSeconds = checked((int)pollInterval.TotalSeconds);
        PollTimeoutSeconds = checked((int)pollTimeout.TotalSeconds);
        PollMaxAttempts = pollMaxAttempts;
        Enabled = enabled;
        PublicVisible = publicVisible;
        PublicIndexable = publicIndexable;
        Latitude = latitude;
        Longitude = longitude;
        LocationPrecision = string.IsNullOrWhiteSpace(locationPrecision) ? null : locationPrecision.Trim();
        UpdatedAt = now;

        var removed = Metrics.Where(existing => !metricKeys.Contains(existing.MetricKey, StringComparer.Ordinal)).ToArray();
        foreach (var metric in removed)
            Metrics.Remove(metric);

        var existingKeys = Metrics.Select(metric => metric.MetricKey).ToHashSet(StringComparer.Ordinal);
        foreach (var metricKey in metricKeys.Where(metricKey => !existingKeys.Contains(metricKey)))
            Metrics.Add(new SensorMetric(Id, metricKey));
    }
}
