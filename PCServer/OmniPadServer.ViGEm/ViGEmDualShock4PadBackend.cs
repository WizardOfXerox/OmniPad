#if WINDOWS
using System;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using OmniPadServer.Core;

namespace OmniPadServer.ViGEm;

/// <summary>
/// Hardware-grade virtual gamepad backend using Nefarius ViGEmBus kernel driver.
/// Emulates authentic Sony PlayStation DualShock 4 controllers (DirectInput / raw HID / W3C Web Gamepad API).
/// </summary>
public sealed class ViGEmDualShock4PadBackend : IPadBackend
{
    private readonly ViGEmClient _client;
    private readonly IDualShock4Controller?[] _pads = new IDualShock4Controller?[IPadBackend.MaxPads];
    private readonly byte[][] _rawReports = new byte[IPadBackend.MaxPads][];
    private readonly byte[] _seqCounters = new byte[IPadBackend.MaxPads];
    private readonly byte[] _touchPacketCounters = new byte[IPadBackend.MaxPads];
    private readonly object[] _padLocks = new object[IPadBackend.MaxPads];
    private bool _disposed;

    public event EventHandler<RumbleEventArgs>? RumbleReceived;

    public ViGEmDualShock4PadBackend()
    {
        try
        {
            _client = new ViGEmClient();
            for (int i = 0; i < IPadBackend.MaxPads; i++)
            {
                _padLocks[i] = new object();
                _rawReports[i] = CreateNeutralRawReport();
            }
        }
        catch (Exception ex)
        {
            throw new ViGEmDriverUnavailableException(
                "Could not connect to ViGEmBus kernel driver for DualShock 4 emulation.", ex);
        }
    }

    private static byte[] CreateNeutralRawReport()
    {
        // 63-byte raw report corresponding to DS4_REPORT_EX.
        // ViGEmBus prepends Report ID 0x01, producing the full 64-byte USB HID report
        // required by modern Chromium (Chrome/Edge) and DirectInput/RawInput.
        var report = new byte[63];
        report[0] = 128; // LeftThumbX (centered)
        report[1] = 128; // LeftThumbY (centered)
        report[2] = 128; // RightThumbX (centered)
        report[3] = 128; // RightThumbY (centered)
        report[4] = 0x08; // D-Pad neutral (0x08), no face buttons
        report[5] = 0x00; // Bumpers/Triggers/Options/Share
        report[6] = 0x00; // Special (PS, Touch click)
        report[7] = 0;    // Left Trigger (0..255)
        report[8] = 0;    // Right Trigger (0..255)
        report[29] = 0x0B; // Battery level (USB plugged in, 100% full)
        // Multi-touch inactive flags (bit 7 = 1 indicates inactive / no finger touch)
        report[34] = 0x80;
        report[38] = 0x80;
        report[43] = 0x80;
        report[47] = 0x80;
        report[52] = 0x80;
        report[56] = 0x80;
        return report;
    }

    public void Connect(int slot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateSlot(slot);

        if (_pads[slot] != null) return;

        var pad = _client.CreateDualShock4Controller();
        pad.AutoSubmitReport = false;

#pragma warning disable CS0618
        pad.FeedbackReceived += (_, e) =>
        {
            RumbleReceived?.Invoke(this, new RumbleEventArgs(slot, e.LargeMotor, e.SmallMotor));
        };
#pragma warning restore CS0618

        pad.Connect();

        // Immediately submit neutral report so Windows DirectInput/RawInput/Browser sees an initialized gamepad
        var report = _rawReports[slot];
        try
        {
            pad.SubmitRawReport(report);
        }
        catch { }

        _pads[slot] = pad;
    }

    public void Submit(int slot, in PadState state)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateSlot(slot);

        var pad = _pads[slot];
        if (pad == null) return;

