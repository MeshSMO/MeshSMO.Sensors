using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace MeshSMO.Sensors.Web.GatewayIngestion;

public sealed record GatewayTelemetryReadingDto(
    [property: JsonPropertyName("metricKey")] string MetricKey,
    [property: JsonPropertyName("numericValue")] double? NumericValue,
    [property: JsonPropertyName("textValue")] string? TextValue);

public sealed record GatewayTelemetrySnapshotDto(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("capturedAt")] DateTimeOffset CapturedAt,
    [property: JsonPropertyName("transport")] string Transport,
    [property: JsonPropertyName("payloadJson")] string PayloadJson,
    [property: JsonPropertyName("readings")] GatewayTelemetryReadingDto[] Readings);

public sealed record GatewayTelemetryBatchDto(
    [property: JsonPropertyName("pendingCount")] long PendingCount,
    [property: JsonPropertyName("snapshots")] IReadOnlyList<GatewayTelemetrySnapshotDto> Snapshots);

/// <summary>HTTP client for the sensor-gateway local telemetry API.</summary>
public sealed class GatewayTelemetryClient(HttpClient httpClient, IOptions<GatewayIngestionOptions> options)
{
    public async Task<GatewayTelemetryBatchDto?> FetchPendingAsync(int batchSize, CancellationToken cancellationToken)
    {
        var url = $"api/telemetry/pending?maxCount={batchSize}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyApiKey(request);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GatewayTelemetryBatchDto>(cancellationToken);
    }

    public async Task AcknowledgeAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/telemetry/ack")
        {
            Content = JsonContent.Create(new { ids }),
        };
        ApplyApiKey(request);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private void ApplyApiKey(HttpRequestMessage request)
    {
        var apiKey = options.Value.ApiKey;
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Add("X-Api-Key", apiKey);
        }
    }
}
