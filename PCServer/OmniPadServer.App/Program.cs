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

// 1. Initialize Gamepad Backend (ViGEm Xbox 360, DualShock 4, or Keyboard/Mouse fallback)
bool forceKbm = args.Any(a => a.Equals("--kbm", StringComparison.OrdinalIgnoreCase) ||
                              a.Equals("--mouse", StringComparison.OrdinalIgnoreCase) ||
                              a.Equals("--keyboard", StringComparison.OrdinalIgnoreCase));
bool useDs4 = args.Any(a => a.Equals("--ds4", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--ps4", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--controller-type=ds4", StringComparison.OrdinalIgnoreCase));
var emulationType = useDs4 ? EmulationType.DualShock4 : EmulationType.Xbox360;
var backend = new SwitchablePadBackend(forceKeyboardMouse: forceKbm, initialType: emulationType);

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
await webServer.StartAsync();

string localIp = NetworkHelper.GetLocalIpAddress();
string webUrl = $"http://{localIp}:{Protocol.DefaultWebPort}";
Console.WriteLine($"[Web Server] Serving Web Gamepad PWA on {webUrl}");

// 6. Check for USB Phone (ADB Reverse Tunnel)
AdbHelper.TrySetupUsbReverse(Protocol.DefaultInputPort, Protocol.DefaultWebPort);

// 7. Display Connection Instructions & Terminal QR Code
Console.WriteLine();
Console.ForegroundColor = ConsoleColor.White;
Console.WriteLine("========================================================================");
Console.WriteLine("  POINT YOUR PHONE CAMERA AT THIS QR CODE TO CONNECT INSTANTLY:");
Console.WriteLine("========================================================================");
Console.ResetColor();

QrCodeHelper.PrintTerminalQr(webUrl);

Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Yellow;
Console.WriteLine($"  Web Controller URL:  {webUrl}");
Console.WriteLine($"  Native UDP Stream:   {localIp}:{Protocol.DefaultInputPort}");
Console.WriteLine("  USB Mode:            Plug phone in USB, run `adb reverse` (or auto)");
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

        Console.Write($"\r[Status] Players: {players}/{SessionManager.MaxSlots} | Input Rate: {hz:0} Hz | Engine: {mode} | Profile: {profileWatcher.CurrentProfile}     ");
    }
}
catch (OperationCanceledException) { }

Console.WriteLine("\nShutting down OmniPad Server...");
profileWatcher.Dispose();
dsuServer.Dispose();
udpServer.Dispose();
discoveryServer.Dispose();
await webServer.DisposeAsync();
backend.Dispose();
Console.WriteLine("Clean shutdown complete. Goodbye!");
