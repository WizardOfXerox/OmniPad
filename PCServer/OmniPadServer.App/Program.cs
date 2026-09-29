using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using OmniPadServer.App;
using OmniPadServer.Core;
using OmniPadServer.ViGEm;

Console.Title = "OmniPad - Universal Ultra-Low Latency Phone Gamepad Server";
Console.OutputEncoding = System.Text.Encoding.UTF8;

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine(@"
   ___                  _ _____           _ 
  / _ \ _ __ ___  _ __ (_|  __ \ __ _  __| |
 | | | | '_ ` _ \| '_ \| | |__) / _` |/ _` |
 | |_| | | | | | | | | | |  ___/ (_| | (_| |
  \___/|_| |_| |_|_| |_|_|_|    \__,_|\__,_|
  Universal Ultra-Low Latency Phone Gamepad Server
");
Console.ResetColor();

// Handle CLI diagnostic & management flags
if (args.Any(a => a.Equals("--bus-status", StringComparison.OrdinalIgnoreCase) || a.Equals("--status", StringComparison.OrdinalIgnoreCase)))
{
    var status = HIDOmniPadBusManager.DetectStatus();
    Console.WriteLine($"Primary Engine:       {status.PrimaryEngine}");
    Console.WriteLine($"ViGEmBus Installed:   {status.ViGEmInstalled} (Running: {status.ViGEmRunning})");
    Console.WriteLine($"HIDMaestro Installed: {status.HIDMaestroInstalled}");
    Console.WriteLine($"Hardware Profiles:    {status.TotalProfilesAvailable}");
    Console.WriteLine($"Summary:              {status.StatusSummary}");
    return;
}

if (args.Any(a => a.Equals("--install-drivers", StringComparison.OrdinalIgnoreCase)))
{
    Console.WriteLine("[HIDOmniPadBus] Installing driver suite...");
    bool ok = HIDOmniPadBusManager.InstallAll();
    Console.WriteLine(ok ? "[HIDOmniPadBus] Driver installation completed." : "[HIDOmniPadBus] Driver installation failed.");
    return;
}

if (args.Any(a => a.Equals("--uninstall-drivers", StringComparison.OrdinalIgnoreCase)))
{
    Console.WriteLine("[HIDOmniPadBus] Purging driver suite...");
    bool ok = HIDOmniPadBusManager.UninstallAll();
    Console.WriteLine(ok ? "[HIDOmniPadBus] Driver uninstallation completed." : "[HIDOmniPadBus] Driver uninstallation failed.");
    return;
}

// Ensure single-instance execution to avoid port collision crashes (10048 / AddressInUseException)
if (!SingleInstanceHelper.TryAcquireOrResolve(args, out var singleInstanceMutex))
{
    return;
}

// 1. Initialize Gamepad Backend (HIDOmniPadBus with 15 Profile Presets or KBM Fallback)
bool forceKbm = args.Any(a => a.Equals("--kbm", StringComparison.OrdinalIgnoreCase) ||
                              a.Equals("--mouse", StringComparison.OrdinalIgnoreCase) ||
                              a.Equals("--keyboard", StringComparison.OrdinalIgnoreCase));

bool debugArg = args.Any(a => a.Equals("--debug", StringComparison.OrdinalIgnoreCase) || a.Equals("-d", StringComparison.OrdinalIgnoreCase));
if (debugArg)
{
    ServerTelemetry.DebugMode = true;
}

var selectedPreset = ControllerProfilePreset.Xbox360_WHQL;

// Check CLI profile flag
string? cliProfile = args.FirstOrDefault(a => a.StartsWith("--profile=", StringComparison.OrdinalIgnoreCase));
if (!string.IsNullOrEmpty(cliProfile))
{
    selectedPreset = ControllerProfileCatalog.Parse(cliProfile[10..]);
}
else if (args.Any(a => a.Equals("--ds4", StringComparison.OrdinalIgnoreCase) || a.Equals("--ps4", StringComparison.OrdinalIgnoreCase)))
{
    selectedPreset = ControllerProfilePreset.DualShock4_WHQL;
}
else if (args.Any(a => a.Equals("--dualsense", StringComparison.OrdinalIgnoreCase) || a.Equals("--ps5", StringComparison.OrdinalIgnoreCase)))
{
    selectedPreset = ControllerProfilePreset.DualSense_PS5;
}
else if (args.Any(a => a.Equals("--switch", StringComparison.OrdinalIgnoreCase)))
{
    selectedPreset = ControllerProfilePreset.Switch_Pro;
}
else
{
    // Check saved profile
    string profileFile = Path.Combine(AppContext.BaseDirectory, "controller_profile.txt");
    if (File.Exists(profileFile))
    {
        try { selectedPreset = ControllerProfileCatalog.Parse(File.ReadAllText(profileFile).Trim()); } catch { }
    }
}

var backend = new SwitchablePadBackend(forceKeyboardMouse: forceKbm, initialPreset: selectedPreset);
ServerTelemetry.ActiveDriverName = backend.CurrentEngineName;
backend.ProfileChanged += (_, engine) => ServerTelemetry.ActiveDriverName = engine;

// 2. Initialize Session Manager
var sessionManager = new SessionManager();
sessionManager.ClientConnected += (slot, ep) =>
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"[+] Player {slot + 1} connected from {ep}");
    Console.ResetColor();
};
sessionManager.ClientDisconnected += (slot, ep) =>
{
    Console.ForegroundColor = ConsoleColor.DarkYellow;
    Console.WriteLine($"[-] Player {slot + 1} disconnected from {ep}");
    Console.ResetColor();
};

// 3. Start CemuHook DSU UDP Motion Server (Port 26760)
var dsuServer = new DsuMotionServer(Protocol.DefaultDsuPort);
dsuServer.Start();
Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine($"[DSU Motion] CemuHook Server listening on 0.0.0.0:{Protocol.DefaultDsuPort} (100 Hz 6-Axis Emulation)");
Console.ResetColor();

// 3b. Motion, Mouse & Profile Watcher Engines
var gyroEngine = new GyroAimEngine();
var mouseEngine = new TouchpadMouseEngine();
var profileWatcher = new ProcessProfileWatcher();
profileWatcher.Start();
Console.WriteLine("[Profiles]   Game process auto-profile watcher started");

// 4. Start UDP Input Server (125-250 Hz stream on 27500)
var udpServer = new UdpInputServer(backend, sessionManager, dsuServer, mouseEngine, gyroEngine);
udpServer.Start();
Console.WriteLine($"[UDP Input]  Listening on 0.0.0.0:{Protocol.DefaultInputPort} (125-250 Hz Stream)");

// 5. Start LAN Discovery Server (Broadcast on 27501)
var discoveryServer = new DiscoveryServer();
discoveryServer.Start();
Console.WriteLine($"[Discovery]  Broadcast listening on 0.0.0.0:{Protocol.DiscoveryPort}");

// 6. Start Web Server & WebSockets (Zero-install PWA on 27502)
var webServer = new WebServer(backend, sessionManager, dsuServer, mouseEngine, gyroEngine, profileWatcher, Protocol.DefaultWebPort);
try
{
    await webServer.StartAsync();
}
catch (Exception ex) when (ex is System.IO.IOException || ex.InnerException is System.Net.Sockets.SocketException)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine();
    Console.WriteLine("========================================================================");
    Console.WriteLine($"  [ERROR] Failed to bind Web Server to port {Protocol.DefaultWebPort} (Port In Use)");
    Console.WriteLine("========================================================================");
    Console.WriteLine($"  Details: {ex.Message}");
    Console.WriteLine("  Another application or background process is using this port.");
    Console.WriteLine("========================================================================");
    Console.ResetColor();

    dsuServer.Dispose();
    udpServer.Dispose();
    discoveryServer.Dispose();
    profileWatcher.Dispose();
    backend.Dispose();
    singleInstanceMutex?.Dispose();
    return;
}

