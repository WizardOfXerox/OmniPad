using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;

namespace OmniPadServer.App;

/// <summary>
/// High-performance, zero-driver Windows SendInput simulator.
/// Executes all mouse and keyboard actions on a dedicated native Win32 thread attached
/// to the user's interactive desktop (WinSta0\default) to guarantee 100% execution
/// without UIPI or desktop isolation blocks.
/// </summary>
public static class WindowsInputSimulator
{
    private const int INPUT_MOUSE = 0;
    private const int INPUT_KEYBOARD = 1;

    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;

    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;

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
    private static extern uint SendInput(uint nInputs, [In] INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyW(uint uCode, uint uMapType);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenDesktop(string lpszDesktop, uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetThreadDesktop(IntPtr hDesktop);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(IntPtr hDesktop);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint NativeThreadProc(IntPtr lpParameter);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateThread(
        IntPtr lpThreadAttributes,
        uint dwStackSize,
        NativeThreadProc lpStartAddress,
        IntPtr lpParameter,
        uint dwCreationFlags,
        out uint lpThreadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private enum InputType { Move, Button, Wheel, Key }

    private readonly struct InputCommand
    {
        public readonly InputType Type;
        public readonly short X;
        public readonly short Y;
        public readonly byte ButtonMask;
        public readonly bool IsDown;
        public readonly ushort VkCode;

        public InputCommand(InputType type, short x, short y, byte btnMask = 0, bool isDown = false, ushort vk = 0)
        {
            Type = type;
            X = x;
            Y = y;
            ButtonMask = btnMask;
            IsDown = isDown;
            VkCode = vk;
        }
    }

    private static readonly BlockingCollection<InputCommand> _queue = new(new ConcurrentQueue<InputCommand>());
    private static NativeThreadProc? _nativeThreadProc;
    private static IntPtr _hThread = IntPtr.Zero;
    private static bool _initialized;
    private static readonly object _initLock = new();

    private static void EnsureWorkerRunning()
    {
        if (_initialized) return;
        lock (_initLock)
        {
            if (_initialized) return;

            if (OperatingSystem.IsWindows())
            {
                _nativeThreadProc = NativeWorkerLoop;
                _hThread = CreateThread(IntPtr.Zero, 0, _nativeThreadProc, IntPtr.Zero, 0, out _);
                if (_hThread != IntPtr.Zero)
                {
                    _initialized = true;
                    CloseHandle(_hThread);
                    _hThread = IntPtr.Zero;
                }
            }
        }
    }

    private static uint NativeWorkerLoop(IntPtr param)
    {
        IntPtr hDesk = OpenDesktop("default", 0, false, 0x01FF);
        if (hDesk != IntPtr.Zero)
        {
            bool ok = SetThreadDesktop(hDesk);
            Console.WriteLine($"[WindowsInputSimulator] Attached native input thread to interactive desktop: {ok}");
        }

        try
        {
            foreach (var cmd in _queue.GetConsumingEnumerable())
            {
                switch (cmd.Type)
                {
                    case InputType.Move:
                        ExecuteMouseMove(cmd.X, cmd.Y);
                        break;
                    case InputType.Button:
                        ExecuteMouseButton(cmd.ButtonMask, cmd.IsDown);
                        break;
                    case InputType.Wheel:
                        ExecuteMouseWheel(cmd.Y);
                        break;
                    case InputType.Key:
                        ExecuteKeyboardKey(cmd.VkCode, cmd.IsDown);
                        break;
                }
            }
        }
        catch { }
        finally
        {
            if (hDesk != IntPtr.Zero)
            {
                CloseDesktop(hDesk);
            }
        }
        return 0;
    }

    public static void MouseMove(short dx, short dy)
    {
        if (dx == 0 && dy == 0) return;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            OmniPadServer.ViGEm.MacInputSimulator.MouseMove(dx, dy);
            return;
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            OmniPadServer.ViGEm.LinuxUinputMouseKeyboardBackend.SendMouseMove(dx, dy);
            return;
        }
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        EnsureWorkerRunning();
        _queue.Add(new InputCommand(InputType.Move, dx, dy));
    }

    public static void MouseButton(byte buttonMask, bool isDown)
    {
        if (buttonMask == 0) return;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            OmniPadServer.ViGEm.MacInputSimulator.MouseButton(buttonMask, isDown);
            return;
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            OmniPadServer.ViGEm.LinuxUinputMouseKeyboardBackend.SendMouseButton(buttonMask, isDown);
            return;
        }
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        EnsureWorkerRunning();
        _queue.Add(new InputCommand(InputType.Button, 0, 0, buttonMask, isDown));
    }

    public static void MouseWheel(short delta)
    {
        if (delta == 0) return;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            OmniPadServer.ViGEm.MacInputSimulator.MouseWheel(delta);
            return;
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            OmniPadServer.ViGEm.LinuxUinputMouseKeyboardBackend.SendMouseWheel(delta);
            return;
        }
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        EnsureWorkerRunning();
        _queue.Add(new InputCommand(InputType.Wheel, 0, delta));
    }

