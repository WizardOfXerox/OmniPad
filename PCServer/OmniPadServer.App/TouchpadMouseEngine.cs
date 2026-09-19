using System;
using System.Runtime.InteropServices;
using OmniPadServer.Core;

namespace OmniPadServer.App;

/// <summary>
/// Translates PS4/PS5 touchpad multi-touch coordinates into native Windows mouse actions
/// (pointer movement, two-finger wheel scrolling, left/right clicks) via SendInput.
/// </summary>
public sealed class TouchpadMouseEngine
{
    public bool Enabled { get; set; } = false;
    public float PointerSensitivity { get; set; } = 1.6f;
    public float ScrollSensitivity { get; set; } = 1.0f;

    private bool _prevF0Active;
    private int _prevF0X;
    private int _prevF0Y;
    private DateTime _f0DownTime;
    private int _f0TotalDistance;

    private bool _prevF1Active;
    private int _prevF1Y;
    private DateTime _f1DownTime;

    private float _accumulatedScrollDelta;
    private bool _isLeftMouseDown;
    private bool _isRightMouseDown;

    public void ProcessTouchpad(in TouchpadState touch)
    {
        if (!Enabled) return;

        var now = DateTime.UtcNow;

        // 1. Two-finger mode: Scrolling
        if (touch.Finger0.IsActive && touch.Finger1.IsActive)
        {
            if (_prevF0Active && _prevF1Active)
            {
                int dy0 = touch.Finger0.Y - _prevF0Y;
                int dy1 = touch.Finger1.Y - _prevF1Y;
                float avgDy = (dy0 + dy1) / 2.0f;

                // Touch coordinate Y increases downwards.
                // Swiping UP on trackpad (avgDy < 0) should scroll UP or Natural Scroll.
                // Standard Windows scroll: negative dy = scroll down (wheel -120), positive dy = scroll up (wheel +120).
                _accumulatedScrollDelta += -avgDy * ScrollSensitivity * 4.0f;

                while (_accumulatedScrollDelta >= 60f)
                {
                    SendMouseWheel(120);
                    _accumulatedScrollDelta -= 60f;
                }
                while (_accumulatedScrollDelta <= -60f)
                {
                    SendMouseWheel(-120);
                    _accumulatedScrollDelta += 60f;
                }
            }

            _prevF0Active = true;
            _prevF0X = touch.Finger0.X;
            _prevF0Y = touch.Finger0.Y;

            _prevF1Active = true;
            _prevF1Y = touch.Finger1.Y;
            return;
        }

        // Two-finger tap check on release
        if (_prevF0Active && _prevF1Active && (!touch.Finger0.IsActive || !touch.Finger1.IsActive))
        {
            var holdDuration = (now - _f1DownTime).TotalMilliseconds;
            if (holdDuration < 300)
            {
                // Two-finger tap = Right click
                SendRightClick();
            }
        }

        // 2. Single-finger mode: Pointer Movement
        if (touch.Finger0.IsActive)
        {
            if (!_prevF0Active)
            {
                // Finger down
                _prevF0Active = true;
                _prevF0X = touch.Finger0.X;
                _prevF0Y = touch.Finger0.Y;
                _f0DownTime = now;
                _f0TotalDistance = 0;
            }
            else
            {
                int dx = touch.Finger0.X - _prevF0X;
                int dy = touch.Finger0.Y - _prevF0Y;
                _f0TotalDistance += Math.Abs(dx) + Math.Abs(dy);

                _prevF0X = touch.Finger0.X;
                _prevF0Y = touch.Finger0.Y;

                // Move cursor
                int moveX = (int)MathF.Round(dx * PointerSensitivity);
                int moveY = (int)MathF.Round(dy * PointerSensitivity);

                if (moveX != 0 || moveY != 0)
                {
                    SendMouseMove(moveX, moveY);
                }
            }
        }
        else
        {
            if (_prevF0Active)
            {
                // Finger 0 released
                _prevF0Active = false;
                var holdMs = (now - _f0DownTime).TotalMilliseconds;

                // Tap-to-click (< 250ms, < 25 total movement units)
                if (holdMs < 250 && _f0TotalDistance < 25)
                {
                    SendLeftClick();
                }
            }
        }

        // 3. Physical Trackpad Click Handling
        if (touch.Clicked)
        {
            if (!_isLeftMouseDown && !_isRightMouseDown)
            {
                // Bottom-right corner (X > 1400, Y > 650 out of 1920x942) -> Right click
                if (touch.Finger0.IsActive && touch.Finger0.X > 1400 && touch.Finger0.Y > 650)
                {
                    SendButtonEvent(MOUSEEVENTF_RIGHTDOWN);
                    _isRightMouseDown = true;
                }
                else
                {
                    SendButtonEvent(MOUSEEVENTF_LEFTDOWN);
                    _isLeftMouseDown = true;
                }
            }
        }
        else
        {
            if (_isLeftMouseDown)
            {
                SendButtonEvent(MOUSEEVENTF_LEFTUP);
                _isLeftMouseDown = false;
            }
            if (_isRightMouseDown)
            {
                SendButtonEvent(MOUSEEVENTF_RIGHTUP);
                _isRightMouseDown = false;
            }
        }

        if (touch.Finger1.IsActive && !_prevF1Active)
        {
            _prevF1Active = true;
            _prevF1Y = touch.Finger1.Y;
            _f1DownTime = now;
        }
        else if (!touch.Finger1.IsActive)
        {
            _prevF1Active = false;
        }
    }

    private static void SendMouseMove(int dx, int dy)
    {
        if (!OperatingSystem.IsWindows()) return;

        INPUT input = new()
        {
            type = INPUT_MOUSE,
            mi = new MOUSEINPUT
            {
                dx = dx,
                dy = dy,
                dwFlags = MOUSEEVENTF_MOVE,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            }
        };

        SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    private static void SendButtonEvent(uint flags)
    {
        if (!OperatingSystem.IsWindows()) return;

        INPUT input = new()
        {
            type = INPUT_MOUSE,
            mi = new MOUSEINPUT
            {
                dwFlags = flags,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            }
        };

        SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    private static void SendLeftClick()
    {
        if (!OperatingSystem.IsWindows()) return;

        INPUT[] inputs =
        [
            new INPUT { type = INPUT_MOUSE, mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_LEFTDOWN } },
            new INPUT { type = INPUT_MOUSE, mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_LEFTUP } }
        ];

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static void SendRightClick()
    {
        if (!OperatingSystem.IsWindows()) return;

        INPUT[] inputs =
        [
            new INPUT { type = INPUT_MOUSE, mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_RIGHTDOWN } },
            new INPUT { type = INPUT_MOUSE, mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_RIGHTUP } }
        ];

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static void SendMouseWheel(int delta)
    {
        if (!OperatingSystem.IsWindows()) return;

        INPUT input = new()
        {
            type = INPUT_MOUSE,
            mi = new MOUSEINPUT
            {
                dwFlags = MOUSEEVENTF_WHEEL,
                mouseData = (uint)delta,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            }
        };

        SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    #region Win32 P/Invoke
    private const uint INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public MOUSEINPUT mi;
    }

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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, [In] INPUT[] pInputs, int cbSize);
    #endregion
}