        var report = _rawReports[slot];
        lock (_padLocks[slot])
        {
            // 1. Thumbsticks: convert -32768..32767 to 0..255 byte
            report[0] = (byte)Math.Clamp((state.ThumbLX + 32768) >> 8, 0, 255);
            report[1] = (byte)Math.Clamp((32767 - state.ThumbLY) >> 8, 0, 255); // DirectInput Y-inverted
            report[2] = (byte)Math.Clamp((state.ThumbRX + 32768) >> 8, 0, 255);
            report[3] = (byte)Math.Clamp((32767 - state.ThumbRY) >> 8, 0, 255); // DirectInput Y-inverted

            // 2. D-Pad (0 = North, 1 = NE, 2 = East, 3 = SE, 4 = South, 5 = SW, 6 = West, 7 = NW, 8 = None)
            bool up = state.IsButtonPressed(Protocol.Buttons.DPadUp);
            bool down = state.IsButtonPressed(Protocol.Buttons.DPadDown);
            bool left = state.IsButtonPressed(Protocol.Buttons.DPadLeft);
            bool right = state.IsButtonPressed(Protocol.Buttons.DPadRight);

            byte dpad = 8;
            if (up && right) dpad = 1;
            else if (down && right) dpad = 3;
            else if (down && left) dpad = 5;
            else if (up && left) dpad = 7;
            else if (up) dpad = 0;
            else if (right) dpad = 2;
            else if (down) dpad = 4;
            else if (left) dpad = 6;

            // 3. Face buttons (Square 0x10, Cross 0x20, Circle 0x40, Triangle 0x80)
            byte b4 = (byte)(dpad & 0x0F);
            if (state.IsButtonPressed(Protocol.Buttons.X)) b4 |= 0x10; // Square
            if (state.IsButtonPressed(Protocol.Buttons.A)) b4 |= 0x20; // Cross
            if (state.IsButtonPressed(Protocol.Buttons.B)) b4 |= 0x40; // Circle
            if (state.IsButtonPressed(Protocol.Buttons.Y)) b4 |= 0x80; // Triangle
            report[4] = b4;

            // 4. Bumpers, Digital Triggers, Options, Share, Thumb Clicks
            byte b5 = 0;
            if (state.IsButtonPressed(Protocol.Buttons.LeftShoulder)) b5 |= 0x01; // L1
            if (state.IsButtonPressed(Protocol.Buttons.RightShoulder)) b5 |= 0x02; // R1
            if (state.LeftTrigger > 128) b5 |= 0x04; // L2
            if (state.RightTrigger > 128) b5 |= 0x08; // R2
            if (state.IsButtonPressed(Protocol.Buttons.Back)) b5 |= 0x10; // Share
            if (state.IsButtonPressed(Protocol.Buttons.Start)) b5 |= 0x20; // Options
            if (state.IsButtonPressed(Protocol.Buttons.LeftThumb)) b5 |= 0x40; // L3
            if (state.IsButtonPressed(Protocol.Buttons.RightThumb)) b5 |= 0x80; // R3
            report[5] = b5;

            // 5. Special buttons: PS button, Touch click, and sequence counter
            byte b6 = (byte)((_seqCounters[slot]++ & 0x3F) << 2);
            if (state.IsButtonPressed(Protocol.Buttons.Guide)) b6 |= 0x01; // PS
            if (state.IsButtonPressed(Protocol.Buttons.Touchpad) || (report[6] & 0x02) != 0) b6 |= 0x02; // Touchpad click
            report[6] = b6;

            // 6. Analog Triggers (0..255)
            report[7] = state.LeftTrigger;
            report[8] = state.RightTrigger;

            try
            {
                pad.SubmitRawReport(report);
            }
            catch { }
        }
    }

    public void SubmitTouchpad(int slot, in TouchpadState state)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateSlot(slot);

        var pad = _pads[slot];
        if (pad == null) return;

        var report = _rawReports[slot];
        lock (_padLocks[slot])
        {
            // 1. Update physical touch click in Byte 6 (Bit 1 = Touchpad Click)
            if (state.Clicked)
            {
                report[6] |= 0x02;
            }
            else
            {
                report[6] &= 0xFD; // Clear bit 1
            }

            // 2. Touch packet count (Byte 32: bTouchPacketsN = 1)
            report[32] = 1;

            // 3. Touch packet counter (Byte 33: sCurrentTouch.bPacketCounter)
            report[33] = _touchPacketCounters[slot]++;

            // 4. Finger 0 (Bytes 34..37):
            // Byte 34: bIsUpTrackingNum1 (Bit 7: 0 = Down/Active, 1 = Up/Inactive; Bits 0-6: Tracking ID)
            if (state.Finger0.IsActive)
            {
                report[34] = (byte)(state.Finger0.Id & 0x7F);
                ushort x = Math.Min(state.Finger0.X, (ushort)1920);
                ushort y = Math.Min(state.Finger0.Y, (ushort)942);
                report[35] = (byte)(x & 0xFF);
                report[36] = (byte)(((x >> 8) & 0x0F) | ((y & 0x0F) << 4));
                report[37] = (byte)((y >> 4) & 0xFF);
            }
            else
            {
                report[34] = 0x80;
                report[35] = 0;
                report[36] = 0;
                report[37] = 0;
            }

            // 5. Finger 1 (Bytes 38..41):
            // Byte 38: bIsUpTrackingNum2 (Bit 7: 0 = Down/Active, 1 = Up/Inactive; Bits 0-6: Tracking ID)
            if (state.Finger1.IsActive)
            {
                report[38] = (byte)(state.Finger1.Id & 0x7F);
                ushort x = Math.Min(state.Finger1.X, (ushort)1920);
                ushort y = Math.Min(state.Finger1.Y, (ushort)942);
                report[39] = (byte)(x & 0xFF);
                report[40] = (byte)(((x >> 8) & 0x0F) | ((y & 0x0F) << 4));
                report[41] = (byte)((y >> 4) & 0xFF);
            }
            else
            {
                report[38] = 0x80;
                report[39] = 0;
                report[40] = 0;
                report[41] = 0;
            }

            try
            {
                pad.SubmitRawReport(report);
            }
            catch { }
        }
    }

    public void Disconnect(int slot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateSlot(slot);

        var pad = _pads[slot];
        if (pad == null) return;

        lock (_padLocks[slot])
        {
            try { pad.Disconnect(); } catch { }
            _pads[slot] = null;
            _rawReports[slot] = CreateNeutralRawReport();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        for (int i = 0; i < _pads.Length; i++)
        {
            if (_pads[i] != null)
            {
                try { _pads[i]!.Disconnect(); } catch { }
                _pads[i] = null;
            }
        }

        _client.Dispose();
    }

    private static void ValidateSlot(int slot)
    {
        if (slot < 0 || slot >= IPadBackend.MaxPads)
            throw new ArgumentOutOfRangeException(nameof(slot), $"Slot must be between 0 and {IPadBackend.MaxPads - 1}");
    }
}
#endif
