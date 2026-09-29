using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using OmniPadServer.Core;

namespace OmniPadServer.ViGEm;

public enum LinuxPadLayout
{
    Xbox360,
    DualShock4
}

/// <summary>
/// Hardware-grade virtual gamepad backend for Linux hosts using native POSIX /dev/uinput.
/// Emulates authentic Microsoft Xbox 360 controllers and Sony DualShock 4 controllers.
/// </summary>
public sealed class LinuxUinputPadBackend : IPadBackend
{
    private readonly IUinputNativeBridge _bridge;
    private readonly int[] _slotFds = new int[IPadBackend.MaxPads];
    private readonly object _lock = new();
    private LinuxPadLayout _layout;
    private bool _disposed;

#pragma warning disable CS0067 // Event is defined by IPadBackend; Linux uinput FF feedback is pending async read loop
    public event EventHandler<RumbleEventArgs>? RumbleReceived;
#pragma warning restore CS0067

    public LinuxPadLayout Layout => _layout;
    public string ControllerName => _layout == LinuxPadLayout.Xbox360 
        ? "Microsoft X-Box 360 pad" 
        : "Sony Interactive Entertainment Wireless Controller";

    public ushort VendorId => _layout == LinuxPadLayout.Xbox360 ? (ushort)0x045E : (ushort)0x054C;
    public ushort ProductId => _layout == LinuxPadLayout.Xbox360 ? (ushort)0x028E : (ushort)0x05C4;

    public LinuxUinputPadBackend(
        LinuxPadLayout layout = LinuxPadLayout.Xbox360,
        IUinputNativeBridge? bridge = null)
    {
        _layout = layout;
        _bridge = bridge ?? new LibcUinputNativeBridge();
        for (int i = 0; i < _slotFds.Length; i++) _slotFds[i] = -1;
    }

    public void SwitchLayout(LinuxPadLayout newLayout)
    {
        lock (_lock)
        {
            if (_disposed || _layout == newLayout) return;
            _layout = newLayout;

            // Re-create existing slots with the new layout
            for (int slot = 0; slot < IPadBackend.MaxPads; slot++)
            {
                if (_slotFds[slot] >= 0)
                {
                    DestroySlot(slot);
                    CreateSlot(slot);
                }
            }
        }
    }

    public void Connect(int slot)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ValidateSlot(slot);

