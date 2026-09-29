using System;

namespace OmniPadServer.Core;

public sealed class RumbleEventArgs : EventArgs
{
    public int Slot { get; }
    public byte LargeMotor { get; }
    public byte SmallMotor { get; }

    public RumbleEventArgs(int slot, byte largeMotor, byte smallMotor)
    {
        Slot = slot;
        LargeMotor = largeMotor;
        SmallMotor = smallMotor;
    }
}

public interface IPadBackend : IDisposable
{
    public const int MaxPads = 16;

    event EventHandler<RumbleEventArgs>? RumbleReceived;

    /// <summary>
    /// Plugs in a virtual controller at the designated player slot (0-3).
    /// </summary>
    void Connect(int slot);

    /// <summary>
    /// Submits a full input snapshot to the virtual controller at the slot.
    /// </summary>
    void Submit(int slot, in PadState state);

    /// <summary>
    /// Submits a multi-touch touchpad packet (DS4/DualSense capacitive touch surface) if supported by the backend.
    /// </summary>
    void SubmitTouchpad(int slot, in TouchpadState state) { }

    /// <summary>
    /// Unplugs the virtual controller at the designated slot.
    /// </summary>
    void Disconnect(int slot);
}
