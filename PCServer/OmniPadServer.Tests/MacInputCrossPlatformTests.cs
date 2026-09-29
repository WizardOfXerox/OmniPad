using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using OmniPadServer.App;
using OmniPadServer.Core;
using OmniPadServer.ViGEm;
using Xunit;

namespace OmniPadServer.Tests;

public class MacInputCrossPlatformTests
{
    [Fact]
    public void TestWindowsVkToMacKeyCode_Alphanumeric()
    {
        Assert.Equal(MacKeyCodes.W, MacInputSimulator.WindowsVkToMacKeyCode(0x57)); // W
        Assert.Equal(MacKeyCodes.A, MacInputSimulator.WindowsVkToMacKeyCode(0x41)); // A
        Assert.Equal(MacKeyCodes.S, MacInputSimulator.WindowsVkToMacKeyCode(0x53)); // S
        Assert.Equal(MacKeyCodes.D, MacInputSimulator.WindowsVkToMacKeyCode(0x44)); // D
        Assert.Equal(MacKeyCodes.R, MacInputSimulator.WindowsVkToMacKeyCode(0x52)); // R
        Assert.Equal(MacKeyCodes.E, MacInputSimulator.WindowsVkToMacKeyCode(0x45)); // E
        Assert.Equal(MacKeyCodes.C, MacInputSimulator.WindowsVkToMacKeyCode(0x43)); // C
        Assert.Equal(MacKeyCodes.Zero, MacInputSimulator.WindowsVkToMacKeyCode(0x30)); // 0
        Assert.Equal(MacKeyCodes.One, MacInputSimulator.WindowsVkToMacKeyCode(0x31)); // 1
    }

    [Fact]
    public void TestWindowsVkToMacKeyCode_ControlsAndModifiers()
    {
        Assert.Equal(MacKeyCodes.Space, MacInputSimulator.WindowsVkToMacKeyCode(0x20)); // Space
        Assert.Equal(MacKeyCodes.Escape, MacInputSimulator.WindowsVkToMacKeyCode(0x1B)); // Esc
        Assert.Equal(MacKeyCodes.Return, MacInputSimulator.WindowsVkToMacKeyCode(0x0D)); // Enter
        Assert.Equal(MacKeyCodes.Tab, MacInputSimulator.WindowsVkToMacKeyCode(0x09)); // Tab
        Assert.Equal(MacKeyCodes.Delete, MacInputSimulator.WindowsVkToMacKeyCode(0x08)); // Backspace -> Mac Delete
        Assert.Equal(MacKeyCodes.ForwardDelete, MacInputSimulator.WindowsVkToMacKeyCode(0x2E)); // Del -> Mac ForwardDelete
        Assert.Equal(MacKeyCodes.Shift, MacInputSimulator.WindowsVkToMacKeyCode(0x10)); // Shift
        Assert.Equal(MacKeyCodes.Control, MacInputSimulator.WindowsVkToMacKeyCode(0x11)); // Ctrl
        Assert.Equal(MacKeyCodes.Option, MacInputSimulator.WindowsVkToMacKeyCode(0x12)); // Alt -> Mac Option
        Assert.Equal(MacKeyCodes.Command, MacInputSimulator.WindowsVkToMacKeyCode(0x5B)); // Win -> Mac Command
        Assert.Equal(MacKeyCodes.UpArrow, MacInputSimulator.WindowsVkToMacKeyCode(0x26)); // Up
        Assert.Equal(MacKeyCodes.DownArrow, MacInputSimulator.WindowsVkToMacKeyCode(0x28)); // Down
        Assert.Equal(MacKeyCodes.LeftArrow, MacInputSimulator.WindowsVkToMacKeyCode(0x25)); // Left
        Assert.Equal(MacKeyCodes.RightArrow, MacInputSimulator.WindowsVkToMacKeyCode(0x27)); // Right
        Assert.Equal(MacKeyCodes.F1, MacInputSimulator.WindowsVkToMacKeyCode(0x70)); // F1
        Assert.Equal(MacKeyCodes.F5, MacInputSimulator.WindowsVkToMacKeyCode(0x74)); // F5
    }

