using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using OmniPadServer.Core;
using OmniPadServer.ViGEm;
using Xunit;

namespace OmniPadServer.Tests;

public class LinuxBackendTests
{
    [Fact]
    public void TestUinputStructureSizesAndLayouts()
    {
        // 1. InputId: 4 ushorts = 8 bytes
        Assert.Equal(8, Marshal.SizeOf<InputId>());

        // 2. InputAbsInfo: 6 ints = 24 bytes
        Assert.Equal(24, Marshal.SizeOf<InputAbsInfo>());

        // 3. UinputAbsSetup: ushort code (2) + 2 padding + InputAbsInfo (24) = 28 bytes
        Assert.Equal(28, Marshal.SizeOf<UinputAbsSetup>());

        // 4. UinputSetup: InputId (8) + Name[80] (80) + uint (4) = 92 bytes
        unsafe
        {
            Assert.Equal(92, sizeof(UinputSetup));
            Assert.Equal(92, Marshal.SizeOf<UinputSetup>());
        }

        // 5. InputEvent: 24 bytes on 64-bit platforms (nint time_sec: 8 + nint time_usec: 8 + ushort: 2 + ushort: 2 + int: 4 = 24)
        if (IntPtr.Size == 8)
        {
            Assert.Equal(24, Marshal.SizeOf<InputEvent>());
        }

        // 6. UinputUserDev: Name[80] + InputId (8) + ff_effects_max (4) + 4 * 64 * 4 = 1116 bytes
        unsafe
        {
            Assert.Equal(1116, sizeof(UinputUserDev));
            Assert.Equal(1116, Marshal.SizeOf<UinputUserDev>());
        }
    }

    [Fact]
    public void TestLinuxUinputConstantsMatchKernelSpec()
    {
        // Event types
        Assert.Equal(0x00, LinuxUinputConstants.EV_SYN);
        Assert.Equal(0x01, LinuxUinputConstants.EV_KEY);
        Assert.Equal(0x02, LinuxUinputConstants.EV_REL);
        Assert.Equal(0x03, LinuxUinputConstants.EV_ABS);
        Assert.Equal(0, LinuxUinputConstants.SYN_REPORT);

        // Relative axes
        Assert.Equal(0x00, LinuxUinputConstants.REL_X);
        Assert.Equal(0x01, LinuxUinputConstants.REL_Y);
        Assert.Equal(0x08, LinuxUinputConstants.REL_WHEEL);

        // Absolute axes
        Assert.Equal(0x00, LinuxUinputConstants.ABS_X);
        Assert.Equal(0x01, LinuxUinputConstants.ABS_Y);
        Assert.Equal(0x02, LinuxUinputConstants.ABS_Z);
        Assert.Equal(0x03, LinuxUinputConstants.ABS_RX);
        Assert.Equal(0x04, LinuxUinputConstants.ABS_RY);
        Assert.Equal(0x05, LinuxUinputConstants.ABS_RZ);
        Assert.Equal(0x10, LinuxUinputConstants.ABS_HAT0X);
        Assert.Equal(0x11, LinuxUinputConstants.ABS_HAT0Y);

        // Buttons
        Assert.Equal(0x110, LinuxUinputConstants.BTN_LEFT);
        Assert.Equal(0x111, LinuxUinputConstants.BTN_RIGHT);
        Assert.Equal(0x112, LinuxUinputConstants.BTN_MIDDLE);
        Assert.Equal(0x130, LinuxUinputConstants.BTN_A);
        Assert.Equal(0x131, LinuxUinputConstants.BTN_B);
        Assert.Equal(0x133, LinuxUinputConstants.BTN_X);
        Assert.Equal(0x134, LinuxUinputConstants.BTN_Y);
        Assert.Equal(0x136, LinuxUinputConstants.BTN_TL);
        Assert.Equal(0x137, LinuxUinputConstants.BTN_TR);
        Assert.Equal(0x138, LinuxUinputConstants.BTN_TL2);
        Assert.Equal(0x139, LinuxUinputConstants.BTN_TR2);
        Assert.Equal(0x13A, LinuxUinputConstants.BTN_SELECT);
        Assert.Equal(0x13B, LinuxUinputConstants.BTN_START);
        Assert.Equal(0x13C, LinuxUinputConstants.BTN_MODE);
        Assert.Equal(0x13D, LinuxUinputConstants.BTN_THUMBL);
        Assert.Equal(0x13E, LinuxUinputConstants.BTN_THUMBR);
        Assert.Equal(0x140, LinuxUinputConstants.KEY_TOUCHPAD);

        // D-Pad buttons
        Assert.Equal(0x220, LinuxUinputConstants.BTN_DPAD_UP);
        Assert.Equal(0x221, LinuxUinputConstants.BTN_DPAD_DOWN);
        Assert.Equal(0x222, LinuxUinputConstants.BTN_DPAD_LEFT);
        Assert.Equal(0x223, LinuxUinputConstants.BTN_DPAD_RIGHT);

        // ioctl numbers
        Assert.Equal(0x5501u, LinuxUinputConstants.UI_DEV_CREATE);
        Assert.Equal(0x5502u, LinuxUinputConstants.UI_DEV_DESTROY);
        Assert.Equal(0x405C5503u, LinuxUinputConstants.UI_DEV_SETUP);
        Assert.Equal(0x401C5504u, LinuxUinputConstants.UI_ABS_SETUP);
        Assert.Equal(0x40045564u, LinuxUinputConstants.UI_SET_EVBIT);
        Assert.Equal(0x40045565u, LinuxUinputConstants.UI_SET_KEYBIT);
        Assert.Equal(0x40045566u, LinuxUinputConstants.UI_SET_RELBIT);
        Assert.Equal(0x40045567u, LinuxUinputConstants.UI_SET_ABSBIT);
    }

