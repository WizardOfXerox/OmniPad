using System;
using System.Collections.Generic;
using OmniPadServer.Core;

namespace OmniPadServer.ViGEm;

/// <summary>
/// Unified virtual controller bus for OmniPad.
/// Transparently integrates Microsoft WHQL-signed ViGEmBus (KMDF) and
/// HIDMaestro (UMDF2 Direct HID) into a single high-performance facade.
/// Supports 15 curated profiles (DualSense PS5, Switch Pro, GameCube, Steam Deck,
/// Xbox Elite 2, Logitech G29 Wheel, HOTAS Flight Stick) with automatic graceful fallback.
/// </summary>
public sealed class HIDOmniPadBus : IPadBackend
{
    private readonly object _lock = new();
    private readonly bool _forceKbm;
    private ControllerProfilePreset _currentPreset;
    private IPadBackend _activeBackend;
    private readonly HashSet<int> _activeSlots = new();
    private bool _disposed;

    public event EventHandler<RumbleEventArgs>? RumbleReceived;
    public event Action<ControllerProfilePreset, string>? ProfileChanged;

    public ControllerProfilePreset CurrentPreset
    {
        get { lock (_lock) return _currentPreset; }
    }

    public string ActiveEngineName => _activeBackend switch
    {
        ViGEmPadBackend => "ViGEm Xbox 360 (Kernel WHQL)",
        ViGEmDualShock4PadBackend => "ViGEm DualShock 4 (Kernel WHQL)",
        HIDMaestroPadBackend hm => $"HIDMaestro UMDF2 ({hm.Profile.Name})",
        _ => "Zero-Driver Keyboard & Mouse"
    };

    public ControllerProfileInfo CurrentInfo => ControllerProfileCatalog.Get(_currentPreset);

    public HIDOmniPadBus()
        : this(false, ControllerProfilePreset.Xbox360_WHQL)
    {
    }

    public HIDOmniPadBus(
        bool forceKeyboardMouse,
        ControllerProfilePreset initialPreset = ControllerProfilePreset.Xbox360_WHQL)
    {
        _forceKbm = forceKeyboardMouse;
        _currentPreset = forceKeyboardMouse ? ControllerProfilePreset.KeyboardMouse : initialPreset;

        _activeBackend = InstantiateBackend(_currentPreset);
        _activeBackend.RumbleReceived += OnRumbleReceived;
    }

    public bool SwitchProfile(ControllerProfilePreset newPreset)
    {
        lock (_lock)
        {
            if (_disposed || _forceKbm || _currentPreset == newPreset)
                return false;

            var newBackend = InstantiateBackend(newPreset);

            // Transfer connected slots
            foreach (int slot in _activeSlots)
            {
                try { _activeBackend.Disconnect(slot); } catch { }
            }

            _activeBackend.RumbleReceived -= OnRumbleReceived;
            try { _activeBackend.Dispose(); } catch { }

            _activeBackend = newBackend;
            _currentPreset = newPreset;
            _activeBackend.RumbleReceived += OnRumbleReceived;

            foreach (int slot in _activeSlots)
            {
                try { _activeBackend.Connect(slot); } catch { }
            }

            ProfileChanged?.Invoke(_currentPreset, ActiveEngineName);
            return true;
        }
    }

    private IPadBackend InstantiateBackend(ControllerProfilePreset preset)
    {
        if (_forceKbm || preset == ControllerProfilePreset.KeyboardMouse)
        {
            return new VirtualMouseKeyboardBackend();
        }

        var info = ControllerProfileCatalog.Get(preset);

        // 1. If profile is targeted for ViGEm KMDF
        if (preset == ControllerProfilePreset.Xbox360_WHQL)
        {
            try
            {
                var be = new ViGEmPadBackend();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("[HIDOmniPadBus] Active: Microsoft Xbox 360 (ViGEmBus WHQL Kernel)");
                Console.ResetColor();
                return be;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[HIDOmniPadBus] ViGEmBus unavailable ({ex.Message}). Trying HIDMaestro fallback...");
                Console.ResetColor();
                // Fallback to HIDMaestro
                return TryCreateHIDMaestro("xbox-360-wired");
            }
        }

        if (preset == ControllerProfilePreset.DualShock4_WHQL)
        {
            try
            {
                var be = new ViGEmDualShock4PadBackend();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("[HIDOmniPadBus] Active: Sony DualShock 4 (ViGEmBus WHQL Kernel)");
                Console.ResetColor();
                return be;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[HIDOmniPadBus] ViGEmBus unavailable ({ex.Message}). Trying HIDMaestro fallback...");
                Console.ResetColor();
                return TryCreateHIDMaestro("dualshock-4-v2");
            }
        }

        // 2. Profile targeted for HIDMaestro (DualSense PS5, Switch Pro, GameCube, Wheels, etc.)
        return TryCreateHIDMaestro(info.UnderlyingProfileId);
    }

    private IPadBackend TryCreateHIDMaestro(string profileId)
    {
        try
        {
            var hm = new HIDMaestroPadBackend(profileId, autoInstallDriver: false);
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"[HIDOmniPadBus] Active: {hm.Profile.Name} (HIDMaestro UMDF2 Direct HID)");
            Console.ResetColor();
            return hm;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[HIDOmniPadBus] HIDMaestro profile '{profileId}' failed: {ex.Message}.");
            Console.ResetColor();

            // Try ViGEm as secondary fallback if applicable
            try
            {
                return new ViGEmPadBackend();
            }
            catch
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[HIDOmniPadBus] Falling back to zero-driver Keyboard/Mouse emulation.");
                Console.ResetColor();
                return new VirtualMouseKeyboardBackend();
            }
        }
    }

    public void Connect(int slot)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _activeSlots.Add(slot);
            try
            {
                _activeBackend.Connect(slot);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[HIDOmniPadBus] Backend Connect slot {slot} failed: {ex.Message}");
                Console.ResetColor();
            }
        }
    }

    public void Submit(int slot, in PadState state)
    {
        lock (_lock)
        {
            if (_disposed) return;
            try
            {
                _activeBackend.Submit(slot, in state);
            }
            catch { }
        }
    }

    public void SubmitTouchpad(int slot, in TouchpadState state)
    {
        lock (_lock)
        {
            if (_disposed) return;
            try
            {
                _activeBackend.SubmitTouchpad(slot, in state);
            }
            catch { }
        }
    }

    public void Disconnect(int slot)
    {
        lock (_lock)
        {
            if (_disposed) return;
            _activeSlots.Remove(slot);
            _activeBackend.Disconnect(slot);
        }
    }

    private void OnRumbleReceived(object? sender, RumbleEventArgs e)
    {
        RumbleReceived?.Invoke(this, e);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;

            foreach (int slot in _activeSlots)
            {
                try { _activeBackend.Disconnect(slot); } catch { }
            }
            _activeSlots.Clear();

            _activeBackend.RumbleReceived -= OnRumbleReceived;
            _activeBackend.Dispose();
        }
    }
}