string localIp = NetworkHelper.GetLocalIpAddress();
string webUrl = $"http://{localIp}:{Protocol.DefaultWebPort}";
string appDeepLink = $"omnipad://{localIp}:{Protocol.DefaultWebPort}";
bool qrIsDeepLink = false;
bool qrInverted = false;
Console.WriteLine($"[Web Server] Serving Web Gamepad PWA on {webUrl}");

// 6. Start USB Phone Hotplug Watcher (Auto-configures sub-1ms ADB reverse tunnel whenever phone is plugged in)
var usbWatcher = new UsbPhoneWatcher(Protocol.DefaultInputPort, Protocol.DefaultWebPort);
usbWatcher.Start();

// 7. Display Connection Instructions & Terminal QR Code
Console.WriteLine();
Console.ForegroundColor = ConsoleColor.White;
Console.WriteLine("========================================================================");
Console.WriteLine("  POINT YOUR PHONE CAMERA AT THIS QR CODE TO CONNECT INSTANTLY:");
Console.WriteLine("========================================================================");
Console.ResetColor();

QrCodeHelper.PrintTerminalQr(webUrl, qrInverted);

Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Yellow;
Console.WriteLine($"  Web Controller URL:  {webUrl}");
Console.WriteLine($"  OmniPad App Link:    {appDeepLink}");
Console.WriteLine($"  Web Gamepad Tester:  {webUrl}/test-gamepad.html");
Console.WriteLine($"  Native UDP Stream:   {localIp}:{Protocol.DefaultInputPort}");
Console.WriteLine("  USB Mode:            Plug phone in USB, run `adb reverse` (or auto)");
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("  Debug Mode:          Press [D] anytime to toggle live accuracy/numbers");
Console.WriteLine("  Toggle QR Code:      Press [Q] anytime to switch QR between Web URL & App Deep Link");
Console.WriteLine("  Invert QR Colors:    Press [I] anytime to flip QR between Light & Dark themes");
Console.WriteLine("========================================================================");
Console.ResetColor();
Console.WriteLine("Server running. Press Ctrl+C to stop.");
Console.WriteLine();