    [Fact]
    public void TestCoordinateNormalization()
    {
        // 1. X-axis: unchanged
        Assert.Equal(0, LinuxUinputPadBackend.NormalizeAxisX(0));
        Assert.Equal(32767, LinuxUinputPadBackend.NormalizeAxisX(32767));
        Assert.Equal(-32768, LinuxUinputPadBackend.NormalizeAxisX(-32768));
        Assert.Equal(16000, LinuxUinputPadBackend.NormalizeAxisX(16000));
        Assert.Equal(-16000, LinuxUinputPadBackend.NormalizeAxisX(-16000));

        // 2. Y-axis: Inverted for Linux screen/kernel coordinates
        // Up (positive on OmniPad/XInput) -> Up (negative in Linux input subsystem)
        Assert.Equal(-1000, LinuxUinputPadBackend.NormalizeAxisY(1000));
        Assert.Equal(-32767, LinuxUinputPadBackend.NormalizeAxisY(32767));
        Assert.Equal(1000, LinuxUinputPadBackend.NormalizeAxisY(-1000));
        Assert.Equal(0, LinuxUinputPadBackend.NormalizeAxisY(0));
        // Boundary condition: short.MinValue (-32768) must not overflow to negative, maps to 32767
        Assert.Equal(32767, LinuxUinputPadBackend.NormalizeAxisY(short.MinValue));

        // 3. Trigger normalization (0..255)
        Assert.Equal(0, LinuxUinputPadBackend.NormalizeTrigger(0));
        Assert.Equal(128, LinuxUinputPadBackend.NormalizeTrigger(128));
        Assert.Equal(255, LinuxUinputPadBackend.NormalizeTrigger(255));

        // 4. D-Pad Hat calculation
        var stateNeutral = PadState.Neutral;
        Assert.Equal(0, LinuxUinputPadBackend.ComputeHat0X(stateNeutral));
        Assert.Equal(0, LinuxUinputPadBackend.ComputeHat0Y(stateNeutral));

        var stateUp = PadState.Neutral;
        stateUp.SetButton(Protocol.Buttons.DPadUp, true);
        Assert.Equal(0, LinuxUinputPadBackend.ComputeHat0X(stateUp));
        Assert.Equal(-1, LinuxUinputPadBackend.ComputeHat0Y(stateUp)); // Up is -1 in Linux Hat

        var stateDown = PadState.Neutral;
        stateDown.SetButton(Protocol.Buttons.DPadDown, true);
        Assert.Equal(0, LinuxUinputPadBackend.ComputeHat0X(stateDown));
        Assert.Equal(1, LinuxUinputPadBackend.ComputeHat0Y(stateDown)); // Down is +1 in Linux Hat

        var stateLeft = PadState.Neutral;
        stateLeft.SetButton(Protocol.Buttons.DPadLeft, true);
        Assert.Equal(-1, LinuxUinputPadBackend.ComputeHat0X(stateLeft));
        Assert.Equal(0, LinuxUinputPadBackend.ComputeHat0Y(stateLeft));

        var stateRight = PadState.Neutral;
        stateRight.SetButton(Protocol.Buttons.DPadRight, true);
        Assert.Equal(1, LinuxUinputPadBackend.ComputeHat0X(stateRight));
        Assert.Equal(0, LinuxUinputPadBackend.ComputeHat0Y(stateRight));

        var stateUpRight = PadState.Neutral;
        stateUpRight.SetButton(Protocol.Buttons.DPadUp, true);
        stateUpRight.SetButton(Protocol.Buttons.DPadRight, true);
        Assert.Equal(1, LinuxUinputPadBackend.ComputeHat0X(stateUpRight));
        Assert.Equal(-1, LinuxUinputPadBackend.ComputeHat0Y(stateUpRight));

        // Opposing directions cancel to 0
        var stateOpposingX = PadState.Neutral;
        stateOpposingX.SetButton(Protocol.Buttons.DPadLeft, true);
        stateOpposingX.SetButton(Protocol.Buttons.DPadRight, true);
        Assert.Equal(0, LinuxUinputPadBackend.ComputeHat0X(stateOpposingX));

        var stateOpposingY = PadState.Neutral;
        stateOpposingY.SetButton(Protocol.Buttons.DPadUp, true);
        stateOpposingY.SetButton(Protocol.Buttons.DPadDown, true);
        Assert.Equal(0, LinuxUinputPadBackend.ComputeHat0Y(stateOpposingY));
    }

