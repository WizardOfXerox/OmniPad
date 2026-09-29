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
                    SendButtonEvent(true, true);
                    _isRightMouseDown = true;
                }
                else
                {
                    SendButtonEvent(false, true);
                    _isLeftMouseDown = true;
                }
            }
        }
        else
        {
            if (_isLeftMouseDown)
            {
                SendButtonEvent(false, false);
                _isLeftMouseDown = false;
            }
            if (_isRightMouseDown)
            {
                SendButtonEvent(true, false);
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
        WindowsInputSimulator.MouseMove((short)dx, (short)dy);
    }

    private static void SendButtonEvent(bool isRight, bool isDown)
    {
        WindowsInputSimulator.MouseButton((byte)(isRight ? 2 : 1), isDown);
    }

    private static void SendLeftClick()
    {
        WindowsInputSimulator.MouseButton(1, true);
        WindowsInputSimulator.MouseButton(1, false);
    }

    private static void SendRightClick()
    {
        WindowsInputSimulator.MouseButton(2, true);
        WindowsInputSimulator.MouseButton(2, false);
    }

    private static void SendMouseWheel(int delta)
    {
        WindowsInputSimulator.MouseWheel((short)delta);
    }
}
