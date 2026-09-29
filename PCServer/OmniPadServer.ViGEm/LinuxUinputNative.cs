using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace OmniPadServer.ViGEm;

#region Linux Input Structures & Constants

public static class LinuxUinputConstants
{
    // Event types (linux/input.h)
    public const ushort EV_SYN = 0x00;
    public const ushort EV_KEY = 0x01;
    public const ushort EV_REL = 0x02;
    public const ushort EV_ABS = 0x03;
    public const ushort EV_MSC = 0x04;
    public const ushort EV_SW = 0x05;
    public const ushort EV_LED = 0x11;
    public const ushort EV_SND = 0x12;
    public const ushort EV_REP = 0x14;
    public const ushort EV_FF = 0x15;

    // Synchronization events
    public const ushort SYN_REPORT = 0;
    public const ushort SYN_CONFIG = 1;
    public const ushort SYN_MT_REPORT = 2;
    public const ushort SYN_DROPPED = 3;

    // Relative axes
    public const ushort REL_X = 0x00;
    public const ushort REL_Y = 0x01;
    public const ushort REL_Z = 0x02;
    public const ushort REL_RX = 0x03;
    public const ushort REL_RY = 0x04;
    public const ushort REL_RZ = 0x05;
    public const ushort REL_HWHEEL = 0x06;
    public const ushort REL_DIAL = 0x07;
    public const ushort REL_WHEEL = 0x08;
    public const ushort REL_MISC = 0x09;

    // Absolute axes
    public const ushort ABS_X = 0x00;
    public const ushort ABS_Y = 0x01;
    public const ushort ABS_Z = 0x02;
    public const ushort ABS_RX = 0x03;
    public const ushort ABS_RY = 0x04;
    public const ushort ABS_RZ = 0x05;
    public const ushort ABS_THROTTLE = 0x06;
    public const ushort ABS_RUDDER = 0x07;
    public const ushort ABS_WHEEL = 0x08;
    public const ushort ABS_GAS = 0x09;
    public const ushort ABS_BRAKE = 0x0A;
    public const ushort ABS_HAT0X = 0x10;
    public const ushort ABS_HAT0Y = 0x11;

    // Buttons: Mouse
    public const ushort BTN_MOUSE = 0x110;
    public const ushort BTN_LEFT = 0x110;
    public const ushort BTN_RIGHT = 0x111;
    public const ushort BTN_MIDDLE = 0x112;
    public const ushort BTN_SIDE = 0x113;
    public const ushort BTN_EXTRA = 0x114;

    // Buttons: Gamepad
    public const ushort BTN_MISC = 0x100;
    public const ushort BTN_0 = 0x100;
    public const ushort BTN_1 = 0x101;
    public const ushort BTN_2 = 0x102;
    public const ushort BTN_3 = 0x103;

    public const ushort BTN_JOYSTICK = 0x120;
    public const ushort BTN_GAMEPAD = 0x130;
    public const ushort BTN_A = 0x130;
    public const ushort BTN_SOUTH = 0x130;
    public const ushort BTN_B = 0x131;
    public const ushort BTN_EAST = 0x131;
    public const ushort BTN_C = 0x132;
    public const ushort BTN_NORTH = 0x133;
    public const ushort BTN_X = 0x133;
    public const ushort BTN_WEST = 0x134;
    public const ushort BTN_Y = 0x134;
    public const ushort BTN_Z = 0x135;
    public const ushort BTN_TL = 0x136;
    public const ushort BTN_TR = 0x137;
    public const ushort BTN_TL2 = 0x138;
    public const ushort BTN_TR2 = 0x139;
    public const ushort BTN_SELECT = 0x13A;
    public const ushort BTN_START = 0x13B;
    public const ushort BTN_MODE = 0x13C;
    public const ushort BTN_THUMBL = 0x13D;
    public const ushort BTN_THUMBR = 0x13E;

