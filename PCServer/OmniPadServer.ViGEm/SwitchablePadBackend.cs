using System;
using System.Collections.Generic;
using OmniPadServer.Core;

namespace OmniPadServer.ViGEm;

public enum EmulationType
{
    Xbox360,
    DualShock4,
    HIDMaestro
}

/// <summary>
/// Dynamic multiplexing backend that allows switching between controller profiles
/// at runtime on-the-fly without dropping connected client sessions.
/// Powered by HIDOmniPadBus.
/// </summary>
public sealed class SwitchablePadBackend : IPadBackend
{
    private readonly HIDOmniPadBus _bus;
    private bool _disposed;

    public event EventHandler<RumbleEventArgs>? RumbleReceived;
    public event Action<EmulationType>? EmulationTypeChanged;
    public event Action<ControllerProfilePreset, string>? ProfileChanged;

    public HIDOmniPadBus Bus => _bus;

    public EmulationType CurrentType => _bus.CurrentPreset switch
    {
        ControllerProfilePreset.DualShock4_WHQL => EmulationType.DualShock4,
        ControllerProfilePreset.Xbox360_WHQL => EmulationType.Xbox360,
        _ => EmulationType.HIDMaestro
    };

    public ControllerProfilePreset CurrentPreset => _bus.CurrentPreset;
    public string CurrentEngineName => _bus.ActiveEngineName;
    public ControllerProfileInfo CurrentInfo => _bus.CurrentInfo;

    public SwitchablePadBackend(bool forceKeyboardMouse = false, ControllerProfilePreset initialPreset = ControllerProfilePreset.Xbox360_WHQL)
    {
        _bus = new HIDOmniPadBus(forceKeyboardMouse, initialPreset);
        _bus.RumbleReceived += (_, e) => RumbleReceived?.Invoke(this, e);
        _bus.ProfileChanged += (preset, engine) =>
        {
            ProfileChanged?.Invoke(preset, engine);
            EmulationTypeChanged?.Invoke(CurrentType);
        };
    }

    public SwitchablePadBackend(bool forceKeyboardMouse, EmulationType initialType)
        : this(forceKeyboardMouse, initialType == EmulationType.DualShock4 ? ControllerProfilePreset.DualShock4_WHQL :
                                  initialType == EmulationType.HIDMaestro ? ControllerProfilePreset.DualSense_PS5 :
                                  ControllerProfilePreset.Xbox360_WHQL)
    {
    }

    public bool SwitchEmulationType(EmulationType newType)
    {
        var targetPreset = newType switch
        {
            EmulationType.DualShock4 => ControllerProfilePreset.DualShock4_WHQL,
            EmulationType.HIDMaestro => ControllerProfilePreset.DualSense_PS5,
            _ => ControllerProfilePreset.Xbox360_WHQL
        };

        return _bus.SwitchProfile(targetPreset);
    }

    public bool SwitchProfile(ControllerProfilePreset preset)
    {
        return _bus.SwitchProfile(preset);
    }

    public void Connect(int slot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _bus.Connect(slot);
    }

    public void Submit(int slot, in PadState state)
    {
        if (_disposed) return;
        _bus.Submit(slot, in state);
    }

    public void SubmitTouchpad(int slot, in TouchpadState state)
    {
        if (_disposed) return;
        _bus.SubmitTouchpad(slot, in state);
    }

    public void Disconnect(int slot)
    {
        if (_disposed) return;
        _bus.Disconnect(slot);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _bus.Dispose();
    }
}
