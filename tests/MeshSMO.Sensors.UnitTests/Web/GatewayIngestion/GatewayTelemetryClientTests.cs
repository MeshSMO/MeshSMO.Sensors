using System.Net;
using System.Text.Json;
using MeshSMO.Sensors.Web.GatewayIngestion;
using Microsoft.Extensions.Options;

namespace MeshSMO.Sensors.UnitTests.Web.GatewayIngestion;

public sealed class GatewayTelemetryClientTests
{
    [Fact]
    public async Task FetchPendingAsync_ParsesBatch_AndSendsApiKey()
    {
        string? apiKey = null;
        string? requestUrl = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            requestUrl = request.RequestUri?.PathAndQuery;
            apiKey = request.Headers.TryGetValues("X-Api-Key", out var values) ? values.SingleOrDefault() : null;
            return (
                HttpStatusCode.OK,
                """{"pendingCount":2,"snapshots":[{"id":7,"capturedAt":"2026-09-05T10:00:00Z","transport":"Http","payloadJson":"{\"core\":{}}"}]}""");
        });
        var client = CreateClient(handler, "secret-key");

        var batch = await client.FetchPendingAsync(50, CancellationToken.None);

        Assert.Equal("/api/telemetry/pending?maxCount=50", requestUrl);
        Assert.Equal("secret-key", apiKey);
        Assert.NotNull(batch);
        Assert.Equal(2, batch.PendingCount);
        var snapshot = Assert.Single(batch.Snapshots);
        Assert.Equal(7, snapshot.Id);
        Assert.Equal("Http", snapshot.Transport);
        Assert.Equal("{\"core\":{}}", snapshot.PayloadJson);
    }

    [Fact]
    public async Task FetchPendingAsync_ErrorStatus_Throws()
    {
        var handler = new StubHttpMessageHandler(_ => (HttpStatusCode.InternalServerError, "{}"));
        var client = CreateClient(handler, apiKey: null);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.FetchPendingAsync(10, CancellationToken.None));
    }

    [Fact]
    public async Task AcknowledgeAsync_PostsIds_WithApiKey()
    {
        string? apiKey = null;
        string? body = null;
        var handler = new StubHttpMessageHandler(async request =>
        {
            apiKey = request.Headers.TryGetValues("X-Api-Key", out var values) ? values.SingleOrDefault() : null;
            body = await request.Content!.ReadAsStringAsync().ConfigureAwait(false);
            return (HttpStatusCode.OK, """{"acknowledged":2}""");
        });
        var client = CreateClient(handler, "secret-key");

        await client.AcknowledgeAsync([3, 4], CancellationToken.None);

        Assert.Equal("secret-key", apiKey);
        var json = JsonDocument.Parse(body!);
        Assert.Equal([3L, 4L], json.RootElement.GetProperty("ids").EnumerateArray()
            .Select(element => element.GetInt64()).ToArray());
    }

    private static GatewayTelemetryClient CreateClient(StubHttpMessageHandler handler, string? apiKey) =>
        new(
            new(handler) { BaseAddress = new("http://localhost/") },
            Options.Create(new GatewayIngestionOptions { BaseUrl = new("http://localhost/"), ApiKey = apiKey }));

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
            return new(statusCode) { Content = new StringContent(content) };
        }
    }
}
