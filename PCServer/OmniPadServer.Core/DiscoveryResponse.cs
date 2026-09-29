using System;
using System.Buffers.Binary;
using System.Text;

namespace OmniPadServer.Core;

/// <summary>
/// Encapsulates the extended UDP discovery response packet.
/// Wire format:
///   [0]: Magic (0xDA)
///   [1]: Version (1)
///   [2]: MsgWelcome (0x03)
///   [3]: NoPad (0xFF)
///   [4..5]: WebPort (uint16 little endian, e.g. 27502)
///   [6]: Hostname byte length (uint8, max 64)
///   [7..]: Hostname (UTF-8 encoded string)
/// </summary>
public readonly struct DiscoveryResponse
{
    public const int HeaderSize = 7;
    public const int MaxHostnameLength = 64;

    public readonly ushort WebPort;
    public readonly string Hostname;

    public DiscoveryResponse(ushort webPort, string hostname)
    {
        WebPort = webPort;
        Hostname = hostname ?? string.Empty;
    }

    public static byte[] Encode(ushort webPort, string hostname)
    {
        byte[] nameBytes = Encoding.UTF8.GetBytes(hostname ?? string.Empty);
        int nameLen = Math.Min(nameBytes.Length, MaxHostnameLength);
        byte[] buffer = new byte[HeaderSize + nameLen];

        buffer[0] = Protocol.MagicByte;
        buffer[1] = Protocol.Version;
        buffer[2] = Protocol.MsgWelcome;
        buffer[3] = Protocol.NoPad;
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4, 2), webPort);
        buffer[6] = (byte)nameLen;

        if (nameLen > 0)
        {
            Buffer.BlockCopy(nameBytes, 0, buffer, HeaderSize, nameLen);
        }

        return buffer;
    }

    public static bool TryParse(ReadOnlySpan<byte> buffer, out DiscoveryResponse response)
    {
        response = default;

        if (buffer.Length < Protocol.SessionMessageSize)
            return false;

        if (buffer[0] != Protocol.MagicByte || buffer[1] != Protocol.Version || buffer[2] != Protocol.MsgWelcome)
            return false;

        ushort port = Protocol.DefaultWebPort;
        string name = string.Empty;

        if (buffer.Length >= HeaderSize)
        {
            port = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(4, 2));
            int nameLen = buffer[6];
            if (nameLen > 0 && buffer.Length >= HeaderSize + nameLen)
            {
                name = Encoding.UTF8.GetString(buffer.Slice(HeaderSize, nameLen));
            }
        }

        response = new DiscoveryResponse(port, name);
        return true;
    }
}