    [Fact]
    public void TestMacInputSimulator_GamepadLeftStickToWasdMapping()
    {
        using var sim = new MacInputSimulator();
        MacInputSimulator.ResetAllKeys();

        // 1. Move Left Stick Up (> 10000) -> W pressed
        var stateW = PadState.Neutral with { ThumbLY = 20000 };
        sim.Submit(0, in stateW);
        Assert.Contains(MacKeyCodes.W, MacInputSimulator.ActivePressedKeys);

        // Release stick
        var stateNeutral = PadState.Neutral;
        sim.Submit(0, in stateNeutral);
        Assert.DoesNotContain(MacKeyCodes.W, MacInputSimulator.ActivePressedKeys);

        // 2. Move Left Stick Down (< -10000) -> S pressed
        var stateS = PadState.Neutral with { ThumbLY = -20000 };
        sim.Submit(0, in stateS);
        Assert.Contains(MacKeyCodes.S, MacInputSimulator.ActivePressedKeys);

        // 3. Move Left Stick Left (< -10000) -> A pressed
        var stateA = PadState.Neutral with { ThumbLX = -20000 };
        sim.Submit(0, in stateA);
        Assert.Contains(MacKeyCodes.A, MacInputSimulator.ActivePressedKeys);

        // 4. Move Left Stick Right (> 10000) -> D pressed
        var stateD = PadState.Neutral with { ThumbLX = 20000 };
        sim.Submit(0, in stateD);
        Assert.Contains(MacKeyCodes.D, MacInputSimulator.ActivePressedKeys);

        MacInputSimulator.ResetAllKeys();
    }

    [Fact]
    public void TestMacInputSimulator_GamepadButtonsToActionKeys()
    {
        using var sim = new MacInputSimulator();
        MacInputSimulator.ResetAllKeys();

        // Button A -> Space
        var stateA = PadState.Neutral with { Buttons = (ushort)Protocol.Buttons.A };
        sim.Submit(0, in stateA);
        Assert.Contains(MacKeyCodes.Space, MacInputSimulator.ActivePressedKeys);

        // Button X -> R
        var stateX = PadState.Neutral with { Buttons = (ushort)Protocol.Buttons.X };
        sim.Submit(0, in stateX);
        Assert.Contains(MacKeyCodes.R, MacInputSimulator.ActivePressedKeys);
        Assert.DoesNotContain(MacKeyCodes.Space, MacInputSimulator.ActivePressedKeys);

        // Button Y -> E
        var stateY = PadState.Neutral with { Buttons = (ushort)Protocol.Buttons.Y };
        sim.Submit(0, in stateY);
        Assert.Contains(MacKeyCodes.E, MacInputSimulator.ActivePressedKeys);

        // Button B -> C
        var stateB = PadState.Neutral with { Buttons = (ushort)Protocol.Buttons.B };
        sim.Submit(0, in stateB);
        Assert.Contains(MacKeyCodes.C, MacInputSimulator.ActivePressedKeys);

        // LeftShoulder -> Shift
        var stateLB = PadState.Neutral with { Buttons = (ushort)Protocol.Buttons.LeftShoulder };
        sim.Submit(0, in stateLB);
        Assert.Contains(MacKeyCodes.Shift, MacInputSimulator.ActivePressedKeys);

        // LeftThumb -> Control
        var stateL3 = PadState.Neutral with { Buttons = (ushort)Protocol.Buttons.LeftThumb };
        sim.Submit(0, in stateL3);
        Assert.Contains(MacKeyCodes.Control, MacInputSimulator.ActivePressedKeys);

        // Start -> Escape
        var stateStart = PadState.Neutral with { Buttons = (ushort)Protocol.Buttons.Start };
        sim.Submit(0, in stateStart);
        Assert.Contains(MacKeyCodes.Escape, MacInputSimulator.ActivePressedKeys);

        sim.Disconnect(0);
        Assert.Empty(MacInputSimulator.ActivePressedKeys);
    }

