using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using OmniPadServer.Core;

namespace OmniPadServer.App;

public static class AdbHelper
{
    private static string? _adbPath;

    public static string? FindAdb()
    {
        if (_adbPath != null && File.Exists(_adbPath)) return _adbPath;

        // 1. Check PATH
        try
        {
            var psi = new ProcessStartInfo("adb", "version")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(1000);
            if (p?.ExitCode == 0)
            {
                _adbPath = "adb";
                return _adbPath;
            }
        }
        catch { }

        // 2. Check standard candidate paths across macOS, Linux, and Windows
        var candidates = GetAdbCandidatePaths();
        foreach (var path in candidates)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                _adbPath = Path.GetFullPath(path);
                return _adbPath;
            }
        }

        return null;
    }

    public static IReadOnlyList<string> GetAdbCandidatePaths()
    {
        var list = new List<string>();

        // macOS standard installation paths
        list.Add("/opt/homebrew/bin/adb"); // Apple Silicon Homebrew
        list.Add("/usr/local/bin/adb");   // Intel Mac Homebrew / Standard Unix

        // Linux standard installation paths
        list.Add("/usr/bin/adb");         // Debian/Ubuntu/Fedora standard package

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home))
        {
            list.Add(Path.Combine(home, "Library", "Android", "sdk", "platform-tools", "adb"));
            list.Add(Path.Combine(home, "Android", "Sdk", "platform-tools", "adb"));
        }

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string? androidHome = Environment.GetEnvironmentVariable("ANDROID_HOME");
        string? androidSdkRoot = Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");
        string exeSuffix = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "";

        if (!string.IsNullOrEmpty(androidHome))
        {
            list.Add(Path.Combine(androidHome, "platform-tools", $"adb{exeSuffix}"));
        }
        if (!string.IsNullOrEmpty(androidSdkRoot))
        {
            list.Add(Path.Combine(androidSdkRoot, "platform-tools", $"adb{exeSuffix}"));
        }

        // Windows standard paths
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (!string.IsNullOrEmpty(localAppData))
            {
                list.Add(Path.Combine(localAppData, "Android", "Sdk", "platform-tools", "adb.exe"));
            }
            list.Add(@"C:\Program Files\Android\platform-tools\adb.exe");
            list.Add(@"C:\Android\platform-tools\adb.exe");
            list.Add(@"C:\platform-tools\adb.exe");
        }

        // Portable / relative directory paths
        list.Add(Path.Combine(AppContext.BaseDirectory, "platform-tools", $"adb{exeSuffix}"));
        list.Add(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "platform-tools", $"adb{exeSuffix}"));

        return list;
    }

    public static async Task<List<(string Serial, string Status, string Details)>> GetConnectedDevicesAsync(string adb)
    {
        var list = new List<(string Serial, string Status, string Details)>();
        try
        {
            var psi = new ProcessStartInfo(adb, "devices -l")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return list;

            string output = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();

            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var line in lines)
            {
                if (line.StartsWith("List of devices attached", StringComparison.OrdinalIgnoreCase))
                    continue;

                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    string serial = parts[0];
                    string status = parts[1];
                    string details = parts.Length > 2 ? string.Join(' ', parts, 2, parts.Length - 2) : "";
                    list.Add((serial, status, details));
                }
            }
        }
        catch { }

        return list;
    }

    public static async Task<bool> RunCommandAsync(string adb, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(adb, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return false;

            await p.WaitForExitAsync();
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Continuous USB Hotplug Watcher that monitors ADB device state in the background.
/// Whenever a phone is plugged in, reconnected, or has USB debugging authorized after server startup,
/// it instantly establishes the sub-1ms reverse tunnel for ports 27500 and 27502.
/// </summary>
public sealed class UsbPhoneWatcher : IDisposable
{
    private readonly int _inputPort;
    private readonly int _webPort;
    private readonly CancellationTokenSource _cts = new();
    private readonly HashSet<string> _activeReversedSerials = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _warnedUnauthorized = new(StringComparer.OrdinalIgnoreCase);
    private Task? _pollTask;

    public event Action<string>? DeviceConnected;
    public event Action<string>? DeviceDisconnected;

    public bool IsWatching { get; private set; }
    public IReadOnlyCollection<string> ConnectedSerials => _activeReversedSerials;

    public UsbPhoneWatcher(int inputPort = Protocol.DefaultInputPort, int webPort = Protocol.DefaultWebPort)
    {
        _inputPort = inputPort;
        _webPort = webPort;
    }

    public void Start()
    {
        if (IsWatching) return;
        IsWatching = true;
        _pollTask = Task.Run(PollLoopAsync);
    }

    private async Task PollLoopAsync()
    {
        // Initial scan immediately
        await CheckDevicesAsync();

        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(1500, _cts.Token);
                await CheckDevicesAsync();
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Debug.WriteLine($"[USB Watcher] Poll error: {ex.Message}");
            }
        }
    }

    public async Task CheckDevicesAsync()
    {
        string? adb = AdbHelper.FindAdb();
        if (adb == null) return;

        var currentDevices = await AdbHelper.GetConnectedDevicesAsync(adb);
        var currentAuthorizedSerials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (serial, status, details) in currentDevices)
        {
            if (status.Equals("device", StringComparison.OrdinalIgnoreCase))
            {
                currentAuthorizedSerials.Add(serial);

                // Newly plugged in or reconnected phone!
                if (!_activeReversedSerials.Contains(serial))
                {
                    bool revInput = await AdbHelper.RunCommandAsync(adb, $"-s {serial} reverse tcp:{_inputPort} tcp:{_inputPort}");
                    bool revWeb = await AdbHelper.RunCommandAsync(adb, $"-s {serial} reverse tcp:{_webPort} tcp:{_webPort}");
                    bool revMic = await AdbHelper.RunCommandAsync(adb, $"-s {serial} reverse tcp:27503 tcp:27503");

                    if (revInput || revWeb || revMic)
                    {
                        _activeReversedSerials.Add(serial);
                        _warnedUnauthorized.Remove(serial);

                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.WriteLine($"\n[USB Hotplug] Phone connected via USB: {serial} ({details})");
                        Console.WriteLine($"[USB Hotplug] Sub-1ms wired tunnel active: http://localhost:{_webPort} & UDP {_inputPort}");
                        Console.ResetColor();

                        DeviceConnected?.Invoke(serial);
                    }
                }
            }
            else if (status.Equals("unauthorized", StringComparison.OrdinalIgnoreCase))
            {
                if (_warnedUnauthorized.Add(serial))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"\n[USB Hotplug] Phone detected ({serial}) but unauthorized. Please tap 'Allow USB debugging' on your phone.");
                    Console.ResetColor();
                }
            }
        }

        // Check for unplugged/disconnected phones
        List<string>? disconnected = null;
        foreach (var active in _activeReversedSerials)
        {
            if (!currentAuthorizedSerials.Contains(active))
            {
                disconnected ??= new List<string>();
                disconnected.Add(active);
            }
        }

        if (disconnected != null)
        {
            foreach (var disc in disconnected)
            {
                _activeReversedSerials.Remove(disc);
                _warnedUnauthorized.Remove(disc);

                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine($"\n[USB Hotplug] Phone disconnected from USB: {disc}");
                Console.ResetColor();

                DeviceDisconnected?.Invoke(disc);
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
