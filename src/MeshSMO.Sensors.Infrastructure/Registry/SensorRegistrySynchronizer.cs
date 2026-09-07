using MeshSMO.Sensors.Application.Abstractions;
using MeshSMO.Sensors.Application.Registry;
using MeshSMO.Sensors.Domain.Sensors;
using MeshSMO.Sensors.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MeshSMO.Sensors.Infrastructure.Registry;

public sealed class SensorRegistrySynchronizer(
    ISensorRegistry registry,
    SensorsDbContext dbContext,
    IClock clock) : ISensorRegistrySynchronizer
{
    public async Task<SensorRegistrySyncResult> SynchronizeAsync(CancellationToken cancellationToken)
    {
        var definitions = await registry.LoadAsync(cancellationToken).ConfigureAwait(false);
        var ids = definitions.Select(definition => definition.Id).ToArray();
        var existing = await dbContext.Sensors
            .Include(sensor => sensor.Metrics)
            .Where(sensor => ids.Contains(sensor.Id))
            .ToDictionaryAsync(sensor => sensor.Id, cancellationToken).ConfigureAwait(false);

        var added = 0;
        var updated = 0;
        var now = clock.UtcNow;

        foreach (var definition in definitions)
        {
            var effectiveMetrics = EffectiveMetrics(definition);
            if (!existing.TryGetValue(definition.Id, out var sensor))
            {
                sensor = CreateSensor(definition, effectiveMetrics, now);
                ApplyChannelMetadata(sensor, definition);
                dbContext.Sensors.Add(sensor);
                added++;
                continue;
            }

            if (!Matches(sensor, definition, effectiveMetrics))
            {
                Apply(sensor, definition, effectiveMetrics, now);
                updated++;
            }

            ApplyChannelMetadata(sensor, definition);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new(added, updated, definitions.Count);
    }

    /// <summary>Metric keys advertised by the sensor: the `metrics` list plus every channel mapping target.</summary>
    private static IReadOnlyList<string> EffectiveMetrics(SensorDefinition definition) =>
        definition.Metrics
            .Concat(definition.Channels.Select(channel => channel.Metric))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static void ApplyChannelMetadata(Sensor sensor, SensorDefinition definition)
    {
        foreach (var channel in definition.Channels)
        {
            var metric = sensor.Metrics.FirstOrDefault(candidate => string.Equals(candidate.MetricKey, channel.Metric, StringComparison.Ordinal));
            if (metric is null)
                continue;

            metric.DisplayName = channel.DisplayName ?? metric.DisplayName;
            metric.Unit = channel.Unit ?? metric.Unit;
        }
    }

    private static Sensor CreateSensor(SensorDefinition definition, IReadOnlyList<string> metrics, DateTimeOffset now) =>
        new(
            definition.Id,
            definition.Slug,
            definition.DisplayName,
            definition.Description,
            definition.MeshPublicKey,
            definition.ProtocolId,
            definition.PollInterval,
            definition.PollTimeout,
            definition.PollMaxAttempts,
            definition.Enabled,
            definition.PublicVisible,
            definition.PublicIndexable,
            definition.Latitude,
            definition.Longitude,
            definition.LocationPrecision,
            metrics,
            now);

    private static void Apply(Sensor sensor, SensorDefinition definition, IReadOnlyList<string> metrics, DateTimeOffset now) =>
        sensor.ApplyConfiguration(
            definition.Slug,
            definition.DisplayName,
            definition.Description,
            definition.MeshPublicKey,
            definition.ProtocolId,
            definition.PollInterval,
            definition.PollTimeout,
            definition.PollMaxAttempts,
            definition.Enabled,
            definition.PublicVisible,
            definition.PublicIndexable,
            definition.Latitude,
            definition.Longitude,
            definition.LocationPrecision,
            metrics,
            now);

    private static bool Matches(Sensor sensor, SensorDefinition definition, IReadOnlyList<string> effectiveMetrics) =>
        sensor.Slug == definition.Slug
        && string.Equals(sensor.DisplayName, definition.DisplayName.Trim()
, StringComparison.Ordinal) && string.Equals(sensor.Description, Normalize(definition.Description)
, StringComparison.Ordinal) && string.Equals(sensor.MeshPublicKey, definition.MeshPublicKey.Trim()
, StringComparison.Ordinal) && string.Equals(sensor.ProtocolId, definition.ProtocolId.Trim()
, StringComparison.Ordinal) && sensor.PollIntervalSeconds == (int)definition.PollInterval.TotalSeconds
        && sensor.PollTimeoutSeconds == (int)definition.PollTimeout.TotalSeconds
        && sensor.PollMaxAttempts == definition.PollMaxAttempts
        && sensor.Enabled == definition.Enabled
        && sensor.PublicVisible == definition.PublicVisible
        && sensor.PublicIndexable == definition.PublicIndexable
        && sensor.Latitude == definition.Latitude
        && sensor.Longitude == definition.Longitude
        && string.Equals(sensor.LocationPrecision, Normalize(definition.LocationPrecision)
, StringComparison.Ordinal) && sensor.Metrics.Select(metric => metric.MetricKey).ToHashSet(StringComparer.Ordinal)
            .SetEquals(effectiveMetrics)
        && ChannelsMatch(sensor, definition);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool ChannelsMatch(Sensor sensor, SensorDefinition definition) =>
        definition.Channels.All(channel =>
        {
            var metric = sensor.Metrics.FirstOrDefault(candidate => string.Equals(candidate.MetricKey, channel.Metric, StringComparison.Ordinal));
            return metric is not null
                && string.Equals(metric.DisplayName, channel.DisplayName ?? metric.DisplayName, StringComparison.Ordinal) && string.Equals(metric.Unit, channel.Unit ?? metric.Unit, StringComparison.Ordinal);
        });
}
