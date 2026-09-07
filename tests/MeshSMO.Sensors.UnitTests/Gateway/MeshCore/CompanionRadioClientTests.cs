using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using MeshSMO.Sensors.Gateway.MeshCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MeshSMO.Sensors.UnitTests.Gateway.MeshCore;

public sealed class CompanionRadioClientTests
{
    private const string DestinationHex = "aabbccddeeff00112233445566778899aabbccddeeff00112233445566778899";

    private static readonly byte[] PublicKeyPrefix = [0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF];

    [Fact]
    public async Task BinaryRequestStripsTimestampAndReconstructsResponseHex()
    {
        using var device = FakeCompanionDevice.Start();
        using var client = CreateClient(device.Port);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var requestTask = client.SendAcquisitionRequestAsync(DestinationHex, "000000000300", 1000, timeout.Token);
        var command = await device.ReceiveFrameAsync(timeout.Token);

        Assert.Equal(50, command[0]); // CMD_SEND_BINARY_REQ
        Assert.Equal(PublicKeyPrefix, command[1..7]);
        Assert.Equal(new byte[] { 0x03, 0x00 }, command[7..]); // gateway timestamp prefix dropped

        await device.SendFrameAsync(SentAck(0x11223344, flooded: true), timeout.Token);
        await device.SendFrameAsync(BinaryResponse(0x11223344, [0x01, 0x67, 0x01, 0x91]), timeout.Token);

        using var result = await requestTask;

        Assert.Equal("ok", result.RootElement.GetProperty("status").GetString());
        // The firmware tag holds the request timestamp echoed by the node, so
        // responseHex is tag(4 LE) + LPP — the repeater's responseHex shape.
        Assert.Equal("4433221101670191", result.RootElement.GetProperty("responseHex").GetString());
        Assert.True(result.RootElement.GetProperty("flooded").GetBoolean());
    }

    [Fact]
    public async Task BinaryRequestTimesOutWhenNodeDoesNotAnswer()
    {
        using var device = FakeCompanionDevice.Start();
        using var client = CreateClient(device.Port);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var requestTask = client.SendAcquisitionRequestAsync(DestinationHex, "000000000300", 500, timeout.Token);
        await device.ReceiveFrameAsync(timeout.Token);
        await device.SendFrameAsync(SentAck(0x01020304, flooded: false), timeout.Token);

        using var result = await requestTask;

        Assert.Equal("timeout", result.RootElement.GetProperty("status").GetString());
        Assert.True(result.RootElement.GetProperty("elapsedMs").GetInt32() >= 400);
    }

    [Fact]
    public async Task BinaryRequestWithoutContactReportsDeviceError()
    {
        using var device = FakeCompanionDevice.Start();
        using var client = CreateClient(device.Port);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var requestTask = client.SendAcquisitionRequestAsync(DestinationHex, "000000000300", 1000, timeout.Token);
        await device.ReceiveFrameAsync(timeout.Token);
        await device.SendFrameAsync([0x01, 0x02], timeout.Token); // RESP_CODE_ERR(ERR_CODE_NOT_FOUND)

        using var result = await requestTask;

        Assert.Equal("error", result.RootElement.GetProperty("status").GetString());
        Assert.Equal(2, result.RootElement.GetProperty("errorCode").GetByte());
    }

