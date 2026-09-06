using MeshSMO.Sensors.Gateway.Api;
using MeshSMO.Sensors.Gateway.LocalStorage;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace MeshSMO.Sensors.Gateway.Api;

public static class TelemetryEndpoints
{
    public static IEndpointRouteBuilder MapTelemetryApi(this IEndpointRouteBuilder app)
    {
        var apiOptions = app.ServiceProvider.GetRequiredService<IOptions<GatewayApiOptions>>().Value;
        if (app is IApplicationBuilder application && !string.IsNullOrWhiteSpace(apiOptions.ApiKey))
        {
            var apiKey = apiOptions.ApiKey;
            application.Use(async (context, next) =>
            {
                if (context.Request.Path.StartsWithSegments("/api/telemetry", StringComparison.Ordinal) &&
                    !string.Equals(context.Request.Headers["X-Api-Key"], apiKey, StringComparison.Ordinal))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsJsonAsync(new { error = "Unauthorized" }).ConfigureAwait(false);
                    return;
                }

                await next().ConfigureAwait(false);
            });
        }

        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready"),
        });

        app.MapGet("/api/telemetry/pending", async Task<IResult> (
            int? maxCount,
            ILocalTelemetryStore store,
            IOptions<GatewayApiOptions> options,
            CancellationToken cancellationToken) =>
        {
            var requested = maxCount is null or < 1 ? 100 : maxCount.Value;
            var limit = Math.Min(requested, options.Value.MaximumBatchSize);

            var snapshots = await store.ReadPendingAsync(limit, cancellationToken).ConfigureAwait(false);
            var pendingCount = await store.CountPendingAsync(cancellationToken).ConfigureAwait(false);

            var items = snapshots
                .Select(snapshot => new TelemetrySnapshotDto(
                    snapshot.Id,
                    snapshot.CapturedAt,
                    snapshot.Transport,
                    snapshot.PayloadJson,
                    snapshot.Readings
                        .Select(reading => new TelemetryReadingDto(
                            reading.MetricKey,
                            reading.NumericValue,
                            reading.TextValue))
                        .ToArray()))
                .ToList();

            return Results.Ok(new TelemetryBatchDto(pendingCount, items));
        });

        app.MapPost("/api/telemetry/ack", async Task<IResult> (
            AcknowledgeRequest request,
            ILocalTelemetryStore store,
            CancellationToken cancellationToken) =>
        {
            if (request.Ids.Count == 0)
                return Results.Ok(new AcknowledgeResponse(0));

            await store.AcknowledgeAsync(request.Ids.ToArray(), cancellationToken).ConfigureAwait(false);
            return Results.Ok(new AcknowledgeResponse(request.Ids.Count));
        });

        return app;
    }
}

public sealed record TelemetryReadingDto(string MetricKey, double? NumericValue, string? TextValue);

public sealed record TelemetrySnapshotDto(
    long Id,
    DateTimeOffset CapturedAt,
    string Transport,
    string PayloadJson,
    TelemetryReadingDto[] Readings);

public sealed record TelemetryBatchDto(long PendingCount, IReadOnlyList<TelemetrySnapshotDto> Snapshots);

public sealed record AcknowledgeRequest(IReadOnlyList<long> Ids);

public sealed record AcknowledgeResponse(int Acknowledged);