// Live Telemetry Loop
var cts = new CancellationTokenSource();
Console.CancelKeyPress += (s, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

if (ServerTelemetry.DebugMode)
{
    ServerTelemetry.StartLogger();
}

// Background Console Key Listener for on-the-fly 'D' toggle
_ = Task.Run(() =>
{
    while (!cts.IsCancellationRequested)
    {
        try
        {
            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.D)
                {
                    ServerTelemetry.DebugMode = !ServerTelemetry.DebugMode;
                    Console.ForegroundColor = ServerTelemetry.DebugMode ? ConsoleColor.Cyan : ConsoleColor.DarkYellow;
                    Console.WriteLine($"\n>>> [DEBUG MODE {(ServerTelemetry.DebugMode ? "ACTIVE (Live Telemetry)" : "DISABLED")}] <<<");
                    Console.ResetColor();
                }
                else if (key.Key == ConsoleKey.Q)
                {
                    qrIsDeepLink = !qrIsDeepLink;
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    Console.WriteLine($"\n>>> [QR CODE FORMAT: {(qrIsDeepLink ? $"OmniPad App Deep Link ({appDeepLink})" : $"Web Browser URL ({webUrl})")}] <<<");
                    Console.ResetColor();
                    QrCodeHelper.PrintTerminalQr(qrIsDeepLink ? appDeepLink : webUrl, qrInverted);
                }
                else if (key.Key == ConsoleKey.I)
                {
                    qrInverted = !qrInverted;
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"\n>>> [QR COLOR: {(qrInverted ? "Inverted Theme" : "Standard Theme")}] <<<");
                    Console.ResetColor();
                    QrCodeHelper.PrintTerminalQr(qrIsDeepLink ? appDeepLink : webUrl, qrInverted);
                }
                else if (key.Key == ConsoleKey.C)
                {
                    Console.Clear();
                }
            }
            Thread.Sleep(50);
        }
        catch { }
    }
});

long lastPackets = 0;
var sw = Stopwatch.StartNew();

try
{
    while (!cts.IsCancellationRequested)
    {
        await Task.Delay(1000, cts.Token);
        double elapsed = sw.Elapsed.TotalSeconds;
        long currentPackets = udpServer.PacketsReceived;
        double hz = (currentPackets - lastPackets) / elapsed;
        lastPackets = currentPackets;
        sw.Restart();

        int players = sessionManager.ConnectedCount;
        string mode = backend.CurrentEngineName;

        if (!ServerTelemetry.DebugMode)
        {
            Console.Write($"\r[Status] Players: {players}/{SessionManager.MaxSlots} | Input Rate: {hz:0} Hz | Engine: {mode} | Profile: {profileWatcher.CurrentProfile} [Press 'D' for Live Debug]     ");
        }
    }
}
catch (OperationCanceledException) { }

Console.WriteLine("\nShutting down OmniPad Server...");
usbWatcher.Dispose();
profileWatcher.Dispose();
dsuServer.Dispose();
udpServer.Dispose();
discoveryServer.Dispose();
await webServer.DisposeAsync();
backend.Dispose();
singleInstanceMutex?.Dispose();
Console.WriteLine("Clean shutdown complete. Goodbye!");
