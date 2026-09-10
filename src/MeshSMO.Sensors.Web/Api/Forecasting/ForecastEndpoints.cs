using MeshSMO.Sensors.Application.Abstractions;
using MeshSMO.Sensors.Domain.Sensors;
using MeshSMO.Sensors.Forecasting.Configuration;
using MeshSMO.Sensors.Forecasting.Models;
using MeshSMO.Sensors.Infrastructure.Persistence;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Polly.Timeout;

namespace MeshSMO.Sensors.Web.Api.Forecasting;

public static class ForecastEndpoints
{
    public static IEndpointRouteBuilder MapForecastApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/sensors/{slug}/forecast", HandleAsync)
            .RequireRateLimiting("forecast-api");
        return app;
    }

    private static async Task<IResult> HandleAsync(
        string slug,
        string? metric,
        string? horizon,
        HttpContext httpContext,
        SensorsDbContext dbContext,
        ForecastCoordinator coordinator,
        IClock clock,
        IOptions<ForecastingOptions> options,
        ILogger<ForecastCoordinator> logger,
        CancellationToken cancellationToken)
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
            .Include(static entity => entity.Metrics)
            .SingleOrDefaultAsync(
                entity => entity.Slug == slugValue && entity.Enabled && entity.PublicVisible,
                cancellationToken).ConfigureAwait(false);
        if (sensor is null)
            return Results.NotFound(new { error = "NotFound" });
        if (string.IsNullOrWhiteSpace(metric))
            return Validation("metric is required.");

        var metricMetadata = sensor.Metrics.SingleOrDefault(
            entity => string.Equals(entity.MetricKey, metric, StringComparison.Ordinal));
        if (metricMetadata is null)
            return Validation($"Unknown metric '{metric}' for sensor '{sensor.Slug.Value}'.");
        if (!TryParseHorizon(horizon, out var horizonValue))
            return Validation("horizon must be one of: 1h, 6h, 12h, 24h.");

        var seriesOptions = ResolveSeriesOptions(options.Value, sensor.Slug.Value, metric);
        if (!seriesOptions.Enabled)
        {
            return Results.Ok(Response(
                sensor.Slug.Value,
                sensor.DisplayName,
                metric,
                metricMetadata.Unit,
                ForecastResult.Unavailable(
                    ForecastAvailability.Disabled,
                    "Forecasting is disabled for this series.",
                    clock.UtcNow,
                    null,
                    TimeSpan.FromMinutes(options.Value.StepMinutes)),
                horizonValue));
        }

        var descriptor = new ForecastDescriptor(
            sensor.Id.Value,
            sensor.Slug.Value,
            metric,
            metricMetadata.Unit,
            TimeSpan.FromSeconds(sensor.PollIntervalSeconds),
            seriesOptions.Minimum,
            seriesOptions.Maximum,
            seriesOptions.MaximumMae);
        try
        {
            var result = await coordinator.GetAsync(descriptor, horizonValue, cancellationToken).ConfigureAwait(false);
            return Results.Ok(Response(
                sensor.Slug.Value,
                sensor.DisplayName,
                metric,
                metricMetadata.Unit,
                result,
                horizonValue));
        }
        catch (Exception exception) when (
            exception is TimeoutRejectedException ||
            exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            httpContext.Response.Headers.RetryAfter = "5";
            return Results.Json(
                new { error = "ForecastUnavailable", message = "Forecast calculation timed out." },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Forecast endpoint failed for {SensorSlug}/{MetricKey}",
                sensor.Slug.Value,
                metric);
            httpContext.Response.Headers.RetryAfter = "5";
            return Results.Json(
                new { error = "ForecastUnavailable", message = "Forecast calculation failed." },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static object Response(
        string slug,
        string displayName,
        string metric,
        string? unit,
        ForecastResult result,
        TimeSpan horizon) => new
        {
            sensor = new { slug, displayName },
            metric = new { key = metric, unit },
            availability = Availability(result.Availability),
            reason = result.Reason,
            generatedAt = result.GeneratedAt,
            lastObservationAt = result.LastObservationAt,
            range = new
            {
                from = result.Points.Count == 0 ? (DateTimeOffset?)null : result.Points[0].Timestamp,
                to = result.Points.Count == 0 ? (DateTimeOffset?)null : result.Points[0].Timestamp + horizon,
                horizon = Horizon(horizon),
                step = $"{result.Step.TotalMinutes:0}m",
            },
            model = result.Diagnostics is null ? null : new
            {
                kind = result.Diagnostics.ModelKind,
                windowSize = result.Diagnostics.WindowSize,
                trainingPoints = result.Diagnostics.TrainingPoints,
                trainingFrom = result.Diagnostics.TrainingFrom,
                trainingTo = result.Diagnostics.TrainingTo,
                observedCoverage = result.Diagnostics.ObservedCoverage,
                interpolatedPoints = result.Diagnostics.InterpolatedPoints,
                mae = result.Diagnostics.Mae,
                rmse = result.Diagnostics.Rmse,
                mase = result.Diagnostics.Mase,
                intervalCoverage = result.Diagnostics.IntervalCoverage,
                confidenceLevel = result.Diagnostics.ConfidenceLevel,
            },
            points = result.Points.Select(static point => new
            {
                timestamp = point.Timestamp,
                predicted = point.Predicted,
                lower = point.Lower,
                upper = point.Upper,
            }),
        };

    private static ForecastSeriesOptions ResolveSeriesOptions(
        ForecastingOptions options,
        string slug,
        string metric)
    {
        var defaults = metric switch
        {
            "humidity" or "percentage" or "soil_moisture" =>
                new ForecastSeriesOptions { Minimum = 0, Maximum = 100 },
            "solar_panel_voltage" =>
                new ForecastSeriesOptions { Minimum = 0, Maximum = 5 },
            _ => new ForecastSeriesOptions(),
        };
        if (!options.Series.TryGetValue($"{slug}.{metric}", out var configured))
            return defaults;

        return new()
        {
            Enabled = configured.Enabled,
            Minimum = configured.Minimum ?? defaults.Minimum,
            Maximum = configured.Maximum ?? defaults.Maximum,
            MaximumMae = configured.MaximumMae,
        };
    }

    private static bool TryParseHorizon(string? value, out TimeSpan horizon)
    {
        horizon = value?.Trim().ToLowerInvariant() switch
        {
            "1h" => TimeSpan.FromHours(1),
            "6h" => TimeSpan.FromHours(6),
            "12h" => TimeSpan.FromHours(12),
            "24h" => TimeSpan.FromHours(24),
            _ => TimeSpan.Zero,
        };
        return horizon > TimeSpan.Zero;
    }

    private static string Horizon(TimeSpan horizon) => $"{horizon.TotalHours:0}h";

    private static string Availability(ForecastAvailability availability) => availability switch
    {
        ForecastAvailability.Ready => "ready",
        ForecastAvailability.InsufficientData => "insufficient_data",
        ForecastAvailability.SparseData => "sparse_data",
        ForecastAvailability.StaleData => "stale_data",
        ForecastAvailability.LowQuality => "low_quality",
        ForecastAvailability.Disabled => "disabled",
        _ => throw new ArgumentOutOfRangeException(nameof(availability), availability, null),
    };

    private static IResult Validation(string message) =>
        Results.BadRequest(new { error = "ValidationError", message });
}