    [Fact]
    public async Task LoginFallsBackToAnonymousRequestWhenContactMissing()
    {
        using var device = FakeCompanionDevice.Start();
        using var client = CreateClient(device.Port);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var loginTask = client.SendAcquisitionLoginAsync(DestinationHex, "hello", 1000, timeout.Token);
        var loginCommand = await device.ReceiveFrameAsync(timeout.Token);

        Assert.Equal(26, loginCommand[0]); // CMD_SEND_LOGIN
        Assert.Equal(PublicKeyPrefix, loginCommand[1..7]);
        Assert.Equal("hello"u8.ToArray(), loginCommand[7..]);

        await device.SendFrameAsync([0x01, 0x02], timeout.Token); // contact not found
        var anonCommand = await device.ReceiveFrameAsync(timeout.Token);

        Assert.Equal(57, anonCommand[0]); // CMD_SEND_ANON_REQ fallback (v13+ auto-contact)
        Assert.Equal(PublicKeyPrefix, anonCommand[1..7]);
        Assert.Equal("hello"u8.ToArray(), anonCommand[7..]);

        await device.SendFrameAsync(SentAck(0x55667788, flooded: false), timeout.Token);
        await device.SendFrameAsync(BinaryResponse(0x55667788, "OK"u8.ToArray()), timeout.Token);

        using var result = await loginTask;

        Assert.Equal("ok", result.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task LoginReportsAuthFailureFromLoginPush()
    {
        using var device = FakeCompanionDevice.Start();
        using var client = CreateClient(device.Port);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var loginTask = client.SendAcquisitionLoginAsync(DestinationHex, "hello", 1000, timeout.Token);
        await device.ReceiveFrameAsync(timeout.Token);
        await device.SendFrameAsync(SentAck(0x01020304, flooded: false), timeout.Token);
        await device.SendFrameAsync(LoginPush(0x86, PublicKeyPrefix), timeout.Token); // PUSH_CODE_LOGIN_FAIL

        using var result = await loginTask;

        Assert.Equal("auth_failed", result.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task LoginSucceedsOnLoginSuccessPush()
    {
        using var device = FakeCompanionDevice.Start();
        using var client = CreateClient(device.Port);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var loginTask = client.SendAcquisitionLoginAsync(DestinationHex, "hello", 1000, timeout.Token);
        await device.ReceiveFrameAsync(timeout.Token);
        await device.SendFrameAsync(SentAck(0x01020304, flooded: false), timeout.Token);
        await device.SendFrameAsync(LoginPush(0x85, PublicKeyPrefix), timeout.Token); // PUSH_CODE_LOGIN_SUCCESS

        using var result = await loginTask;

        Assert.Equal("ok", result.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task StaleTaggedResponseIsIgnored()
    {
        using var device = FakeCompanionDevice.Start();
        using var client = CreateClient(device.Port);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var requestTask = client.SendAcquisitionRequestAsync(DestinationHex, "000000000300", 1000, timeout.Token);
        await device.ReceiveFrameAsync(timeout.Token);
        await device.SendFrameAsync(SentAck(0x11223344, flooded: false), timeout.Token);
        await device.SendFrameAsync(BinaryResponse(0xDEADBEEF, [0x99]), timeout.Token); // stale answer
        await device.SendFrameAsync(BinaryResponse(0x11223344, [0x01, 0x67, 0x01, 0x91]), timeout.Token);

        using var result = await requestTask;

        Assert.Equal("ok", result.RootElement.GetProperty("status").GetString());
        Assert.Equal("4433221101670191", result.RootElement.GetProperty("responseHex").GetString());
    }

    [Fact]
    public async Task LoginPasswordLongerThanMeshLimitIsRejectedLocally()
    {
        using var device = FakeCompanionDevice.Start();
        using var client = CreateClient(device.Port);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.SendAcquisitionLoginAsync(DestinationHex, new('x', 16), 1000, timeout.Token));

        Assert.Empty(device.ReceivedFrames);
    }

    private static CompanionRadioClient CreateClient(int port) =>
        new(
            Options.Create(new MeshCoreOptions
            {
                Mode = MeshCoreConnectionMode.Companion,
                Companion = new() { Host = "127.0.0.1", Port = port },
            }),
            NullLogger<CompanionRadioClient>.Instance);

    private static byte[] SentAck(uint tag, bool flooded)
    {
        var frame = new byte[10];
        frame[0] = 0x06; // RESP_CODE_SENT
        frame[1] = flooded ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(2), tag);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(6), 500u); // est_timeout
        return frame;
    }

    private static byte[] BinaryResponse(uint tag, ReadOnlySpan<byte> body)
    {
        var frame = new byte[6 + body.Length];
        frame[0] = 0x8C; // PUSH_CODE_BINARY_RESPONSE
        frame[1] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(2), tag);
        body.CopyTo(frame.AsSpan(6));
        return frame;
    }

    private static byte[] LoginPush(byte code, ReadOnlySpan<byte> pubKeyPrefix)
    {
        var frame = new byte[2 + pubKeyPrefix.Length];
        frame[0] = code;
        frame[1] = 0;
        pubKeyPrefix.CopyTo(frame.AsSpan(2));
        return frame;
    }

    /// <summary>Scriptable stand-in for a companion radio: TCP server speaking the frame protocol.</summary>
    private sealed class FakeCompanionDevice : IDisposable
    {
        private readonly TcpListener _listener;
        private TcpClient? _client;
        private NetworkStream? _stream;

        private FakeCompanionDevice(TcpListener listener) => _listener = listener;

        public List<byte[]> ReceivedFrames { get; } = [];

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public static FakeCompanionDevice Start()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return new FakeCompanionDevice(listener);
        }

        public async Task<byte[]> ReceiveFrameAsync(CancellationToken cancellationToken)
        {
            await EnsureClientConnectedAsync(cancellationToken).ConfigureAwait(false);
            var header = new byte[CompanionFrameCodec.HeaderLength];
            await ReadExactAsync(header, cancellationToken).ConfigureAwait(false);
            Assert.Equal(CompanionFrameCodec.HostToDeviceFlag, header[0]);
            var length = header[1] | (header[2] << 8);
            var frame = new byte[length];
            await ReadExactAsync(frame, cancellationToken).ConfigureAwait(false);
            ReceivedFrames.Add(frame);
            return frame;
        }

        public async Task SendFrameAsync(byte[] frame, CancellationToken cancellationToken)
        {
            await EnsureClientConnectedAsync(cancellationToken).ConfigureAwait(false);
            var packet = new byte[3 + frame.Length];
            packet[0] = CompanionFrameCodec.DeviceToHostFlag;
            packet[1] = (byte)frame.Length;
            packet[2] = (byte)(frame.Length >> 8);
            frame.CopyTo(packet, 3);
            await _stream!.WriteAsync(packet, cancellationToken).ConfigureAwait(false);
        }

        public void Dispose()
        {
            _stream?.Dispose();
            _client?.Dispose();
            _listener.Stop();
        }

        private async Task EnsureClientConnectedAsync(CancellationToken cancellationToken)
        {
            if (_client is not null)
                return;

            // The client connects lazily on its first command; accept that
            // connection (not a pre-made one) so both ends talk to each other.
            _client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            _stream = _client.GetStream();
        }

        private async Task ReadExactAsync(byte[] buffer, CancellationToken cancellationToken)
        {
            var read = 0;
            while (read < buffer.Length)
            {
                var count = await _stream!.ReadAsync(buffer.AsMemory(read), cancellationToken).ConfigureAwait(false);
                Assert.NotEqual(0, count);
                read += count;
            }
        }
    }
}
