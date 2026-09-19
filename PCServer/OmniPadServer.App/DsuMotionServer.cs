using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using OmniPadServer.Core;

namespace OmniPadServer.App;

/// <summary>
/// Hardware-grade CemuHook DSU UDP Motion Server.
/// Listens on UDP port 26760 (default), delivering 100Hz 6-axis gyro, accelerometer,
/// gamepad state, and touchpad coordinates to emulators (Cemu, Yuzu, Ryujinx, Dolphin, Citra, PCSX2, RPCS3).
/// </summary>
public sealed class DsuMotionServer : IDisposable
{
    private const ushort ProtocolVersion = 1001;
    private const int HeaderSize = 16;
    private const int ReportPacketSize = 100;
    private const double ClientTimeoutSeconds = 5.0;

    private readonly UdpClient _socket;
    private readonly uint _serverId;
    private readonly CancellationTokenSource _cts = new();

    private readonly PadState[] _padStates = new PadState[IPadBackend.MaxPads];
    private readonly MotionState[] _motionStates = new MotionState[IPadBackend.MaxPads];
    private readonly TouchpadState[] _touchpadStates = new TouchpadState[IPadBackend.MaxPads];
    private readonly uint[] _packetCounters = new uint[IPadBackend.MaxPads];
    private readonly object _stateLock = new();

    private readonly ConcurrentDictionary<IPEndPoint, DateTime> _subscribedClients = new();

    private Task? _receiveTask;
    private Task? _broadcastTask;
    private bool _disposed;

    // Fast table-driven IEEE 802.3 CRC32
    private static readonly uint[] CrcTable = InitializeCrcTable();

    public int Port { get; }
    public bool IsRunning { get; private set; }

    public DsuMotionServer(int port = Protocol.DefaultDsuPort)
    {
        Port = port;
        _serverId = (uint)Random.Shared.Next(1, int.MaxValue);

        _socket = new UdpClient();
        _socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _socket.Client.Bind(new IPEndPoint(IPAddress.Any, port));

        // Initialize neutral states
        for (int i = 0; i < IPadBackend.MaxPads; i++)
        {
            _padStates[i] = PadState.Neutral;
            _motionStates[i] = MotionState.Neutral;
            _touchpadStates[i] = TouchpadState.Neutral;
        }
    }

    public void Start()
    {
        if (IsRunning) return;
        IsRunning = true;

        _receiveTask = Task.Run(ReceiveLoopAsync);
        _broadcastTask = Task.Run(BroadcastLoopAsync);
    }

    public void UpdatePadState(int slot, in PadState state)
    {
        if (slot < 0 || slot >= IPadBackend.MaxPads) return;
        lock (_stateLock)
        {
            _padStates[slot] = state;
        }
    }

    public void UpdateMotion(int slot, in MotionState motion)
    {
        if (slot < 0 || slot >= IPadBackend.MaxPads) return;
        lock (_stateLock)
        {
            _motionStates[slot] = motion;
        }
    }

    public void UpdateTouchpad(int slot, in TouchpadState touchpad)
    {
        if (slot < 0 || slot >= IPadBackend.MaxPads) return;
        lock (_stateLock)
        {
            _touchpadStates[slot] = touchpad;
        }
    }

    private async Task ReceiveLoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var result = await _socket.ReceiveAsync(_cts.Token);
                var data = result.Buffer;
                var endpoint = result.RemoteEndPoint;

                if (!ValidateIncomingPacket(data, out int payloadOffset, out uint messageType))
                    continue;

                switch (messageType)
                {
                    case 0x100000: // Version Request
                        await SendVersionResponseAsync(endpoint);
                        break;

                    case 0x100001: // List Ports / Port Info Request
                        int reqCount = data.Length >= payloadOffset + 8 ? BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(payloadOffset + 4, 4)) : 1;
                        await SendPortInfoResponseAsync(endpoint, reqCount);
                        break;

