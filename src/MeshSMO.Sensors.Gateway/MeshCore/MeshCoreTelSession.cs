namespace MeshSMO.Sensors.Gateway.MeshCore;

/// <summary>
/// Panel session shared by every consumer of the repeater HTTP API (Worker,
/// SensorTelemetryPoller). The MeshCoreTel web panel keeps a single global
/// token and every successful login invalidates the previous one, so clients
/// that authenticate independently keep invalidating each other's token and
/// end up re-logging in before every request. The session logs in once and
/// re-authenticates only after the panel answers 401.
/// </summary>
public sealed class MeshCoreTelSession
{
    private readonly SemaphoreSlim _loginLock = new(1, 1);
    private string? _token;

    public async Task<string> GetTokenAsync(
        Func<CancellationToken, Task<string>> loginAsync,
        CancellationToken cancellationToken)
    {
        var token = _token;
        if (!string.IsNullOrEmpty(token))
        {
            return token;
        }

        await _loginLock.WaitAsync(cancellationToken);
        try
        {
            _token ??= await loginAsync(cancellationToken);
            return _token;
        }
        finally
        {
            _loginLock.Release();
        }
    }

    public async Task<string> RefreshTokenAsync(
        string staleToken,
        Func<CancellationToken, Task<string>> loginAsync,
        CancellationToken cancellationToken)
    {
        await _loginLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrEmpty(_token) && !string.Equals(_token, staleToken, StringComparison.Ordinal))
            {
                // Another consumer already re-authenticated while we waited.
                return _token;
            }

            _token = await loginAsync(cancellationToken);
            return _token;
        }
        finally
        {
            _loginLock.Release();
        }
    }

    public void ClearToken() => _token = null;
}
