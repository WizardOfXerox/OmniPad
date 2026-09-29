using System;
using OmniPadServer.App;
using OmniPadServer.Core;
using OmniPadServer.ViGEm;
using Xunit;

namespace OmniPadServer.Tests;

public class TouchpadAndDriverResilienceTests
{
    [Fact]
    public void TestTouchpadPacketParseAndSerialize()
    {
        var state = new TouchpadState
        {
            Clicked = true,
            Finger0 = new TouchPoint { IsActive = true, Id = 1, X = 1200, Y = 600 },
            Finger1 = new TouchPoint { IsActive = true, Id = 2, X = 800, Y = 400 }
        };

        var packet = new TouchpadPacket(pad: 0, state);
        byte[] buffer = new byte[Protocol.TouchpadPacketSize];
        packet.WriteTo(buffer);

        Assert.Equal(Protocol.MagicByte, buffer[0]);
        Assert.Equal(Protocol.Version, buffer[1]);
        Assert.Equal(Protocol.MsgTouchpad, buffer[2]);
        Assert.Equal(0, buffer[3]); // Pad

        // Parse back
        Assert.True(TouchpadPacket.TryParse(buffer, out var parsed));
        Assert.Equal(0, parsed.Pad);
        Assert.True(parsed.State.Clicked);
        Assert.True(parsed.State.Finger0.IsActive);
        Assert.Equal(1200, parsed.State.Finger0.X);
        Assert.Equal(600, parsed.State.Finger0.Y);
        Assert.True(parsed.State.Finger1.IsActive);
        Assert.Equal(800, parsed.State.Finger1.X);
        Assert.Equal(400, parsed.State.Finger1.Y);
    }

    [Fact]
    public void TestTouchpad12BitCoordinatePacking()
    {
        // Sony DualShock 4 / DualSense packing specification:
        // Byte 0: X[7..0]
        // Byte 1: X[11..8] | (Y[3..0] << 4)
        // Byte 2: Y[11..4]
        ushort x = 1920; // 0x0780
        ushort y = 942;  // 0x03AE

        byte b0 = (byte)(x & 0xFF);
        byte b1 = (byte)(((x >> 8) & 0x0F) | ((y & 0x0F) << 4));
        byte b2 = (byte)((y >> 4) & 0xFF);

        // Unpack
        ushort unpackedX = (ushort)(b0 | ((b1 & 0x0F) << 8));
        ushort unpackedY = (ushort)(((b1 >> 4) & 0x0F) | (b2 << 4));

        Assert.Equal(x, unpackedX);
        Assert.Equal(y, unpackedY);
    }

    [Fact]
    public void TestHIDOmniPadBusSubmitTouchpadResilience()
    {
        using var bus = new HIDOmniPadBus(forceKeyboardMouse: true);
        bus.Connect(0);

        var touchState = new TouchpadState
        {
            Clicked = true,
            Finger0 = new TouchPoint { IsActive = true, Id = 1, X = 960, Y = 471 },
            Finger1 = default
        };

        // Must not throw or crash on zero-driver KBM backend
        bus.SubmitTouchpad(0, in touchState);
        bus.Submit(0, PadState.Neutral);
        bus.Disconnect(0);
    }

    [Fact]
    public void TestServerTelemetryRecording()
    {
        ServerTelemetry.RecordConnect(0, "192.168.1.50:5000");

        var padState = new PadState
        {
            ThumbLX = 16384,
            ThumbLY = -16384,
            LeftTrigger = 128,
            RightTrigger = 255
        };
        padState.SetButton(Protocol.Buttons.A, true);
        ServerTelemetry.RecordPad(0, in padState);

        var touchState = new TouchpadState
        {
            Clicked = true,
            Finger0 = new TouchPoint { IsActive = true, Id = 5, X = 960, Y = 471 },
            Finger1 = default
        };
        ServerTelemetry.RecordTouch(0, in touchState);

        var snap = ServerTelemetry.GetSnapshot();
        Assert.NotNull(snap);
        Assert.NotEmpty(snap.Slots);

        var s0 = snap.Slots[0];
        Assert.True(s0.IsConnected);
        Assert.Equal(16384, s0.ThumbLX);
        Assert.Equal(0.5, s0.NormLX);
        Assert.True(s0.LeftMagnitude > 0.6);
        Assert.Equal(100.0, s0.RightTriggerPct);
        Assert.Contains("A", s0.ActiveButtons);

        // Touchpad telemetry verification
        Assert.True(s0.TouchClicked);
        Assert.True(s0.TouchF0Active);
        Assert.Equal(960, s0.TouchF0X);
        Assert.Equal(471, s0.TouchF0Y);
        Assert.Equal(50.0, s0.TouchF0NormX);
        Assert.Equal(50.0, s0.TouchF0NormY);

        ServerTelemetry.RecordDisconnect(0);
        var snap2 = ServerTelemetry.GetSnapshot();
        Assert.False(snap2.Slots[0].IsConnected);
    }
}
