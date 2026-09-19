using System;
using System.Buffers.Binary;
using System.Text;
using OmniPadServer.App;
using OmniPadServer.Core;
using Xunit;

namespace OmniPadServer.Tests;

public class DsuProtocolTests
{
    [Fact]
    public void Crc32_StandardVector_123456789_MatchesSpecification()
    {
        // The standard test vector for IEEE 802.3 CRC32 of ASCII "123456789" is 0xCBF43926.
        byte[] testBytes = Encoding.ASCII.GetBytes("123456789");
        uint crc = DsuMotionServer.CalculateCrc32(testBytes);
        Assert.Equal(0xCBF43926u, crc);
    }

    [Fact]
    public void DsuServer_CanInstantiateAndManagePorts()
    {
        // Test port initialization and state updates without network collisions
        using var server = new DsuMotionServer(26769); // Use dedicated test port
        server.Start();
        Assert.True(server.IsRunning);

        var padState = PadState.Neutral;
        padState.SetButton(Protocol.Buttons.A, true);
        padState.SetButton(Protocol.Buttons.Touchpad, true);
        server.UpdatePadState(0, in padState);

        var motion = new MotionState
        {
            TimestampUs = 1000000,
            AccelX = 0f,
            AccelY = 1f,
            AccelZ = 0f,
            GyroX = 5f,
            GyroY = 10f,
            GyroZ = 15f
        };
        server.UpdateMotion(0, in motion);

        var touch = new TouchpadState
        {
            Clicked = true,
            Finger0 = new TouchPoint { IsActive = true, Id = 0, X = 960, Y = 471 }
        };
        server.UpdateTouchpad(0, in touch);
    }

    [Fact]
    public async Task DsuServer_VersionRequest_ReturnsProtocolVersion1001()
    {
        using var server = new DsuMotionServer(26768);
        server.Start();

        using var client = new System.Net.Sockets.UdpClient();
        client.Client.ReceiveTimeout = 2000;

        // Build valid DSUC Version Request
        byte[] request = new byte[20]; // 16 header + 4 payload (action 0x100000)
        request[0] = (byte)'D';
        request[1] = (byte)'S';
        request[2] = (byte)'U';
        request[3] = (byte)'C';
        BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(4, 2), 1001);
        BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(6, 2), 4); // 4 bytes payload
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(12, 4), 0x12345678); // ClientId
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(16, 4), 0x100000); // Version request action

        // Compute and write CRC32
        uint crc = DsuMotionServer.CalculateCrc32(request);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(8, 4), crc);

        var targetEp = new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 26768);
        await client.SendAsync(request, request.Length, targetEp);

        var result = await client.ReceiveAsync();
        byte[] response = result.Buffer;

        Assert.True(response.Length >= 22);
        Assert.Equal((byte)'D', response[0]);
        Assert.Equal((byte)'S', response[1]);
        Assert.Equal((byte)'U', response[2]);
        Assert.Equal((byte)'S', response[3]);

        uint action = BinaryPrimitives.ReadUInt32LittleEndian(response.AsSpan(16, 4));
        Assert.Equal(0x100000u, action);

        ushort reportedVersion = BinaryPrimitives.ReadUInt16LittleEndian(response.AsSpan(20, 2));
        Assert.Equal(1001, reportedVersion);
    }
}
