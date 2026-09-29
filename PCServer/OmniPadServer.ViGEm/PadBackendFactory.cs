using System;
using System.Runtime.InteropServices;
using OmniPadServer.Core;

namespace OmniPadServer.ViGEm;

/// <summary>
/// Dynamic factory for creating OS-specific gamepad and input backends.
/// On Windows: Employs ViGEm / HIDMaestro / SendInput.
/// On Linux: Employs native /dev/uinput LinuxUinputPadBackend / LinuxUinputMouseKeyboardBackend.
/// </summary>
public static class PadBackendFactory
{
    /// <summary>
    /// Creates the appropriate IPadBackend based on operating system and options.
    /// Returns a tuple of the backend and whether it uses hardware-grade drivers (ViGEm/HIDMaestro/uinput).
    /// </summary>
    public static (IPadBackend Backend, bool IsHardware) CreateBackend(
        bool forceKeyboardMouse = false,
        ControllerProfilePreset initialPreset = ControllerProfilePreset.Xbox360_WHQL)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return (new MacInputSimulator(), false);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (forceKeyboardMouse || initialPreset == ControllerProfilePreset.KeyboardMouse)
            {
                return (new LinuxUinputMouseKeyboardBackend(), false);
            }

            var layout = (initialPreset == ControllerProfilePreset.DualShock4_WHQL ||
                          initialPreset == ControllerProfilePreset.DualSense_PS5 ||
                          initialPreset == ControllerProfilePreset.DualSense_Edge)
                ? LinuxPadLayout.DualShock4
                : LinuxPadLayout.Xbox360;

            return (new LinuxUinputPadBackend(layout), true);
        }

        if (forceKeyboardMouse || initialPreset == ControllerProfilePreset.KeyboardMouse)
        {
            return (new VirtualMouseKeyboardBackend(), false);
        }

        var bus = new HIDOmniPadBus(forceKeyboardMouse, initialPreset);
        bool isHw = bus.ActiveEngineName.Contains("Kernel") || bus.ActiveEngineName.Contains("UMDF2");
        return (bus, isHw);
    }

    public static (IPadBackend Backend, bool IsHardware) CreateBackend(
        bool forceKeyboardMouse,
        EmulationType initialType)
    {
        var preset = initialType switch
        {
            EmulationType.DualShock4 => ControllerProfilePreset.DualShock4_WHQL,
            EmulationType.HIDMaestro => ControllerProfilePreset.DualSense_PS5,
            _ => ControllerProfilePreset.Xbox360_WHQL
        };

        return CreateBackend(forceKeyboardMouse, preset);
    }

    /// <summary>
    /// Creates a SwitchablePadBackend instance.
    /// </summary>
    public static IPadBackend CreatePadBackend(
        bool forceKeyboardMouse = false,
        ControllerProfilePreset initialPreset = ControllerProfilePreset.Xbox360_WHQL)
    {
        return new SwitchablePadBackend(forceKeyboardMouse, initialPreset);
    }

    /// <summary>
    /// Creates the platform-appropriate virtual keyboard & mouse fallback backend.
    /// </summary>
    public static IPadBackend CreateKeyboardMouseBackend()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new MacInputSimulator();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return new LinuxUinputMouseKeyboardBackend();
        }

        return new VirtualMouseKeyboardBackend();
    }

    /// <summary>
    /// Creates a native Linux uinput gamepad backend.
    /// </summary>
    public static LinuxUinputPadBackend CreateLinuxUinputPad(
        LinuxPadLayout layout = LinuxPadLayout.Xbox360,
        IUinputNativeBridge? bridge = null)
    {
        return new LinuxUinputPadBackend(layout, bridge);
    }
}
