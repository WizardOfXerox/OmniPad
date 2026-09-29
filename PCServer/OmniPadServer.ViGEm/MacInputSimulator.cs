using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using OmniPadServer.Core;

namespace OmniPadServer.ViGEm;

[StructLayout(LayoutKind.Sequential)]
public struct CGPoint
{
    public double X;
    public double Y;

    public CGPoint(double x, double y)
    {
        X = x;
        Y = y;
    }
}

public static class MacKeyCodes
{
    public const ushort A = 0x00;
    public const ushort S = 0x01;
    public const ushort D = 0x02;
    public const ushort F = 0x03;
    public const ushort H = 0x04;
    public const ushort G = 0x05;
    public const ushort Z = 0x06;
    public const ushort X = 0x07;
    public const ushort C = 0x08;
    public const ushort V = 0x09;
    public const ushort B = 0x0B;
    public const ushort Q = 0x0C;
    public const ushort W = 0x0D;
    public const ushort E = 0x0E;
    public const ushort R = 0x0F;
    public const ushort Y = 0x10;
    public const ushort T = 0x11;
    public const ushort One = 0x12;
    public const ushort Two = 0x13;
    public const ushort Three = 0x14;
    public const ushort Four = 0x15;
    public const ushort Six = 0x16;
    public const ushort Five = 0x17;
    public const ushort Equal = 0x18;
    public const ushort Nine = 0x19;
    public const ushort Seven = 0x1A;
    public const ushort Minus = 0x1B;
    public const ushort Eight = 0x1C;
    public const ushort Zero = 0x1D;
    public const ushort RightBracket = 0x1E;
    public const ushort O = 0x1F;
    public const ushort U = 0x20;
    public const ushort LeftBracket = 0x21;
    public const ushort I = 0x22;
    public const ushort P = 0x23;
    public const ushort Return = 0x24;
    public const ushort L = 0x25;
    public const ushort J = 0x26;
    public const ushort Quote = 0x27;
    public const ushort K = 0x28;
    public const ushort Semicolon = 0x29;
    public const ushort Backslash = 0x2A;
    public const ushort Comma = 0x2B;
    public const ushort Slash = 0x2C;
    public const ushort N = 0x2D;
    public const ushort M = 0x2E;
    public const ushort Period = 0x2F;
    public const ushort Tab = 0x30;
    public const ushort Space = 0x31;
    public const ushort Backquote = 0x32;
    public const ushort Delete = 0x33;
    public const ushort Escape = 0x35;
    public const ushort Command = 0x37;
    public const ushort Shift = 0x38;
    public const ushort CapsLock = 0x39;
    public const ushort Option = 0x3A;
    public const ushort Control = 0x3B;
    public const ushort RightShift = 0x3C;
    public const ushort RightOption = 0x3D;
    public const ushort RightControl = 0x3E;
    public const ushort Function = 0x3F;
    public const ushort F1 = 0x7A;
    public const ushort F2 = 0x78;
    public const ushort F3 = 0x63;
    public const ushort F4 = 0x76;
    public const ushort F5 = 0x60;
    public const ushort F6 = 0x61;
    public const ushort F7 = 0x62;
    public const ushort F8 = 0x64;
    public const ushort F9 = 0x65;
    public const ushort F10 = 0x6D;
    public const ushort F11 = 0x67;
    public const ushort F12 = 0x6F;
    public const ushort Help = 0x72;
    public const ushort Home = 0x73;
    public const ushort PageUp = 0x74;
    public const ushort ForwardDelete = 0x75;
    public const ushort End = 0x77;
    public const ushort PageDown = 0x79;
    public const ushort LeftArrow = 0x7B;
    public const ushort RightArrow = 0x7C;
    public const ushort DownArrow = 0x7D;
    public const ushort UpArrow = 0x7E;
}