    // D-Pad buttons
    public const ushort BTN_DPAD_UP = 0x220;
    public const ushort BTN_DPAD_DOWN = 0x221;
    public const ushort BTN_DPAD_LEFT = 0x222;
    public const ushort BTN_DPAD_RIGHT = 0x223;

    // Touchpad button
    public const ushort KEY_TOUCHPAD = 0x140;

    // Bus types
    public const ushort BUS_PCI = 0x01;
    public const ushort BUS_ISAPNP = 0x02;
    public const ushort BUS_USB = 0x03;
    public const ushort BUS_HIL = 0x04;
    public const ushort BUS_BLUETOOTH = 0x05;
    public const ushort BUS_VIRTUAL = 0x06;

    // ioctls
    public const uint UI_DEV_CREATE = 0x5501;
    public const uint UI_DEV_DESTROY = 0x5502;
    public const uint UI_DEV_SETUP = 0x405C5503;
    public const uint UI_ABS_SETUP = 0x401C5504;
    public const uint UI_SET_EVBIT = 0x40045564;
    public const uint UI_SET_KEYBIT = 0x40045565;
    public const uint UI_SET_RELBIT = 0x40045566;
    public const uint UI_SET_ABSBIT = 0x40045567;
    public const uint UI_SET_MSCBIT = 0x40045568;
    public const uint UI_SET_PROPBIT = 0x4004556E;

    // POSIX open flags
    public const int O_RDONLY = 0x0000;
    public const int O_WRONLY = 0x0001;
    public const int O_RDWR = 0x0002;
    public const int O_NONBLOCK = 0x0800; // 2048 decimal (Linux x86_64 / arm64)
}

[StructLayout(LayoutKind.Sequential)]
public struct InputId
{
    public ushort Bustype;
    public ushort Vendor;
    public ushort Product;
    public ushort Version;
}

[StructLayout(LayoutKind.Sequential)]
public struct InputAbsInfo
{
    public int Value;
    public int Minimum;
    public int Maximum;
    public int Fuzz;
    public int Flat;
    public int Resolution;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
public unsafe struct UinputSetup
{
    public InputId Id;
    public fixed byte Name[80];
    public uint FfEffectsMax;

    public void SetName(string name)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(name);
        fixed (byte* p = Name)
        {
            int len = Math.Min(bytes.Length, 79);
            for (int i = 0; i < len; i++) p[i] = bytes[i];
            p[len] = 0;
        }
    }

