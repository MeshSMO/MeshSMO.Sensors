using MeshSMO.Sensors.Domain.Sensors;
using MeshSMO.Sensors.Infrastructure.Persistence;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace MeshSMO.Sensors.Web.Api;

public static class SensorApiEndpoints
{
    private const string BatteryVoltageMetricKey = "battery_voltage";
    private const string BatteryMetricKey = "battery";

    public static IEndpointRouteBuilder MapSensorApi(this IEndpointRouteBuilder app)
    {
        var sensors = app.MapGroup("/api/v1/sensors").RequireRateLimiting("public-api");

        sensors.MapGet("/", async Task<IResult> (SensorsDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var list = await LoadPublicSensors(dbContext, cancellationToken).ConfigureAwait(false);
            var statuses = await dbContext.SensorStatuses
                .AsNoTracking()
                .ToDictionaryAsync(snapshot => snapshot.SensorId, snapshot => snapshot.State, cancellationToken).ConfigureAwait(false);
            var batteryVoltages = await LoadBatteryVoltages(dbContext, cancellationToken).ConfigureAwait(false);
            return Results.Json(
                new
                {
                    sensors = list.Select(sensor => ToSummary(
                        sensor,
                        statuses.GetValueOrDefault(sensor.Id),
                        batteryVoltages.GetValueOrDefault(sensor.Id))),
                },
                statusCode: 200);
        });

        sensors.MapGet("/{slug}", async Task<IResult> (
            string slug,
            SensorsDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            SensorSlug slugValue;
            try
            {
                slugValue = new(slug);
            }
            catch (ArgumentException)
            {
                return Results.NotFound(new { error = "NotFound" });
            }

            var sensor = await dbContext.Sensors
                .AsNoTracking()
                .Include(entity => entity.Metrics)
                .SingleOrDefaultAsync(
                    entity => entity.Slug == slugValue && entity.Enabled && entity.PublicVisible,
                    cancellationToken).ConfigureAwait(false);
            if (sensor is null)
                return Results.NotFound(new { error = "NotFound" });

            var status = await dbContext.SensorStatuses
                .AsNoTracking()
                .SingleOrDefaultAsync(snapshot => snapshot.SensorId == sensor.Id, cancellationToken).ConfigureAwait(false);
            return Results.Json(new
            {
                slug = sensor.Slug.Value,
                displayName = sensor.DisplayName,
                description = sensor.Description,
                location = sensor.Latitude is null || sensor.Longitude is null
                    ? null
                    : new { latitude = sensor.Latitude, longitude = sensor.Longitude, precision = sensor.LocationPrecision },
                metrics = sensor.Metrics.Select(metric => metric.MetricKey).OrderBy(key => key, StringComparer.Ordinal).ToArray(),
                protocol = sensor.ProtocolId,
                pollIntervalSeconds = sensor.PollIntervalSeconds,
                state = (status?.State ?? SensorState.Unknown).ToString(),
            });
        });

        sensors.MapGet("/{slug}/status", async Task<IResult> (
            string slug,
            SensorsDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            SensorSlug slugValue;
            try
            {
                slugValue = new(slug);
            }
            catch (ArgumentException)
            {
                return Results.NotFound(new { error = "NotFound" });
            }

            var sensor = await dbContext.Sensors
                .AsNoTracking()
                .Include(entity => entity.Metrics)
                .SingleOrDefaultAsync(
                    entity => entity.Slug == slugValue && entity.Enabled && entity.PublicVisible,
                    cancellationToken).ConfigureAwait(false);
            if (sensor is null)
                return Results.NotFound(new { error = "NotFound" });

            var status = await dbContext.SensorStatuses
                .AsNoTracking()
                .SingleOrDefaultAsync(snapshot => snapshot.SensorId == sensor.Id, cancellationToken).ConfigureAwait(false);
            return Results.Json(ToStatus(status));
        });

        sensors.MapGet("/{slug}/latest", async Task<IResult> (
            string slug,
            SensorsDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            SensorSlug slugValue;
            try
            {
                slugValue = new(slug);
            }
            catch (ArgumentException)
            {
                return Results.NotFound(new { error = "NotFound" });
            }

            var sensor = await dbContext.Sensors
                .AsNoTracking()
                .Include(entity => entity.Metrics)
                .SingleOrDefaultAsync(
                    entity => entity.Slug == slugValue && entity.Enabled && entity.PublicVisible,
                    cancellationToken).ConfigureAwait(false);
            if (sensor is null)
                return Results.NotFound(new { error = "NotFound" });

            var metricMeta = await dbContext.SensorMetrics
                .AsNoTracking()
                .Where(metric => metric.SensorId == sensor.Id)
                .ToDictionaryAsync(
                    metric => metric.MetricKey,
                    metric => new { metric.DisplayName, metric.Unit },
StringComparer.Ordinal, cancellationToken).ConfigureAwait(false);
            var values = await dbContext.MeasurementValues
                .AsNoTracking()
                .Where(value => value.SensorId == sensor.Id && value.Timestamp == dbContext.MeasurementValues
                    .Where(inner => inner.SensorId == sensor.Id && inner.MetricKey == value.MetricKey)
                    .Max(inner => inner.Timestamp))
                .OrderBy(value => value.MetricKey)
                .Select(value => new { value.MetricKey, value.Timestamp, value.NumericValue, value.TextValue, value.Unit })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            return Results.Json(new
            {
                values = values.Select(value => new
                {
                    metric = value.MetricKey,
                    displayName = metricMeta.TryGetValue(value.MetricKey, out var meta) ? meta.DisplayName : null,
                    timestamp = value.Timestamp,
                    value.NumericValue,
                    value.TextValue,
                    unit = value.Unit ?? (metricMeta.TryGetValue(value.MetricKey, out meta) ? meta.Unit : null),
                }),
            });
        });

        app.MapGet("/api/v1/dashboard", async Task<IResult> (
            SensorsDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var sensors = await LoadPublicSensors(dbContext, cancellationToken).ConfigureAwait(false);
            var statuses = await dbContext.SensorStatuses
                .AsNoTracking()
                .ToDictionaryAsync(snapshot => snapshot.SensorId, snapshot => snapshot.State, cancellationToken).ConfigureAwait(false);
            var batteryVoltages = await LoadBatteryVoltages(dbContext, cancellationToken).ConfigureAwait(false);
            var summary = DashboardSummary.Build(sensors.Select(sensor => statuses.GetValueOrDefault(sensor.Id, SensorState.Unknown)));
            return Results.Json(new
            {
                summary = new
                {
                    total = summary.Total,
                    online = summary.Online,
                    degraded = summary.Degraded,
                    offline = summary.Offline,
                    unknown = summary.Unknown,
                },
                sensors = sensors.Select(sensor => ToSummary(
                    sensor,
                    statuses.GetValueOrDefault(sensor.Id, SensorState.Unknown),
                    batteryVoltages.GetValueOrDefault(sensor.Id))),
            });
        }).RequireRateLimiting("public-api");

        return app;
    }