/// <summary>
/// Native macOS host input simulator using CoreGraphics CGEvent C-APIs.
/// Translates OmniPad gamepad sticks, buttons, touchpad, and KBM streams into
/// macOS CGEvent keyboard and mouse actions without any third-party kernel extensions.
/// </summary>
public sealed class MacInputSimulator : IPadBackend
{
    private const string CoreGraphicsLib = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundationLib = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    public const uint kCGHIDEventTap = 0;
    public const uint kCGSessionEventTap = 1;

    public const uint kCGEventNull = 0;
    public const uint kCGEventLeftMouseDown = 1;
    public const uint kCGEventLeftMouseUp = 2;
    public const uint kCGEventRightMouseDown = 3;
    public const uint kCGEventRightMouseUp = 4;
    public const uint kCGEventMouseMoved = 5;
    public const uint kCGEventLeftMouseDragged = 6;
    public const uint kCGEventRightMouseDragged = 7;
    public const uint kCGEventScrollWheel = 22;
    public const uint kCGEventOtherMouseDown = 25;
    public const uint kCGEventOtherMouseUp = 26;
    public const uint kCGEventOtherMouseDragged = 27;

    public const uint kCGMouseButtonLeft = 0;
    public const uint kCGMouseButtonRight = 1;
    public const uint kCGMouseButtonCenter = 2;

    public const uint kCGScrollEventUnitPixel = 0;
    public const uint kCGScrollEventUnitLine = 1;

    private static readonly object _stateLock = new();
    private static CGPoint _currentPosition = new(960, 540);
    private static bool _isLeftMouseDown;
    private static bool _isRightMouseDown;
    private static bool _isMiddleMouseDown;
    private static readonly HashSet<ushort> _pressedKeys = new();

    public static CGPoint CurrentPosition
    {
        get { lock (_stateLock) return _currentPosition; }
        set { lock (_stateLock) _currentPosition = value; }
    }

    public static bool IsLeftMouseDown
    {
        get { lock (_stateLock) return _isLeftMouseDown; }
    }

    public static bool IsRightMouseDown
    {
        get { lock (_stateLock) return _isRightMouseDown; }
    }

    public static bool IsMiddleMouseDown
    {
        get { lock (_stateLock) return _isMiddleMouseDown; }
    }

    public static IReadOnlyCollection<ushort> ActivePressedKeys
    {
        get { lock (_stateLock) return new List<ushort>(_pressedKeys); }
    }

    private bool _disposed;
    private PadState _lastState = PadState.Neutral;
    private bool _lastTouchpadClicked;

#pragma warning disable CS0067
    public event EventHandler<RumbleEventArgs>? RumbleReceived;
#pragma warning restore CS0067

    public void Connect(int slot)
    {
        // No driver connection needed on macOS
    }

    public void Submit(int slot, in PadState state)
    {
        if (_disposed || slot != 0) return;

        // 1. Mouse cursor movement from Right Stick
        int dx = state.ThumbRX / 1600;
        int dy = -state.ThumbRY / 1600;
        if (dx != 0 || dy != 0)
        {
            MouseMove((short)dx, (short)dy);
        }

        // 2. Mouse Clicks from Triggers (RT = Left Click, LT = Right Click)
        bool rtDown = state.RightTrigger > 128;
        bool lastRtDown = _lastState.RightTrigger > 128;
        if (rtDown != lastRtDown)
        {
            MouseButton(1, rtDown);
        }

        bool ltDown = state.LeftTrigger > 128;
        bool lastLtDown = _lastState.LeftTrigger > 128;
        if (ltDown != lastLtDown)
        {
            MouseButton(2, ltDown);
        }

        // 3. WASD Movement from Left Stick
        UpdateKey(state.ThumbLY > 10000, _lastState.ThumbLY > 10000, MacKeyCodes.W);
        UpdateKey(state.ThumbLY < -10000, _lastState.ThumbLY < -10000, MacKeyCodes.S);
        UpdateKey(state.ThumbLX < -10000, _lastState.ThumbLX < -10000, MacKeyCodes.A);
        UpdateKey(state.ThumbLX > 10000, _lastState.ThumbLX > 10000, MacKeyCodes.D);

        // 4. Action Keys
        UpdateKey(state.IsButtonPressed(Protocol.Buttons.A), _lastState.IsButtonPressed(Protocol.Buttons.A), MacKeyCodes.Space);
        UpdateKey(state.IsButtonPressed(Protocol.Buttons.X), _lastState.IsButtonPressed(Protocol.Buttons.X), MacKeyCodes.R);
        UpdateKey(state.IsButtonPressed(Protocol.Buttons.Y), _lastState.IsButtonPressed(Protocol.Buttons.Y), MacKeyCodes.E);
        UpdateKey(state.IsButtonPressed(Protocol.Buttons.B), _lastState.IsButtonPressed(Protocol.Buttons.B), MacKeyCodes.C);
        UpdateKey(state.IsButtonPressed(Protocol.Buttons.LeftShoulder), _lastState.IsButtonPressed(Protocol.Buttons.LeftShoulder), MacKeyCodes.Shift);
        UpdateKey(state.IsButtonPressed(Protocol.Buttons.LeftThumb), _lastState.IsButtonPressed(Protocol.Buttons.LeftThumb), MacKeyCodes.Control);
        UpdateKey(state.IsButtonPressed(Protocol.Buttons.Start), _lastState.IsButtonPressed(Protocol.Buttons.Start), MacKeyCodes.Escape);

        _lastState = state;
    }

