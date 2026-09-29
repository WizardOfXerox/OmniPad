using System;
using System.Collections.Concurrent;
using System.Diagnostics;
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
    private readonly MicStreamServer _micServer = new();
    private readonly ScreenStreamServer _screenServer = new();
    private readonly ConcurrentDictionary<WebSocket, SemaphoreSlim> _sendLocks = new();

    private async Task SafeSendAsync(WebSocket socket, byte[] data, WebSocketMessageType messageType = WebSocketMessageType.Binary, bool endOfMessage = true, CancellationToken ct = default)
    {
        if (socket.State != WebSocketState.Open) return;
        var sem = _sendLocks.GetOrAdd(socket, _ => new SemaphoreSlim(1, 1));
        try
        {
            await sem.WaitAsync(ct).ConfigureAwait(false);
        }
        catch { return; }

        try
        {
            if (socket.State == WebSocketState.Open)
            {
                await socket.SendAsync(data, messageType, endOfMessage, ct).ConfigureAwait(false);
            }
        }
        catch { }
        finally
        {
            try { sem.Release(); } catch { }
        }
    }

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
            else if (context.Request.Path == "/ws/mic")
            {
                if (context.WebSockets.IsWebSocketRequest)
                {
                    using var micSocket = await context.WebSockets.AcceptWebSocketAsync();
                    await _micServer.HandleWebSocketAsync(micSocket);
                }
                else
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                }
            }
            else if (context.Request.Path == "/ws/screen")
            {
                if (context.WebSockets.IsWebSocketRequest)
                {
                    using var screenSocket = await context.WebSockets.AcceptWebSocketAsync();
                    await _screenServer.HandleWebSocketAsync(screenSocket);
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
                    bool isHm = string.Equals(type, "hidmaestro", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(type, "browser", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(type, "universal", StringComparison.OrdinalIgnoreCase);

                    var targetType = isDs4 ? OmniPadServer.ViGEm.EmulationType.DualShock4 :
                                     isHm ? OmniPadServer.ViGEm.EmulationType.HIDMaestro :
                                     OmniPadServer.ViGEm.EmulationType.Xbox360;

                    if (_backend is OmniPadServer.ViGEm.SwitchablePadBackend switchable)
                    {
                        switchable.SwitchEmulationType(targetType);
                    }

                    string typeStr = isDs4 ? "dualshock4" : isHm ? "hidmaestro" : "xbox360";
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync($"{{\"success\":true,\"type\":\"{typeStr}\"}}");
                }
                else
                {
                    context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                }
            }
            else if (context.Request.Path == "/api/bus/status")
            {
                var busStatus = OmniPadServer.ViGEm.HIDOmniPadBusManager.DetectStatus();
                string currentPreset = _backend is OmniPadServer.ViGEm.SwitchablePadBackend s ? s.CurrentPreset.ToString() : "Xbox360_WHQL";
                string currentEngine = _backend is OmniPadServer.ViGEm.SwitchablePadBackend sb ? sb.CurrentEngineName : "Hardware Gamepad";

                var presetList = OmniPadServer.ViGEm.ControllerProfileCatalog.Profiles.Values.Select(p => new
                {
                    id = p.Preset.ToString(),
                    displayName = p.DisplayName,
                    category = p.Category,
                    engine = p.Engine,
                    description = p.Description,
                    supportsTouchpad = p.SupportsTouchpad,
                    supportsMotion = p.SupportsMotion,
                    supportsPaddles = p.SupportsPaddles,
                    isSimulation = p.IsSimulation
                });

                var json = System.Text.Json.JsonSerializer.Serialize(new
                {
                    vigemInstalled = busStatus.ViGEmInstalled,
                    vigemRunning = busStatus.ViGEmRunning,
                    vigemVersion = busStatus.ViGEmVersion,
                    hidMaestroInstalled = busStatus.HIDMaestroInstalled,
                    totalProfilesAvailable = busStatus.TotalProfilesAvailable,
                    primaryEngine = busStatus.PrimaryEngine,
                    statusSummary = busStatus.StatusSummary,
                    currentPreset,
                    currentEngine,
                    presets = presetList
                });

                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(json);
            }
            else if (context.Request.Path == "/api/bus/install")
            {
                if (HttpMethods.IsPost(context.Request.Method))
                {
                    bool ok = OmniPadServer.ViGEm.HIDOmniPadBusManager.InstallAll();
                    var busStatus = OmniPadServer.ViGEm.HIDOmniPadBusManager.DetectStatus();
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync($"{{\"success\":{ok.ToString().ToLowerInvariant()},\"primaryEngine\":\"{busStatus.PrimaryEngine}\"}}");
                }
                else
                {
                    context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                }
            }
            else if (context.Request.Path == "/api/bus/uninstall")
            {
                if (HttpMethods.IsPost(context.Request.Method))
                {
                    bool ok = OmniPadServer.ViGEm.HIDOmniPadBusManager.UninstallAll();
                    var busStatus = OmniPadServer.ViGEm.HIDOmniPadBusManager.DetectStatus();
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync($"{{\"success\":{ok.ToString().ToLowerInvariant()},\"primaryEngine\":\"{busStatus.PrimaryEngine}\"}}");
                }
                else
                {
                    context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                }
            }
            else if (context.Request.Path == "/api/settings/controller-profile")
            {
                if (HttpMethods.IsGet(context.Request.Method))
                {
                    string currentPreset = _backend is OmniPadServer.ViGEm.SwitchablePadBackend s ? s.CurrentPreset.ToString() : "Xbox360_WHQL";
                    string currentEngine = _backend is OmniPadServer.ViGEm.SwitchablePadBackend sb ? sb.CurrentEngineName : "Hardware Gamepad";
                    var currentInfo = _backend is OmniPadServer.ViGEm.SwitchablePadBackend sbb ? sbb.CurrentInfo : OmniPadServer.ViGEm.ControllerProfileCatalog.Get(OmniPadServer.ViGEm.ControllerProfilePreset.Xbox360_WHQL);

                    var json = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        preset = currentPreset,
                        engine = currentEngine,
                        info = currentInfo
                    });

                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(json);
                }
                else if (HttpMethods.IsPost(context.Request.Method))
                {
                    string? presetStr = context.Request.Query["preset"].ToString();
                    if (string.IsNullOrWhiteSpace(presetStr) && context.Request.HasFormContentType && context.Request.Form.TryGetValue("preset", out var formPreset))
                    {
                        presetStr = formPreset.ToString();
                    }

                    var parsedPreset = OmniPadServer.ViGEm.ControllerProfileCatalog.Parse(presetStr);
                    if (_backend is OmniPadServer.ViGEm.SwitchablePadBackend switchable)
                    {
                        switchable.SwitchProfile(parsedPreset);
                    }

                    try
                    {
                        string settingsFile = Path.Combine(AppContext.BaseDirectory, "controller_profile.txt");
                        File.WriteAllText(settingsFile, parsedPreset.ToString());
                    }
                    catch { }

                    string engine = _backend is OmniPadServer.ViGEm.SwitchablePadBackend sb ? sb.CurrentEngineName : "Hardware Gamepad";
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync($"{{\"success\":true,\"preset\":\"{parsedPreset}\",\"engine\":\"{engine}\"}}");
                }
                else
                {
                    context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                }
            }
            else if (context.Request.Path == "/api/updater/status")
            {
                var updateInfo = await UpdateChecker.CheckForUpdatesAsync(AppContext.BaseDirectory);
                var json = System.Text.Json.JsonSerializer.Serialize(new
                {
                    currentVersion = updateInfo.CurrentVersion,
                    latestVersion = updateInfo.LatestVersion,
                    isUpdateAvailable = updateInfo.IsUpdateAvailable,
                    releaseTitle = updateInfo.ReleaseTitle,
                    releaseNotes = updateInfo.ReleaseNotes
                });
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(json);
            }
            else if (context.Request.Path == "/api/updater/apply")
            {
                if (HttpMethods.IsPost(context.Request.Method))
                {
                    string updaterBinaryName = OperatingSystem.IsWindows() ? "OmniPadUpdater.exe" : "OmniPadUpdater";
                    string updaterExe = Path.Combine(AppContext.BaseDirectory, updaterBinaryName);
                    if (!File.Exists(updaterExe))
                    {
                        string targetFramework = OperatingSystem.IsWindows() ? "net8.0-windows" : "net8.0";
                        updaterExe = Path.Combine(AppContext.BaseDirectory, "..", "OmniPadUpdater.App", "bin", "Debug", targetFramework, updaterBinaryName);
                    }

                    if (File.Exists(updaterExe))
                    {
                        int currentPid = Environment.ProcessId;
                        Process.Start(new ProcessStartInfo(updaterExe, $"--apply --pid {currentPid} --silent")
                        {
                            UseShellExecute = !OperatingSystem.IsLinux()
                        });

                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync("{\"success\":true,\"message\":\"OmniPadUpdater launched. Server restarting...\"}");
                    }
                    else
                    {
                        context.Response.StatusCode = StatusCodes.Status404NotFound;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync($"{{\"success\":false,\"message\":\"{updaterBinaryName} binary not found.\"}}");
                    }
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
            else if (context.Request.Path == "/api/test-pulse")
            {
                // Pulse Button A / Cross and thumbstick on slot 0 for browser Gamepad API activation
                _backend.Connect(0);
                var pulseState = PadState.Neutral;
                pulseState.SetButton(Protocol.Buttons.A, true);
                pulseState.ThumbLX = 25000;
                pulseState.ThumbLY = 20000;
                _backend.Submit(0, pulseState);

                _ = Task.Run(async () =>
                {
                    for (int i = 0; i < 20; i++)
                    {
                        await Task.Delay(100);
                        pulseState.ThumbLX = (short)(25000 - i * 1000);
                        _backend.Submit(0, pulseState);
                    }
                    _backend.Submit(0, PadState.Neutral);
                });

                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"pulsed\":true}");
            }
            else if (context.Request.Path == "/api/debug/telemetry")
            {
                context.Response.ContentType = "application/json";
                var snapshot = ServerTelemetry.GetSnapshot();
                await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(snapshot));
            }
            else if (context.Request.Path == "/api/debug/toggle")
            {
                if (HttpMethods.IsPost(context.Request.Method))
                {
                    string? enableStr = context.Request.Query["enable"].ToString();
                    if (bool.TryParse(enableStr, out bool enable))
                    {
                        ServerTelemetry.DebugMode = enable;
                    }
                    else
                    {
                        ServerTelemetry.DebugMode = !ServerTelemetry.DebugMode;
                    }
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync($"{{\"success\":true,\"debugMode\":{ServerTelemetry.DebugMode.ToString().ToLowerInvariant()}}}");
                }
                else
                {
                    context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                }
            }
            else if (context.Request.Path == "/api/test-rumble")
            {
                int s = int.TryParse(context.Request.Query["slot"].ToString(), out int qSlot) ? qSlot : 0;
                byte l = byte.TryParse(context.Request.Query["large"].ToString(), out byte qLarge) ? qLarge : (byte)255;
                byte sm = byte.TryParse(context.Request.Query["small"].ToString(), out byte qSmall) ? qSmall : (byte)128;

                ServerTelemetry.RecordRumble(s, l, sm);

                if (_slotSockets.TryGetValue((byte)s, out var ws) && ws.State == WebSocketState.Open)
                {
                    byte[] rMsg = [Protocol.MagicByte, Protocol.Version, Protocol.MsgRumble, (byte)s, l, sm, 0, 0];
                    await SafeSendAsync(ws, rMsg);
                }

                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync($"{{\"success\":true,\"slot\":{s},\"large\":{l},\"small\":{sm}}}");
            }
            else
            {
                await next();
            }
        });

        // 2. Static files (WebClient UI)
        string contentPath = webRoot ?? "";
        if (string.IsNullOrEmpty(contentPath) || !Directory.Exists(contentPath))
        {
            string candidate5 = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "WebClient"));
            string candidate4 = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "WebClient"));
            string candidate1 = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "WebClient"));

            if (Directory.Exists(candidate5)) contentPath = candidate5;
            else if (Directory.Exists(candidate4)) contentPath = candidate4;
            else if (Directory.Exists(candidate1)) contentPath = candidate1;
            else
            {
                contentPath = Path.Combine(AppContext.BaseDirectory, "wwwroot");
                Directory.CreateDirectory(contentPath);
            }
        }
        Console.WriteLine($"[Web Server] Serving WebClient static files from: {contentPath}");

        var fileProvider = new PhysicalFileProvider(contentPath);
        _app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
        _app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });
    }

    public async Task StartAsync()
    {
        _micServer.StartTcpListener(27503);
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
                await SafeSendAsync(ws, msg);
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
            await SafeSendAsync(socket, fullMsg);
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
        ServerTelemetry.RecordConnect(currentSlot, endPoint.ToString());

        // Send WELCOME packet to browser
        byte[] welcomeMsg = [Protocol.MagicByte, Protocol.Version, Protocol.MsgWelcome, currentSlot];
        await SafeSendAsync(socket, welcomeMsg);

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
            await SafeSendAsync(socket, profMsg);
        }

        // Broadcast updated slot statuses to all clients
        _ = BroadcastSlotStatusAsync();

        // Subscribe to rumble for this web socket
        EventHandler<RumbleEventArgs> rumbleHandler = async (_, e) =>
        {
            ServerTelemetry.RecordRumble(e.Slot, e.LargeMotor, e.SmallMotor);
            byte dynamicSlot = _socketToSlot.TryGetValue(socket, out byte s) ? s : currentSlot;
            if (e.Slot == dynamicSlot && socket.State == WebSocketState.Open)
            {
                byte[] rumbleMsg = [Protocol.MagicByte, Protocol.Version, Protocol.MsgRumble, (byte)e.Slot, e.LargeMotor, e.SmallMotor];
                await SafeSendAsync(socket, rumbleMsg);
            }
        };

        _backend.RumbleReceived += rumbleHandler;

        byte[] buffer = new byte[65536];
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
                            ServerTelemetry.RecordPad(slot, input.State);
                        }
                    }
                }
                // 1b. Motion packet (36 bytes: 6-Axis Gyro & Accel)
                else if (result.Count == Protocol.MotionPacketSize && buffer[0] == Protocol.MagicByte && buffer[2] == Protocol.MsgMotion)
                {
                    if (MotionPacket.TryParse(buffer.AsSpan(0, result.Count), out var motionPkt))
                    {
                        byte activeSlot = _socketToSlot.TryGetValue(socket, out byte s) ? s : currentSlot;
                        _dsuServer?.UpdateMotion(activeSlot, motionPkt.Motion);
                        _gyroAimEngine?.ProcessMotion(motionPkt.Motion, 0.01, out _, out _, out _);
                        ServerTelemetry.RecordMotion(activeSlot, motionPkt.Motion);
                    }
                }
                // 1c. Touchpad packet (13 bytes: PS4 Touchpad 1920x942 coordinates)
                else if (result.Count == Protocol.TouchpadPacketSize && buffer[0] == Protocol.MagicByte && buffer[2] == Protocol.MsgTouchpad)
                {
                    if (TouchpadPacket.TryParse(buffer.AsSpan(0, result.Count), out var touchPkt))
                    {
                        byte activeSlot = _socketToSlot.TryGetValue(socket, out byte s) ? s : currentSlot;
                        _backend.SubmitTouchpad(activeSlot, touchPkt.State);
                        _dsuServer?.UpdateTouchpad(activeSlot, touchPkt.State);
                        _touchpadMouseEngine?.ProcessTouchpad(touchPkt.State);
                        ServerTelemetry.RecordTouch(activeSlot, touchPkt.State);
                    }
                }
                // 1e. Virtual Mouse Move (8 bytes: Magic, Ver, 0x30, slot, dx(i16), dy(i16))
                else if (result.Count >= Protocol.MouseMovePacketSize && buffer[0] == Protocol.MagicByte && buffer[2] == Protocol.MsgMouseMove)
                {
                    short dx = BitConverter.ToInt16(buffer, 4);
                    short dy = BitConverter.ToInt16(buffer, 6);
                    WindowsInputSimulator.MouseMove(dx, dy);
                }
                // 1f. Virtual Mouse Button (6 bytes: Magic, Ver, 0x31, slot, btnMask, isDown)
                else if (result.Count >= Protocol.MouseButtonPacketSize && buffer[0] == Protocol.MagicByte && buffer[2] == Protocol.MsgMouseButton)
                {
                    byte btnMask = buffer[4];
                    bool isDown = buffer[5] != 0;
                    WindowsInputSimulator.MouseButton(btnMask, isDown);
                }
                // 1g. Virtual Mouse Wheel (6 bytes: Magic, Ver, 0x32, slot, delta(i16))
                else if (result.Count >= Protocol.MouseWheelPacketSize && buffer[0] == Protocol.MagicByte && buffer[2] == Protocol.MsgMouseWheel)
                {
                    short delta = BitConverter.ToInt16(buffer, 4);
                    WindowsInputSimulator.MouseWheel(delta);
                }
                // 1h. Virtual Gaming Keyboard Key (7 bytes: Magic, Ver, 0x33, slot, vkCode(u16), isDown)
                else if (result.Count >= Protocol.KeyboardKeyPacketSize && buffer[0] == Protocol.MagicByte && buffer[2] == Protocol.MsgKeyboardKey)
                {
                    ushort vkCode = BitConverter.ToUInt16(buffer, 4);
                    bool isDown = buffer[6] != 0;
                    WindowsInputSimulator.KeyboardKey(vkCode, isDown);
                }
                // 1d. Set Controller Type (4 bytes: Magic, Ver, 0x12, Type: 0=Xbox, 1=DS4, 2=HIDMaestro)
                else if (result.Count >= 4 && buffer[0] == Protocol.MagicByte && buffer[2] == Protocol.MsgSetControllerType)
                {
                    byte typeByte = buffer[3];
                    if (_backend is OmniPadServer.ViGEm.SwitchablePadBackend switchable)
                    {
                        var target = typeByte == 1 ? OmniPadServer.ViGEm.EmulationType.DualShock4 :
                                     typeByte == 2 ? OmniPadServer.ViGEm.EmulationType.HIDMaestro :
                                     OmniPadServer.ViGEm.EmulationType.Xbox360;
                        switchable.SwitchEmulationType(target);
                    }
                }
                // 2. Ping packet
                else if (result.Count >= 4 && buffer[0] == Protocol.MagicByte && buffer[2] == Protocol.MsgPing)
                {
                    byte[] pongMsg = [Protocol.MagicByte, Protocol.Version, Protocol.MsgPong, currentSlot];
                    await SafeSendAsync(socket, pongMsg);
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
                        await SafeSendAsync(socket, newWelcome);
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
                        await SafeSendAsync(targetSocket, promptMsg);
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
                                await SafeSendAsync(requesterSocket, welcomeReq);

                                // Send updated welcome to target (this socket)
                                byte[] welcomeTar = [Protocol.MagicByte, Protocol.Version, Protocol.MsgWelcome, currentSlot];
                                await SafeSendAsync(socket, welcomeTar);

                                _ = BroadcastSlotStatusAsync();
                            }
                        }
                        else
                        {
                            // Send decline to requester: MsgSwapDeclined = 0x0E, targetSlot in byte 3
                            byte[] declinedMsg = [Protocol.MagicByte, Protocol.Version, Protocol.MsgSwapDeclined, currentSlot];
                            await SafeSendAsync(requesterSocket, declinedMsg);
                        }
                    }
                }
                // 6. Share layout request (MsgShareLayoutRequest = 0x14, targetSlot in buffer[3], payloadLen in buffer[4..5])
                else if (result.Count >= 6 && buffer[0] == Protocol.MagicByte && buffer[2] == Protocol.MsgShareLayoutRequest)
                {
                    byte targetSlot = buffer[3];
                    ushort payloadLen = (ushort)(buffer[4] | (buffer[5] << 8));
                    int totalExpected = 6 + payloadLen;

                    if (result.Count >= totalExpected)
                    {
                        // Build prompt message: [MagicByte, Version, MsgShareLayoutPrompt, senderSlot, lenLow, lenHigh, ...jsonBytes]
                        byte[] promptMsg = new byte[totalExpected];
                        promptMsg[0] = Protocol.MagicByte;
                        promptMsg[1] = Protocol.Version;
                        promptMsg[2] = Protocol.MsgShareLayoutPrompt;
                        promptMsg[3] = currentSlot;
                        promptMsg[4] = buffer[4];
                        promptMsg[5] = buffer[5];
                        Array.Copy(buffer, 6, promptMsg, 6, payloadLen);

                        if (targetSlot == 0xFF)
                        {
                            // Broadcast to all other connected active players
                            foreach (var (slotId, targetSock) in _slotSockets.ToArray())
                            {
                                if (slotId != currentSlot && targetSock.State == WebSocketState.Open)
                                {
                                    await SafeSendAsync(targetSock, promptMsg);
                                }
                            }
                        }
                        else if (targetSlot != currentSlot && _slotSockets.TryGetValue(targetSlot, out var targetSocket) && targetSocket.State == WebSocketState.Open)
                        {
                            await SafeSendAsync(targetSocket, promptMsg);
                        }
                    }
                }
                // 7. Share layout response (MsgShareLayoutResponse = 0x16, senderSlot in buffer[3], accepted in buffer[4])
                else if (result.Count >= 5 && buffer[0] == Protocol.MagicByte && buffer[2] == Protocol.MsgShareLayoutResponse)
                {
                    byte senderSlot = buffer[3];
                    bool accepted = buffer[4] == 1;

                    if (_slotSockets.TryGetValue(senderSlot, out var senderSocket) && senderSocket.State == WebSocketState.Open)
                    {
                        int extraBytes = result.Count - 5;
                        byte[] respMsg = new byte[4 + extraBytes];
                        respMsg[0] = Protocol.MagicByte;
                        respMsg[1] = Protocol.Version;
                        respMsg[2] = accepted ? Protocol.MsgShareLayoutAccepted : Protocol.MsgShareLayoutDeclined;
                        respMsg[3] = currentSlot;
                        if (extraBytes > 0)
                        {
                            Array.Copy(buffer, 5, respMsg, 4, extraBytes);
                        }

                        await SafeSendAsync(senderSocket, respMsg);
                    }
                }
                // 8. Explicit BYE message (MsgBye = 0x04) on browser unload/close
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

            if (_sendLocks.TryRemove(socket, out var sem))
            {
                sem.Dispose();
            }

            if (_socketToSlot.TryRemove(socket, out byte slotToFree))
            {
                ServerTelemetry.RecordDisconnect(slotToFree);
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
                await SafeSendAsync(ws, msg);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _cts.Dispose();
        await _audioServer.DisposeAsync();
        await _micServer.DisposeAsync();
        await _screenServer.DisposeAsync();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