    private static async Task<List<Sensor>> LoadPublicSensors(SensorsDbContext dbContext, CancellationToken cancellationToken) =>
        await dbContext.Sensors
            .AsNoTracking()
            .Include(sensor => sensor.Metrics)
            .Where(sensor => sensor.Enabled && sensor.PublicVisible)
            .OrderBy(sensor => sensor.Slug)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Последнее значение заряда батареи по каждому публичному датчику.
    /// Ключи — по договорённости с gateway (канал LPP 116); battery_voltage имеет приоритет.
    /// Значения скрытых датчиков в ответ не попадают: словарь читается только по id публичных.
    /// </summary>
    private static async Task<Dictionary<SensorId, double?>> LoadBatteryVoltages(
        SensorsDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var batteryMetricKeys = new[] { BatteryVoltageMetricKey, BatteryMetricKey };
        var rows = await dbContext.MeasurementValues
            .AsNoTracking()
            .Where(value => batteryMetricKeys.Contains(value.MetricKey)
                && value.Timestamp == dbContext.MeasurementValues
                    .Where(latest => latest.SensorId == value.SensorId && latest.MetricKey == value.MetricKey)
                    .Max(latest => latest.Timestamp))
            .Select(value => new { value.SensorId, value.MetricKey, value.NumericValue })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var voltages = new Dictionary<SensorId, double?>();
        foreach (var row in rows.OrderByDescending(row => string.Equals(row.MetricKey, BatteryVoltageMetricKey, StringComparison.Ordinal)))
        {
            if (row.NumericValue is { } voltage)
                voltages.TryAdd(row.SensorId, voltage);
        }

        return voltages;
    }

    private static object ToSummary(Sensor sensor, SensorState? state = null, double? batteryVoltage = null) => new
    {
        slug = sensor.Slug.Value,
        displayName = sensor.DisplayName,
        description = sensor.Description,
        latitude = sensor.Latitude,
        longitude = sensor.Longitude,
        metrics = sensor.Metrics.Select(metric => metric.MetricKey).OrderBy(key => key, StringComparer.Ordinal).ToArray(),
        batteryVoltage,
        state = (state ?? SensorState.Unknown).ToString(),
    };

    private static object ToStatus(SensorStatusSnapshot? status) => new
    {
        state = (status?.State ?? SensorState.Unknown).ToString(),
        lastPollAt = status?.LastPollAt,
        lastSuccessAt = status?.LastSuccessAt,
        consecutiveFailures = status?.ConsecutiveFailures ?? 0,
        lastRssi = status?.LastRssi,
        lastSnr = status?.LastSnr,
        updatedAt = status?.UpdatedAt,
    };

    public sealed record DashboardSummary(int Total, int Online, int Degraded, int Offline, int Unknown)
    {
        public static DashboardSummary Build(IEnumerable<SensorState> states)
        {
            var total = 0;
            var online = 0;
            var degraded = 0;
            var offline = 0;
            var unknown = 0;
            foreach (var state in states)
            {
                total++;
                switch (state)
                {
                    case SensorState.Online:
                        online++;
                        break;
                    case SensorState.Degraded:
                        degraded++;
                        break;
                    case SensorState.Offline:
                        offline++;
                        break;
                    default:
                        unknown++;
                        break;
                }
            }

            return new(total, online, degraded, offline, unknown);
        }
    }
}