    public static void KeyboardKey(ushort vkCode, bool isDown)
    {
        if (vkCode == 0) return;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            OmniPadServer.ViGEm.MacInputSimulator.KeyboardKey(vkCode, isDown, isVk: true);
            return;
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            OmniPadServer.ViGEm.LinuxUinputMouseKeyboardBackend.SendKeyboardKey(vkCode, isDown);
            return;
        }
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        EnsureWorkerRunning();
        _queue.Add(new InputCommand(InputType.Key, 0, 0, 0, isDown, vkCode));
    }

    private static void ExecuteMouseMove(short dx, short dy)
    {
        INPUT[] inputs =
        [
            new INPUT
            {
                type = INPUT_MOUSE,
                u = new INPUT_UNION
                {
                    mi = new MOUSEINPUT
                    {
                        dx = dx,
                        dy = dy,
                        dwFlags = MOUSEEVENTF_MOVE,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            }
        ];

        SendInput(1, inputs, Marshal.SizeOf<INPUT>());
    }

    private static void ExecuteMouseButton(byte buttonMask, bool isDown)
    {
        uint flags = 0;
        if ((buttonMask & 1) != 0) // Left
            flags |= isDown ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP;
        if ((buttonMask & 2) != 0) // Right
            flags |= isDown ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP;
        if ((buttonMask & 4) != 0) // Middle
            flags |= isDown ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP;

        if (flags == 0) return;

        INPUT[] inputs =
        [
            new INPUT
            {
                type = INPUT_MOUSE,
                u = new INPUT_UNION
                {
                    mi = new MOUSEINPUT
                    {
                        dwFlags = flags,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            }
        ];

        uint ret = SendInput(1, inputs, Marshal.SizeOf<INPUT>());
        Console.WriteLine($"[WindowsInputSimulator] MouseBtn mask={buttonMask} down={isDown} (ret={ret})");
    }

    private static void ExecuteMouseWheel(short delta)
    {
        INPUT[] inputs =
        [
            new INPUT
            {
                type = INPUT_MOUSE,
                u = new INPUT_UNION
                {
                    mi = new MOUSEINPUT
                    {
                        dwFlags = MOUSEEVENTF_WHEEL,
                        mouseData = (uint)(int)delta,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            }
        ];

        uint ret = SendInput(1, inputs, Marshal.SizeOf<INPUT>());
        Console.WriteLine($"[WindowsInputSimulator] MouseWheel delta={delta} (ret={ret})");
    }

    private static void ExecuteKeyboardKey(ushort vkCode, bool isDown)
    {
        uint flags = isDown ? 0 : KEYEVENTF_KEYUP;
        // Extended keys: Arrows (0x25-0x28), PageUp/Down (0x21-0x22), End/Home (0x23-0x24), Insert/Delete (0x2D-0x2E), Win keys (0x5B-0x5C), Right Alt (0xA5), Right Ctrl (0xA3)
        if (vkCode is >= 0x21 and <= 0x28 or 0x2D or 0x2E or 0x5B or 0x5C or 0xA3 or 0xA5)
        {
            flags |= KEYEVENTF_EXTENDEDKEY;
        }

        uint scan = MapVirtualKeyW(vkCode, 0); // MAPVK_VK_TO_VSC = 0

        INPUT[] inputs =
        [
            new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new INPUT_UNION
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = vkCode,
                        wScan = (ushort)scan,
                        dwFlags = flags,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            }
        ];

        uint ret = SendInput(1, inputs, Marshal.SizeOf<INPUT>());
        Console.WriteLine($"[WindowsInputSimulator] Key vk=0x{vkCode:X2} scan=0x{scan:X2} down={isDown} (ret={ret})");
    }
}
