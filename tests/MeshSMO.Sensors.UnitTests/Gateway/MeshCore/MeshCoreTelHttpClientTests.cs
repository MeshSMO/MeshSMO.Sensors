using System.Net;
using System.Text;
using MeshSMO.Sensors.Gateway.MeshCore;
using Microsoft.Extensions.Options;

namespace MeshSMO.Sensors.UnitTests.Gateway.MeshCore;

public sealed class MeshCoreTelHttpClientTests
{
    [Fact]
    public async Task ExecuteCommandLogsInAndSendsSessionToken()
    {
        var handler = new RecordingHandler(
            TextResponse(HttpStatusCode.OK, "session-token\n"),
            TextResponse(HttpStatusCode.OK, "MeshCoreTel 1.2.3"));
        using var client = CreateClient(handler);

        var result = await client.ExecuteCommandAsync("ver", CancellationToken.None);

        Assert.Equal("MeshCoreTel 1.2.3", result);
        Assert.Collection(
            handler.Requests,
            login =>
            {
                Assert.Equal(HttpMethod.Post, login.Method);
                Assert.Equal("https://repeater.local/login", login.Uri.AbsoluteUri);
                Assert.Equal("secret", login.Body);
                Assert.Null(login.AuthToken);
            },
            command =>
            {
                Assert.Equal(HttpMethod.Post, command.Method);
                Assert.Equal("https://repeater.local/api/command", command.Uri.AbsoluteUri);
                Assert.Equal("ver", command.Body);
                Assert.Equal("session-token", command.AuthToken);
            });
    }

    [Fact]
    public async Task UnauthorizedCommandReauthenticatesAndRetriesOnce()
    {
        var handler = new RecordingHandler(
            TextResponse(HttpStatusCode.OK, "first-token"),
            TextResponse(HttpStatusCode.Unauthorized, "Unauthorized"),
            TextResponse(HttpStatusCode.OK, "second-token"),
            TextResponse(HttpStatusCode.OK, "OK"));
        using var client = CreateClient(handler);

        var result = await client.ExecuteCommandAsync("advert", CancellationToken.None);

        Assert.Equal("OK", result);
        Assert.Equal(4, handler.Requests.Count);
        Assert.Equal("first-token", handler.Requests[1].AuthToken);
        Assert.Equal("second-token", handler.Requests[3].AuthToken);
    }

    [Fact]
    public async Task GetStatsRequestsOnlyOneEscapedSeries()
    {
        var handler = new RecordingHandler(
            TextResponse(HttpStatusCode.OK, "token"),
            JsonResponse(HttpStatusCode.OK, "{\"current\":42}"));
        using var client = CreateClient(handler);

        using var result = await client.GetStatsAsync("sensor temp", CancellationToken.None);

        Assert.Equal(42, result.RootElement.GetProperty("current").GetInt32());
        Assert.Equal(
            "https://repeater.local/api/stats?series=sensor%20temp",
            handler.Requests[1].Uri.AbsoluteUri);
        Assert.Equal("token", handler.Requests[1].AuthToken);
    }

    [Fact]
    public async Task ApiErrorIncludesStatusAndResponseBody()
    {
        var handler = new RecordingHandler(
            TextResponse(HttpStatusCode.OK, "token"),
            TextResponse(HttpStatusCode.ServiceUnavailable, "Stats disabled"));
        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<MeshCoreTelApiException>(
            () => client.GetStatsAsync(null, CancellationToken.None));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.Equal("Stats disabled", exception.ResponseBody);
    }

