using System;
using System.Collections.Generic;
using OmniPadServer.Core;

namespace OmniPadServer.ViGEm;

/// <summary>
/// Virtual keyboard and mouse backend for Linux hosts using native /dev/uinput.
/// Emulates an authentic USB keyboard and mouse for zero-driver fallback mode and direct web/trackpad control.
/// </summary>
public sealed class LinuxUinputMouseKeyboardBackend : IPadBackend
{
    private static readonly object _staticLock = new();
    private static LinuxUinputMouseKeyboardBackend? _defaultInstance;

    private readonly IUinputNativeBridge _bridge;
    private readonly object _lock = new();
    private int _fd = -1;
    private bool _disposed;
    private PadState _lastState = PadState.Neutral;
    private bool _lastTouchpadClicked;

#pragma warning disable CS0067
    public event EventHandler<RumbleEventArgs>? RumbleReceived;
#pragma warning restore CS0067

    public LinuxUinputMouseKeyboardBackend(IUinputNativeBridge? bridge = null)
    {
        _bridge = bridge ?? new LibcUinputNativeBridge();
        EnsureDeviceCreated();
    }

    public static LinuxUinputMouseKeyboardBackend GetDefaultInstance()
    {
        if (_defaultInstance != null) return _defaultInstance;
        lock (_staticLock)
        {
            _defaultInstance ??= new LinuxUinputMouseKeyboardBackend();
            return _defaultInstance;
        }
    }

