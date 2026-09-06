using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace MeshSMO.Sensors.Gateway.MeshCore;

public sealed class MeshCoreTelHttpClient(
    HttpClient httpClient,
    MeshCoreTelSession session,
    IOptions<MeshCoreOptions> options) : IMeshCoreTelClient, IDisposable
{
    private const int MaximumCommandBytes = 191;
    private const int MaximumPasswordBytes = 79;
    private const int MaximumErrorBodyLength = 512;
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private readonly string _adminPassword = options.Value.Http.AdminPassword;

    public string TransportName => "http";

    public Task ConnectAsync(CancellationToken cancellationToken) =>
        session.GetTokenAsync(AuthenticateAsync, cancellationToken);

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
            cancellationToken);
    }

    private Task<JsonDocument> SendAcquisitionAsync(string path, object body, CancellationToken cancellationToken) =>
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
            cancellationToken);

    public void Dispose() => _requestLock.Dispose();

    private async Task<T> SendAuthorizedAsync<T>(
        Func<HttpRequestMessage> requestFactory,
        Func<HttpResponseMessage, CancellationToken, Task<T>> readResponse,
        CancellationToken cancellationToken)
    {
        await _requestLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var token = await session.GetTokenAsync(AuthenticateAsync, cancellationToken).ConfigureAwait(false);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var request = requestFactory();
                request.Headers.TryAddWithoutValidation("X-Auth-Token", token);
                using var response = await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
                {
                    token = await session.RefreshTokenAsync(token, AuthenticateAsync, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
                return await readResponse(response, cancellationToken).ConfigureAwait(false);
            }

            throw new InvalidOperationException("The MeshCoreTel authorization retry loop exited unexpectedly.");
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
