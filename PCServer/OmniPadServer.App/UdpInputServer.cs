using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using OmniPadServer.Core;

namespace OmniPadServer.App;

public sealed class UdpInputServer : IDisposable
{
    private readonly UdpClient _socket;
    private readonly IPadBackend _backend;
    private readonly SessionManager _sessionManager;
    private readonly DsuMotionServer? _dsuServer;
    private readonly TouchpadMouseEngine? _touchpadMouseEngine;
    private readonly GyroAimEngine? _gyroAimEngine;
    private readonly CancellationTokenSource _cts = new();
    private Task? _receiverTask;
    private Task? _timeoutTask;

    private long _packetsReceived;
    public long PacketsReceived => Interlocked.Read(ref _packetsReceived);

    public UdpInputServer(
        IPadBackend backend,
        SessionManager sessionManager,
        DsuMotionServer? dsuServer = null,
        TouchpadMouseEngine? touchpadMouseEngine = null,
        GyroAimEngine? gyroAimEngine = null,
        int port = Protocol.DefaultInputPort)
    {
        _backend = backend;
        _sessionManager = sessionManager;
        _dsuServer = dsuServer;
        _touchpadMouseEngine = touchpadMouseEngine;
        _gyroAimEngine = gyroAimEngine;

        _socket = new UdpClient();
        _socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _socket.Client.Bind(new IPEndPoint(IPAddress.Any, port));

        // Forward in-game rumble from virtual controller to phone
        _backend.RumbleReceived += OnRumbleReceived;
    }

    public void Start()
    {
        _receiverTask = Task.Run(ReceiveLoopAsync);
        _timeoutTask = Task.Run(TimeoutLoopAsync);
    }

