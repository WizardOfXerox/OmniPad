using System;
using System.Collections.Generic;
using OmniPadServer.Core;

namespace OmniPadServer.ViGEm;

/// <summary>
/// Dynamic multiplexing backend that allows switching between Xbox 360, DualShock 4, and Keyboard/Mouse
/// at runtime on-the-fly without dropping connected client sessions.
/// </summary>
public sealed class SwitchablePadBackend : IPadBackend
{
    private readonly object _lock = new();
    private readonly bool _forceKbm;
    private IPadBackend _currentBackend;
    private EmulationType _currentType;
    private readonly HashSet<int> _activeSlots = new();
    private bool _disposed;

    public event EventHandler<RumbleEventArgs>? RumbleReceived;
    public event Action<EmulationType>? EmulationTypeChanged;

    public EmulationType CurrentType
    {
        get { lock (_lock) return _currentType; }
    }

    public string CurrentEngineName => _currentType switch
    {
        EmulationType.DualShock4 => "Virtual DualShock 4 (ViGEm)",
        EmulationType.Xbox360 => "Virtual Xbox 360 (ViGEm)",
        _ => "Virtual Mouse & Keyboard"
    };

    public SwitchablePadBackend(bool forceKeyboardMouse = false, EmulationType initialType = EmulationType.Xbox360)
    {
        _forceKbm = forceKeyboardMouse;
        _currentType = initialType;

        var (backend, _) = PadBackendFactory.CreateBackend(forceKeyboardMouse, initialType);
        _currentBackend = backend;
        _currentBackend.RumbleReceived += OnRumbleReceived;
    }

    public bool SwitchEmulationType(EmulationType newType)
    {
        lock (_lock)
        {
            if (_disposed || _forceKbm || _currentType == newType)
                return false;

            try
            {
                var (newBackend, isHardware) = PadBackendFactory.CreateBackend(false, newType);
                if (!isHardware && newBackend is VirtualMouseKeyboardBackend)
                {
                    // If ViGEm isn't installed, don't break current backend
                    return false;
                }

                // Disconnect slots on old backend
                foreach (int slot in _activeSlots)
                {
                    try { _currentBackend.Disconnect(slot); } catch { }
                }

                _currentBackend.RumbleReceived -= OnRumbleReceived;
                try { _currentBackend.Dispose(); } catch { }

                _currentBackend = newBackend;
                _currentType = newType;
                _currentBackend.RumbleReceived += OnRumbleReceived;

                // Reconnect active slots on new backend
                foreach (int slot in _activeSlots)
                {
                    try { _currentBackend.Connect(slot); } catch { }
                }

                EmulationTypeChanged?.Invoke(newType);
                return true;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[Backend] Failed to switch controller type to {newType}: {ex.Message}");
                Console.ResetColor();
                return false;
            }
        }
    }

    public void Connect(int slot)
    {
        lock (_lock)
        {
            if (_disposed) return;
            _activeSlots.Add(slot);
            _currentBackend.Connect(slot);
        }
    }

    public void Submit(int slot, in PadState state)
    {
        lock (_lock)
        {
            if (_disposed) return;
            _currentBackend.Submit(slot, in state);
        }
    }

    public void Disconnect(int slot)
    {
        lock (_lock)
        {
            if (_disposed) return;
            _activeSlots.Remove(slot);
            _currentBackend.Disconnect(slot);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _activeSlots.Clear();
            _currentBackend.RumbleReceived -= OnRumbleReceived;
            _currentBackend.Dispose();
        }
    }

    private void OnRumbleReceived(object? sender, RumbleEventArgs e)
    {
        RumbleReceived?.Invoke(this, e);
    }
}
