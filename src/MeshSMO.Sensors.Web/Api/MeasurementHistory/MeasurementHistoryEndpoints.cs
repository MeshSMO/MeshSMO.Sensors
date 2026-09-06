using MeshSMO.Sensors.Domain.Sensors;
using MeshSMO.Sensors.Infrastructure.Persistence;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace MeshSMO.Sensors.Web.Api.MeasurementHistory;

public static class MeasurementHistoryEndpoints
{
    public static IEndpointRouteBuilder MapMeasurementHistoryApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/sensors/{slug}/measurements", async Task<IResult> (
            string slug,
            string? metric,
            DateTimeOffset? from,
            DateTimeOffset? to,
            string? resolution,
            SensorsDbContext dbContext,
            MeasurementHistoryReader reader,
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

            if (!MeasurementResolutionPolicy.TryParse(resolution, out var requestedResolution))
                return Validation("resolution must be one of: auto, raw, 5m, 15m, 1h, 6h, 1d.");
            if (string.IsNullOrWhiteSpace(metric))
                return Validation("metric is required.");
            if (from is null || to is null)
                return Validation("from and to are required and must be ISO-8601 timestamps.");
            var fromValue = from.Value.ToUniversalTime();
            var toValue = to.Value.ToUniversalTime();
            if (fromValue >= toValue)
                return Validation("from must be earlier than to.");

            var range = toValue - fromValue;
            var effectiveResolution = requestedResolution ?? MeasurementResolutionPolicy.ResolveAuto(range);
            var allowedRange = MeasurementResolutionPolicy.AllowedRange(effectiveResolution);
            if (range > MeasurementResolutionPolicy.MaxRange || range > allowedRange)
            {
                return Validation(
                    $"Range is too large for resolution '{MeasurementResolutionPolicy.ToApiString(effectiveResolution)}'; " +
                    $"the maximum is {MeasurementResolutionPolicy.AllowedRange(effectiveResolution).TotalDays:0} day(s).");
            }

            var metricMeta = sensor.Metrics
                .SingleOrDefault(entity => string.Equals(entity.MetricKey, metric, StringComparison.Ordinal));
            if (metricMeta is null)
            {
                return Validation(
                    $"Unknown metric '{metric}' for sensor '{sensor.Slug.Value}'.");
            }

            var points = await reader.ReadAsync(
                sensor.Id.Value,
                metric,
                fromValue,
                toValue,
                effectiveResolution,
                cancellationToken).ConfigureAwait(false);
            return Results.Ok(new
            {
                sensor = new { slug = sensor.Slug.Value, displayName = sensor.DisplayName },
                metric = new { key = metric, unit = metricMeta.Unit },
                range = new
                {
                    from = fromValue,
                    to = toValue,
                    resolution = MeasurementResolutionPolicy.ToApiString(effectiveResolution),
                },
                points = points.Select(point => new
                {
                    timestamp = point.Timestamp,
                    min = point.Min,
                    avg = point.Avg,
                    max = point.Max,
                }),
            });
        }).RequireRateLimiting("public-api");

        return app;
    }

    private static IResult Validation(string message) =>
        Results.BadRequest(new { error = "ValidationError", message });
}
