using System;

namespace OmniPadServer.Core;

/// <summary>
/// Snapshot of a gamepad state.
/// </summary>
public struct PadState : IEquatable<PadState>
{
    public ushort Buttons;
    public byte LeftTrigger;
    public byte RightTrigger;
    public short ThumbLX;
    public short ThumbLY;
    public short ThumbRX;
    public short ThumbRY;

    public static readonly PadState Neutral = new()
    {
        Buttons = 0,
        LeftTrigger = 0,
        RightTrigger = 0,
        ThumbLX = 0,
        ThumbLY = 0,
        ThumbRX = 0,
        ThumbRY = 0
    };

    public bool IsButtonPressed(Protocol.Buttons button)
    {
        return (Buttons & (ushort)button) != 0;
    }

    public void SetButton(Protocol.Buttons button, bool pressed)
    {
        if (pressed)
            Buttons |= (ushort)button;
        else
            Buttons &= (ushort)~button;
    }

    public bool Equals(PadState other) =>
        Buttons == other.Buttons &&
        LeftTrigger == other.LeftTrigger &&
        RightTrigger == other.RightTrigger &&
        ThumbLX == other.ThumbLX &&
        ThumbLY == other.ThumbLY &&
        ThumbRX == other.ThumbRX &&
        ThumbRY == other.ThumbRY;

    public override bool Equals(object? obj) => obj is PadState other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(Buttons, LeftTrigger, RightTrigger, ThumbLX, ThumbLY, ThumbRX, ThumbRY);

    public static bool operator ==(PadState left, PadState right) => left.Equals(right);
    public static bool operator !=(PadState left, PadState right) => !left.Equals(right);
}
