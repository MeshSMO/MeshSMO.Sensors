using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace MeshSMO.Sensors.Gateway.MeshCore;

public sealed class MeshCoreTelHttpClient(
    HttpClient httpClient,
    IOptions<MeshCoreOptions> options) : IMeshCoreTelClient, IDisposable
{
    private const int MaximumCommandBytes = 191;
    private const int MaximumPasswordBytes = 79;
    private const int MaximumErrorBodyLength = 512;
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private readonly string _adminPassword = options.Value.Http.AdminPassword;
    private string? _token;

    public string TransportName => "http";

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        await _requestLock.WaitAsync(cancellationToken);
        try
        {
            await AuthenticateAsync(cancellationToken);
        }
        finally
        {
            _requestLock.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        await _requestLock.WaitAsync(cancellationToken);
        try
        {
            _token = null;
        }
        finally
        {
            _requestLock.Release();
        }
    }

    public Task<string> ExecuteCommandAsync(string command, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        EnsureUtf8Length(command, MaximumCommandBytes, nameof(command));

        return SendAuthorizedAsync(
            () => CreateTextRequest(HttpMethod.Post, "api/command", command),
            static async (response, token) => await response.Content.ReadAsStringAsync(token),
            cancellationToken);
    }

    public Task<JsonDocument> GetStatsAsync(string? series, CancellationToken cancellationToken)
    {
        var path = string.IsNullOrWhiteSpace(series)
            ? "api/stats"
            : $"api/stats?series={Uri.EscapeDataString(series)}";

        return SendAuthorizedAsync(
            () => new HttpRequestMessage(HttpMethod.Get, path),
            static async (response, token) =>
                await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token), cancellationToken: token),
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
            () => new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(body),
                    Encoding.UTF8,
                    "application/json"),
            },
            static async (response, token) =>
                await JsonDocument.ParseAsync(
                    await response.Content.ReadAsStreamAsync(token),
                    cancellationToken: token),
            cancellationToken);

    public void Dispose()
    {
        _requestLock.Dispose();
    }

    private async Task<T> SendAuthorizedAsync<T>(
        Func<HttpRequestMessage> requestFactory,
        Func<HttpResponseMessage, CancellationToken, Task<T>> readResponse,
        CancellationToken cancellationToken)
    {
        await _requestLock.WaitAsync(cancellationToken);
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                if (string.IsNullOrEmpty(_token))
                {
                    await AuthenticateAsync(cancellationToken);
                }

                using var request = requestFactory();
                request.Headers.TryAddWithoutValidation("X-Auth-Token", _token);
                using var response = await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
                {
                    _token = null;
                    continue;
                }

                await EnsureSuccessAsync(response, cancellationToken);
                return await readResponse(response, cancellationToken);
            }

            throw new InvalidOperationException("The MeshCoreTel authorization retry loop exited unexpectedly.");
        }
        finally
        {
            _requestLock.Release();
        }
    }

    private async Task AuthenticateAsync(CancellationToken cancellationToken)
    {
        EnsureUtf8Length(_adminPassword, MaximumPasswordBytes, nameof(MeshCoreHttpOptions.AdminPassword));

        using var request = CreateTextRequest(HttpMethod.Post, "login", _adminPassword);
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var token = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
        if (string.IsNullOrEmpty(token))
        {
            throw new InvalidOperationException("MeshCoreTel API returned an empty authentication token.");
        }

        _token = token;
    }

    private static HttpRequestMessage CreateTextRequest(HttpMethod method, string path, string body)
    {
        var request = new HttpRequestMessage(method, path);
        request.Content = new StringContent(body, Encoding.UTF8);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain")
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
        {
            return;
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (responseBody.Length > MaximumErrorBodyLength)
        {
            responseBody = responseBody[..MaximumErrorBodyLength];
        }

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
