namespace MeshSMO.Sensors.Gateway.MeshCore;

/// <summary>
/// Frame codec of the MeshCore companion serial/Wi-Fi transports: every frame
/// is prefixed with a direction flag (host→device 0x3C, device→host 0x3E) and
/// a little-endian uint16 length (see the firmware's ArduinoSerialInterface
/// and SerialWifiInterface; MAX_FRAME_SIZE = 176).
/// </summary>
public static class CompanionFrameCodec
{
    public const byte HostToDeviceFlag = 0x3C; // '<'

    public const byte DeviceToHostFlag = 0x3E; // '>'

    public const int HeaderLength = 3;

    public const int MaxFrameLength = 176;

    /// <summary>Wraps a frame (command code + body) into a host→device packet.</summary>
    public static byte[] Encode(ReadOnlySpan<byte> frame)
    {
        if (frame.Length == 0)
            throw new ArgumentOutOfRangeException(nameof(frame), "The frame must not be empty.");
        if (frame.Length > MaxFrameLength)
            throw new ArgumentOutOfRangeException(nameof(frame), $"The frame must not exceed {MaxFrameLength} bytes.");

        var packet = new byte[HeaderLength + frame.Length];
        packet[0] = HostToDeviceFlag;
        packet[1] = (byte)frame.Length;
        packet[2] = (byte)(frame.Length >> 8);
        frame.CopyTo(packet.AsSpan(HeaderLength));
        return packet;
    }

    /// <summary>
    /// Decodes one device→host frame from the start of the buffer. Returns
    /// false when the buffer does not start with a complete valid frame.
    /// </summary>
    public static bool TryDecode(ReadOnlySpan<byte> buffer, out ReadOnlySpan<byte> frame, out int consumed)
    {
        frame = default;
        consumed = 0;
        if (buffer.Length < HeaderLength || buffer[0] != DeviceToHostFlag)
            return false;

        var length = buffer[1] | (buffer[2] << 8);
        if (length == 0 || length > MaxFrameLength || buffer.Length < HeaderLength + length)
            return false;

        frame = buffer.Slice(HeaderLength, length);
        consumed = HeaderLength + length;
        return true;
    }
}
