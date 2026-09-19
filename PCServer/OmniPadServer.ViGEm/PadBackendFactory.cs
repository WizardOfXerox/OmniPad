using System;
using OmniPadServer.Core;

namespace OmniPadServer.ViGEm;

public enum EmulationType
{
    Xbox360,
    DualShock4
}

public static class PadBackendFactory
{
    public static (IPadBackend Backend, bool IsHardwareGamepad) CreateBackend(
        bool forceKeyboardMouse = false,
        EmulationType emulationType = EmulationType.Xbox360)
    {
        if (forceKeyboardMouse)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[Backend] Forced Virtual Keyboard & Mouse Mode (SendInput).");
            Console.ResetColor();
            return (new VirtualMouseKeyboardBackend(), false);
        }

        try
        {
            IPadBackend backend = emulationType == EmulationType.DualShock4
                ? new ViGEmDualShock4PadBackend()
                : new ViGEmPadBackend();

            Console.ForegroundColor = ConsoleColor.Green;
            string typeStr = emulationType == EmulationType.DualShock4 
                ? "Sony DualShock 4 (PS4 DirectInput / HID)" 
                : "Microsoft Xbox 360 (XInput / WGI)";
            Console.WriteLine($"[Backend] ViGEmBus Kernel Driver Detected! Hardware {typeStr} Emulation active ({IPadBackend.MaxPads} Slots max).");
            Console.ResetColor();
            return (backend, true);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[Backend] ViGEmBus driver not found (" + ex.Message + ").");
            Console.WriteLine("[Backend] Falling back to zero-driver Keyboard/Mouse emulation.");
            Console.WriteLine("[Backend] To enable real Xbox/PS4 controller in games, install ViGEmBus from:");
            Console.WriteLine("          https://github.com/nefarius/ViGEmBus/releases");
            Console.ResetColor();

            return (new VirtualMouseKeyboardBackend(), false);
        }
    }
}