    public void SubmitTouchpad(int slot, in TouchpadState state)
    {
        if (_disposed || slot != 0) return;
        if (state.Clicked != _lastTouchpadClicked)
        {
            _lastTouchpadClicked = state.Clicked;
            MouseButton(1, state.Clicked);
        }
    }

    public void Disconnect(int slot)
    {
        ResetAllKeys();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ResetAllKeys();
    }

    private static void UpdateKey(bool isDown, bool wasDown, ushort macKeyCode)
    {
        if (isDown && !wasDown)
        {
            KeyboardKey(macKeyCode, true);
        }
        else if (!isDown && wasDown)
        {
            KeyboardKey(macKeyCode, false);
        }
    }

    public static void MouseMove(short dx, short dy)
    {
        lock (_stateLock)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                try
                {
                    IntPtr curEvent = CGEventCreate(IntPtr.Zero);
                    if (curEvent != IntPtr.Zero)
                    {
                        var loc = CGEventGetLocation(curEvent);
                        CFRelease(curEvent);
                        _currentPosition = loc;
                    }
                }
                catch { }
            }

            double targetX = Math.Max(0, _currentPosition.X + dx);
            double targetY = Math.Max(0, _currentPosition.Y + dy);
            _currentPosition = new CGPoint(targetX, targetY);

            uint eventType;
            uint mouseBtn = kCGMouseButtonLeft;

            if (_isLeftMouseDown)
            {
                eventType = kCGEventLeftMouseDragged;
            }
            else if (_isRightMouseDown)
            {
                eventType = kCGEventRightMouseDragged;
                mouseBtn = kCGMouseButtonRight;
            }
            else if (_isMiddleMouseDown)
            {
                eventType = kCGEventOtherMouseDragged;
                mouseBtn = kCGMouseButtonCenter;
            }
            else
            {
                eventType = kCGEventMouseMoved;
            }

