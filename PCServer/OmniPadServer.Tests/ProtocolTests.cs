using System;
using OmniPadServer.Core;
using Xunit;

namespace OmniPadServer.Tests;

public class ProtocolTests
{
    // Golden Vector matching the specification:
    // Pad: 2
    // Seq: 0x12345678 (78 56 34 12)
    // Buttons: 0x1004 (BTN_A | BTN_DPAD_LEFT) -> (04 10)
    // LT / RT: 32 / 200 (20 C8)
    // LX / LY: 1000 / -1000 (E8 03 / 18 FC)
    // RX / RY: 32767 / -32768 (FF 7F / 00 80)
    private static readonly byte[] GoldenBytes = Convert.FromHexString(
        "da01010278563412041020c8e80318fcff7f0080");

    [Fact]
    public void InputPacket_GoldenVector_RoundTripsCorrectly()
    {
        Assert.True(InputPacket.TryParse(GoldenBytes, out var packet));
        Assert.Equal(2, packet.Pad);
        Assert.Equal(0x12345678u, packet.Sequence);
        Assert.Equal((ushort)(Protocol.Buttons.A | Protocol.Buttons.DPadLeft), packet.State.Buttons);
        Assert.Equal(32, packet.State.LeftTrigger);
        Assert.Equal(200, packet.State.RightTrigger);
        Assert.Equal(1000, packet.State.ThumbLX);
        Assert.Equal(-1000, packet.State.ThumbLY);
        Assert.Equal(32767, packet.State.ThumbRX);
        Assert.Equal(-32768, packet.State.ThumbRY);

        byte[] reencoded = new byte[20];
        packet.WriteTo(reencoded);
        Assert.Equal(GoldenBytes, reencoded);
    }

    [Fact]
    public void InputPacket_GarbageBytes_AreRejected()
    {
        byte[] zeroBuffer = new byte[20];
        Assert.False(InputPacket.TryParse(zeroBuffer, out _));

        byte[] shortBuffer = new byte[19];
        Assert.False(InputPacket.TryParse(shortBuffer, out _));

        byte[] wrongMagic = (byte[])GoldenBytes.Clone();
        wrongMagic[0] = 0xAA;
        Assert.False(InputPacket.TryParse(wrongMagic, out _));
    }

    [Fact]
    public void SessionMessage_RoundTrip_Works()
    {
        byte[] buffer = new byte[4];
        var msg = new SessionMessage(Protocol.MsgWelcome, 1);
        msg.WriteTo(buffer);

        Assert.True(SessionMessage.TryParse(buffer, out var parsed));
        Assert.Equal(Protocol.MsgWelcome, parsed.Type);
        Assert.Equal(1, parsed.Pad);
    }

    [Fact]
    public void MotionPacket_RoundTripsCorrectly()
    {
        var motion = new MotionState
        {
            TimestampUs = 123456789012345,
            AccelX = 0.12f,
            AccelY = -0.98f,
            AccelZ = 0.05f,
            GyroX = 12.5f,
            GyroY = -3.2f,
            GyroZ = 45.0f
        };

        var packet = new MotionPacket(1, in motion);
        byte[] buffer = new byte[Protocol.MotionPacketSize];
        packet.WriteTo(buffer);

        Assert.True(MotionPacket.TryParse(buffer, out var parsed));
        Assert.Equal(1, parsed.Pad);
        Assert.Equal(motion.TimestampUs, parsed.Motion.TimestampUs);
        Assert.Equal(motion.AccelX, parsed.Motion.AccelX, 3);
        Assert.Equal(motion.AccelY, parsed.Motion.AccelY, 3);
        Assert.Equal(motion.AccelZ, parsed.Motion.AccelZ, 3);
        Assert.Equal(motion.GyroX, parsed.Motion.GyroX, 3);
        Assert.Equal(motion.GyroY, parsed.Motion.GyroY, 3);
        Assert.Equal(motion.GyroZ, parsed.Motion.GyroZ, 3);
    }

    [Fact]
    public void TouchpadPacket_RoundTripsCorrectly()
    {
        var touch = new TouchpadState
        {
            Clicked = true,
            Finger0 = new TouchPoint { IsActive = true, Id = 0, X = 960, Y = 471 },
            Finger1 = new TouchPoint { IsActive = true, Id = 1, X = 1200, Y = 300 }
        };

        var packet = new TouchpadPacket(2, in touch);
        byte[] buffer = new byte[Protocol.TouchpadPacketSize];
        packet.WriteTo(buffer);

        Assert.True(TouchpadPacket.TryParse(buffer, out var parsed));
        Assert.Equal(2, parsed.Pad);
        Assert.True(parsed.State.Clicked);
        Assert.True(parsed.State.Finger0.IsActive);
        Assert.Equal(960, parsed.State.Finger0.X);
        Assert.Equal(471, parsed.State.Finger0.Y);
        Assert.True(parsed.State.Finger1.IsActive);
        Assert.Equal(1200, parsed.State.Finger1.X);
        Assert.Equal(300, parsed.State.Finger1.Y);
    }

    [Fact]
    public void PadState_TouchpadButton_WorksCorrectly()
    {
        var state = PadState.Neutral;
        Assert.False(state.IsButtonPressed(Protocol.Buttons.Touchpad));

        state.SetButton(Protocol.Buttons.Touchpad, true);
        Assert.True(state.IsButtonPressed(Protocol.Buttons.Touchpad));
        Assert.Equal(0x0800, state.Buttons & (ushort)Protocol.Buttons.Touchpad);

        state.SetButton(Protocol.Buttons.Touchpad, false);
        Assert.False(state.IsButtonPressed(Protocol.Buttons.Touchpad));
    }

    [Fact]
    public void RumbleMessage_RoundTrip_Works()
    {
        byte[] buffer = new byte[6];
        var rumble = new RumbleMessage(2, 255, 128);
        rumble.WriteTo(buffer);

        Assert.True(RumbleMessage.TryParse(buffer, out var parsed));
        Assert.Equal(2, parsed.Pad);
        Assert.Equal(255, parsed.LargeMotor);
        Assert.Equal(128, parsed.SmallMotor);
    }
}
