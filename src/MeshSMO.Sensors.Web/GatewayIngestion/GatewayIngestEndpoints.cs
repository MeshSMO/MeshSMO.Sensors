using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace MeshSMO.Sensors.Web.GatewayIngestion;

/// <summary>
/// Push mode: the gateway delivers telemetry batches to this API itself
/// instead of being polled. Only mapped when Gateway:Mode is Push; protected
/// by the same X-Api-Key scheme the gateway applies to its own telemetry API.
/// </summary>
public static class GatewayIngestEndpoints
{
    public static IEndpointRouteBuilder MapGatewayIngestApi(this IEndpointRouteBuilder app)
    {
        var ingestionOptions = app.ServiceProvider.GetRequiredService<IOptions<GatewayIngestionOptions>>().Value;
        var ingestOptions = app.ServiceProvider.GetRequiredService<IOptions<GatewayIngestOptions>>().Value;
        if (ingestionOptions.Mode != GatewayDeliveryMode.Push || string.IsNullOrWhiteSpace(ingestOptions.ApiKey))
            return app;

        var apiKey = ingestOptions.ApiKey;
        if (app is IApplicationBuilder application)
        {
            application.Use(async (context, next) =>
            {
                if (context.Request.Path.StartsWithSegments("/api/telemetry/ingest", StringComparison.Ordinal) &&
                    !IsApiKeyValid(context.Request.Headers["X-Api-Key"].ToString(), apiKey))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsJsonAsync(new { error = "Unauthorized" }).ConfigureAwait(false);
                    return;
                }

                await next().ConfigureAwait(false);
            });
        }

        app.MapPost("/api/telemetry/ingest", async Task<IResult> (
            GatewayTelemetryBatchDto? batch,
            IServiceScopeFactory scopeFactory,
            IOptions<GatewayIngestOptions> options,
            CancellationToken cancellationToken) =>
        {
            if (batch is null)
                return Results.Ok(new GatewayIngestResponse(0));

            if (batch.GatewayId == Guid.Empty)
            {
                return Results.Json(
                    new { error = "GatewayIdRequired" },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (batch.Snapshots.Count == 0)
                return Results.Ok(new GatewayIngestResponse(0));

            if (batch.Snapshots.Count > options.Value.MaximumBatchSize)
            {
                return Results.Json(
                    new { error = "BatchTooLarge" },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            int imported;
            var scope = scopeFactory.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var importer = scope.ServiceProvider.GetRequiredService<GatewayTelemetryImporter>();
                imported = await importer.ImportBatchAsync(
                    batch.GatewayId,
                    batch.Snapshots,
                    cancellationToken).ConfigureAwait(false);
            }

            return Results.Ok(new GatewayIngestResponse(imported));
        });

        return app;
    }

    private static bool IsApiKeyValid(string provided, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(provided),
            Encoding.UTF8.GetBytes(expected));
}

/// <summary>
/// Accepted is the number of newly stored snapshots; a 2xx response means the
/// whole batch is durably recorded (or was already known), so the gateway can
/// acknowledge it.
/// </summary>
public sealed record GatewayIngestResponse(int Accepted);
