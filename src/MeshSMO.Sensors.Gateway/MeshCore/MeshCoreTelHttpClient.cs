using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MeshSMO.Sensors.Gateway.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace MeshSMO.Sensors.Gateway.MeshCore;

public sealed class MeshCoreTelHttpClient(
    HttpClient httpClient,
    MeshCoreTelSession session,
    IOptions<MeshCoreOptions> options) : IMeshCoreTelClient, IDisposable
{
    private const int MaximumCommandBytes = 191;
    private const int MaximumPasswordBytes = 79;
    private const int MaximumErrorBodyLength = 512;
    private const double AcquisitionTimeoutFactor = 2.5;
    private const double AcquisitionOverheadMilliseconds = 5_000;
    private readonly ResiliencePipeline<HttpResponseMessage> _authorizationPipeline =
        GatewayResiliencePipelines.CreateAuthorizationPipeline();
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private readonly TimeSpan _panelTimeout = TimeSpan.FromSeconds(options.Value.Http.TimeoutSeconds);
    private readonly string _adminPassword = options.Value.Http.AdminPassword;

    public string TransportName => "http";

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_panelTimeout);
        await session.GetTokenAsync(AuthenticateAsync, timeout.Token).ConfigureAwait(false);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        session.ClearToken();
        return Task.CompletedTask;
    }

    public Task<string> ExecuteCommandAsync(string command, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        EnsureUtf8Length(command, MaximumCommandBytes, nameof(command));

        return SendAuthorizedAsync(
            () => CreateTextRequest(HttpMethod.Post, "api/command", command),
            static async (response, token) => await response.Content.ReadAsStringAsync(token).ConfigureAwait(false),
            _panelTimeout,
            cancellationToken);
    }

    public Task<JsonDocument> GetStatsAsync(string? series, CancellationToken cancellationToken)
    {
        var path = string.IsNullOrWhiteSpace(series)
            ? "api/stats"
            : $"api/stats?series={Uri.EscapeDataString(series)}";

        return SendAuthorizedAsync(
            () => new(HttpMethod.Get, path),
            static async (response, token) =>
                await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token), cancellationToken: token).ConfigureAwait(false),
            _panelTimeout,
            cancellationToken);
    }

    public Task<JsonDocument> GetTelemetryAsync(CancellationToken cancellationToken) =>
        GetStatsAsync(null, cancellationToken);

    public Task<JsonDocument> SendAcquisitionRequestAsync(
        string destinationHex,
        string payloadHex,
        int timeoutMilliseconds,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationHex);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadHex);

        return SendAcquisitionAsync(
            "api/request",
            new { destination = destinationHex, payload = payloadHex, timeoutMs = timeoutMilliseconds },
            timeoutMilliseconds,
            cancellationToken);
    }

    public Task<JsonDocument> SendAcquisitionLoginAsync(
        string destinationHex,
        string password,
        int timeoutMilliseconds,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationHex);
        ArgumentNullException.ThrowIfNull(password);

        return SendAcquisitionAsync(
            "api/login",
            new { destination = destinationHex, password, timeoutMs = timeoutMilliseconds },
            timeoutMilliseconds,
            cancellationToken);
    }

    private Task<JsonDocument> SendAcquisitionAsync(
        string path,
        object body,
        int timeoutMilliseconds,
        CancellationToken cancellationToken) =>
        SendAuthorizedAsync(
            () => new(HttpMethod.Post, path)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(body),
                    Encoding.UTF8,
                    "application/json"),
            },
            static async (response, token) =>
                await JsonDocument.ParseAsync(
                    await response.Content.ReadAsStreamAsync(token),
                    cancellationToken: token).ConfigureAwait(false),
            AcquisitionHttpTimeout(timeoutMilliseconds),
            cancellationToken);

    /// <summary>
    /// HTTP bound for one acquisition call. The firmware may spend up to two
    /// radio windows on a single call (direct attempt times out, then a flood
    /// retry in the same call, spec §8.6), so the bound scales with the
    /// requested window; the fixed slack covers the TLS handshake and the
    /// panel auth leg that precede the radio work.
    /// </summary>
    internal static TimeSpan AcquisitionHttpTimeout(int timeoutMilliseconds) =>
        TimeSpan.FromMilliseconds((timeoutMilliseconds * AcquisitionTimeoutFactor) + AcquisitionOverheadMilliseconds);

    public void Dispose() => _requestLock.Dispose();

    private async Task<T> SendAuthorizedAsync<T>(
        Func<HttpRequestMessage> requestFactory,
        Func<HttpResponseMessage, CancellationToken, Task<T>> readResponse,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        await _requestLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // HttpClient.Timeout cannot be widened per request, so it stays
            // infinite and every call is bounded here instead: panel calls by
            // MeshCore:Http:TimeoutSeconds, acquisition calls by the window
            // derived bound.
            using var timeoutScope = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutScope.CancelAfter(timeout);
            cancellationToken = timeoutScope.Token;

            var authToken = await session.GetTokenAsync(AuthenticateAsync, cancellationToken).ConfigureAwait(false);
            var attempt = 0;
            using var response = await _authorizationPipeline.ExecuteAsync(async retryToken =>
            {
                if (attempt > 0)
                {
                    authToken = await session.RefreshTokenAsync(
                        authToken,
                        AuthenticateAsync,
                        retryToken).ConfigureAwait(false);
                }

                attempt++;
                using var request = requestFactory();
                request.Headers.TryAddWithoutValidation("X-Auth-Token", authToken);
                return await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    retryToken).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);

            await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
            return await readResponse(response, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _requestLock.Release();
        }
    }

    private async Task<string> AuthenticateAsync(CancellationToken cancellationToken)
    {
        EnsureUtf8Length(_adminPassword, MaximumPasswordBytes, nameof(MeshCoreHttpOptions.AdminPassword));

        using var request = CreateTextRequest(HttpMethod.Post, "login", _adminPassword);
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        var token = (await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)).Trim();
        if (string.IsNullOrEmpty(token))
            throw new InvalidOperationException("MeshCoreTel API returned an empty authentication token.");

        return token;
    }

    private static HttpRequestMessage CreateTextRequest(HttpMethod method, string path, string body)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = new StringContent(body, Encoding.UTF8)
        };
        request.Content.Headers.ContentType = new("text/plain")
        {
            CharSet = "utf-8",
        };
        return request;
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (responseBody.Length > MaximumErrorBodyLength)
            responseBody = responseBody[..MaximumErrorBodyLength];

        throw new MeshCoreTelApiException(response.StatusCode, responseBody);
    }

    private static void EnsureUtf8Length(string value, int maximumBytes, string parameterName)
    {
        if (Encoding.UTF8.GetByteCount(value) > maximumBytes)
        {
            throw new ArgumentException(
                $"The value must be no longer than {maximumBytes} bytes in UTF-8.",
                parameterName);
        }
    }
}
