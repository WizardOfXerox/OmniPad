using System;
using System.Buffers.Binary;

namespace OmniPadServer.Core;

public readonly struct SessionMessage
{
    public readonly byte Type;
    public readonly byte Pad;

    public SessionMessage(byte type, byte pad)
    {
        Type = type;
        Pad = pad;
    }

    public static bool TryParse(ReadOnlySpan<byte> buffer, out SessionMessage message)
    {
        message = default;

        if (buffer.Length != Protocol.SessionMessageSize)
            return false;

        if (buffer[0] != Protocol.MagicByte || buffer[1] != Protocol.Version)
            return false;

        byte type = buffer[2];
        if (type != Protocol.MsgHello &&
            type != Protocol.MsgWelcome &&
            type != Protocol.MsgBye &&
            type != Protocol.MsgDiscover)
        {
            return false;
        }

        byte pad = buffer[3];
        message = new SessionMessage(type, pad);
        return true;
    }

    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < Protocol.SessionMessageSize)
            throw new ArgumentException("Destination buffer too small", nameof(destination));

        destination[0] = Protocol.MagicByte;
        destination[1] = Protocol.Version;
        destination[2] = Type;
        destination[3] = Pad;
    }
}

public readonly struct RumbleMessage
{
    public readonly byte Pad;
    public readonly byte LargeMotor; // Low frequency (heavy)
    public readonly byte SmallMotor; // High frequency (light)

    public RumbleMessage(byte pad, byte largeMotor, byte smallMotor)
    {
        Pad = pad;
        LargeMotor = largeMotor;
        SmallMotor = smallMotor;
    }

    public static bool TryParse(ReadOnlySpan<byte> buffer, out RumbleMessage message)
    {
        message = default;
        if (buffer.Length < Protocol.RumbleMessageSize)
            return false;

        if (buffer[0] != Protocol.MagicByte || buffer[1] != Protocol.Version || buffer[2] != Protocol.MsgRumble)
            return false;

        message = new RumbleMessage(buffer[3], buffer[4], buffer[5]);
        return true;
    }

    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < Protocol.RumbleMessageSize)
            throw new ArgumentException("Destination buffer too small", nameof(destination));

        destination[0] = Protocol.MagicByte;
        destination[1] = Protocol.Version;
        destination[2] = Protocol.MsgRumble;
        destination[3] = Pad;
        destination[4] = LargeMotor;
        destination[5] = SmallMotor;
    }
}
