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
    string Source);
