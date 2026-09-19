using System;
using System.Buffers.Binary;
using System.Numerics;

namespace OmniPadServer.Core;

/// <summary>
/// 6-Axis Motion State (Accelerometer in Gs, Gyroscope in degrees per second).
/// </summary>
public struct MotionState : IEquatable<MotionState>
{
    public ulong TimestampUs;
    public float AccelX; // In G (1G = ~9.80665 m/s^2)
    public float AccelY;
    public float AccelZ;
    public float GyroX;  // In deg/s
    public float GyroY;
    public float GyroZ;

    public static readonly MotionState Neutral = new()
    {
        TimestampUs = 0,
        AccelX = 0f,
        AccelY = 0f,
        AccelZ = 1f, // Default 1G downwards on Z
        GyroX = 0f,
        GyroY = 0f,
        GyroZ = 0f
    };

    public Vector3 Accelerometer => new(AccelX, AccelY, AccelZ);
    public Vector3 Gyroscope => new(GyroX, GyroY, GyroZ);

    public bool Equals(MotionState other) =>
        TimestampUs == other.TimestampUs &&
        AccelX.Equals(other.AccelX) &&
        AccelY.Equals(other.AccelY) &&
        AccelZ.Equals(other.AccelZ) &&
        GyroX.Equals(other.GyroX) &&
        GyroY.Equals(other.GyroY) &&
        GyroZ.Equals(other.GyroZ);

    public override bool Equals(object? obj) => obj is MotionState other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(TimestampUs, AccelX, AccelY, AccelZ, GyroX, GyroY, GyroZ);
    public static bool operator ==(MotionState left, MotionState right) => left.Equals(right);
    public static bool operator !=(MotionState left, MotionState right) => !left.Equals(right);
}

/// <summary>
/// Zero-allocation binary codec for the 36-byte Motion packet (MsgMotion = 0x10).
/// </summary>
public readonly struct MotionPacket
{
    public readonly byte Pad;
    public readonly MotionState Motion;

    public MotionPacket(byte pad, in MotionState motion)
    {
        Pad = pad;
        Motion = motion;
    }

    public static bool TryParse(ReadOnlySpan<byte> buffer, out MotionPacket packet)
    {
        packet = default;
        if (buffer.Length < Protocol.MotionPacketSize)
            return false;

        if (buffer[0] != Protocol.MagicByte ||
            buffer[1] != Protocol.Version ||
            buffer[2] != Protocol.MsgMotion)
        {
            return false;
        }

        byte pad = buffer[3];
        ulong timestamp = BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(4, 8));
        float ax = BitConverter.UInt32BitsToSingle(BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(12, 4)));
        float ay = BitConverter.UInt32BitsToSingle(BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(16, 4)));
        float az = BitConverter.UInt32BitsToSingle(BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(20, 4)));
        float gx = BitConverter.UInt32BitsToSingle(BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(24, 4)));
        float gy = BitConverter.UInt32BitsToSingle(BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(28, 4)));
        float gz = BitConverter.UInt32BitsToSingle(BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(32, 4)));

        packet = new MotionPacket(pad, new MotionState
        {
            TimestampUs = timestamp,
            AccelX = ax,
            AccelY = ay,
            AccelZ = az,
            GyroX = gx,
            GyroY = gy,
            GyroZ = gz
        });
        return true;
    }

    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < Protocol.MotionPacketSize)
            throw new ArgumentException("Destination buffer too small", nameof(destination));

        destination[0] = Protocol.MagicByte;
        destination[1] = Protocol.Version;
        destination[2] = Protocol.MsgMotion;
        destination[3] = Pad;

        BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(4, 8), Motion.TimestampUs);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(12, 4), BitConverter.SingleToUInt32Bits(Motion.AccelX));
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(16, 4), BitConverter.SingleToUInt32Bits(Motion.AccelY));
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(20, 4), BitConverter.SingleToUInt32Bits(Motion.AccelZ));
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(24, 4), BitConverter.SingleToUInt32Bits(Motion.GyroX));
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(28, 4), BitConverter.SingleToUInt32Bits(Motion.GyroY));
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(32, 4), BitConverter.SingleToUInt32Bits(Motion.GyroZ));
    }
}