    [Fact]
    public void TestXbox360AndDualShock4LayoutProfiles()
    {
        var xboxBackend = new LinuxUinputPadBackend(LinuxPadLayout.Xbox360, new InMemoryUinputNativeBridge());
        Assert.Equal(LinuxPadLayout.Xbox360, xboxBackend.Layout);
        Assert.Equal("Microsoft X-Box 360 pad", xboxBackend.ControllerName);
        Assert.Equal(0x045E, xboxBackend.VendorId);
        Assert.Equal(0x028E, xboxBackend.ProductId);

        var ds4Backend = new LinuxUinputPadBackend(LinuxPadLayout.DualShock4, new InMemoryUinputNativeBridge());
        Assert.Equal(LinuxPadLayout.DualShock4, ds4Backend.Layout);
        Assert.Equal("Sony Interactive Entertainment Wireless Controller", ds4Backend.ControllerName);
        Assert.Equal(0x054C, ds4Backend.VendorId);
        Assert.Equal(0x05C4, ds4Backend.ProductId);
    }

    [Fact]
    public void TestLinuxUinputPadBackend_MockLifecycleAndEvents()
    {
        var bridge = new InMemoryUinputNativeBridge();
        using var backend = new LinuxUinputPadBackend(LinuxPadLayout.Xbox360, bridge);

        // 1. Connect Slot 0
        backend.Connect(0);
        Assert.Single(bridge.OpenFds);
        int fd = bridge.OpenFds.First();

        // Check userdev properties
        Assert.NotEmpty(bridge.UserDevs);
        var userDev = bridge.UserDevs.First().UserDev;
        Assert.Equal("Microsoft X-Box 360 pad", userDev.GetName());
        Assert.Equal(0x045E, userDev.Id.Vendor);
        Assert.Equal(0x028E, userDev.Id.Product);

        // Verify DEV_CREATE ioctl called
        Assert.Contains(bridge.Ioctls, i => i.Fd == fd && i.Request == LinuxUinputConstants.UI_DEV_CREATE);

        // 2. Submit Input State
        var padState = new PadState
        {
            ThumbLX = 5000,
            ThumbLY = 10000,
            ThumbRX = -7000,
            ThumbRY = -15000,
            LeftTrigger = 100,
            RightTrigger = 200
        };
        padState.SetButton(Protocol.Buttons.A, true);
        padState.SetButton(Protocol.Buttons.DPadUp, true);

        backend.Submit(0, in padState);

        var lastBatch = bridge.WrittenEvents.Last().Events;
        Assert.NotEmpty(lastBatch);

        // Assert axes
        Assert.Contains(lastBatch, e => e.Type == LinuxUinputConstants.EV_ABS && e.Code == LinuxUinputConstants.ABS_X && e.Value == 5000);
        Assert.Contains(lastBatch, e => e.Type == LinuxUinputConstants.EV_ABS && e.Code == LinuxUinputConstants.ABS_Y && e.Value == -10000); // Inverted Y
        Assert.Contains(lastBatch, e => e.Type == LinuxUinputConstants.EV_ABS && e.Code == LinuxUinputConstants.ABS_RX && e.Value == -7000);
        Assert.Contains(lastBatch, e => e.Type == LinuxUinputConstants.EV_ABS && e.Code == LinuxUinputConstants.ABS_RY && e.Value == 15000); // Inverted Y
        Assert.Contains(lastBatch, e => e.Type == LinuxUinputConstants.EV_ABS && e.Code == LinuxUinputConstants.ABS_Z && e.Value == 100);
        Assert.Contains(lastBatch, e => e.Type == LinuxUinputConstants.EV_ABS && e.Code == LinuxUinputConstants.ABS_RZ && e.Value == 200);

        // Assert D-Pad
        Assert.Contains(lastBatch, e => e.Type == LinuxUinputConstants.EV_ABS && e.Code == LinuxUinputConstants.ABS_HAT0Y && e.Value == -1);
        Assert.Contains(lastBatch, e => e.Type == LinuxUinputConstants.EV_KEY && e.Code == LinuxUinputConstants.BTN_DPAD_UP && e.Value == 1);

        // Assert button A
        Assert.Contains(lastBatch, e => e.Type == LinuxUinputConstants.EV_KEY && e.Code == LinuxUinputConstants.BTN_A && e.Value == 1);

        // Assert SYN_REPORT
        Assert.Equal(LinuxUinputConstants.EV_SYN, lastBatch.Last().Type);
        Assert.Equal(LinuxUinputConstants.SYN_REPORT, lastBatch.Last().Code);

        // 3. Disconnect Slot 0
        backend.Disconnect(0);
        Assert.Contains(bridge.Ioctls, i => i.Fd == fd && i.Request == LinuxUinputConstants.UI_DEV_DESTROY);
        Assert.Empty(bridge.OpenFds);
    }

