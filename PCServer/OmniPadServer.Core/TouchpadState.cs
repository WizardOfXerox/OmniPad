using System;
using System.Buffers.Binary;

namespace OmniPadServer.Core;

/// <summary>
/// Touch point on the DualShock 4 touchpad (1920x942 native resolution).
/// </summary>
public struct TouchPoint : IEquatable<TouchPoint>
{
    public bool IsActive;
    public byte Id;
    public ushort X; // 0..1920
    public ushort Y; // 0..942

    public bool Equals(TouchPoint other) =>
        IsActive == other.IsActive &&
        Id == other.Id &&
        X == other.X &&
        Y == other.Y;

    public override bool Equals(object? obj) => obj is TouchPoint other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(IsActive, Id, X, Y);
    public static bool operator ==(TouchPoint left, TouchPoint right) => left.Equals(right);
    public static bool operator !=(TouchPoint left, TouchPoint right) => !left.Equals(right);
}

/// <summary>
/// DualShock 4 multi-touch state with physical click and 2 active tracking fingers.
/// </summary>
public struct TouchpadState : IEquatable<TouchpadState>
{
    public const ushort ResolutionX = 1920;
    public const ushort ResolutionY = 942;

    public bool Clicked;
    public TouchPoint Finger0;
    public TouchPoint Finger1;

    public static readonly TouchpadState Neutral = new()
    {
        Clicked = false,
        Finger0 = default,
        Finger1 = default
    };

    public bool Equals(TouchpadState other) =>
        Clicked == other.Clicked &&
        Finger0.Equals(other.Finger0) &&
        Finger1.Equals(other.Finger1);

    public override bool Equals(object? obj) => obj is TouchpadState other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Clicked, Finger0, Finger1);
    public static bool operator ==(TouchpadState left, TouchpadState right) => left.Equals(right);
    public static bool operator !=(TouchpadState left, TouchpadState right) => !left.Equals(right);
}

/// <summary>
/// Zero-allocation binary codec for the 13-byte Touchpad packet (MsgTouchpad = 0x11).
/// </summary>
public readonly struct TouchpadPacket
{
    public readonly byte Pad;
    public readonly TouchpadState State;

    public TouchpadPacket(byte pad, in TouchpadState state)
    {
        Pad = pad;
        State = state;
    }

    public static bool TryParse(ReadOnlySpan<byte> buffer, out TouchpadPacket packet)
    {
        packet = default;
        if (buffer.Length < Protocol.TouchpadPacketSize)
            return false;

        if (buffer[0] != Protocol.MagicByte ||
            buffer[1] != Protocol.Version ||
            buffer[2] != Protocol.MsgTouchpad)
        {
            return false;
        }

        byte pad = buffer[3];
        byte flags = buffer[4];

        bool clicked = (flags & 0x01) != 0;
        bool f0Active = (flags & 0x02) != 0;
        bool f1Active = (flags & 0x04) != 0;

        ushort f0X = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(5, 2));
        ushort f0Y = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(7, 2));
        ushort f1X = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(9, 2));
        ushort f1Y = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(11, 2));

        packet = new TouchpadPacket(pad, new TouchpadState
        {
            Clicked = clicked,
            Finger0 = new TouchPoint { IsActive = f0Active, Id = 0, X = f0X, Y = f0Y },
            Finger1 = new TouchPoint { IsActive = f1Active, Id = 1, X = f1X, Y = f1Y }
        });
        return true;
    }

    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < Protocol.TouchpadPacketSize)
            throw new ArgumentException("Destination buffer too small", nameof(destination));

        destination[0] = Protocol.MagicByte;
        destination[1] = Protocol.Version;
        destination[2] = Protocol.MsgTouchpad;
        destination[3] = Pad;

        byte flags = 0;
        if (State.Clicked) flags |= 0x01;
        if (State.Finger0.IsActive) flags |= 0x02;
        if (State.Finger1.IsActive) flags |= 0x04;
        destination[4] = flags;

        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(5, 2), State.Finger0.X);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(7, 2), State.Finger0.Y);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(9, 2), State.Finger1.X);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(11, 2), State.Finger1.Y);
    }
}