    private void EnsureDeviceCreated()
    {
        lock (_lock)
        {
            if (_fd >= 0) return;

            int fd = _bridge.Open("/dev/uinput", LinuxUinputConstants.O_WRONLY | LinuxUinputConstants.O_NONBLOCK);
            if (fd < 0)
            {
                fd = _bridge.Open("/dev/input/uinput", LinuxUinputConstants.O_WRONLY | LinuxUinputConstants.O_NONBLOCK);
            }

            if (fd < 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[LinuxUinputKBM] Error: Cannot open /dev/uinput. " +
                                  "Ensure user belongs to 'input' group: sudo usermod -aG input $USER");
                Console.ResetColor();
                return;
            }

            // 1. Enable Event types
            _bridge.Ioctl(fd, LinuxUinputConstants.UI_SET_EVBIT, LinuxUinputConstants.EV_SYN);
            _bridge.Ioctl(fd, LinuxUinputConstants.UI_SET_EVBIT, LinuxUinputConstants.EV_KEY);
            _bridge.Ioctl(fd, LinuxUinputConstants.UI_SET_EVBIT, LinuxUinputConstants.EV_REL);

            // 2. Enable Mouse Relative Axes
            _bridge.Ioctl(fd, LinuxUinputConstants.UI_SET_RELBIT, LinuxUinputConstants.REL_X);
            _bridge.Ioctl(fd, LinuxUinputConstants.UI_SET_RELBIT, LinuxUinputConstants.REL_Y);
            _bridge.Ioctl(fd, LinuxUinputConstants.UI_SET_RELBIT, LinuxUinputConstants.REL_WHEEL);
            _bridge.Ioctl(fd, LinuxUinputConstants.UI_SET_RELBIT, LinuxUinputConstants.REL_HWHEEL);

            // 3. Enable Mouse Buttons
            _bridge.Ioctl(fd, LinuxUinputConstants.UI_SET_KEYBIT, LinuxUinputConstants.BTN_LEFT);
            _bridge.Ioctl(fd, LinuxUinputConstants.UI_SET_KEYBIT, LinuxUinputConstants.BTN_RIGHT);
            _bridge.Ioctl(fd, LinuxUinputConstants.UI_SET_KEYBIT, LinuxUinputConstants.BTN_MIDDLE);

            // 4. Enable All Keyboard Keys
            for (ushort k = 1; k <= 248; k++)
            {
                _bridge.Ioctl(fd, LinuxUinputConstants.UI_SET_KEYBIT, k);
            }

            // 5. Setup Device Information
            var userDev = new UinputUserDev();
            userDev.SetName("OmniPad Virtual Keyboard and Mouse");
            userDev.Id = new InputId
            {
                Bustype = LinuxUinputConstants.BUS_USB,
                Vendor = 0x1234,
                Product = 0x5678,
                Version = 1
            };

            _bridge.WriteUserDev(fd, in userDev);
            _bridge.Ioctl(fd, LinuxUinputConstants.UI_DEV_CREATE, 0);

            _fd = fd;
        }
    }

    public void Connect(int slot)
    {
        EnsureDeviceCreated();
    }

    public void Submit(int slot, in PadState state)
    {
        if (_disposed || slot != 0) return; // Player 1 maps to KBM
        EnsureDeviceCreated();

        // 1. Mouse movement from Right Stick
        int dx = state.ThumbRX / 1600;
        int dy = -state.ThumbRY / 1600;
        if (dx != 0 || dy != 0)
        {
            InternalSendMouseMove(dx, dy);
        }

        // 2. Mouse Clicks from Triggers (RT = Left Click, LT = Right Click)
        bool rtDown = state.RightTrigger > 128;
        bool lastRtDown = _lastState.RightTrigger > 128;
        if (rtDown != lastRtDown)
        {
            InternalSendMouseButtonEvent(LinuxUinputConstants.BTN_LEFT, rtDown);
        }

        bool ltDown = state.LeftTrigger > 128;
        bool lastLtDown = _lastState.LeftTrigger > 128;
        if (ltDown != lastLtDown)
        {
            InternalSendMouseButtonEvent(LinuxUinputConstants.BTN_RIGHT, ltDown);
        }

        // 3. WASD Movement from Left Stick
        UpdateKey(state.ThumbLY > 10000, _lastState.ThumbLY > 10000, LinuxKeyCodes.KEY_W);
        UpdateKey(state.ThumbLY < -10000, _lastState.ThumbLY < -10000, LinuxKeyCodes.KEY_S);
        UpdateKey(state.ThumbLX < -10000, _lastState.ThumbLX < -10000, LinuxKeyCodes.KEY_A);
        UpdateKey(state.ThumbLX > 10000, _lastState.ThumbLX > 10000, LinuxKeyCodes.KEY_D);

        // 4. Action Keys
        UpdateKey(state.IsButtonPressed(Protocol.Buttons.A), _lastState.IsButtonPressed(Protocol.Buttons.A), LinuxKeyCodes.KEY_SPACE);
        UpdateKey(state.IsButtonPressed(Protocol.Buttons.X), _lastState.IsButtonPressed(Protocol.Buttons.X), LinuxKeyCodes.KEY_R);
        UpdateKey(state.IsButtonPressed(Protocol.Buttons.Y), _lastState.IsButtonPressed(Protocol.Buttons.Y), LinuxKeyCodes.KEY_E);
        UpdateKey(state.IsButtonPressed(Protocol.Buttons.B), _lastState.IsButtonPressed(Protocol.Buttons.B), LinuxKeyCodes.KEY_C);
        UpdateKey(state.IsButtonPressed(Protocol.Buttons.LeftShoulder), _lastState.IsButtonPressed(Protocol.Buttons.LeftShoulder), LinuxKeyCodes.KEY_LEFTSHIFT);
        UpdateKey(state.IsButtonPressed(Protocol.Buttons.LeftThumb), _lastState.IsButtonPressed(Protocol.Buttons.LeftThumb), LinuxKeyCodes.KEY_LEFTCTRL);
        UpdateKey(state.IsButtonPressed(Protocol.Buttons.Start), _lastState.IsButtonPressed(Protocol.Buttons.Start), LinuxKeyCodes.KEY_ESC);

        _lastState = state;
    }

    public void SubmitTouchpad(int slot, in TouchpadState state)
    {
        if (_disposed || slot != 0) return;
        if (state.Clicked != _lastTouchpadClicked)
        {
            _lastTouchpadClicked = state.Clicked;
            InternalSendMouseButtonEvent(LinuxUinputConstants.BTN_LEFT, state.Clicked);
        }
    }

    public void Disconnect(int slot)
    {
        ResetAllKeys();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            ResetAllKeys();

            if (_fd >= 0)
            {
                try
                {
                    _bridge.Ioctl(_fd, LinuxUinputConstants.UI_DEV_DESTROY, 0);
                    _bridge.Close(_fd);
                }
                catch { }
                _fd = -1;
            }
        }
    }

    private void UpdateKey(bool isDown, bool wasDown, ushort key)
    {
        if (isDown && !wasDown)
            InternalSendKeyEvent(key, true);
        else if (!isDown && wasDown)
            InternalSendKeyEvent(key, false);
    }

    private void ResetAllKeys()
    {
        InternalSendKeyEvent(LinuxKeyCodes.KEY_W, false);
        InternalSendKeyEvent(LinuxKeyCodes.KEY_A, false);
        InternalSendKeyEvent(LinuxKeyCodes.KEY_S, false);
        InternalSendKeyEvent(LinuxKeyCodes.KEY_D, false);
        InternalSendKeyEvent(LinuxKeyCodes.KEY_SPACE, false);
        InternalSendKeyEvent(LinuxKeyCodes.KEY_R, false);
        InternalSendKeyEvent(LinuxKeyCodes.KEY_E, false);
        InternalSendKeyEvent(LinuxKeyCodes.KEY_C, false);
        InternalSendKeyEvent(LinuxKeyCodes.KEY_LEFTSHIFT, false);
        InternalSendKeyEvent(LinuxKeyCodes.KEY_LEFTCTRL, false);
        InternalSendKeyEvent(LinuxKeyCodes.KEY_ESC, false);
        InternalSendMouseButtonEvent(LinuxUinputConstants.BTN_LEFT, false);
        InternalSendMouseButtonEvent(LinuxUinputConstants.BTN_RIGHT, false);
    }

    #region Direct Virtual Input (Static & Instance)

    public static void SendMouseMove(int dx, int dy) => GetDefaultInstance().InternalSendMouseMove(dx, dy);

    public static void SendMouseButton(byte buttonMask, bool isDown) => GetDefaultInstance().InternalSendMouseButton(buttonMask, isDown);

    public static void SendMouseWheel(int delta) => GetDefaultInstance().InternalSendMouseWheel(delta);

    public static void SendKeyboardKey(ushort vkCode, bool isDown) => GetDefaultInstance().InternalSendKeyboardKey(vkCode, isDown);

    public void InternalSendMouseMove(int dx, int dy)
    {
        lock (_lock)
        {
            if (_disposed || _fd < 0) return;
            Span<InputEvent> events = stackalloc InputEvent[3];
            int count = 0;
            if (dx != 0) events[count++] = new InputEvent(LinuxUinputConstants.EV_REL, LinuxUinputConstants.REL_X, dx);
            if (dy != 0) events[count++] = new InputEvent(LinuxUinputConstants.EV_REL, LinuxUinputConstants.REL_Y, dy);
            events[count++] = new InputEvent(LinuxUinputConstants.EV_SYN, LinuxUinputConstants.SYN_REPORT, 0);
            _bridge.Write(_fd, events.Slice(0, count));
        }
    }

    public void InternalSendMouseButton(byte buttonMask, bool isDown)
    {
        lock (_lock)
        {
            if (_disposed || _fd < 0) return;
            Span<InputEvent> events = stackalloc InputEvent[4];
            int count = 0;

            if ((buttonMask & 1) != 0)
                events[count++] = new InputEvent(LinuxUinputConstants.EV_KEY, LinuxUinputConstants.BTN_LEFT, isDown ? 1 : 0);
            if ((buttonMask & 2) != 0)
                events[count++] = new InputEvent(LinuxUinputConstants.EV_KEY, LinuxUinputConstants.BTN_RIGHT, isDown ? 1 : 0);
            if ((buttonMask & 4) != 0)
                events[count++] = new InputEvent(LinuxUinputConstants.EV_KEY, LinuxUinputConstants.BTN_MIDDLE, isDown ? 1 : 0);

            if (count > 0)
            {
                events[count++] = new InputEvent(LinuxUinputConstants.EV_SYN, LinuxUinputConstants.SYN_REPORT, 0);
                _bridge.Write(_fd, events.Slice(0, count));
            }
        }
    }

    public void InternalSendMouseButtonEvent(ushort buttonCode, bool isDown)
    {
        lock (_lock)
        {
            if (_disposed || _fd < 0) return;
            Span<InputEvent> events = stackalloc InputEvent[2];
            events[0] = new InputEvent(LinuxUinputConstants.EV_KEY, buttonCode, isDown ? 1 : 0);
            events[1] = new InputEvent(LinuxUinputConstants.EV_SYN, LinuxUinputConstants.SYN_REPORT, 0);
            _bridge.Write(_fd, events);
        }
    }

    public void InternalSendMouseWheel(int delta)
    {
        lock (_lock)
        {
            if (_disposed || _fd < 0) return;
            int ticks = delta / 120;
            if (ticks == 0) ticks = delta > 0 ? 1 : -1;

            Span<InputEvent> events = stackalloc InputEvent[2];
            events[0] = new InputEvent(LinuxUinputConstants.EV_REL, LinuxUinputConstants.REL_WHEEL, ticks);
            events[1] = new InputEvent(LinuxUinputConstants.EV_SYN, LinuxUinputConstants.SYN_REPORT, 0);
            _bridge.Write(_fd, events);
        }
    }

    public void InternalSendKeyboardKey(ushort vkCode, bool isDown)
    {
        ushort linuxKey = LinuxKeyCodes.FromVkCode(vkCode);
        if (linuxKey == 0) return;
        InternalSendKeyEvent(linuxKey, isDown);
    }

    public void InternalSendKeyEvent(ushort linuxKey, bool isDown)
    {
        lock (_lock)
        {
            if (_disposed || _fd < 0) return;
            Span<InputEvent> events = stackalloc InputEvent[2];
            events[0] = new InputEvent(LinuxUinputConstants.EV_KEY, linuxKey, isDown ? 1 : 0);
            events[1] = new InputEvent(LinuxUinputConstants.EV_SYN, LinuxUinputConstants.SYN_REPORT, 0);
            _bridge.Write(_fd, events);
        }
    }

    #endregion
}

