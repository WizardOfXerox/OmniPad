using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
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
    private readonly DsuMotionServer? _dsuServer;
    private readonly TouchpadMouseEngine? _touchpadMouseEngine;
    private readonly GyroAimEngine? _gyroAimEngine;
    private readonly ProcessProfileWatcher? _profileWatcher;
    private readonly ConcurrentDictionary<byte, WebSocket> _slotSockets = new();
    private readonly ConcurrentDictionary<WebSocket, byte> _socketToSlot = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly AudioStreamServer _audioServer = new();

    public WebServer(
        IPadBackend backend,
        SessionManager sessionManager,
        DsuMotionServer? dsuServer = null,
        TouchpadMouseEngine? touchpadMouseEngine = null,
        GyroAimEngine? gyroAimEngine = null,
        ProcessProfileWatcher? profileWatcher = null,
        int port = Protocol.DefaultWebPort,
        string? webRoot = null)
    {
        _backend = backend;
        _sessionManager = sessionManager;
        _dsuServer = dsuServer;
        _touchpadMouseEngine = touchpadMouseEngine;
        _gyroAimEngine = gyroAimEngine;
        _profileWatcher = profileWatcher;

        if (_profileWatcher != null)
        {
            _profileWatcher.ProfileChanged += profile => _ = BroadcastActiveProfileAsync(profile);
        }

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
            else if (context.Request.Path == "/api/status")
            {
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"status\":\"ok\",\"server\":\"OmniPad\"}");
            }
            else if (context.Request.Path == "/api/network/info")
            {
                var lanIps = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                                 ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                                 !ni.Description.Contains("VirtualBox", StringComparison.OrdinalIgnoreCase) &&
                                 !ni.Description.Contains("VMware", StringComparison.OrdinalIgnoreCase) &&
                                 !ni.Description.Contains("vEthernet", StringComparison.OrdinalIgnoreCase))
                    .SelectMany(ni => ni.GetIPProperties().UnicastAddresses)
                    .Where(ua => ua.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                                 !IPAddress.IsLoopback(ua.Address) &&
                                 !ua.Address.ToString().StartsWith("169.254.") &&
                                 !ua.Address.ToString().StartsWith("192.168.56."))
                    .Select(ua => ua.Address.ToString())
                    .Distinct()
                    .ToList();

                string ipArrayJson = string.Join(",", lanIps.Select(ip => $"\"{ip}\""));
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync($"{{\"port\":{Protocol.DefaultWebPort},\"lanIps\":[{ipArrayJson}]}}");
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
            else if (context.Request.Path == "/api/settings/controller-type")
            {
                if (HttpMethods.IsGet(context.Request.Method))
                {
                    string currentType = _backend is OmniPadServer.ViGEm.SwitchablePadBackend s
                        ? (s.CurrentType == OmniPadServer.ViGEm.EmulationType.DualShock4 ? "dualshock4" : "xbox360")
                        : "xbox360";
                    string engine = _backend is OmniPadServer.ViGEm.SwitchablePadBackend sb ? sb.CurrentEngineName : "Hardware Gamepad";
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync($"{{\"type\":\"{currentType}\",\"engine\":\"{engine}\"}}");
                }
                else if (HttpMethods.IsPost(context.Request.Method))
                {
                    string? type = context.Request.Query["type"].ToString();
                    if (string.IsNullOrWhiteSpace(type) && context.Request.HasFormContentType && context.Request.Form.TryGetValue("type", out var formType))
                    {
                        type = formType.ToString();
                    }

                    bool isDs4 = string.Equals(type, "dualshock4", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(type, "ds4", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(type, "ps4", StringComparison.OrdinalIgnoreCase);

                    if (_backend is OmniPadServer.ViGEm.SwitchablePadBackend switchable)
                    {
                        switchable.SwitchEmulationType(isDs4 ? OmniPadServer.ViGEm.EmulationType.DualShock4 : OmniPadServer.ViGEm.EmulationType.Xbox360);
                    }

                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync($"{{\"success\":true,\"type\":\"{(isDs4 ? "dualshock4" : "xbox360")}\"}}");
                }
                else
                {
                    context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                }
            }
            else if (context.Request.Path == "/api/settings/mouse-mode")
            {
                if (HttpMethods.IsGet(context.Request.Method))
                {
                    bool enabled = _touchpadMouseEngine?.Enabled ?? false;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync($"{{\"enabled\":{enabled.ToString().ToLowerInvariant()}}}");
                }
                else if (HttpMethods.IsPost(context.Request.Method))
                {
                    string? enabledStr = context.Request.Query["enabled"].ToString();
                    if (string.IsNullOrWhiteSpace(enabledStr) && context.Request.HasFormContentType && context.Request.Form.TryGetValue("enabled", out var formVal))
                    {
                        enabledStr = formVal.ToString();
                    }

                    bool enabled = string.Equals(enabledStr, "true", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(enabledStr, "1", StringComparison.OrdinalIgnoreCase);

                    if (_touchpadMouseEngine != null)
                    {
                        _touchpadMouseEngine.Enabled = enabled;
                    }

                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync($"{{\"success\":true,\"enabled\":{enabled.ToString().ToLowerInvariant()}}}");
                }
                else
                {
                    context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                }
            }
            else if (context.Request.Path == "/api/settings/profile")
            {
                if (HttpMethods.IsPost(context.Request.Method))
                {
                    string target = context.Request.Query["name"].ToString();
                    if (!string.IsNullOrWhiteSpace(target))
                    {
                        _ = BroadcastActiveProfileAsync(target);
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync($"{{\"success\":true,\"profile\":\"{target}\"}}");
                        return;
                    }
                }
                string current = _profileWatcher?.CurrentProfile ?? "xbox360";
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync($"{{\"profile\":\"{current}\"}}");
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
            string siblingWebClient = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "WebClient"));
            if (Directory.Exists(siblingWebClient))
            {
                contentPath = siblingWebClient;
            }
            else
            {
                contentPath = Path.Combine(AppContext.BaseDirectory, "wwwroot");
                Directory.CreateDirectory(contentPath);
            }
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

        // Send initial active profile if available
        if (_profileWatcher != null && !string.IsNullOrEmpty(_profileWatcher.CurrentProfile))
        {
            byte[] profileBytes = System.Text.Encoding.ASCII.GetBytes(_profileWatcher.CurrentProfile);
            byte[] profMsg = new byte[4 + profileBytes.Length];
            profMsg[0] = Protocol.MagicByte;
            profMsg[1] = Protocol.Version;
            profMsg[2] = Protocol.MsgActiveProfile;
            profMsg[3] = (byte)profileBytes.Length;
            Buffer.BlockCopy(profileBytes, 0, profMsg, 4, profileBytes.Length);
            try { await socket.SendAsync(profMsg, WebSocketMessageType.Binary, true, CancellationToken.None); } catch { }
        }

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
                            _dsuServer?.UpdatePadState(slot, input.State);
                        }
                    }
                }
                // 1b. Motion packet (36 bytes: 6-Axis Gyro & Accel)
                else if (result.Count == Protocol.MotionPacketSize && buffer[0] == Protocol.MagicByte && buffer[2] == Protocol.MsgMotion)
                {
                    if (MotionPacket.TryParse(buffer.AsSpan(0, result.Count), out var motionPkt))
                    {
                        _dsuServer?.UpdateMotion(currentSlot, motionPkt.Motion);
                        _gyroAimEngine?.ProcessMotion(motionPkt.Motion, 0.01, out _, out _, out _);
                    }
                }
                // 1c. Touchpad packet (13 bytes: PS4 Touchpad 1920x942 coordinates)
                else if (result.Count == Protocol.TouchpadPacketSize && buffer[0] == Protocol.MagicByte && buffer[2] == Protocol.MsgTouchpad)
                {
                    if (TouchpadPacket.TryParse(buffer.AsSpan(0, result.Count), out var touchPkt))
                    {
                        _dsuServer?.UpdateTouchpad(currentSlot, touchPkt.State);
                        _touchpadMouseEngine?.ProcessTouchpad(touchPkt.State);
                    }
                }
                // 1d. Set Controller Type (4 bytes: Magic, Ver, 0x12, Type: 0=Xbox, 1=DS4)
                else if (result.Count >= 4 && buffer[0] == Protocol.MagicByte && buffer[2] == Protocol.MsgSetControllerType)
                {
                    byte typeByte = buffer[3];
                    if (_backend is OmniPadServer.ViGEm.SwitchablePadBackend switchable)
                    {
                        switchable.SwitchEmulationType(typeByte == 1 ? OmniPadServer.ViGEm.EmulationType.DualShock4 : OmniPadServer.ViGEm.EmulationType.Xbox360);
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

    public async Task BroadcastActiveProfileAsync(string profile)
    {
        byte[] profileBytes = System.Text.Encoding.ASCII.GetBytes(profile);
        byte[] msg = new byte[4 + profileBytes.Length];
        msg[0] = Protocol.MagicByte;
        msg[1] = Protocol.Version;
        msg[2] = Protocol.MsgActiveProfile;
        msg[3] = (byte)profileBytes.Length;
        Buffer.BlockCopy(profileBytes, 0, msg, 4, profileBytes.Length);

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

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _cts.Dispose();
        await _audioServer.DisposeAsync();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
