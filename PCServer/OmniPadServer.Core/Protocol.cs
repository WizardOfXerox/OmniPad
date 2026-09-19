using System;

namespace OmniPadServer.Core;

/// <summary>
/// OmniPad canonical wire protocol specification.
/// UDP Little-Endian 20-byte Input Frame.
/// </summary>
public static class Protocol
{
    public const byte MagicByte = 0xDA;
    public const byte Version = 1;

    public const int DefaultInputPort = 27500;
    public const int DiscoveryPort = 27501;
    public const int DefaultWebPort = 27502;

    public const int DefaultDsuPort = 26760;

    public const int InputPacketSize = 20;
    public const int SessionMessageSize = 4;
    public const int RumbleMessageSize = 6;
    public const int PingMessageSize = 12; // Magic(1) + Ver(1) + Type(1) + Pad(1) + Timestamp(8)
    public const int MotionPacketSize = 36; // Magic(1) + Ver(1) + Type(1) + Pad(1) + Timestamp(8) + Accel(12) + Gyro(12)
    public const int TouchpadPacketSize = 13; // Magic(1) + Ver(1) + Type(1) + Pad(1) + Flags(1) + F0(4) + F1(4)

    public const byte NoPad = 0xFF;
    public const double SessionTimeoutSeconds = 5.0;

    // Message Types
    public const byte MsgInput = 0x01;
    public const byte MsgHello = 0x02;
    public const byte MsgWelcome = 0x03;
    public const byte MsgBye = 0x04;
    public const byte MsgRumble = 0x05;
    public const byte MsgDiscover = 0x06;
    public const byte MsgPing = 0x07;
    public const byte MsgPong = 0x08;
    public const byte MsgSlotStatus = 0x09;
    public const byte MsgSwitchSlot = 0x0A;
    public const byte MsgSwapRequest = 0x0B;
    public const byte MsgSwapPrompt = 0x0C;
    public const byte MsgSwapResponse = 0x0D;
    public const byte MsgSwapDeclined = 0x0E;
    public const byte MsgMotion = 0x10;
    public const byte MsgTouchpad = 0x11;
    public const byte MsgSetControllerType = 0x12;
    public const byte MsgActiveProfile = 0x13;

    // Button Bitmasks (exact match to Windows XINPUT_GAMEPAD + PS4 Touchpad)
    [Flags]
    public enum Buttons : ushort
    {
        None = 0,
        DPadUp = 0x0001,
        DPadDown = 0x0002,
        DPadLeft = 0x0004,
        DPadRight = 0x0008,
        Start = 0x0010,
        Back = 0x0020,
        LeftThumb = 0x0040,
        RightThumb = 0x0080,
        LeftShoulder = 0x0100,
        RightShoulder = 0x0200,
        Guide = 0x0400,
        Touchpad = 0x0800,
        A = 0x1000,
        B = 0x2000,
        X = 0x4000,
        Y = 0x8000
    }
}