    [Fact]
    public void TestLinuxUinputPadBackend_DualShock4LayoutAndTouchpad()
    {
        var bridge = new InMemoryUinputNativeBridge();
        using var backend = new LinuxUinputPadBackend(LinuxPadLayout.DualShock4, bridge);

        backend.Connect(0);
        var userDev = bridge.UserDevs.First().UserDev;
        Assert.Equal("Sony Interactive Entertainment Wireless Controller", userDev.GetName());
        Assert.Equal(0x054C, userDev.Id.Vendor);
        Assert.Equal(0x05C4, userDev.Id.Product);

        // Submit Touchpad Click
        var touchState = new TouchpadState { Clicked = true };
        backend.SubmitTouchpad(0, in touchState);

        var lastBatch = bridge.WrittenEvents.Last().Events;
        Assert.Contains(lastBatch, e => e.Type == LinuxUinputConstants.EV_KEY && e.Code == LinuxUinputConstants.KEY_TOUCHPAD && e.Value == 1);
        Assert.Contains(lastBatch, e => e.Type == LinuxUinputConstants.EV_SYN && e.Code == LinuxUinputConstants.SYN_REPORT);
    }

    [Fact]
    public void TestLinuxUinputPadBackend_SwitchLayoutAtRuntime()
    {
        var bridge = new InMemoryUinputNativeBridge();
        using var backend = new LinuxUinputPadBackend(LinuxPadLayout.Xbox360, bridge);
        backend.Connect(0);

        Assert.Equal(LinuxPadLayout.Xbox360, backend.Layout);

        // Switch to DualShock 4
        backend.SwitchLayout(LinuxPadLayout.DualShock4);
        Assert.Equal(LinuxPadLayout.DualShock4, backend.Layout);

        // Verify slot was destroyed and recreated with DS4 layout
        var latestUserDev = bridge.UserDevs.Last().UserDev;
        Assert.Equal("Sony Interactive Entertainment Wireless Controller", latestUserDev.GetName());
        Assert.Equal(0x054C, latestUserDev.Id.Vendor);
    }

