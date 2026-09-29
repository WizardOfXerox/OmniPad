using System;
using OmniPadServer.Core;
using OmniPadServer.ViGEm;
using Xunit;

namespace OmniPadServer.Tests;

public class HIDOmniPadBusTests
{
    [Fact]
    public void TestCatalogContainsAll15Presets()
    {
        Assert.Equal(15, ControllerProfileCatalog.Profiles.Count);

        Assert.True(ControllerProfileCatalog.Profiles.ContainsKey(ControllerProfilePreset.Xbox360_WHQL));
        Assert.True(ControllerProfileCatalog.Profiles.ContainsKey(ControllerProfilePreset.DualShock4_WHQL));
        Assert.True(ControllerProfileCatalog.Profiles.ContainsKey(ControllerProfilePreset.DualSense_PS5));
        Assert.True(ControllerProfileCatalog.Profiles.ContainsKey(ControllerProfilePreset.DualSense_Edge));
        Assert.True(ControllerProfileCatalog.Profiles.ContainsKey(ControllerProfilePreset.Switch_Pro));
        Assert.True(ControllerProfileCatalog.Profiles.ContainsKey(ControllerProfilePreset.JoyCon_L));
        Assert.True(ControllerProfileCatalog.Profiles.ContainsKey(ControllerProfilePreset.JoyCon_R));
        Assert.True(ControllerProfileCatalog.Profiles.ContainsKey(ControllerProfilePreset.GameCube));
        Assert.True(ControllerProfileCatalog.Profiles.ContainsKey(ControllerProfilePreset.SteamDeck));
        Assert.True(ControllerProfileCatalog.Profiles.ContainsKey(ControllerProfilePreset.Xbox_Elite_Series2));
        Assert.True(ControllerProfileCatalog.Profiles.ContainsKey(ControllerProfilePreset.Xbox_Series_XS));
        Assert.True(ControllerProfileCatalog.Profiles.ContainsKey(ControllerProfilePreset.RacingWheel_Logitech));
        Assert.True(ControllerProfileCatalog.Profiles.ContainsKey(ControllerProfilePreset.FlightSim_HOTAS));
        Assert.True(ControllerProfileCatalog.Profiles.ContainsKey(ControllerProfilePreset.ArcadeStick_HORI));
        Assert.True(ControllerProfileCatalog.Profiles.ContainsKey(ControllerProfilePreset.KeyboardMouse));
    }

    [Theory]
    [InlineData("xbox360", ControllerProfilePreset.Xbox360_WHQL)]
    [InlineData("ps4", ControllerProfilePreset.DualShock4_WHQL)]
    [InlineData("dualsense", ControllerProfilePreset.DualSense_PS5)]
    [InlineData("switch-pro", ControllerProfilePreset.Switch_Pro)]
    [InlineData("gamecube", ControllerProfilePreset.GameCube)]
    [InlineData("steamdeck", ControllerProfilePreset.SteamDeck)]
    [InlineData("wheel", ControllerProfilePreset.RacingWheel_Logitech)]
    [InlineData("hotas", ControllerProfilePreset.FlightSim_HOTAS)]
    [InlineData("kbm", ControllerProfilePreset.KeyboardMouse)]
    public void TestProfileParsing(string input, ControllerProfilePreset expected)
    {
        var parsed = ControllerProfileCatalog.Parse(input);
        Assert.Equal(expected, parsed);
    }

    [Fact]
    public void TestDetectStatusDoesNotThrow()
    {
        var status = HIDOmniPadBusManager.DetectStatus();
        Assert.NotNull(status);
        Assert.NotEmpty(status.PrimaryEngine);
        Assert.NotEmpty(status.StatusSummary);
        Assert.True(status.TotalProfilesAvailable >= 0);
    }

    [Fact]
    public void TestHIDOmniPadBusInitializationAndFallback()
    {
        using var bus = new HIDOmniPadBus();
        Assert.NotNull(bus);
        Assert.NotEmpty(bus.ActiveEngineName);
        Assert.NotNull(bus.CurrentInfo);

        // Test slot connect and disconnect
        bus.Connect(0);
        var state = new PadState();
        bus.Submit(0, in state);
        bus.Disconnect(0);
    }

    [Fact]
    public void TestSwitchablePadBackendIntegration()
    {
        using var backend = new SwitchablePadBackend(forceKeyboardMouse: false, ControllerProfilePreset.KeyboardMouse);
        Assert.Equal(ControllerProfilePreset.KeyboardMouse, backend.CurrentPreset);

        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux))
        {
            Assert.Equal("Linux uinput Virtual Keyboard & Mouse", backend.CurrentEngineName);
        }
        else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
        {
            Assert.Equal("macOS CoreGraphics Input Simulator", backend.CurrentEngineName);
        }
        else
        {
            Assert.Equal("Zero-Driver Keyboard & Mouse", backend.CurrentEngineName);
        }

        // Connect slot and verify no crash
        backend.Connect(1);
        backend.Disconnect(1);
    }
}
