#if WINDOWS
using System;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using OmniPadServer.Core;

namespace OmniPadServer.ViGEm;

/// <summary>
/// Thrown when the ViGEmBus driver is not installed on the system.
/// </summary>
public sealed class ViGEmDriverUnavailableException : Exception
{
    public ViGEmDriverUnavailableException(string message, Exception? inner = null)
        : base(message, inner) { }
}

/// <summary>
/// Hardware-grade virtual gamepad backend using Nefarius ViGEmBus kernel driver.
/// Emulates authentic Microsoft Xbox 360 controllers.
/// </summary>
public sealed class ViGEmPadBackend : IPadBackend
{
    private readonly ViGEmClient _client;
    private readonly IXbox360Controller?[] _pads = new IXbox360Controller?[IPadBackend.MaxPads];
    private readonly object[] _padLocks = [new(), new(), new(), new()];
    private bool _disposed;

    public event EventHandler<RumbleEventArgs>? RumbleReceived;

    public ViGEmPadBackend()
    {
        try
        {
            _client = new ViGEmClient();
        }
        catch (Exception ex)
        {
            throw new ViGEmDriverUnavailableException(
                "Could not connect to ViGEmBus kernel driver. ViGEmBus is likely not installed.", ex);
        }
    }

    public void Connect(int slot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateSlot(slot);

        if (_pads[slot] != null) return; // already connected

        var pad = _client.CreateXbox360Controller();
        pad.AutoSubmitReport = false;

        // In-game rumble feedback capture from Windows game
        pad.FeedbackReceived += (_, e) =>
        {
            RumbleReceived?.Invoke(this, new RumbleEventArgs(slot, e.LargeMotor, e.SmallMotor));
        };

        pad.Connect();
        // Immediately submit neutral report so Windows DirectInput/XInput/Browser sees an initialized gamepad
        pad.SetButtonsFull(0);
        pad.LeftTrigger = 0;
        pad.RightTrigger = 0;
        pad.LeftThumbX = 0;
        pad.LeftThumbY = 0;
        pad.RightThumbX = 0;
        pad.RightThumbY = 0;
        try { pad.SubmitReport(); } catch { }
        _pads[slot] = pad;
    }

    public void Submit(int slot, in PadState state)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateSlot(slot);

        var pad = _pads[slot];
        if (pad == null) return;

        lock (_padLocks[slot])
        {
            // Atomic assignment into native report buffer
            pad.SetButtonsFull(state.Buttons);
            pad.LeftTrigger = state.LeftTrigger;
            pad.RightTrigger = state.RightTrigger;
            pad.LeftThumbX = state.ThumbLX;
            pad.LeftThumbY = state.ThumbLY;
            pad.RightThumbX = state.ThumbRX;
            pad.RightThumbY = state.ThumbRY;

            try
            {
                pad.SubmitReport();
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

        try { pad.Disconnect(); } catch { /* best effort */ }
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
#endif