            PostMouseEvent(eventType, _currentPosition, mouseBtn);
        }
    }

    public static void MouseButton(byte buttonMask, bool isDown)
    {
        lock (_stateLock)
        {
            // Left Button (1)
            if ((buttonMask & 1) != 0)
            {
                _isLeftMouseDown = isDown;
                uint type = isDown ? kCGEventLeftMouseDown : kCGEventLeftMouseUp;
                PostMouseEvent(type, _currentPosition, kCGMouseButtonLeft);
            }

            // Right Button (2)
            if ((buttonMask & 2) != 0)
            {
                _isRightMouseDown = isDown;
                uint type = isDown ? kCGEventRightMouseDown : kCGEventRightMouseUp;
                PostMouseEvent(type, _currentPosition, kCGMouseButtonRight);
            }

            // Middle Button (4)
            if ((buttonMask & 4) != 0)
            {
                _isMiddleMouseDown = isDown;
                uint type = isDown ? kCGEventOtherMouseDown : kCGEventOtherMouseUp;
                PostMouseEvent(type, _currentPosition, kCGMouseButtonCenter);
            }
        }
    }

    public static void MouseWheel(short delta)
    {
        if (delta == 0) return;
        int lines = delta / 120;
        if (lines == 0) lines = delta > 0 ? 1 : -1;
        PostScrollWheelEvent(lines);
    }

    public static void KeyboardKey(ushort keyOrVk, bool isDown, bool isVk = false)
    {
        ushort macKey = isVk ? WindowsVkToMacKeyCode(keyOrVk) : keyOrVk;

        lock (_stateLock)
        {
            if (isDown)
            {
                _pressedKeys.Add(macKey);
            }
            else
            {
                _pressedKeys.Remove(macKey);
            }
        }

        PostKeyEvent(macKey, isDown);
    }

    public static void ResetAllKeys()
    {
        lock (_stateLock)
        {
            foreach (ushort key in new List<ushort>(_pressedKeys))
            {
                PostKeyEvent(key, false);
            }
            _pressedKeys.Clear();

            if (_isLeftMouseDown)
            {
                _isLeftMouseDown = false;
                PostMouseEvent(kCGEventLeftMouseUp, _currentPosition, kCGMouseButtonLeft);
            }
            if (_isRightMouseDown)
            {
                _isRightMouseDown = false;
                PostMouseEvent(kCGEventRightMouseUp, _currentPosition, kCGMouseButtonRight);
            }
            if (_isMiddleMouseDown)
            {
                _isMiddleMouseDown = false;
                PostMouseEvent(kCGEventOtherMouseUp, _currentPosition, kCGMouseButtonCenter);
            }
        }
    }

    public static void PostMouseEvent(uint eventType, CGPoint position, uint mouseButton)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return;

        try
        {
            IntPtr ev = CGEventCreateMouseEvent(IntPtr.Zero, eventType, position, mouseButton);
            if (ev != IntPtr.Zero)
            {
                CGEventPost(kCGHIDEventTap, ev);
                CFRelease(ev);
            }
        }
        catch { }
    }

    public static void PostKeyEvent(ushort macKeyCode, bool isDown)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return;

        try
        {
            IntPtr ev = CGEventCreateKeyboardEvent(IntPtr.Zero, macKeyCode, isDown);
            if (ev != IntPtr.Zero)
            {
                CGEventPost(kCGHIDEventTap, ev);
                CFRelease(ev);
            }
        }
        catch { }
    }

    public static void PostScrollWheelEvent(int lines)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return;

        try
        {
            IntPtr ev = CGEventCreateScrollWheelEvent(IntPtr.Zero, kCGScrollEventUnitLine, 1, lines);
            if (ev != IntPtr.Zero)
            {
                CGEventPost(kCGHIDEventTap, ev);
                CFRelease(ev);
            }
        }
        catch { }
    }

    public static ushort WindowsVkToMacKeyCode(ushort vkCode)
    {
        return vkCode switch
        {
            0x08 => MacKeyCodes.Delete,
            0x09 => MacKeyCodes.Tab,
            0x0D => MacKeyCodes.Return,
            0x10 or 0xA0 => MacKeyCodes.Shift,
            0xA1 => MacKeyCodes.RightShift,
            0x11 or 0xA2 => MacKeyCodes.Control,
            0xA3 => MacKeyCodes.RightControl,
            0x12 or 0xA4 => MacKeyCodes.Option,
            0xA5 => MacKeyCodes.RightOption,
            0x14 => MacKeyCodes.CapsLock,
            0x1B => MacKeyCodes.Escape,
            0x20 => MacKeyCodes.Space,
            0x21 => MacKeyCodes.PageUp,
            0x22 => MacKeyCodes.PageDown,
            0x23 => MacKeyCodes.End,
            0x24 => MacKeyCodes.Home,
            0x25 => MacKeyCodes.LeftArrow,
            0x26 => MacKeyCodes.UpArrow,
            0x27 => MacKeyCodes.RightArrow,
            0x28 => MacKeyCodes.DownArrow,
            0x2D => MacKeyCodes.Help,
            0x2E => MacKeyCodes.ForwardDelete,
            0x30 => MacKeyCodes.Zero,
            0x31 => MacKeyCodes.One,
            0x32 => MacKeyCodes.Two,
            0x33 => MacKeyCodes.Three,
            0x34 => MacKeyCodes.Four,
            0x35 => MacKeyCodes.Five,
            0x36 => MacKeyCodes.Six,
            0x37 => MacKeyCodes.Seven,
            0x38 => MacKeyCodes.Eight,
            0x39 => MacKeyCodes.Nine,
            0x41 => MacKeyCodes.A,
            0x42 => MacKeyCodes.B,
            0x43 => MacKeyCodes.C,
            0x44 => MacKeyCodes.D,
            0x45 => MacKeyCodes.E,
            0x46 => MacKeyCodes.F,
            0x47 => MacKeyCodes.G,
            0x48 => MacKeyCodes.H,
            0x49 => MacKeyCodes.I,
            0x4A => MacKeyCodes.J,
            0x4B => MacKeyCodes.K,
            0x4C => MacKeyCodes.L,
            0x4D => MacKeyCodes.M,
            0x4E => MacKeyCodes.N,
            0x4F => MacKeyCodes.O,
            0x50 => MacKeyCodes.P,
            0x51 => MacKeyCodes.Q,
            0x52 => MacKeyCodes.R,
            0x53 => MacKeyCodes.S,
            0x54 => MacKeyCodes.T,
            0x55 => MacKeyCodes.U,
            0x56 => MacKeyCodes.V,
            0x57 => MacKeyCodes.W,
            0x58 => MacKeyCodes.X,
            0x59 => MacKeyCodes.Y,
            0x5A => MacKeyCodes.Z,
            0x5B or 0x5C => MacKeyCodes.Command,
            0x70 => MacKeyCodes.F1,
            0x71 => MacKeyCodes.F2,
            0x72 => MacKeyCodes.F3,
            0x73 => MacKeyCodes.F4,
            0x74 => MacKeyCodes.F5,
            0x75 => MacKeyCodes.F6,
            0x76 => MacKeyCodes.F7,
            0x77 => MacKeyCodes.F8,
            0x78 => MacKeyCodes.F9,
            0x79 => MacKeyCodes.F10,
            0x7A => MacKeyCodes.F11,
            0x7B => MacKeyCodes.F12,
            _ => (ushort)vkCode
        };
    }

    #region CoreGraphics & CoreFoundation P/Invoke
    [DllImport(CoreGraphicsLib)]
    private static extern IntPtr CGEventCreateMouseEvent(
        IntPtr source,
        uint mouseType,
        CGPoint mouseCursorPosition,
        uint mouseButton);

    [DllImport(CoreGraphicsLib)]
    private static extern IntPtr CGEventCreateKeyboardEvent(
        IntPtr source,
        ushort virtualKey,
        [MarshalAs(UnmanagedType.I1)] bool keyDown);

    [DllImport(CoreGraphicsLib)]
    private static extern IntPtr CGEventCreateScrollWheelEvent(
        IntPtr source,
        uint units,
        uint wheelCount,
        int wheel1);

    [DllImport(CoreGraphicsLib)]
    private static extern void CGEventPost(
        uint tap,
        IntPtr @event);

    [DllImport(CoreGraphicsLib)]
    private static extern IntPtr CGEventCreate(IntPtr source);

    [DllImport(CoreGraphicsLib)]
    private static extern CGPoint CGEventGetLocation(IntPtr @event);

    [DllImport(CoreFoundationLib)]
    private static extern void CFRelease(IntPtr cf);
    #endregion
}
