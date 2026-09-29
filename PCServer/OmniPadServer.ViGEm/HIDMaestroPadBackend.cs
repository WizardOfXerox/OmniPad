#if WINDOWS
using System;
using System.Collections.Generic;
using HIDMaestro;
using OmniPadServer.Core;

namespace OmniPadServer.ViGEm;

/// <summary>
/// Next-generation UMDF2 virtual gamepad backend using HIDMaestro.
/// Emulates authentic Xbox 360 controllers with 100% detection compatibility
/// across both native PC games (XInput) and modern web browsers (W3C Gamepad API in Chrome/Edge).
/// </summary>
public sealed class HIDMaestroPadBackend : IPadBackend
{
    private readonly HMContext _ctx;
    private readonly HMProfile _profile;
    private readonly HMController?[] _controllers = new HMController?[IPadBackend.MaxPads];
    private bool _disposed;

    public event EventHandler<RumbleEventArgs>? RumbleReceived;
    public HMProfile Profile => _profile;

    public HIDMaestroPadBackend(string profileId = "xbox-360-wired", bool autoInstallDriver = false)
    {
        _ctx = new HMContext();
        _ctx.LoadDefaultProfiles();
        if (autoInstallDriver)
        {
            try { _ctx.InstallDriver(); } catch { }
        }

        _profile = _ctx.GetProfile(profileId) 
            ?? _ctx.GetProfile("xbox-360-wired")
            ?? throw new InvalidOperationException($"HIDMaestro profile '{profileId}' not found.");
    }

    public void Connect(int slot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateSlot(slot);

        if (_controllers[slot] != null) return;

        var ctrl = _ctx.CreateController(_profile);
        int capturedSlot = slot;

        ctrl.OutputReceived += (_, packet) =>
        {
            if (packet.Source == HMOutputSource.XInput && packet.Data.Length >= 4)
            {
                var span = packet.Data.Span;
                byte leftMotor = span[2];
                byte rightMotor = span[3];
                RumbleReceived?.Invoke(this, new RumbleEventArgs(capturedSlot, leftMotor, rightMotor));
            }
        };

        // Submit initial neutral state
        ctrl.SubmitState(new HMGamepadState
        {
            Axes = HMGamepadStateHelpers.StandardAxes(_profile,
                leftStickX: 0.5f,
                leftStickY: 0.5f,
                rightStickX: 0.5f,
                rightStickY: 0.5f,
                leftTrigger: 0.0f,
                rightTrigger: 0.0f),
            Buttons = HMButton.None,
            Hat = HMHat.None
        });

        _controllers[slot] = ctrl;
    }

    public void Submit(int slot, in PadState state)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateSlot(slot);

        var ctrl = _controllers[slot];
        if (ctrl == null) return;

        // Map buttons
        HMButton buttons = HMButton.None;
        if ((state.Buttons & (ushort)Protocol.Buttons.A) != 0) buttons |= HMButton.A;
        if ((state.Buttons & (ushort)Protocol.Buttons.B) != 0) buttons |= HMButton.B;
        if ((state.Buttons & (ushort)Protocol.Buttons.X) != 0) buttons |= HMButton.X;
        if ((state.Buttons & (ushort)Protocol.Buttons.Y) != 0) buttons |= HMButton.Y;
        if ((state.Buttons & (ushort)Protocol.Buttons.LeftShoulder) != 0) buttons |= HMButton.LeftBumper;
        if ((state.Buttons & (ushort)Protocol.Buttons.RightShoulder) != 0) buttons |= HMButton.RightBumper;
        if ((state.Buttons & (ushort)Protocol.Buttons.Back) != 0) buttons |= HMButton.Back;
        if ((state.Buttons & (ushort)Protocol.Buttons.Start) != 0) buttons |= HMButton.Start;
        if ((state.Buttons & (ushort)Protocol.Buttons.LeftThumb) != 0) buttons |= HMButton.LeftStick;
        if ((state.Buttons & (ushort)Protocol.Buttons.RightThumb) != 0) buttons |= HMButton.RightStick;
        if ((state.Buttons & (ushort)Protocol.Buttons.Guide) != 0) buttons |= HMButton.Guide;
        if ((state.Buttons & (ushort)Protocol.Buttons.Touchpad) != 0) buttons |= HMButton.Touchpad;

        // Map D-Pad Hat
        bool up = (state.Buttons & (ushort)Protocol.Buttons.DPadUp) != 0;
        bool down = (state.Buttons & (ushort)Protocol.Buttons.DPadDown) != 0;
        bool left = (state.Buttons & (ushort)Protocol.Buttons.DPadLeft) != 0;
        bool right = (state.Buttons & (ushort)Protocol.Buttons.DPadRight) != 0;

        HMHat hat = HMHat.None;
        if (up && right) hat = HMHat.NorthEast;
        else if (up && left) hat = HMHat.NorthWest;
        else if (up) hat = HMHat.North;
        else if (down && right) hat = HMHat.SouthEast;
        else if (down && left) hat = HMHat.SouthWest;
        else if (down) hat = HMHat.South;
        else if (right) hat = HMHat.East;
        else if (left) hat = HMHat.West;

        // Normalize axes: ThumbLX/Y/RX/RY are short (-32768..32767) -> 0..1 (0.5 is centered)
        float lx = (state.ThumbLX + 32768f) / 65535f;
        float ly = (state.ThumbLY + 32768f) / 65535f;
        float rx = (state.ThumbRX + 32768f) / 65535f;
        float ry = (state.ThumbRY + 32768f) / 65535f;
        float lt = state.LeftTrigger / 255f;
        float rt = state.RightTrigger / 255f;

        var hmState = new HMGamepadState
        {
            Axes = HMGamepadStateHelpers.StandardAxes(_profile,
                leftStickX: lx,
                leftStickY: ly,
                rightStickX: rx,
                rightStickY: ry,
                leftTrigger: lt,
                rightTrigger: rt),
            Buttons = buttons,
            Hat = hat
        };

        ctrl.SubmitState(hmState);
    }

    public void Disconnect(int slot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateSlot(slot);

        var ctrl = _controllers[slot];
        if (ctrl == null) return;

        try { ctrl.Dispose(); } catch { }
        _controllers[slot] = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        for (int i = 0; i < _controllers.Length; i++)
        {
            if (_controllers[i] != null)
            {
                try { _controllers[i]!.Dispose(); } catch { }
                _controllers[i] = null;
            }
        }

        try { _ctx.Dispose(); } catch { }
    }

    private static void ValidateSlot(int slot)
    {
        if (slot < 0 || slot >= IPadBackend.MaxPads)
            throw new ArgumentOutOfRangeException(nameof(slot), $"Slot must be between 0 and {IPadBackend.MaxPads - 1}");
    }
}
#endif