    public string GetName()
    {
        fixed (byte* p = Name)
        {
            return Marshal.PtrToStringAnsi((IntPtr)p) ?? string.Empty;
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct UinputAbsSetup
{
    public ushort Code;
    public InputAbsInfo Absinfo;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
public unsafe struct UinputUserDev
{
    public fixed byte Name[80];
    public InputId Id;
    public int FfEffectsMax;
    public fixed int Absmax[64];
    public fixed int Absmin[64];
    public fixed int Absfuzz[64];
    public fixed int Absflat[64];

    public void SetName(string name)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(name);
        fixed (byte* p = Name)
        {
            int len = Math.Min(bytes.Length, 79);
            for (int i = 0; i < len; i++) p[i] = bytes[i];
            p[len] = 0;
        }
    }

    public string GetName()
    {
        fixed (byte* p = Name)
        {
            return Marshal.PtrToStringAnsi((IntPtr)p) ?? string.Empty;
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct InputEvent
{
    public nint TimeSec;
    public nint TimeUsec;
    public ushort Type;
    public ushort Code;
    public int Value;

    public InputEvent(ushort type, ushort code, int value)
    {
        TimeSec = 0;
        TimeUsec = 0;
        Type = type;
        Code = code;
        Value = value;
    }
}

#endregion

#region Linux Native POSIX Bridge

public interface IUinputNativeBridge
{
    int Open(string path, int flags);
    int Ioctl(int fd, uint request, int arg);
    int IoctlSetup(int fd, in UinputSetup setup);
    int IoctlAbsSetup(int fd, in UinputAbsSetup absSetup);
    int Write(int fd, ReadOnlySpan<InputEvent> events);
    int WriteUserDev(int fd, in UinputUserDev userDev);
    int Close(int fd);
}

public sealed class LibcUinputNativeBridge : IUinputNativeBridge
{
    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int PosixOpen(string pathname, int flags);

    [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static extern int PosixIoctl(int fd, nuint request, int arg);

    [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static extern int PosixIoctl(int fd, nuint request, in UinputSetup setup);

    [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static extern int PosixIoctl(int fd, nuint request, in UinputAbsSetup absSetup);

    [DllImport("libc", EntryPoint = "write", SetLastError = true)]
    private static extern unsafe nint PosixWrite(int fd, void* buffer, nuint count);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int PosixClose(int fd);

    public int Open(string path, int flags) => PosixOpen(path, flags);

    public int Ioctl(int fd, uint request, int arg) => PosixIoctl(fd, request, arg);

    public int IoctlSetup(int fd, in UinputSetup setup) => PosixIoctl(fd, request: LinuxUinputConstants.UI_DEV_SETUP, setup);

    public int IoctlAbsSetup(int fd, in UinputAbsSetup absSetup) => PosixIoctl(fd, request: LinuxUinputConstants.UI_DEV_SETUP, absSetup);

    public unsafe int Write(int fd, ReadOnlySpan<InputEvent> events)
    {
        fixed (InputEvent* ptr = events)
        {
            nuint bytes = (nuint)(events.Length * sizeof(InputEvent));
            return (int)PosixWrite(fd, ptr, bytes);
        }
    }

    public unsafe int WriteUserDev(int fd, in UinputUserDev userDev)
    {
        fixed (UinputUserDev* ptr = &userDev)
        {
            nuint bytes = (nuint)sizeof(UinputUserDev);
            return (int)PosixWrite(fd, ptr, bytes);
        }
    }

    public int Close(int fd) => PosixClose(fd);
}

/// <summary>
/// In-memory mock bridge for unit testing and running on platforms without /dev/uinput.
/// </summary>
public sealed class InMemoryUinputNativeBridge : IUinputNativeBridge
{
    private int _nextFd = 100;
    public readonly HashSet<int> OpenFds = new();
    public readonly List<(int Fd, uint Request, int Arg)> Ioctls = new();
    public readonly List<(int Fd, UinputSetup Setup)> Setups = new();
    public readonly List<(int Fd, UinputAbsSetup AbsSetup)> AbsSetups = new();
    public readonly List<(int Fd, List<InputEvent> Events)> WrittenEvents = new();
    public readonly List<(int Fd, UinputUserDev UserDev)> UserDevs = new();

    public int Open(string path, int flags)
    {
        int fd = ++_nextFd;
        OpenFds.Add(fd);
        return fd;
    }

    public int Ioctl(int fd, uint request, int arg)
    {
        Ioctls.Add((fd, request, arg));
        return 0;
    }

    public int IoctlSetup(int fd, in UinputSetup setup)
    {
        Setups.Add((fd, setup));
        return 0;
    }

    public int IoctlAbsSetup(int fd, in UinputAbsSetup absSetup)
    {
        AbsSetups.Add((fd, absSetup));
        return 0;
    }

    public int Write(int fd, ReadOnlySpan<InputEvent> events)
    {
        var list = new List<InputEvent>(events.Length);
        for (int i = 0; i < events.Length; i++) list.Add(events[i]);
        WrittenEvents.Add((fd, list));
        return events.Length * Marshal.SizeOf<InputEvent>();
    }

    public int WriteUserDev(int fd, in UinputUserDev userDev)
    {
        UserDevs.Add((fd, userDev));
        return Marshal.SizeOf<UinputUserDev>();
    }

    public int Close(int fd)
    {
        OpenFds.Remove(fd);
        return 0;
    }
}

#endregion
