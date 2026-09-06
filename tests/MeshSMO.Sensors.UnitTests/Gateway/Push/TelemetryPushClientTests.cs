using System.Net;
using System.Text.Json;
using MeshSMO.Sensors.Gateway.Api;
using MeshSMO.Sensors.Gateway.Push;
using Microsoft.Extensions.Options;

namespace MeshSMO.Sensors.UnitTests.Gateway.Push;

public sealed class TelemetryPushClientTests
{
    [Fact]
    public async Task PushAsync_PostsBatch_WithApiKey()
    {
        string? apiKey = null;
        string? requestUrl = null;
        string? body = null;
        var handler = new StubHttpMessageHandler(async request =>
        {
            requestUrl = request.RequestUri?.PathAndQuery;
            apiKey = request.Headers.TryGetValues("X-Api-Key", out var values) ? values.SingleOrDefault() : null;
            body = await request.Content!.ReadAsStringAsync().ConfigureAwait(false);
            return (HttpStatusCode.OK, """{"accepted":2}""");
        });
        var client = CreateClient(handler, "secret-key");
        var batch = new TelemetryBatchDto(2,
        [
            new TelemetrySnapshotDto(11, DateTimeOffset.UnixEpoch, "Serial", """{"a":1}""",
                [new TelemetryReadingDto("core.battery_mv", 4100, null)]),
            new TelemetrySnapshotDto(12, DateTimeOffset.UnixEpoch, "Serial", """{"a":2}""",
                [new TelemetryReadingDto("radio.rssi", -92, null)]),
        ]);

        var accepted = await client.PushAsync(batch, CancellationToken.None);

        Assert.Equal("/api/telemetry/ingest", requestUrl);
        Assert.Equal("secret-key", apiKey);
        Assert.Equal(2, accepted);
        var json = JsonDocument.Parse(body!);
        Assert.Equal(2, json.RootElement.GetProperty("pendingCount").GetInt64());
        var snapshots = json.RootElement.GetProperty("snapshots").EnumerateArray().ToArray();
        Assert.Equal(2, snapshots.Length);
        Assert.Equal(11, snapshots[0].GetProperty("id").GetInt64());
        Assert.Equal("Serial", snapshots[0].GetProperty("transport").GetString());
        Assert.Equal(
            "core.battery_mv",
            snapshots[0].GetProperty("readings").EnumerateArray().Single()
                .GetProperty("metricKey").GetString());
    }

    [Fact]
    public async Task PushAsync_ErrorStatus_Throws()
    {
        var handler = new StubHttpMessageHandler(_ => (HttpStatusCode.ServiceUnavailable, "{}"));
        var client = CreateClient(handler, "secret-key");
        var batch = new TelemetryBatchDto(1, []);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.PushAsync(batch, CancellationToken.None));
    }

    private static TelemetryPushClient CreateClient(StubHttpMessageHandler handler, string? apiKey) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") },
            Options.Create(new TelemetryPushOptions { ApiUrl = new Uri("http://localhost/"), ApiKey = apiKey }));

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, Task<(HttpStatusCode, string)>> responder) : HttpMessageHandler
    {
        public StubHttpMessageHandler(Func<HttpRequestMessage, (HttpStatusCode, string)> responder)
            : this(request => Task.FromResult(responder(request)))
        {
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var (statusCode, content) = await responder(request).ConfigureAwait(false);
            return new HttpResponseMessage(statusCode) { Content = new StringContent(content) };
        }
    }
}