    private async Task ReceiveLoopAsync()
    {
        byte[] welcomeBuffer = new byte[Protocol.SessionMessageSize];

        try
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    var result = await _socket.ReceiveAsync(_cts.Token);
                    var buffer = result.Buffer;
                    var remoteEp = result.RemoteEndPoint;

                    // Case 1: 20-byte input frame (Hot path)
                    if (buffer.Length == Protocol.InputPacketSize)
                    {
                        if (InputPacket.TryParse(buffer, out var input))
                        {
                            if (_sessionManager.TryProcessInput(remoteEp, input.Pad, input.Sequence, out byte assignedSlot))
                            {
                                _backend.Submit(assignedSlot, input.State);
                                _dsuServer?.UpdatePadState(assignedSlot, input.State);
                                ServerTelemetry.RecordPad(assignedSlot, input.State);
                                Interlocked.Increment(ref _packetsReceived);
                            }
                        }
                        continue;
                    }

                    // Case 1b: 36-byte motion frame (6-Axis Gyro & Accel)
                    if (buffer.Length == Protocol.MotionPacketSize)
                    {
                        if (MotionPacket.TryParse(buffer, out var motionPkt))
                        {
                            if (_sessionManager.TryGetSlot(remoteEp, out byte assignedSlot))
                            {
                                _sessionManager.TouchSession(assignedSlot);
                                _dsuServer?.UpdateMotion(assignedSlot, motionPkt.Motion);
                                _gyroAimEngine?.ProcessMotion(motionPkt.Motion, 0.01, out _, out _, out _);
                                ServerTelemetry.RecordMotion(assignedSlot, motionPkt.Motion);
                                Interlocked.Increment(ref _packetsReceived);
                            }
                        }
                        continue;
                    }

                    // Case 1c: 13-byte touchpad frame (PS4 Touchpad 1920x942 coordinates)
                    if (buffer.Length == Protocol.TouchpadPacketSize)
                    {
                        if (TouchpadPacket.TryParse(buffer, out var touchPkt))
                        {
                            if (_sessionManager.TryGetSlot(remoteEp, out byte assignedSlot))
                            {
                                _sessionManager.TouchSession(assignedSlot);
                                _backend.SubmitTouchpad(assignedSlot, touchPkt.State);
                                _dsuServer?.UpdateTouchpad(assignedSlot, touchPkt.State);
                                _touchpadMouseEngine?.ProcessTouchpad(touchPkt.State);
                                ServerTelemetry.RecordTouch(assignedSlot, touchPkt.State);
                                Interlocked.Increment(ref _packetsReceived);
                            }
                        }
                        continue;
                    }

                    // Case 1d: Virtual Mouse Move (8 bytes: Magic, Ver, 0x30, slot, dx(i16), dy(i16))
                    if (buffer.Length >= Protocol.MouseMovePacketSize && buffer[0] == Protocol.MagicByte && buffer[2] == Protocol.MsgMouseMove)
                    {
                        if (_sessionManager.TryGetSlot(remoteEp, out byte slot)) _sessionManager.TouchSession(slot);
                        short dx = BitConverter.ToInt16(buffer, 4);
                        short dy = BitConverter.ToInt16(buffer, 6);
                        WindowsInputSimulator.MouseMove(dx, dy);
                        continue;
                    }

                    // Case 1e: Virtual Mouse Button (6 bytes: Magic, Ver, 0x31, slot, btnMask, isDown)
                    if (buffer.Length >= Protocol.MouseButtonPacketSize && buffer[0] == Protocol.MagicByte && buffer[2] == Protocol.MsgMouseButton)
                    {
                        if (_sessionManager.TryGetSlot(remoteEp, out byte slot)) _sessionManager.TouchSession(slot);
                        byte btnMask = buffer[4];
                        bool isDown = buffer[5] != 0;
                        WindowsInputSimulator.MouseButton(btnMask, isDown);
                        continue;
                    }

                    // Case 1f: Virtual Mouse Wheel (6 bytes: Magic, Ver, 0x32, slot, delta(i16))
                    if (buffer.Length >= Protocol.MouseWheelPacketSize && buffer[0] == Protocol.MagicByte && buffer[2] == Protocol.MsgMouseWheel)
                    {
                        if (_sessionManager.TryGetSlot(remoteEp, out byte slot)) _sessionManager.TouchSession(slot);
                        short delta = BitConverter.ToInt16(buffer, 4);
                        WindowsInputSimulator.MouseWheel(delta);
                        continue;
                    }

                    // Case 1g: Virtual Gaming Keyboard Key (7 bytes: Magic, Ver, 0x33, slot, vkCode(u16), isDown)
                    if (buffer.Length >= Protocol.KeyboardKeyPacketSize && buffer[0] == Protocol.MagicByte && buffer[2] == Protocol.MsgKeyboardKey)
                    {
                        if (_sessionManager.TryGetSlot(remoteEp, out byte slot)) _sessionManager.TouchSession(slot);
                        ushort vkCode = BitConverter.ToUInt16(buffer, 4);
                        bool isDown = buffer[6] != 0;
                        WindowsInputSimulator.KeyboardKey(vkCode, isDown);
                        continue;
                    }

                    // Case 2: 4-byte session message (Handshake, Disconnect)
                    if (buffer.Length == Protocol.SessionMessageSize)
                    {
                        if (SessionMessage.TryParse(buffer, out var sessionMsg))
                        {
                            if (sessionMsg.Type == Protocol.MsgHello)
                            {
                                byte assigned = _sessionManager.AssignSlot(remoteEp);
                                if (assigned != Protocol.NoPad)
                                {
                                    _backend.Connect(assigned);
                                    ServerTelemetry.RecordConnect(assigned, remoteEp.ToString());
                                }

                                // Send WELCOME answer with assigned slot (or NoPad if full)
                                var welcome = new SessionMessage(Protocol.MsgWelcome, assigned);
                                welcome.WriteTo(welcomeBuffer);
                                await _socket.SendAsync(welcomeBuffer, welcomeBuffer.Length, remoteEp);
                            }
                            else if (sessionMsg.Type == Protocol.MsgBye)
                            {
                                if (_sessionManager.Disconnect(remoteEp, sessionMsg.Pad))
                                {
                                    _backend.Disconnect(sessionMsg.Pad);
                                    ServerTelemetry.RecordDisconnect(sessionMsg.Pad);
                                }
                            }
                        }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    if (ServerTelemetry.DebugMode)
                    {
                        Console.WriteLine($"[UDP Input] Packet error: {ex.Message}");
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"[UDP Input] Fatal receiver loop error: {ex.Message}");
        }
    }

    private void OnRumbleReceived(object? sender, RumbleEventArgs e)
    {
        ServerTelemetry.RecordRumble(e.Slot, e.LargeMotor, e.SmallMotor);
        var ep = _sessionManager.GetClientEndPoint(e.Slot);
        if (ep is IPEndPoint ipEp)
        {
            byte[] rumbleBuffer = new byte[Protocol.RumbleMessageSize];
            var msg = new RumbleMessage((byte)e.Slot, e.LargeMotor, e.SmallMotor);
            msg.WriteTo(rumbleBuffer);
            try
            {
                _socket.Send(rumbleBuffer, rumbleBuffer.Length, ipEp);
            }
            catch { }
        }
    }

    private async Task TimeoutLoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                await Task.Delay(500, _cts.Token);
                _sessionManager.CheckTimeouts();
            }
        }
        catch (OperationCanceledException) { }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _backend.RumbleReceived -= OnRumbleReceived;
        _socket.Dispose();
        _cts.Dispose();
    }
}
