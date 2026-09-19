using System;
using System.Buffers.Binary;

namespace OmniPadServer.Core;

/// <summary>
/// High-performance, zero-allocation codec for the 20-byte input frame.
/// </summary>
public readonly struct InputPacket
{
    public readonly byte Pad;
    public readonly uint Sequence;
    public readonly PadState State;

    public InputPacket(byte pad, uint sequence, in PadState state)
    {
        Pad = pad;
        Sequence = sequence;
        State = state;
    }

    /// <summary>
    /// Parses a 20-byte binary frame. Returns true if valid, false if corrupted or mismatched.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> buffer, out InputPacket packet)
    {
        packet = default;

        if (buffer.Length < Protocol.InputPacketSize)
            return false;

        if (buffer[0] != Protocol.MagicByte ||
            buffer[1] != Protocol.Version ||
            buffer[2] != Protocol.MsgInput)
        {
            return false;
        }

        byte pad = buffer[3];
        uint sequence = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(4, 4));

        PadState state = new()
        {
            Buttons = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(8, 2)),
            LeftTrigger = buffer[10],
            RightTrigger = buffer[11],
            ThumbLX = BinaryPrimitives.ReadInt16LittleEndian(buffer.Slice(12, 2)),
            ThumbLY = BinaryPrimitives.ReadInt16LittleEndian(buffer.Slice(14, 2)),
            ThumbRX = BinaryPrimitives.ReadInt16LittleEndian(buffer.Slice(16, 2)),
            ThumbRY = BinaryPrimitives.ReadInt16LittleEndian(buffer.Slice(18, 2))
        };

        packet = new InputPacket(pad, sequence, in state);
        return true;
    }

    /// <summary>
    /// Encodes this packet into a 20-byte destination buffer.
    /// </summary>
    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < Protocol.InputPacketSize)
            throw new ArgumentException("Destination buffer too small", nameof(destination));

        destination[0] = Protocol.MagicByte;
        destination[1] = Protocol.Version;
        destination[2] = Protocol.MsgInput;
        destination[3] = Pad;

        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(4, 4), Sequence);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(8, 2), State.Buttons);
        destination[10] = State.LeftTrigger;
        destination[11] = State.RightTrigger;
        BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(12, 2), State.ThumbLX);
        BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(14, 2), State.ThumbLY);
        BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(16, 2), State.ThumbRX);
        BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(18, 2), State.ThumbRY);
    }
}
