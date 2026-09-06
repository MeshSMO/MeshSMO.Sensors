using MeshSMO.Sensors.Domain.Sensors;

namespace MeshSMO.Sensors.Application.Registry;

public sealed record SensorDefinition(
    SensorId Id,
    SensorSlug Slug,
    string DisplayName,
    string? Description,
    string MeshPublicKey,
    string ProtocolId,
    TimeSpan PollInterval,
    TimeSpan PollTimeout,
    int PollMaxAttempts,
    bool Enabled,
    bool PublicVisible,
    bool PublicIndexable,
    double? Latitude,
    double? Longitude,
    string? LocationPrecision,
    IReadOnlyList<string> Metrics,
    string Source,
    IReadOnlyList<TelemetryChannelMapping> Channels)
{
    public SensorDefinition(
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
        IReadOnlyList<string> metrics,
        string source)
        : this(id, slug, displayName, description, meshPublicKey, protocolId, pollInterval, pollTimeout,
            pollMaxAttempts, enabled, publicVisible, publicIndexable, latitude, longitude, locationPrecision,
            metrics, source, [])
    {
    }
}
