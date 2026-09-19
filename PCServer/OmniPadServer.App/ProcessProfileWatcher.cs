using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OmniPadServer.App;

/// <summary>
/// Monitors the active foreground window on Windows and automatically detects running games
/// to notify connected OmniPad clients of matching controller layout presets.
/// </summary>
public sealed class ProcessProfileWatcher : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private Task? _pollTask;
    private string _currentProfile = "xbox360";

    public bool Enabled { get; set; } = true;
    public string CurrentProfile => _currentProfile;

    /// <summary>
    /// Event fired whenever the foreground game changes layout profiles.
    /// Passes the layout preset ID (e.g. "arcade", "fps", "racing", "switch_pro", "snes_retro").
    /// </summary>
    public event Action<string>? ProfileChanged;

    /// <summary>
    /// Process name (without extension, lowercase) to layout preset mapping.
    /// </summary>
    public ConcurrentDictionary<string, string> ProcessMap { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        // Fighting Games -> Arcade Fightstick
        ["streetfighter6"] = "arcade",
        ["streetfighterv"] = "arcade",
        ["tekken8"] = "arcade",
        ["tekken7"] = "arcade",
        ["guiltygear"] = "arcade",
        ["ggst"] = "arcade",
        ["dragonballfighterz"] = "arcade",
        ["mortalkombat1"] = "arcade",
        ["sf6"] = "arcade",

        // Racing Games -> Steering Wheel & Pedals
        ["forzahorizon5"] = "racing",
        ["forzahorizon4"] = "racing",
        ["f1_24"] = "racing",
        ["f1_23"] = "racing",
        ["assettocorsa"] = "racing",
        ["dirt"] = "racing",
        ["needforspeed"] = "racing",

        // Shooters -> FPS Precision & Flick Stick
        ["cyberpunk2077"] = "fps",
        ["doometernal"] = "fps",
        ["doom"] = "fps",
        ["haloinfinite"] = "fps",
        ["callofduty"] = "fps",
        ["apex"] = "fps",
        ["overwatch"] = "fps",
        ["fortnite"] = "fps",

        // Nintendo & Modern Emulators -> Switch Pro
        ["yuzu"] = "switch_pro",
        ["ryujinx"] = "switch_pro",
        ["cemu"] = "switch_pro",
        ["citra-qt"] = "switch_pro",
        ["citra"] = "switch_pro",
        ["dolphin"] = "switch_pro",

        // Retro Emulators -> SNES Classic
        ["snes9x"] = "snes_retro",
        ["retroarch"] = "snes_retro",
        ["fceux"] = "snes_retro",
        ["nestopia"] = "snes_retro",
        ["visualboyadvance"] = "snes_retro",
        ["mgba"] = "snes_retro"
    };

    public void Start()
    {
        if (!OperatingSystem.IsWindows()) return;
        _pollTask = Task.Run(PollLoopAsync);
    }

    private async Task PollLoopAsync()
    {
        string lastDetectedProcess = string.Empty;

        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(1000, _cts.Token);
                if (!Enabled) continue;

                string procName = GetForegroundProcessName();
                if (string.IsNullOrWhiteSpace(procName) || string.Equals(procName, lastDetectedProcess, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                lastDetectedProcess = procName;

                if (ProcessMap.TryGetValue(procName, out string? targetProfile) && !string.IsNullOrEmpty(targetProfile))
                {
                    if (!string.Equals(_currentProfile, targetProfile, StringComparison.OrdinalIgnoreCase))
                    {
                        _currentProfile = targetProfile;
                        ProfileChanged?.Invoke(targetProfile);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                // Silently swallow transient process query errors
                Debug.WriteLine($"[ProcessWatcher] Error: {ex.Message}");
            }
        }
    }

    public string GetForegroundProcessName()
    {
        if (!OperatingSystem.IsWindows()) return string.Empty;

        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return string.Empty;

        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return string.Empty;

        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }

    #region Win32 P/Invoke
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    #endregion
}