    [Fact]
    public void TestMacInputSimulator_MouseMovementAndTriggers()
    {
        using var sim = new MacInputSimulator();
        MacInputSimulator.CurrentPosition = new CGPoint(500, 500);

        // Right stick: RX=3200 (dx=2), RY=-1600 (dy=1)
        var stateAim = PadState.Neutral with { ThumbRX = 3200, ThumbRY = -1600 };
        sim.Submit(0, in stateAim);
        Assert.Equal(502, MacInputSimulator.CurrentPosition.X);
        Assert.Equal(501, MacInputSimulator.CurrentPosition.Y);

        // RT Trigger > 128 -> Left Click
        var stateRt = PadState.Neutral with { RightTrigger = 200 };
        sim.Submit(0, in stateRt);
        Assert.True(MacInputSimulator.IsLeftMouseDown);

        // RT Trigger release
        sim.Submit(0, in PadState.Neutral);
        Assert.False(MacInputSimulator.IsLeftMouseDown);

        // LT Trigger > 128 -> Right Click
        var stateLt = PadState.Neutral with { LeftTrigger = 200 };
        sim.Submit(0, in stateLt);
        Assert.True(MacInputSimulator.IsRightMouseDown);

        // LT Trigger release
        sim.Submit(0, in PadState.Neutral);
        Assert.False(MacInputSimulator.IsRightMouseDown);
    }

    [Fact]
    public void TestMacInputSimulator_TouchpadClick()
    {
        using var sim = new MacInputSimulator();
        var touchDown = new TouchpadState { Clicked = true };
        sim.SubmitTouchpad(0, in touchDown);
        Assert.True(MacInputSimulator.IsLeftMouseDown);

        var touchUp = new TouchpadState { Clicked = false };
        sim.SubmitTouchpad(0, in touchUp);
        Assert.False(MacInputSimulator.IsLeftMouseDown);
    }

    [Fact]
    public void TestAdbHelper_CandidatePathsIncludeMacAndLinuxPaths()
    {
        var candidates = AdbHelper.GetAdbCandidatePaths();
        Assert.Contains("/opt/homebrew/bin/adb", candidates);
        Assert.Contains("/usr/local/bin/adb", candidates);
        Assert.Contains("/usr/bin/adb", candidates);
    }

    [Fact]
    public void TestPadBackendFactory_Creation()
    {
        // Test fallback KBM backend selection
        var kbmBackend = PadBackendFactory.CreateKeyboardMouseBackend();
        Assert.NotNull(kbmBackend);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Assert.IsType<MacInputSimulator>(kbmBackend);
            var (backend, isHardware) = PadBackendFactory.CreateBackend();
            Assert.IsType<MacInputSimulator>(backend);
            Assert.False(isHardware);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Assert.IsType<LinuxUinputMouseKeyboardBackend>(kbmBackend);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.IsType<VirtualMouseKeyboardBackend>(kbmBackend);
            var (backend, isHardware) = PadBackendFactory.CreateBackend(forceKeyboardMouse: true);
            Assert.IsType<VirtualMouseKeyboardBackend>(backend);
            Assert.False(isHardware);
        }
    }

    [Fact]
    public async Task TestCrossPlatformServerHardening_SafeInstantiation()
    {
        // 1. AudioStreamServer: must not throw on any platform during init/dispose
        var audioServer = new AudioStreamServer();
        Assert.Equal(0, audioServer.ConnectedListeners);
        await audioServer.DisposeAsync();

        // 2. MicStreamServer: must start and dispose without unhandled platform exceptions
        var micServer = new MicStreamServer();
        Assert.False(micServer.IsStreaming);
        await micServer.DisposeAsync();

        // 3. ScreenStreamServer: constructor and dispose must be safe
        var screenServer = new ScreenStreamServer();
        Assert.Equal(0, screenServer.ConnectedClients);
        await screenServer.DisposeAsync();
    }
}