            if (_slotFds[slot] >= 0) return; // already connected
            CreateSlot(slot);
        }
    }

    private void CreateSlot(int slot)
    {
        int fd = _bridge.Open("/dev/uinput", LinuxUinputConstants.O_WRONLY | LinuxUinputConstants.O_NONBLOCK);
        if (fd < 0)
        {
            fd = _bridge.Open("/dev/input/uinput", LinuxUinputConstants.O_WRONLY | LinuxUinputConstants.O_NONBLOCK);
        }

        if (fd < 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[LinuxUinputPad] Error: Cannot open /dev/uinput for slot {slot}. " +
                              "Ensure kernel module 'uinput' is loaded (modprobe uinput) and user has permissions (e.g. sudo usermod -aG input $USER).");
            Console.ResetColor();
            return;
        }

        // Enable event types
        _bridge.Ioctl(fd, LinuxUinputConstants.UI_SET_EVBIT, LinuxUinputConstants.EV_SYN);
        _bridge.Ioctl(fd, LinuxUinputConstants.UI_SET_EVBIT, LinuxUinputConstants.EV_KEY);
        _bridge.Ioctl(fd, LinuxUinputConstants.UI_SET_EVBIT, LinuxUinputConstants.EV_ABS);

        // Enable gamepad buttons
        _bridge.Ioctl(fd, LinuxUinputConstants.UI_SET_KEYBIT, LinuxUinputConstants.BTN_A);
        _bridge.Ioctl(fd, LinuxUinputConstants.UI_SET_KEYBIT, LinuxUinputConstants.BTN_B);
        _bridge.Ioctl(fd, LinuxUinputConstants.UI_SET_KEYBIT, LinuxUinputConstants.BTN_X);
        _bridge.Ioctl(fd, LinuxUinputConstants.UI_SET_KEYBIT, LinuxUinputConstants.BTN_Y);
        _bridge.Ioctl(fd, LinuxUinputConstants.UI_SET_KEYBIT, LinuxUinputConstants.BTN_TL);
        _bridge.Ioctl(fd, LinuxUinputConstants.UI_SET_KEYBIT, LinuxUinputConstants.BTN_TR);
        _bridge.Ioctl(fd, LinuxUinputConstants.UI_SET_KEYBIT, LinuxUinputConstants.BTN_SELECT);
        _bridge.Ioctl(fd, LinuxUinputConstants.UI_SET_KEYBIT, LinuxUinputConstants.BTN_START);
        _bridge.Ioctl(fd, LinuxUinputConstants.UI_SET_KEYBIT, LinuxUinputConstants.BTN_MODE);
        _bridge.Ioctl(fd, LinuxUinputConstants.UI_SET_KEYBIT, LinuxUinputConstants.BTN_THUMBL);
        _bridge.Ioctl(fd, LinuxUinputConstants.UI_SET_KEYBIT, LinuxUinputConstants.BTN_THUMBR);

        // Enable D-Pad digital buttons
        _bridge.Ioctl(fd, LinuxUinputConstants.UI_SET_KEYBIT, LinuxUinputConstants.BTN_DPAD_UP);
        _bridge.Ioctl(fd, LinuxUinputConstants.UI_SET_KEYBIT, LinuxUinputConstants.BTN_DPAD_DOWN);
        _bridge.Ioctl(fd, LinuxUinputConstants.UI_SET_KEYBIT, LinuxUinputConstants.BTN_DPAD_LEFT);
        _bridge.Ioctl(fd, LinuxUinputConstants.UI_SET_KEYBIT, LinuxUinputConstants.BTN_DPAD_RIGHT);

        if (_layout == LinuxPadLayout.DualShock4)
        {
            _bridge.Ioctl(fd, LinuxUinputConstants.UI_SET_KEYBIT, LinuxUinputConstants.BTN_TL2);
            _bridge.Ioctl(fd, LinuxUinputConstants.UI_SET_KEYBIT, LinuxUinputConstants.BTN_TR2);
            _bridge.Ioctl(fd, LinuxUinputConstants.UI_SET_KEYBIT, LinuxUinputConstants.KEY_TOUCHPAD);
        }

        // Enable axes
        _bridge.Ioctl(fd, LinuxUinputConstants.UI_SET_ABSBIT, LinuxUinputConstants.ABS_X);
        _bridge.Ioctl(fd, LinuxUinputConstants.UI_SET_ABSBIT, LinuxUinputConstants.ABS_Y);
        _bridge.Ioctl(fd, LinuxUinputConstants.UI_SET_ABSBIT, LinuxUinputConstants.ABS_Z);
        _bridge.Ioctl(fd, LinuxUinputConstants.UI_SET_ABSBIT, LinuxUinputConstants.ABS_RX);
        _bridge.Ioctl(fd, LinuxUinputConstants.UI_SET_ABSBIT, LinuxUinputConstants.ABS_RY);
        _bridge.Ioctl(fd, LinuxUinputConstants.UI_SET_ABSBIT, LinuxUinputConstants.ABS_RZ);
        _bridge.Ioctl(fd, LinuxUinputConstants.UI_SET_ABSBIT, LinuxUinputConstants.ABS_HAT0X);
        _bridge.Ioctl(fd, LinuxUinputConstants.UI_SET_ABSBIT, LinuxUinputConstants.ABS_HAT0Y);

        // Configure axis properties via uinput_user_dev
        var userDev = new UinputUserDev();
        userDev.SetName(ControllerName);
        userDev.Id = new InputId
        {
            Bustype = LinuxUinputConstants.BUS_USB,
            Vendor = VendorId,
            Product = ProductId,
            Version = _layout == LinuxPadLayout.Xbox360 ? (ushort)0x0114 : (ushort)0x8111
        };

        unsafe
        {
            // Left Stick
            userDev.Absmin[LinuxUinputConstants.ABS_X] = -32768;
            userDev.Absmax[LinuxUinputConstants.ABS_X] = 32767;
            userDev.Absfuzz[LinuxUinputConstants.ABS_X] = 16;
            userDev.Absflat[LinuxUinputConstants.ABS_X] = 128;

            userDev.Absmin[LinuxUinputConstants.ABS_Y] = -32768;
            userDev.Absmax[LinuxUinputConstants.ABS_Y] = 32767;
            userDev.Absfuzz[LinuxUinputConstants.ABS_Y] = 16;
            userDev.Absflat[LinuxUinputConstants.ABS_Y] = 128;

            // Right Stick
            userDev.Absmin[LinuxUinputConstants.ABS_RX] = -32768;
            userDev.Absmax[LinuxUinputConstants.ABS_RX] = 32767;
            userDev.Absfuzz[LinuxUinputConstants.ABS_RX] = 16;
            userDev.Absflat[LinuxUinputConstants.ABS_RX] = 128;

            userDev.Absmin[LinuxUinputConstants.ABS_RY] = -32768;
            userDev.Absmax[LinuxUinputConstants.ABS_RY] = 32767;
            userDev.Absfuzz[LinuxUinputConstants.ABS_RY] = 16;
            userDev.Absflat[LinuxUinputConstants.ABS_RY] = 128;

            // Analog Triggers (0..255)
            userDev.Absmin[LinuxUinputConstants.ABS_Z] = 0;
            userDev.Absmax[LinuxUinputConstants.ABS_Z] = 255;
            userDev.Absmin[LinuxUinputConstants.ABS_RZ] = 0;
            userDev.Absmax[LinuxUinputConstants.ABS_RZ] = 255;

            // D-Pad Hat (-1..1)
            userDev.Absmin[LinuxUinputConstants.ABS_HAT0X] = -1;
            userDev.Absmax[LinuxUinputConstants.ABS_HAT0X] = 1;
            userDev.Absmin[LinuxUinputConstants.ABS_HAT0Y] = -1;
            userDev.Absmax[LinuxUinputConstants.ABS_HAT0Y] = 1;
        }

        _bridge.WriteUserDev(fd, in userDev);
        _bridge.Ioctl(fd, LinuxUinputConstants.UI_DEV_CREATE, 0);

        _slotFds[slot] = fd;

        // Emit neutral state snapshot
        Submit(slot, PadState.Neutral);
    }

    public void Submit(int slot, in PadState state)
    {
        lock (_lock)
        {
            if (_disposed) return;
            if (slot < 0 || slot >= IPadBackend.MaxPads) return;
            int fd = _slotFds[slot];
            if (fd < 0) return;

            Span<InputEvent> events = stackalloc InputEvent[32];
            int count = 0;

            // 1. Analog axes (with Y inversion for Linux coordinate system)
            events[count++] = new InputEvent(LinuxUinputConstants.EV_ABS, LinuxUinputConstants.ABS_X, NormalizeAxisX(state.ThumbLX));
            events[count++] = new InputEvent(LinuxUinputConstants.EV_ABS, LinuxUinputConstants.ABS_Y, NormalizeAxisY(state.ThumbLY));
            events[count++] = new InputEvent(LinuxUinputConstants.EV_ABS, LinuxUinputConstants.ABS_RX, NormalizeAxisX(state.ThumbRX));
            events[count++] = new InputEvent(LinuxUinputConstants.EV_ABS, LinuxUinputConstants.ABS_RY, NormalizeAxisY(state.ThumbRY));
            events[count++] = new InputEvent(LinuxUinputConstants.EV_ABS, LinuxUinputConstants.ABS_Z, NormalizeTrigger(state.LeftTrigger));
            events[count++] = new InputEvent(LinuxUinputConstants.EV_ABS, LinuxUinputConstants.ABS_RZ, NormalizeTrigger(state.RightTrigger));

            // 2. D-pad Hat & digital buttons
            events[count++] = new InputEvent(LinuxUinputConstants.EV_ABS, LinuxUinputConstants.ABS_HAT0X, ComputeHat0X(state));
            events[count++] = new InputEvent(LinuxUinputConstants.EV_ABS, LinuxUinputConstants.ABS_HAT0Y, ComputeHat0Y(state));

            events[count++] = new InputEvent(LinuxUinputConstants.EV_KEY, LinuxUinputConstants.BTN_DPAD_UP, state.IsButtonPressed(Protocol.Buttons.DPadUp) ? 1 : 0);
            events[count++] = new InputEvent(LinuxUinputConstants.EV_KEY, LinuxUinputConstants.BTN_DPAD_DOWN, state.IsButtonPressed(Protocol.Buttons.DPadDown) ? 1 : 0);
            events[count++] = new InputEvent(LinuxUinputConstants.EV_KEY, LinuxUinputConstants.BTN_DPAD_LEFT, state.IsButtonPressed(Protocol.Buttons.DPadLeft) ? 1 : 0);
            events[count++] = new InputEvent(LinuxUinputConstants.EV_KEY, LinuxUinputConstants.BTN_DPAD_RIGHT, state.IsButtonPressed(Protocol.Buttons.DPadRight) ? 1 : 0);

            // 3. Digital action buttons
            events[count++] = new InputEvent(LinuxUinputConstants.EV_KEY, LinuxUinputConstants.BTN_A, state.IsButtonPressed(Protocol.Buttons.A) ? 1 : 0);
            events[count++] = new InputEvent(LinuxUinputConstants.EV_KEY, LinuxUinputConstants.BTN_B, state.IsButtonPressed(Protocol.Buttons.B) ? 1 : 0);
            events[count++] = new InputEvent(LinuxUinputConstants.EV_KEY, LinuxUinputConstants.BTN_X, state.IsButtonPressed(Protocol.Buttons.X) ? 1 : 0);
            events[count++] = new InputEvent(LinuxUinputConstants.EV_KEY, LinuxUinputConstants.BTN_Y, state.IsButtonPressed(Protocol.Buttons.Y) ? 1 : 0);
            events[count++] = new InputEvent(LinuxUinputConstants.EV_KEY, LinuxUinputConstants.BTN_TL, state.IsButtonPressed(Protocol.Buttons.LeftShoulder) ? 1 : 0);
            events[count++] = new InputEvent(LinuxUinputConstants.EV_KEY, LinuxUinputConstants.BTN_TR, state.IsButtonPressed(Protocol.Buttons.RightShoulder) ? 1 : 0);
            events[count++] = new InputEvent(LinuxUinputConstants.EV_KEY, LinuxUinputConstants.BTN_SELECT, state.IsButtonPressed(Protocol.Buttons.Back) ? 1 : 0);
            events[count++] = new InputEvent(LinuxUinputConstants.EV_KEY, LinuxUinputConstants.BTN_START, state.IsButtonPressed(Protocol.Buttons.Start) ? 1 : 0);
            events[count++] = new InputEvent(LinuxUinputConstants.EV_KEY, LinuxUinputConstants.BTN_MODE, state.IsButtonPressed(Protocol.Buttons.Guide) ? 1 : 0);
            events[count++] = new InputEvent(LinuxUinputConstants.EV_KEY, LinuxUinputConstants.BTN_THUMBL, state.IsButtonPressed(Protocol.Buttons.LeftThumb) ? 1 : 0);
            events[count++] = new InputEvent(LinuxUinputConstants.EV_KEY, LinuxUinputConstants.BTN_THUMBR, state.IsButtonPressed(Protocol.Buttons.RightThumb) ? 1 : 0);

            if (_layout == LinuxPadLayout.DualShock4)
            {
                events[count++] = new InputEvent(LinuxUinputConstants.EV_KEY, LinuxUinputConstants.KEY_TOUCHPAD, state.IsButtonPressed(Protocol.Buttons.Touchpad) ? 1 : 0);
            }

            // 4. SYN_REPORT to flush event batch to kernel
            events[count++] = new InputEvent(LinuxUinputConstants.EV_SYN, LinuxUinputConstants.SYN_REPORT, 0);

            _bridge.Write(fd, events.Slice(0, count));
        }
    }

    public void SubmitTouchpad(int slot, in TouchpadState state)
    {
        lock (_lock)
        {
            if (_disposed) return;
            if (slot < 0 || slot >= IPadBackend.MaxPads) return;
            int fd = _slotFds[slot];
            if (fd < 0) return;

            if (_layout == LinuxPadLayout.DualShock4)
            {
                Span<InputEvent> events = stackalloc InputEvent[2];
                events[0] = new InputEvent(LinuxUinputConstants.EV_KEY, LinuxUinputConstants.KEY_TOUCHPAD, state.Clicked ? 1 : 0);
                events[1] = new InputEvent(LinuxUinputConstants.EV_SYN, LinuxUinputConstants.SYN_REPORT, 0);
                _bridge.Write(fd, events);
            }
        }
    }

    public void Disconnect(int slot)
    {
        lock (_lock)
        {
            if (_disposed) return;
            ValidateSlot(slot);
            DestroySlot(slot);
        }
    }

    private void DestroySlot(int slot)
    {
        int fd = _slotFds[slot];
        if (fd >= 0)
        {
            try
            {
                _bridge.Ioctl(fd, LinuxUinputConstants.UI_DEV_DESTROY, 0);
                _bridge.Close(fd);
            }
            catch { }
            _slotFds[slot] = -1;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;

            for (int slot = 0; slot < IPadBackend.MaxPads; slot++)
            {
                DestroySlot(slot);
            }
        }
    }

    private static void ValidateSlot(int slot)
    {
        if (slot < 0 || slot >= IPadBackend.MaxPads)
            throw new ArgumentOutOfRangeException(nameof(slot), $"Slot must be between 0 and {IPadBackend.MaxPads - 1}");
    }

    #region Coordinate Normalization Helpers

    public static short NormalizeAxisX(short thumbX) => thumbX;

    /// <summary>
    /// Linux input subsystem convention has Y-axis positive DOWN, negative UP.
    /// OmniPad protocol / Windows XInput has positive UP, negative DOWN.
    /// </summary>
    public static short NormalizeAxisY(short thumbY) =>
        thumbY == short.MinValue ? short.MaxValue : (short)-thumbY;

    public static int NormalizeTrigger(byte trigger) => trigger;

    public static int ComputeHat0X(PadState state)
    {
        bool left = state.IsButtonPressed(Protocol.Buttons.DPadLeft);
        bool right = state.IsButtonPressed(Protocol.Buttons.DPadRight);
        if (left && !right) return -1;
        if (right && !left) return 1;
        return 0;
    }

    public static int ComputeHat0Y(PadState state)
    {
        bool up = state.IsButtonPressed(Protocol.Buttons.DPadUp);
        bool down = state.IsButtonPressed(Protocol.Buttons.DPadDown);
        if (up && !down) return -1; // Up is negative in Linux HAT convention
        if (down && !up) return 1;
        return 0;
    }

    #endregion
}
