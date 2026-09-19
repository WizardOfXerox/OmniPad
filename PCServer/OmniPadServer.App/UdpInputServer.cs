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

    public long PacketsReceived { get; private set; }

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
                            PacketsReceived++;
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
                            _dsuServer?.UpdateMotion(assignedSlot, motionPkt.Motion);
                            _gyroAimEngine?.ProcessMotion(motionPkt.Motion, 0.01, out _, out _, out _);
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
                            _dsuServer?.UpdateTouchpad(assignedSlot, touchPkt.State);
                            _touchpadMouseEngine?.ProcessTouchpad(touchPkt.State);
                        }
                    }
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
                            }
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"[UDP Input] Receiver loop error: {ex.Message}");
        }
    }

    private void OnRumbleReceived(object? sender, RumbleEventArgs e)
    {
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
