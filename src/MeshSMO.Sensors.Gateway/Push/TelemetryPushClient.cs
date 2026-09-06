using System.Net.Http.Json;
using System.Text.Json.Serialization;
using MeshSMO.Sensors.Gateway.Api;
using Microsoft.Extensions.Options;

namespace MeshSMO.Sensors.Gateway.Push;

public sealed record TelemetryPushResponse(
    [property: JsonPropertyName("accepted")] int Accepted);

/// <summary>HTTP client that delivers telemetry batches to the main API ingest endpoint.</summary>
public sealed class TelemetryPushClient(HttpClient httpClient, IOptions<TelemetryPushOptions> options)
{
    /// <summary>
    /// Posts a batch to api/telemetry/ingest and returns the number of newly
    /// imported snapshots reported by the API. Throws on any non-success
    /// status so the worker keeps the batch in the local outbox.
    /// </summary>
    public async Task<int> PushAsync(TelemetryBatchDto batch, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/telemetry/ingest")
        {
            Content = JsonContent.Create(batch),
        };
        var apiKey = options.Value.ApiKey;
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Add("X-Api-Key", apiKey);
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<TelemetryPushResponse>(cancellationToken);
        return result?.Accepted ?? 0;
    }
}