                    case 0x100002: // Pad Data Request / Subscription
                        _subscribedClients[endpoint] = DateTime.UtcNow;
                        break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!_disposed)
                Console.WriteLine($"[DSU Motion] Receive error: {ex.Message}");
        }
    }

    private async Task BroadcastLoopAsync()
    {
        byte[] reportBuffer = new byte[ReportPacketSize];
        var periodicTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(10)); // 100Hz standard update rate

        try
        {
            while (await periodicTimer.WaitForNextTickAsync(_cts.Token))
            {
                if (_subscribedClients.IsEmpty)
                    continue;

                // Prune dead clients (>5s timeout)
                var now = DateTime.UtcNow;
                foreach (var (client, lastSeen) in _subscribedClients)
                {
                    if ((now - lastSeen).TotalSeconds > ClientTimeoutSeconds)
                    {
                        _subscribedClients.TryRemove(client, out _);
                    }
                }

                if (_subscribedClients.IsEmpty)
                    continue;

                // Send 100Hz report for active slots (Slots 0..3 standard emulation)
                for (int slot = 0; slot < Math.Min(4, IPadBackend.MaxPads); slot++)
                {
                    PadState pad;
                    MotionState motion;
                    TouchpadState touch;
                    uint counter;

                    lock (_stateLock)
                    {
                        pad = _padStates[slot];
                        motion = _motionStates[slot];
                        touch = _touchpadStates[slot];
                        counter = ++_packetCounters[slot];
                    }

                    BuildPadDataReport(slot, counter, in pad, in motion, in touch, reportBuffer);

                    foreach (var client in _subscribedClients.Keys)
                    {
                        try
                        {
                            await _socket.SendAsync(reportBuffer, reportBuffer.Length, client);
                        }
                        catch { }
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private bool ValidateIncomingPacket(ReadOnlySpan<byte> buffer, out int payloadOffset, out uint messageType)
    {
        payloadOffset = 0;
        messageType = 0;

        if (buffer.Length < 20)
            return false;

        // Check magic 'DSUC' (0x43555344)
        if (buffer[0] != 'D' || buffer[1] != 'S' || buffer[2] != 'U' || buffer[3] != 'C')
            return false;

        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(4, 2));
        if (version > ProtocolVersion)
            return false;

        ushort payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(6, 2));
        if (payloadLength + HeaderSize > buffer.Length)
            return false;

        uint packetCrc = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(8, 4));

        // Zero out CRC field for verification
        Span<byte> checkBuffer = stackalloc byte[HeaderSize + payloadLength];
        buffer[..(HeaderSize + payloadLength)].CopyTo(checkBuffer);
        checkBuffer.Slice(8, 4).Clear();

        uint calculatedCrc = CalculateCrc32(checkBuffer);
        if (packetCrc != calculatedCrc)
            return false;

        payloadOffset = HeaderSize;
        messageType = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(16, 4));
        return true;
    }

    private async Task SendVersionResponseAsync(IPEndPoint target)
    {
        byte[] response = new byte[HeaderSize + 6];
        int idx = BeginPacket(response);

        BinaryPrimitives.WriteUInt32LittleEndian(response.AsSpan(idx, 4), 0x100000); // DsusVersion
        idx += 4;
        BinaryPrimitives.WriteUInt16LittleEndian(response.AsSpan(idx, 2), ProtocolVersion);
        idx += 2;

        FinishPacket(response);
        await _socket.SendAsync(response, response.Length, target);
    }

    private async Task SendPortInfoResponseAsync(IPEndPoint target, int count)
    {
        if (count <= 0) count = 1;

        byte[] response = new byte[HeaderSize + 16];

        for (int i = 0; i < count; i++)
        {
            byte padId = (byte)i;
            if (padId >= 4) continue;

            response.AsSpan().Clear();
            int idx = BeginPacket(response);

            BinaryPrimitives.WriteUInt32LittleEndian(response.AsSpan(idx, 4), 0x100001); // DsusPortInfo
            idx += 4;

            response[idx++] = padId;               // Pad ID
            response[idx++] = 0x02;                // State: Connected
            response[idx++] = 0x02;                // Model: DualShock 4
            response[idx++] = 0x02;                // Connection: Bluetooth (2) / USB (1)

            // Synthetic MAC: 00:43:48:49:41:slot (ASCII "CHIA")
            response[idx++] = 0x00;
            response[idx++] = 0x43;
            response[idx++] = 0x48;
            response[idx++] = 0x49;
            response[idx++] = 0x41;
            response[idx++] = padId;

            response[idx++] = 0x05;                // Battery: Full (5)
            response[idx++] = 0x01;                // Is Active

            FinishPacket(response);
            await _socket.SendAsync(response, response.Length, target);
        }
    }

    private void BuildPadDataReport(
        int slot, uint packetCounter, in PadState pad, in MotionState motion, in TouchpadState touch, Span<byte> output)
    {
        output.Clear();
        int idx = BeginPacket(output);

        BinaryPrimitives.WriteUInt32LittleEndian(output.Slice(idx, 4), 0x100002); // DsusPadData
        idx += 4;

        output[idx++] = (byte)slot;            // Pad ID
        output[idx++] = 0x02;                  // Connected
        output[idx++] = 0x02;                  // DualShock 4
        output[idx++] = 0x02;                  // Bluetooth

        // MAC: 00:43:48:49:41:slot
        output[idx++] = 0x00;
        output[idx++] = 0x43;
        output[idx++] = 0x48;
        output[idx++] = 0x49;
        output[idx++] = 0x41;
        output[idx++] = (byte)slot;

        output[idx++] = 0x05;                  // Battery Full
        output[idx++] = 0x01;                  // Active

        // Packet Counter (4 bytes)
        BinaryPrimitives.WriteUInt32LittleEndian(output.Slice(idx, 4), packetCounter);
        idx += 4;

        // Buttons 1 (D-Pad, Options, R3, L3, Share)
        byte b1 = 0;
        if (pad.IsButtonPressed(Protocol.Buttons.DPadLeft)) b1 |= 0x80;
        if (pad.IsButtonPressed(Protocol.Buttons.DPadDown)) b1 |= 0x40;
        if (pad.IsButtonPressed(Protocol.Buttons.DPadRight)) b1 |= 0x20;
        if (pad.IsButtonPressed(Protocol.Buttons.DPadUp)) b1 |= 0x10;
        if (pad.IsButtonPressed(Protocol.Buttons.Start)) b1 |= 0x08;
        if (pad.IsButtonPressed(Protocol.Buttons.RightThumb)) b1 |= 0x04;
        if (pad.IsButtonPressed(Protocol.Buttons.LeftThumb)) b1 |= 0x02;
        if (pad.IsButtonPressed(Protocol.Buttons.Back)) b1 |= 0x01;
        output[idx++] = b1;

        // Buttons 2 (Square, Cross, Circle, Triangle, R1, L1, R2, L2)
        byte b2 = 0;
        if (pad.IsButtonPressed(Protocol.Buttons.X)) b2 |= 0x80; // Square
        if (pad.IsButtonPressed(Protocol.Buttons.A)) b2 |= 0x40; // Cross
        if (pad.IsButtonPressed(Protocol.Buttons.B)) b2 |= 0x20; // Circle
        if (pad.IsButtonPressed(Protocol.Buttons.Y)) b2 |= 0x10; // Triangle
        if (pad.IsButtonPressed(Protocol.Buttons.RightShoulder)) b2 |= 0x08; // R1
        if (pad.IsButtonPressed(Protocol.Buttons.LeftShoulder)) b2 |= 0x04;  // L1
        if (pad.RightTrigger == 255) b2 |= 0x02;                            // R2
        if (pad.LeftTrigger == 255) b2 |= 0x01;                             // L2
        output[idx++] = b2;

        output[idx++] = (byte)(pad.IsButtonPressed(Protocol.Buttons.Guide) ? 1 : 0); // PS
        output[idx++] = (byte)(pad.IsButtonPressed(Protocol.Buttons.Touchpad) || touch.Clicked ? 1 : 0); // Touchpad

        // Sticks (0..255, center 128)
        byte lx = (byte)Math.Clamp((pad.ThumbLX + 32768) >> 8, 0, 255);
        byte ly = (byte)Math.Clamp((pad.ThumbLY + 32768) >> 8, 0, 255);
        byte rx = (byte)Math.Clamp((pad.ThumbRX + 32768) >> 8, 0, 255);
        byte ry = (byte)Math.Clamp((pad.ThumbRY + 32768) >> 8, 0, 255);
        output[idx++] = lx;
        output[idx++] = ly;
        output[idx++] = rx;
        output[idx++] = ry;

        // Analog D-Pad & Face buttons (12 bytes)
        output[idx++] = (byte)(pad.IsButtonPressed(Protocol.Buttons.DPadLeft) ? 255 : 0);
        output[idx++] = (byte)(pad.IsButtonPressed(Protocol.Buttons.DPadDown) ? 255 : 0);
        output[idx++] = (byte)(pad.IsButtonPressed(Protocol.Buttons.DPadRight) ? 255 : 0);
        output[idx++] = (byte)(pad.IsButtonPressed(Protocol.Buttons.DPadUp) ? 255 : 0);

        output[idx++] = (byte)(pad.IsButtonPressed(Protocol.Buttons.X) ? 255 : 0);
        output[idx++] = (byte)(pad.IsButtonPressed(Protocol.Buttons.A) ? 255 : 0);
        output[idx++] = (byte)(pad.IsButtonPressed(Protocol.Buttons.B) ? 255 : 0);
        output[idx++] = (byte)(pad.IsButtonPressed(Protocol.Buttons.Y) ? 255 : 0);

        output[idx++] = (byte)(pad.IsButtonPressed(Protocol.Buttons.RightShoulder) ? 255 : 0);
        output[idx++] = (byte)(pad.IsButtonPressed(Protocol.Buttons.LeftShoulder) ? 255 : 0);
        output[idx++] = pad.RightTrigger;
        output[idx++] = pad.LeftTrigger;

        // Touchpad Finger 0 (6 bytes: Active, Id, X, Y)
        output[idx++] = (byte)(touch.Finger0.IsActive ? 1 : 0);
        output[idx++] = touch.Finger0.Id;
        BinaryPrimitives.WriteUInt16LittleEndian(output.Slice(idx, 2), touch.Finger0.X);
        idx += 2;
        BinaryPrimitives.WriteUInt16LittleEndian(output.Slice(idx, 2), touch.Finger0.Y);
        idx += 2;

        // Touchpad Finger 1 (6 bytes: Active, Id, X, Y)
        output[idx++] = (byte)(touch.Finger1.IsActive ? 1 : 0);
        output[idx++] = touch.Finger1.Id;
        BinaryPrimitives.WriteUInt16LittleEndian(output.Slice(idx, 2), touch.Finger1.X);
        idx += 2;
        BinaryPrimitives.WriteUInt16LittleEndian(output.Slice(idx, 2), touch.Finger1.Y);
        idx += 2;

        // Motion Timestamp (ulong in microseconds)
        ulong timestamp = motion.TimestampUs > 0 ? motion.TimestampUs : (ulong)(DateTime.UtcNow.Ticks / 10);
        BinaryPrimitives.WriteUInt64LittleEndian(output.Slice(idx, 8), timestamp);
        idx += 8;

        // Accelerometer in Gs: BetterJoy / CemuHook coordinate space (Y, -Z, X)
        BinaryPrimitives.WriteUInt32LittleEndian(output.Slice(idx, 4), BitConverter.SingleToUInt32Bits(motion.AccelY));
        idx += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(output.Slice(idx, 4), BitConverter.SingleToUInt32Bits(-motion.AccelZ));
        idx += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(output.Slice(idx, 4), BitConverter.SingleToUInt32Bits(motion.AccelX));
        idx += 4;

        // Gyroscope in deg/s: BetterJoy / CemuHook coordinate space (Pitch = Y, Yaw = Z, Roll = X)
        BinaryPrimitives.WriteUInt32LittleEndian(output.Slice(idx, 4), BitConverter.SingleToUInt32Bits(motion.GyroY));
        idx += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(output.Slice(idx, 4), BitConverter.SingleToUInt32Bits(motion.GyroZ));
        idx += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(output.Slice(idx, 4), BitConverter.SingleToUInt32Bits(motion.GyroX));
        idx += 4;

        FinishPacket(output);
    }

    private int BeginPacket(Span<byte> buffer)
    {
        buffer[0] = (byte)'D';
        buffer[1] = (byte)'S';
        buffer[2] = (byte)'U';
        buffer[3] = (byte)'S';

        BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(4, 2), ProtocolVersion);
        // Length (6..7) filled in FinishPacket
        // CRC (8..11) filled in FinishPacket
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(12, 4), _serverId);

        return HeaderSize;
    }

    private void FinishPacket(Span<byte> buffer)
    {
        ushort payloadSize = (ushort)(buffer.Length - HeaderSize);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(6, 2), payloadSize);

        // Clear CRC field to calculate CRC over entire packet
        buffer.Slice(8, 4).Clear();
        uint crc = CalculateCrc32(buffer);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(8, 4), crc);
    }

    public static uint CalculateCrc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        for (int i = 0; i < data.Length; i++)
        {
            byte index = (byte)((crc ^ data[i]) & 0xFF);
            crc = (crc >> 8) ^ CrcTable[index];
        }
        return ~crc;
    }

    private static uint[] InitializeCrcTable()
    {
        const uint poly = 0xEDB88320;
        uint[] table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint crc = i;
            for (int j = 0; j < 8; j++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ poly : crc >> 1;
            }
            table[i] = crc;
        }
        return table;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        IsRunning = false;

        _cts.Cancel();
        _socket.Dispose();
        _subscribedClients.Clear();
    }
}
