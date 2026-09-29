using System;
using System.Runtime.InteropServices;
using OmniPadServer.Core;

namespace OmniPadServer.ViGEm;

/// <summary>
/// Driverless fallback backend using standard Windows SendInput API.
/// Allows any Windows PC (even without admin rights or ViGEmBus) to be controlled
/// via WASD + Mouse Look + Click or Trackpad mode.
/// </summary>
public sealed class VirtualMouseKeyboardBackend : IPadBackend
{
    private bool _disposed;
    private PadState _lastState = PadState.Neutral;
    private bool _lastTouchpadClicked;

#pragma warning disable CS0067
    public event EventHandler<RumbleEventArgs>? RumbleReceived;
#pragma warning restore CS0067

    public void Connect(int slot)
    {
        // No driver connection needed
    }

    public void Submit(int slot, in PadState state)
    {
        if (_disposed || slot != 0) return; // KBM mode currently maps player 1

        // 1. Mouse movement from Right Stick (or Gyro mapped to RX/RY)
        int dx = state.ThumbRX / 1600; // sensitivity divisor
        int dy = -state.ThumbRY / 1600; // positive up on controller -> negative dy on screen
        if (dx != 0 || dy != 0)
        {
            SendMouseMove(dx, dy);
        }

        // 2. Mouse Clicks from Triggers (RT = Left Click, LT = Right Click)
        bool rtDown = state.RightTrigger > 128;
        bool lastRtDown = _lastState.RightTrigger > 128;
        if (rtDown != lastRtDown)
        {
            SendMouseButton(rtDown, MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP);
        }

        bool ltDown = state.LeftTrigger > 128;
        bool lastLtDown = _lastState.LeftTrigger > 128;
        if (ltDown != lastLtDown)
        {
            SendMouseButton(ltDown, MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP);
        }

        // 3. WASD Movement from Left Stick
        UpdateKey(state.ThumbLY > 10000, _lastState.ThumbLY > 10000, VK_W);
        UpdateKey(state.ThumbLY < -10000, _lastState.ThumbLY < -10000, VK_S);
        UpdateKey(state.ThumbLX < -10000, _lastState.ThumbLX < -10000, VK_A);
        UpdateKey(state.ThumbLX > 10000, _lastState.ThumbLX > 10000, VK_D);

        // 4. Action Keys
        UpdateKey(state.IsButtonPressed(Protocol.Buttons.A), _lastState.IsButtonPressed(Protocol.Buttons.A), VK_SPACE);
        UpdateKey(state.IsButtonPressed(Protocol.Buttons.X), _lastState.IsButtonPressed(Protocol.Buttons.X), VK_R);
        UpdateKey(state.IsButtonPressed(Protocol.Buttons.Y), _lastState.IsButtonPressed(Protocol.Buttons.Y), VK_E);
        UpdateKey(state.IsButtonPressed(Protocol.Buttons.B), _lastState.IsButtonPressed(Protocol.Buttons.B), VK_C);
        UpdateKey(state.IsButtonPressed(Protocol.Buttons.LeftShoulder), _lastState.IsButtonPressed(Protocol.Buttons.LeftShoulder), VK_SHIFT);
        UpdateKey(state.IsButtonPressed(Protocol.Buttons.LeftThumb), _lastState.IsButtonPressed(Protocol.Buttons.LeftThumb), VK_CONTROL);
        UpdateKey(state.IsButtonPressed(Protocol.Buttons.Start), _lastState.IsButtonPressed(Protocol.Buttons.Start), VK_ESCAPE);

        _lastState = state;
    }

    public void Disconnect(int slot)
    {
        // Release all pressed keys
        ResetAllKeys();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ResetAllKeys();
    }

    private void ResetAllKeys()
    {
        UpdateKey(false, true, VK_W);
        UpdateKey(false, true, VK_A);
        UpdateKey(false, true, VK_S);
        UpdateKey(false, true, VK_D);
        UpdateKey(false, true, VK_SPACE);
        UpdateKey(false, true, VK_R);
        UpdateKey(false, true, VK_E);
        UpdateKey(false, true, VK_C);
        UpdateKey(false, true, VK_SHIFT);
        UpdateKey(false, true, VK_CONTROL);
        UpdateKey(false, true, VK_ESCAPE);
        SendMouseButton(false, MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP);
        SendMouseButton(false, MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP);
        if (_lastTouchpadClicked)
        {
            _lastTouchpadClicked = false;
            SendMouseButton(false, MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP);
        }
    }

    private static void UpdateKey(bool isDown, bool wasDown, ushort vk)
    {
        if (isDown && !wasDown)
            SendKey(vk, false);
        else if (!isDown && wasDown)
            SendKey(vk, true);
    }

    public void SubmitTouchpad(int slot, in TouchpadState state)
    {
        if (_disposed || slot != 0) return;
        if (state.Clicked != _lastTouchpadClicked)
        {
            _lastTouchpadClicked = state.Clicked;
            SendMouseButton(state.Clicked, MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP);
        }
    }

    #region Win32 SendInput P/Invoke

    private const int INPUT_MOUSE = 0;
    private const int INPUT_KEYBOARD = 1;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private const ushort VK_W = 0x57;
    private const ushort VK_A = 0x41;
    private const ushort VK_S = 0x53;
    private const ushort VK_D = 0x44;
    private const ushort VK_SPACE = 0x20;
    private const ushort VK_R = 0x52;
    private const ushort VK_E = 0x45;
    private const ushort VK_C = 0x43;
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_ESCAPE = 0x1B;

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUT_UNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public INPUT_UNION u;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, [MarshalAs(UnmanagedType.LPArray), In] INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyW(uint uCode, uint uMapType);

    private static void SendMouseMove(int dx, int dy)
    {
        INPUT[] inputs = new INPUT[1];
        inputs[0].type = INPUT_MOUSE;
        inputs[0].u.mi.dx = dx;
        inputs[0].u.mi.dy = dy;
        inputs[0].u.mi.dwFlags = MOUSEEVENTF_MOVE;
        SendInput(1, inputs, Marshal.SizeOf<INPUT>());
    }

    private static void SendMouseButton(bool isDown, uint downFlag, uint upFlag)
    {
        INPUT[] inputs = new INPUT[1];
        inputs[0].type = INPUT_MOUSE;
        inputs[0].u.mi.dwFlags = isDown ? downFlag : upFlag;
        SendInput(1, inputs, Marshal.SizeOf<INPUT>());
    }

    private static void SendKey(ushort vk, bool isUp)
    {
        uint scan = MapVirtualKeyW(vk, 0); // MAPVK_VK_TO_VSC = 0
        uint flags = isUp ? KEYEVENTF_KEYUP : 0;
        if (vk is >= 0x21 and <= 0x28 or 0x2D or 0x2E or 0x5B or 0x5C or 0xA3 or 0xA5)
        {
            flags |= KEYEVENTF_EXTENDEDKEY;
        }

        INPUT[] inputs = new INPUT[1];
        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].u.ki.wVk = vk;
        inputs[0].u.ki.wScan = (ushort)scan;
        inputs[0].u.ki.dwFlags = flags;
        SendInput(1, inputs, Marshal.SizeOf<INPUT>());
    }

    #endregion
}