    [Fact]
    public async Task CommandLongerThanFirmwareBufferIsRejectedLocally()
    {
        var handler = new RecordingHandler();
        using var client = CreateClient(handler);

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.ExecuteCommandAsync(new string('я', 96), CancellationToken.None));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SecondClientInstanceReusesSharedSessionToken()
    {
        var handler = new RecordingHandler(
            TextResponse(HttpStatusCode.OK, "shared-token\n"),
            JsonResponse(HttpStatusCode.OK, "{\"current\":42}"),
            TextResponse(HttpStatusCode.OK, "MeshCoreTel 1.2.3"));
        var session = new MeshCoreTelSession();
        using var first = CreateClient(handler, session);
        using var second = CreateClient(handler, session);

        using var stats = await first.GetStatsAsync(null, CancellationToken.None);
        var version = await second.ExecuteCommandAsync("ver", CancellationToken.None);

        Assert.Equal(42, stats.RootElement.GetProperty("current").GetInt32());
        Assert.Equal("MeshCoreTel 1.2.3", version);
        Assert.Single(handler.Requests, request => request.Uri.AbsoluteUri.EndsWith("/login", StringComparison.Ordinal));
        Assert.All(handler.Requests.Skip(1), request => Assert.Equal("shared-token", request.AuthToken));
    }

    [Fact]
    public async Task ConnectAsyncDoesNotReloginWhenSessionTokenAlreadyExists()
    {
        var handler = new RecordingHandler(
            TextResponse(HttpStatusCode.OK, "shared-token"),
            TextResponse(HttpStatusCode.OK, "shared-token"));
        var session = new MeshCoreTelSession();
        using var first = CreateClient(handler, session);
        using var second = CreateClient(handler, session);

        await first.ConnectAsync(CancellationToken.None);
        await second.ConnectAsync(CancellationToken.None);

        Assert.Single(handler.Requests, request => request.Uri.AbsoluteUri.EndsWith("/login", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ClientReusesTokenRefreshedByAnotherInstanceAfterUnauthorized()
    {
        var handler = new RecordingHandler(
            TextResponse(HttpStatusCode.OK, "first-token"),
            JsonResponse(HttpStatusCode.OK, "{\"current\":1}"),
            TextResponse(HttpStatusCode.Unauthorized, "Unauthorized"),
            TextResponse(HttpStatusCode.OK, "second-token"),
            JsonResponse(HttpStatusCode.OK, "{\"current\":2}"),
            TextResponse(HttpStatusCode.OK, "OK"));
        var session = new MeshCoreTelSession();
        using var first = CreateClient(handler, session);
        using var second = CreateClient(handler, session);

        using var firstStats = await first.GetStatsAsync(null, CancellationToken.None);
        using var secondStats = await second.GetStatsAsync(null, CancellationToken.None);
        var command = await first.ExecuteCommandAsync("ver", CancellationToken.None);

        Assert.Equal(1, firstStats.RootElement.GetProperty("current").GetInt32());
        Assert.Equal(2, secondStats.RootElement.GetProperty("current").GetInt32());
        Assert.Equal("OK", command);
        Assert.Equal(2, handler.Requests.Count(request => request.Uri.AbsoluteUri.EndsWith("/login", StringComparison.Ordinal)));
        Assert.Equal("second-token", handler.Requests[4].AuthToken);
        Assert.Equal("second-token", handler.Requests[5].AuthToken);
    }

    private static MeshCoreTelHttpClient CreateClient(RecordingHandler handler, MeshCoreTelSession? session = null)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://repeater.local/"),
        };
        var options = Options.Create(new MeshCoreOptions
        {
            Mode = MeshCoreConnectionMode.Http,
            Http = new MeshCoreHttpOptions
            {
                BaseAddress = httpClient.BaseAddress,
                AdminPassword = "secret",
            },
        });

        return new MeshCoreTelHttpClient(httpClient, session ?? new MeshCoreTelSession(), options);
    }

    private static HttpResponseMessage TextResponse(HttpStatusCode statusCode, string content) => new HttpResponseMessage(statusCode)
    {
        Content = new StringContent(content, Encoding.UTF8, "text/plain"),
    };

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string content) => new HttpResponseMessage(statusCode)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json"),
    };

    private sealed class RecordingHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        public List<RequestSnapshot> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            request.Headers.TryGetValues("X-Auth-Token", out var tokenValues);
            Requests.Add(new RequestSnapshot(
                request.Method,
                request.RequestUri!,
                body,
                tokenValues?.SingleOrDefault()));

            return _responses.Dequeue();
        }
    }

    private sealed record RequestSnapshot(
        HttpMethod Method,
        Uri Uri,
        string? Body,
        string? AuthToken);
}