public static class LinuxKeyCodes
{
    public const ushort KEY_ESC = 1;
    public const ushort KEY_1 = 2;
    public const ushort KEY_2 = 3;
    public const ushort KEY_3 = 4;
    public const ushort KEY_4 = 5;
    public const ushort KEY_5 = 6;
    public const ushort KEY_6 = 7;
    public const ushort KEY_7 = 8;
    public const ushort KEY_8 = 9;
    public const ushort KEY_9 = 10;
    public const ushort KEY_0 = 11;
    public const ushort KEY_BACKSPACE = 14;
    public const ushort KEY_TAB = 15;
    public const ushort KEY_Q = 16;
    public const ushort KEY_W = 17;
    public const ushort KEY_E = 18;
    public const ushort KEY_R = 19;
    public const ushort KEY_T = 20;
    public const ushort KEY_Y = 21;
    public const ushort KEY_U = 22;
    public const ushort KEY_I = 23;
    public const ushort KEY_O = 24;
    public const ushort KEY_P = 25;
    public const ushort KEY_ENTER = 28;
    public const ushort KEY_LEFTCTRL = 29;
    public const ushort KEY_A = 30;
    public const ushort KEY_S = 31;
    public const ushort KEY_D = 32;
    public const ushort KEY_F = 33;
    public const ushort KEY_G = 34;
    public const ushort KEY_H = 35;
    public const ushort KEY_J = 36;
    public const ushort KEY_K = 37;
    public const ushort KEY_L = 38;
    public const ushort KEY_LEFTSHIFT = 42;
    public const ushort KEY_Z = 44;
    public const ushort KEY_X = 45;
    public const ushort KEY_C = 46;
    public const ushort KEY_V = 47;
    public const ushort KEY_B = 48;
    public const ushort KEY_N = 49;
    public const ushort KEY_M = 50;
    public const ushort KEY_LEFTALT = 56;
    public const ushort KEY_SPACE = 57;
    public const ushort KEY_CAPSLOCK = 58;
    public const ushort KEY_F1 = 59;
    public const ushort KEY_F2 = 60;
    public const ushort KEY_F3 = 61;
    public const ushort KEY_F4 = 62;
    public const ushort KEY_F5 = 63;
    public const ushort KEY_F6 = 64;
    public const ushort KEY_F7 = 65;
    public const ushort KEY_F8 = 66;
    public const ushort KEY_F9 = 67;
    public const ushort KEY_F10 = 68;
    public const ushort KEY_RIGHTCTRL = 97;
    public const ushort KEY_RIGHTALT = 100;
    public const ushort KEY_HOME = 102;
    public const ushort KEY_UP = 103;
    public const ushort KEY_PAGEUP = 104;
    public const ushort KEY_LEFT = 105;
    public const ushort KEY_RIGHT = 106;
    public const ushort KEY_END = 107;
    public const ushort KEY_DOWN = 108;
    public const ushort KEY_PAGEDOWN = 109;
    public const ushort KEY_INSERT = 110;
    public const ushort KEY_DELETE = 111;
    public const ushort KEY_F11 = 87;
    public const ushort KEY_F12 = 88;

