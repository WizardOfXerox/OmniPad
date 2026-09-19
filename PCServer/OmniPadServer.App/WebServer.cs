using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using OmniPadServer.Core;

namespace OmniPadServer.App;

public sealed class WebServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly IPadBackend _backend;
    private readonly SessionManager _sessionManager;
    private readonly ConcurrentDictionary<byte, WebSocket> _slotSockets = new();
    private readonly ConcurrentDictionary<WebSocket, byte> _socketToSlot = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly AudioStreamServer _audioServer = new();

    public WebServer(IPadBackend backend, SessionManager sessionManager, int port = Protocol.DefaultWebPort, string? webRoot = null)
    {
        _backend = backend;
        _sessionManager = sessionManager;

        // Auto-clean backend and broadcast status whenever any session times out or disconnects
        _sessionManager.ClientDisconnected += (slot, ep) =>
        {
            if (_slotSockets.TryRemove(slot, out var deadSocket))
            {
                _socketToSlot.TryRemove(deadSocket, out _);
                try { deadSocket.Abort(); } catch { }
            }
            _backend.Disconnect(slot);
            _ = BroadcastSlotStatusAsync();
        };

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
        builder.Logging.ClearProviders(); // Keep console clean for gamepad telemetry

        _app = builder.Build();

        // 1. WebSocket support for real-time web gamepad input
        _app.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromSeconds(5)
        });

        _app.Use(async (context, next) =>
        {
            if (context.Request.Path == "/ws")
            {
                if (context.WebSockets.IsWebSocketRequest)
                {
                    string? sid = context.Request.Query["sid"].ToString();
                    if (string.IsNullOrWhiteSpace(sid)) sid = null;
                    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                    await HandleWebSocketAsync(webSocket, context.Connection.RemoteIpAddress, context.Connection.RemotePort, sid);
                }
                else
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                }
            }
            else if (context.Request.Path == "/api/bye" || context.Request.Path == "/bye")
            {
                // Fast beacon endpoint for page unload / tab closure
                string? sid = context.Request.Query["sid"].ToString();
                if (string.IsNullOrWhiteSpace(sid) && context.Request.HasFormContentType && context.Request.Form.TryGetValue("sid", out var sidForm))
                {
                    sid = sidForm.ToString();
                }

                if (!string.IsNullOrWhiteSpace(sid))
                {
                    _sessionManager.DisconnectBySessionId(sid);
                }
                else if (byte.TryParse(context.Request.Query["slot"].ToString(), out byte slot))
                {
                    _sessionManager.DisconnectBySlot(slot);
                }

                context.Response.StatusCode = StatusCodes.Status200OK;
                await context.Response.WriteAsync("OK");
            }
            else if (context.Request.Path == "/audio")
            {
                if (context.WebSockets.IsWebSocketRequest)
                {
                    using var audioSocket = await context.WebSockets.AcceptWebSocketAsync();
                    await _audioServer.HandleWebSocketAsync(audioSocket);
                }
                else
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                }
            }
            else
            {
                await next();
            }
        });

        // 2. Static files (WebClient UI)
        string contentPath = webRoot ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "WebClient"));
        if (!Directory.Exists(contentPath))
        {
            contentPath = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            Directory.CreateDirectory(contentPath);
        }

        var fileProvider = new PhysicalFileProvider(contentPath);
        _app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
        _app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });
    }

    public async Task StartAsync()
    {
        await _app.StartAsync();
        _ = TimeoutReaperLoopAsync();
    }

    private async Task TimeoutReaperLoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                await Task.Delay(1000, _cts.Token);
                _sessionManager.CheckTimeouts();
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task BroadcastSlotStatusAsync()
    {
        var statuses = _sessionManager.GetSlotStatuses();
        byte[] msg = new byte[4 + statuses.Length];
        msg[0] = Protocol.MagicByte;
        msg[1] = Protocol.Version;
        msg[2] = Protocol.MsgSlotStatus;
        msg[3] = (byte)statuses.Length;
        Buffer.BlockCopy(statuses, 0, msg, 4, statuses.Length);
        foreach (var ws in _slotSockets.Values)
        {
            if (ws.State == WebSocketState.Open)
            {
                try
                {
                    await ws.SendAsync(msg, WebSocketMessageType.Binary, true, CancellationToken.None);
                }
                catch { }
            }
        }
    }

    private async Task HandleWebSocketAsync(WebSocket socket, IPAddress? remoteIp, int remotePort, string? sessionId = null)
    {
        var endPoint = new IPEndPoint(remoteIp ?? IPAddress.Loopback, remotePort);
        byte assignedSlot = _sessionManager.AssignSlot(endPoint, isPersistent: true, sessionId: sessionId);

        if (assignedSlot == Protocol.NoPad)
        {
            // Full
            byte[] fullMsg = [Protocol.MagicByte, Protocol.Version, Protocol.MsgWelcome, Protocol.NoPad];
            await socket.SendAsync(fullMsg, WebSocketMessageType.Binary, true, CancellationToken.None);
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server full", CancellationToken.None);
            return;
        }

        byte currentSlot = assignedSlot;

        // If an existing socket was still attached to this slot (e.g. from before page reload), abort it
        if (_slotSockets.TryGetValue(currentSlot, out var oldSocket) && oldSocket != socket)
        {
            _slotSockets.TryRemove(currentSlot, out _);
            _socketToSlot.TryRemove(oldSocket, out _);
            try { oldSocket.Abort(); } catch { }
        }

        _slotSockets[currentSlot] = socket;
        _socketToSlot[socket] = currentSlot;
        _backend.Connect(currentSlot);

        // Send WELCOME packet to browser
        byte[] welcomeMsg = [Protocol.MagicByte, Protocol.Version, Protocol.MsgWelcome, currentSlot];
        await socket.SendAsync(welcomeMsg, WebSocketMessageType.Binary, true, CancellationToken.None);

        // Broadcast updated slot statuses to all clients
        _ = BroadcastSlotStatusAsync();

        // Subscribe to rumble for this web socket
        EventHandler<RumbleEventArgs> rumbleHandler = async (_, e) =>
        {
            if (e.Slot == currentSlot && socket.State == WebSocketState.Open)
            {
                byte[] rumbleMsg = [Protocol.MagicByte, Protocol.Version, Protocol.MsgRumble, (byte)e.Slot, e.LargeMotor, e.SmallMotor];
                try
                {
                    await socket.SendAsync(rumbleMsg, WebSocketMessageType.Binary, true, CancellationToken.None);
                }
                catch { }
            }
        };

        _backend.RumbleReceived += rumbleHandler;

        byte[] buffer = new byte[256];
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                _sessionManager.TouchSession(currentSlot);

                // 1. Input packet
                if (result.Count == Protocol.InputPacketSize)
                {
                    if (InputPacket.TryParse(buffer.AsSpan(0, result.Count), out var input))
                    {
                        if (_sessionManager.TryProcessInput(endPoint, input.Pad, input.Sequence, out byte slot))
                        {
                            _backend.Submit(slot, input.State);
                        }
                    }
                }
                // 2. Ping packet
                else if (result.Count >= 4 && buffer[0] == Protocol.MagicByte && buffer[2] == Protocol.MsgPing)
                {
                    byte[] pongMsg = [Protocol.MagicByte, Protocol.Version, Protocol.MsgPong, currentSlot];
                    try
                    {
                        await socket.SendAsync(pongMsg, WebSocketMessageType.Binary, true, CancellationToken.None);
                    }
                    catch { }
                }
                // 3. Switch to empty slot (MsgSwitchSlot = 0x0A, targetSlot in buffer[3])
                else if (result.Count >= 4 && buffer[0] == Protocol.MagicByte && buffer[2] == Protocol.MsgSwitchSlot)
                {
                    byte targetSlot = buffer[3];
                    if (targetSlot != currentSlot && _sessionManager.MoveToEmptySlot(currentSlot, targetSlot))
                    {
                        _backend.Connect(targetSlot);
                        _backend.Disconnect(currentSlot);

                        _slotSockets.TryRemove(currentSlot, out _);
                        _slotSockets[targetSlot] = socket;
                        _socketToSlot[socket] = targetSlot;

                        currentSlot = targetSlot;

                        byte[] newWelcome = [Protocol.MagicByte, Protocol.Version, Protocol.MsgWelcome, currentSlot];
                        await socket.SendAsync(newWelcome, WebSocketMessageType.Binary, true, CancellationToken.None);
                        _ = BroadcastSlotStatusAsync();
                    }
                }
                // 4. Swap request (MsgSwapRequest = 0x0B, targetSlot in buffer[3])
                else if (result.Count >= 4 && buffer[0] == Protocol.MagicByte && buffer[2] == Protocol.MsgSwapRequest)
                {
                    byte targetSlot = buffer[3];
                    if (targetSlot != currentSlot && _slotSockets.TryGetValue(targetSlot, out var targetSocket) && targetSocket.State == WebSocketState.Open)
                    {
                        // Send prompt to target socket: MsgSwapPrompt = 0x0C, requesterSlot in byte 3
                        byte[] promptMsg = [Protocol.MagicByte, Protocol.Version, Protocol.MsgSwapPrompt, currentSlot];
                        await targetSocket.SendAsync(promptMsg, WebSocketMessageType.Binary, true, CancellationToken.None);
                    }
                }
                // 5. Swap response (MsgSwapResponse = 0x0D, requesterSlot in buffer[3], accepted in buffer[4])
                else if (result.Count >= 5 && buffer[0] == Protocol.MagicByte && buffer[2] == Protocol.MsgSwapResponse)
                {
                    byte requesterSlot = buffer[3];
                    bool accepted = buffer[4] == 1;

                    if (_slotSockets.TryGetValue(requesterSlot, out var requesterSocket) && requesterSocket.State == WebSocketState.Open)
                    {
                        if (accepted)
                        {
                            if (_sessionManager.SwapSlots(requesterSlot, currentSlot))
                            {
                                byte oldRequesterSlot = requesterSlot;
                                byte oldTargetSlot = currentSlot;

                                // Update sockets mapping in both directions
                                _slotSockets[oldRequesterSlot] = socket;
                                _slotSockets[oldTargetSlot] = requesterSocket;
                                _socketToSlot[socket] = oldRequesterSlot;
                                _socketToSlot[requesterSocket] = oldTargetSlot;

                                currentSlot = oldRequesterSlot;

                                // Send updated welcome to requester
                                byte[] welcomeReq = [Protocol.MagicByte, Protocol.Version, Protocol.MsgWelcome, oldTargetSlot];
                                await requesterSocket.SendAsync(welcomeReq, WebSocketMessageType.Binary, true, CancellationToken.None);

                                // Send updated welcome to target (this socket)
                                byte[] welcomeTar = [Protocol.MagicByte, Protocol.Version, Protocol.MsgWelcome, currentSlot];
                                await socket.SendAsync(welcomeTar, WebSocketMessageType.Binary, true, CancellationToken.None);

                                _ = BroadcastSlotStatusAsync();
                            }
                        }
                        else
                        {
                            // Send decline to requester: MsgSwapDeclined = 0x0E, targetSlot in byte 3
                            byte[] declinedMsg = [Protocol.MagicByte, Protocol.Version, Protocol.MsgSwapDeclined, currentSlot];
                            await requesterSocket.SendAsync(declinedMsg, WebSocketMessageType.Binary, true, CancellationToken.None);
                        }
                    }
                }
                // 6. Explicit BYE message (MsgBye = 0x04) on browser unload/close
                else if (result.Count >= 3 && buffer[0] == Protocol.MagicByte && buffer[2] == Protocol.MsgBye)
                {
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not WebSocketException)
        {
            Console.WriteLine($"[WS Error] {ex.Message}");
        }
        finally
        {
            _backend.RumbleReceived -= rumbleHandler;

            if (_socketToSlot.TryRemove(socket, out byte slotToFree))
            {
                if (_slotSockets.TryGetValue(slotToFree, out var ws) && ws == socket)
                {
                    _slotSockets.TryRemove(slotToFree, out _);
                    if (_sessionManager.DisconnectBySlot(slotToFree, sessionId))
                    {
                        _backend.Disconnect(slotToFree);
                    }
                }
            }

            _ = BroadcastSlotStatusAsync();

            try
            {
                if (socket.State == WebSocketState.Open)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnected", CancellationToken.None);
                }
            }
            catch { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _cts.Dispose();
        await _audioServer.DisposeAsync();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
