using MeshSMO.Sensors.Gateway.MeshCore;

namespace MeshSMO.Sensors.UnitTests.Gateway.MeshCore;

public sealed class CompanionFrameCodecTests
{
    [Fact]
    public void EncodePrependsFlagAndLittleEndianLength()
    {
        var packet = CompanionFrameCodec.Encode([0x32, 0xAA, 0xBB]);

        Assert.Equal(new byte[] { 0x3C, 0x03, 0x00, 0x32, 0xAA, 0xBB }, packet);
    }

    [Fact]
    public void EncodeRejectsEmptyAndOversizedFrames()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CompanionFrameCodec.Encode([]));
        Assert.Throws<ArgumentOutOfRangeException>(() => CompanionFrameCodec.Encode(new byte[CompanionFrameCodec.MaxFrameLength + 1]));
    }

    [Fact]
    public void DecodeParsesDeviceFlagAndLittleEndianLength()
    {
        // RESP_CODE_SENT frame: code + flooded + tag(4) + est_timeout(4).
        var buffer = new byte[] { 0x3E, 0x0A, 0x00, 0x06, 0x01, 0x01, 0x02, 0x03, 0x04, 0x10, 0x00, 0x00, 0x00 };

        var decoded = CompanionFrameCodec.TryDecode(buffer, out var frame, out var consumed);

        Assert.True(decoded);
        Assert.Equal(13, consumed);
        Assert.Equal(10, frame.Length);
        Assert.Equal(0x06, frame[0]);
        Assert.Equal(0x04030201u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(2, 4)));
    }

    [Fact]
    public void DecodeRejectsWrongFlagAndIncompleteFrames()
    {
        Assert.False(CompanionFrameCodec.TryDecode([0x3C, 0x01, 0x00, 0x06], out _, out _));
        Assert.False(CompanionFrameCodec.TryDecode([0x3E, 0x02, 0x00, 0x06], out _, out _));
        Assert.False(CompanionFrameCodec.TryDecode([0x3E, 0x00, 0x00], out _, out _));
        Assert.False(CompanionFrameCodec.TryDecode([0x3E], out _, out _));
    }
}