    public static ushort FromVkCode(ushort vk)
    {
        return vk switch
        {
            0x1B => KEY_ESC,
            0x20 => KEY_SPACE,
            0x0D => KEY_ENTER,
            0x08 => KEY_BACKSPACE,
            0x09 => KEY_TAB,
            0x10 or 0xA0 => KEY_LEFTSHIFT,
            0xA1 => KEY_LEFTSHIFT,
            0x11 or 0xA2 => KEY_LEFTCTRL,
            0xA3 => KEY_RIGHTCTRL,
            0x12 or 0xA4 => KEY_LEFTALT,
            0xA5 => KEY_RIGHTALT,
            0x25 => KEY_LEFT,
            0x26 => KEY_UP,
            0x27 => KEY_RIGHT,
            0x28 => KEY_DOWN,
            0x21 => KEY_PAGEUP,
            0x22 => KEY_PAGEDOWN,
            0x23 => KEY_END,
            0x24 => KEY_HOME,
            0x2D => KEY_INSERT,
            0x2E => KEY_DELETE,
            // Letters A-Z (0x41 - 0x5A)
            0x41 => KEY_A,
            0x42 => KEY_B,
            0x43 => KEY_C,
            0x44 => KEY_D,
            0x45 => KEY_E,
            0x46 => KEY_F,
            0x47 => KEY_G,
            0x48 => KEY_H,
            0x49 => KEY_I,
            0x4A => KEY_J,
            0x4B => KEY_K,
            0x4C => KEY_L,
            0x4D => KEY_M,
            0x4E => KEY_N,
            0x4F => KEY_O,
            0x50 => KEY_P,
            0x51 => KEY_Q,
            0x52 => KEY_R,
            0x53 => KEY_S,
            0x54 => KEY_T,
            0x55 => KEY_U,
            0x56 => KEY_V,
            0x57 => KEY_W,
            0x58 => KEY_X,
            0x59 => KEY_Y,
            0x5A => KEY_Z,
            // Numbers 0-9 (0x30 - 0x39)
            0x30 => KEY_0,
            0x31 => KEY_1,
            0x32 => KEY_2,
            0x33 => KEY_3,
            0x34 => KEY_4,
            0x35 => KEY_5,
            0x36 => KEY_6,
            0x37 => KEY_7,
            0x38 => KEY_8,
            0x39 => KEY_9,
            // Function Keys F1-F12 (0x70 - 0x7B)
            0x70 => KEY_F1,
            0x71 => KEY_F2,
            0x72 => KEY_F3,
            0x73 => KEY_F4,
            0x74 => KEY_F5,
            0x75 => KEY_F6,
            0x76 => KEY_F7,
            0x77 => KEY_F8,
            0x78 => KEY_F9,
            0x79 => KEY_F10,
            0x7A => KEY_F11,
            0x7B => KEY_F12,
            _ => 0
        };
    }
}
