using System;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using OmniPadServer.Core;

namespace OmniPadServer.ViGEm;

/// <summary>
/// Hardware-grade virtual gamepad backend using Nefarius ViGEmBus kernel driver.
/// Emulates authentic Sony PlayStation DualShock 4 controllers (DirectInput / raw HID).
/// </summary>
public sealed class ViGEmDualShock4PadBackend : IPadBackend
{
    private readonly ViGEmClient _client;
    private readonly IDualShock4Controller?[] _pads = new IDualShock4Controller?[IPadBackend.MaxPads];
    private bool _disposed;

    public event EventHandler<RumbleEventArgs>? RumbleReceived;

    public ViGEmDualShock4PadBackend()
    {
        try
        {
            _client = new ViGEmClient();
        }
        catch (Exception ex)
        {
            throw new ViGEmDriverUnavailableException(
                "Could not connect to ViGEmBus kernel driver for DualShock 4 emulation.", ex);
        }
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
        _pads[slot] = pad;
    }

    public void Submit(int slot, in PadState state)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateSlot(slot);

        var pad = _pads[slot];
        if (pad == null) return;

        // Map face buttons
        pad.SetButtonState(DualShock4Button.Cross, state.IsButtonPressed(Protocol.Buttons.A));
        pad.SetButtonState(DualShock4Button.Circle, state.IsButtonPressed(Protocol.Buttons.B));
        pad.SetButtonState(DualShock4Button.Square, state.IsButtonPressed(Protocol.Buttons.X));
        pad.SetButtonState(DualShock4Button.Triangle, state.IsButtonPressed(Protocol.Buttons.Y));

        // Bumpers & triggers
        pad.SetButtonState(DualShock4Button.ShoulderLeft, state.IsButtonPressed(Protocol.Buttons.LeftShoulder));
        pad.SetButtonState(DualShock4Button.ShoulderRight, state.IsButtonPressed(Protocol.Buttons.RightShoulder));
        pad.SetButtonState(DualShock4Button.TriggerLeft, state.LeftTrigger > 128);
        pad.SetButtonState(DualShock4Button.TriggerRight, state.RightTrigger > 128);

        // Thumbstick clicks
        pad.SetButtonState(DualShock4Button.ThumbLeft, state.IsButtonPressed(Protocol.Buttons.LeftThumb));
        pad.SetButtonState(DualShock4Button.ThumbRight, state.IsButtonPressed(Protocol.Buttons.RightThumb));

        // Navigation & Special Buttons
        pad.SetButtonState(DualShock4Button.Options, state.IsButtonPressed(Protocol.Buttons.Start));
        pad.SetButtonState(DualShock4Button.Share, state.IsButtonPressed(Protocol.Buttons.Back));
        pad.SetButtonState(DualShock4SpecialButton.Ps, state.IsButtonPressed(Protocol.Buttons.Guide));
        pad.SetButtonState(DualShock4SpecialButton.Touchpad, state.IsButtonPressed(Protocol.Buttons.Touchpad));

        // Sliders (Triggers 0..255)
        pad.SetSliderValue(DualShock4Slider.LeftTrigger, state.LeftTrigger);
        pad.SetSliderValue(DualShock4Slider.RightTrigger, state.RightTrigger);

        // D-Pad direction
        bool up = state.IsButtonPressed(Protocol.Buttons.DPadUp);
        bool down = state.IsButtonPressed(Protocol.Buttons.DPadDown);
        bool left = state.IsButtonPressed(Protocol.Buttons.DPadLeft);
        bool right = state.IsButtonPressed(Protocol.Buttons.DPadRight);

        var dpad = DualShock4DPadDirection.None;
        if (up && right) dpad = DualShock4DPadDirection.Northeast;
        else if (down && right) dpad = DualShock4DPadDirection.Southeast;
        else if (down && left) dpad = DualShock4DPadDirection.Southwest;
        else if (up && left) dpad = DualShock4DPadDirection.Northwest;
        else if (up) dpad = DualShock4DPadDirection.North;
        else if (down) dpad = DualShock4DPadDirection.South;
        else if (left) dpad = DualShock4DPadDirection.West;
        else if (right) dpad = DualShock4DPadDirection.East;
        pad.SetDPadDirection(dpad);

        // Analog Sticks (convert -32768..32767 to 0..255 byte)
        byte lx = (byte)Math.Clamp((state.ThumbLX + 32768) >> 8, 0, 255);
        byte ly = (byte)Math.Clamp((32767 - state.ThumbLY) >> 8, 0, 255); // DirectInput Y-invert
        byte rx = (byte)Math.Clamp((state.ThumbRX + 32768) >> 8, 0, 255);
        byte ry = (byte)Math.Clamp((32767 - state.ThumbRY) >> 8, 0, 255);

        pad.SetAxisValue(DualShock4Axis.LeftThumbX, lx);
        pad.SetAxisValue(DualShock4Axis.LeftThumbY, ly);
        pad.SetAxisValue(DualShock4Axis.RightThumbX, rx);
        pad.SetAxisValue(DualShock4Axis.RightThumbY, ry);

        pad.SubmitReport();
    }

    public void Disconnect(int slot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateSlot(slot);

        var pad = _pads[slot];
        if (pad == null) return;

        try { pad.Disconnect(); } catch { }
        _pads[slot] = null;
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
