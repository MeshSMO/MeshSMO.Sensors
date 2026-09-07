using System.IO.Ports;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace MeshSMO.Sensors.Gateway.MeshCore;

public sealed class RepeaterSerialClient(IOptions<MeshCoreOptions> options) : IRepeaterClient, IDisposable
{
    private const int MaximumCommandBytes = 159;
    private readonly MeshCoreSerialOptions _options = options.Value.Serial;
    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private SerialPort? _port;
    private StreamReader? _reader;

    public string TransportName => "serial";

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_port?.IsOpen == true)
            return Task.CompletedTask;

        DisposePort();
        var port = new SerialPort(_options.PortName, _options.BaudRate)
        {
            Encoding = Encoding.UTF8,
            NewLine = "\n",
            DtrEnable = false,
            RtsEnable = false,
        };
        port.Open();
        port.DiscardInBuffer();
        _port = port;
        _reader = new(port.BaseStream, Encoding.UTF8, false, leaveOpen: true);
        return Task.CompletedTask;
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DisposePort();
        }
        finally
        {
            _commandLock.Release();
        }
    }

    public async Task<string> ExecuteCommandAsync(string command, CancellationToken cancellationToken)
    {
        var lines = await ExecuteCommandLinesAsync(command, cancellationToken).ConfigureAwait(false);
        return string.Join('\n', lines);
    }

    public async Task<JsonDocument> GetTelemetryAsync(CancellationToken cancellationToken)
    {
        var core = await ExecuteCommandAsync("stats-core", cancellationToken).ConfigureAwait(false);
        var radio = await ExecuteCommandAsync("stats-radio", cancellationToken).ConfigureAwait(false);
        var packets = await ExecuteCommandAsync("stats-packets", cancellationToken).ConfigureAwait(false);
        var sensors = await ReadSensorSettingsAsync(cancellationToken).ConfigureAwait(false);

        return JsonSerializer.SerializeToDocument(new
        {
            core,
            radio,
            packets,
            sensors,
        });
    }

    public void Dispose()
    {
        DisposePort();
        _commandLock.Dispose();
    }

    private async Task<IReadOnlyList<string>> ExecuteCommandLinesAsync(
        string command,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        if (Encoding.UTF8.GetByteCount(command) > MaximumCommandBytes)
        {
            throw new ArgumentException(
                $"A repeater serial command must be no longer than {MaximumCommandBytes} bytes in UTF-8.",
                nameof(command));
        }

        await _commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var port = _port is { IsOpen: true }
                ? _port
                : throw new InvalidOperationException("The repeater serial port is not connected.");
            var reader = _reader ?? throw new InvalidOperationException("The repeater serial reader is unavailable.");

            var bytes = Encoding.UTF8.GetBytes($"{command}\r");
            await port.BaseStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await port.BaseStream.FlushAsync(cancellationToken).ConfigureAwait(false);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(_options.CommandTimeoutSeconds));

            string? reply = null;
            while (!timeout.IsCancellationRequested)
            {
                var line = await ReadLineAsync(reader, command, cancellationToken, timeout.Token).ConfigureAwait(false);

                if (RepeaterSerialResponseParser.TryExtractReply(line, out var extractedReply))
                {
                    reply = extractedReply;
                    break;
                }
            }

            if (reply is null)
                throw new TimeoutException($"The repeater did not answer the '{command}' command.");

            var result = new List<string> { reply };
            if (command.StartsWith("sensor list", StringComparison.Ordinal) &&
                RepeaterSerialResponseParser.TryParseSensorCount(reply, out var count))
            {
                var requestedStart = TryGetSensorListStart(command);
                var expectedValueLines = Math.Max(0, count - requestedStart);
                while (result.Count - 1 < expectedValueLines)
                {
                    var line = await ReadLineAsync(reader, command, cancellationToken, timeout.Token).ConfigureAwait(false);

                    line = line.Trim();
                    if (line.Length == 0)
                        continue;

                    result.Add(line);
                    if (line.StartsWith("... next:", StringComparison.Ordinal))
                        break;
                }
            }

            return result;
        }
        finally
        {
            _commandLock.Release();
        }
    }

    private static async Task<string> ReadLineAsync(
        StreamReader reader,
        string command,
        CancellationToken cancellationToken,
        CancellationToken timeoutToken)
    {
        try
        {
            return await reader.ReadLineAsync(timeoutToken).ConfigureAwait(false) ??
                throw new IOException("The repeater closed the serial connection.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"The repeater did not answer the '{command}' command.");
        }
    }

    private async Task<IReadOnlyDictionary<string, string>> ReadSensorSettingsAsync(
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var start = 0;

        while (true)
        {
            var lines = await ExecuteCommandLinesAsync($"sensor list {start}", cancellationToken).ConfigureAwait(false);
            var page = RepeaterSerialResponseParser.ParseSensorPage(lines);
            foreach (var pair in page.Values)
                result[pair.Key] = pair.Value;

            if (page.NextIndex is null || page.NextIndex <= start)
                return result;

            start = page.NextIndex.Value;
        }
    }

    private void DisposePort()
    {
        _reader?.Dispose();
        _reader = null;
        _port?.Dispose();
        _port = null;
    }

    private static int TryGetSensorListStart(string command)
    {
        const string prefix = "sensor list";
        var value = command.AsSpan(prefix.Length).Trim();
        return int.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var start) && start > 0 ? start : 0;
    }
}