    [Fact]
    public void TestLinuxKeyCodesMapping()
    {
        Assert.Equal(LinuxKeyCodes.KEY_W, LinuxKeyCodes.FromVkCode(0x57)); // VK_W
        Assert.Equal(LinuxKeyCodes.KEY_A, LinuxKeyCodes.FromVkCode(0x41)); // VK_A
        Assert.Equal(LinuxKeyCodes.KEY_S, LinuxKeyCodes.FromVkCode(0x53)); // VK_S
        Assert.Equal(LinuxKeyCodes.KEY_D, LinuxKeyCodes.FromVkCode(0x44)); // VK_D
        Assert.Equal(LinuxKeyCodes.KEY_SPACE, LinuxKeyCodes.FromVkCode(0x20)); // VK_SPACE
        Assert.Equal(LinuxKeyCodes.KEY_ESC, LinuxKeyCodes.FromVkCode(0x1B)); // VK_ESCAPE
        Assert.Equal(LinuxKeyCodes.KEY_ENTER, LinuxKeyCodes.FromVkCode(0x0D)); // VK_RETURN
        Assert.Equal(LinuxKeyCodes.KEY_BACKSPACE, LinuxKeyCodes.FromVkCode(0x08));
        Assert.Equal(LinuxKeyCodes.KEY_TAB, LinuxKeyCodes.FromVkCode(0x09));
        Assert.Equal(LinuxKeyCodes.KEY_UP, LinuxKeyCodes.FromVkCode(0x26));
        Assert.Equal(LinuxKeyCodes.KEY_DOWN, LinuxKeyCodes.FromVkCode(0x28));
        Assert.Equal(LinuxKeyCodes.KEY_LEFT, LinuxKeyCodes.FromVkCode(0x25));
        Assert.Equal(LinuxKeyCodes.KEY_RIGHT, LinuxKeyCodes.FromVkCode(0x27));
    }

    [Fact]
    public void TestLinuxUinputMouseKeyboardBackend_MockSubmission()
    {
        var bridge = new InMemoryUinputNativeBridge();
        using var kbm = new LinuxUinputMouseKeyboardBackend(bridge);

        // 1. Direct Mouse Move
        kbm.InternalSendMouseMove(15, -8);
        var moveEvents = bridge.WrittenEvents.Last().Events;
        Assert.Contains(moveEvents, e => e.Type == LinuxUinputConstants.EV_REL && e.Code == LinuxUinputConstants.REL_X && e.Value == 15);
        Assert.Contains(moveEvents, e => e.Type == LinuxUinputConstants.EV_REL && e.Code == LinuxUinputConstants.REL_Y && e.Value == -8);
        Assert.Contains(moveEvents, e => e.Type == LinuxUinputConstants.EV_SYN && e.Code == LinuxUinputConstants.SYN_REPORT);

        // 2. Direct Mouse Button Click (Left = 1)
        kbm.InternalSendMouseButton(1, true);
        var btnEvents = bridge.WrittenEvents.Last().Events;
        Assert.Contains(btnEvents, e => e.Type == LinuxUinputConstants.EV_KEY && e.Code == LinuxUinputConstants.BTN_LEFT && e.Value == 1);

        // 3. Direct Mouse Wheel
        kbm.InternalSendMouseWheel(120);
        var wheelEvents = bridge.WrittenEvents.Last().Events;
        Assert.Contains(wheelEvents, e => e.Type == LinuxUinputConstants.EV_REL && e.Code == LinuxUinputConstants.REL_WHEEL && e.Value == 1);

        // 4. Direct Keyboard Key (VK_W = 0x57)
        kbm.InternalSendKeyboardKey(0x57, true);
        var keyEvents = bridge.WrittenEvents.Last().Events;
        Assert.Contains(keyEvents, e => e.Type == LinuxUinputConstants.EV_KEY && e.Code == LinuxKeyCodes.KEY_W && e.Value == 1);

        // 5. Gamepad State to WASD / Mouse Look
        var padState = new PadState
        {
            ThumbRX = 3200, // dx = 3200 / 1600 = 2
            ThumbRY = -1600, // dy = -(-1600) / 1600 = 1
            RightTrigger = 200 // RT > 128 -> Left click
        };
        kbm.Submit(0, in padState);

        var padEvents = bridge.WrittenEvents.Last().Events;
        Assert.Contains(padEvents, e => e.Type == LinuxUinputConstants.EV_KEY && e.Code == LinuxUinputConstants.BTN_LEFT && e.Value == 1);
    }
}
