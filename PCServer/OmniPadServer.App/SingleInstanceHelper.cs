using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace OmniPadServer.App;

/// <summary>
/// Manages single-instance lifecycle for OmniPadServer.
/// Prevents port collision crashes (10048 / AddressInUseException) when multiple instances are launched.
/// </summary>
public static class SingleInstanceHelper
{
    private const string MutexName = @"Global\OmniPadServer_SingleInstance_Mutex";
    private static Mutex? _mutex;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_RESTORE = 9;

    /// <summary>
    /// Checks if another instance is already running. If so, provides a user-friendly prompt or auto-takeover.
    /// Returns true if this instance can safely proceed to start; false if it should exit.
    /// </summary>
    public static bool TryAcquireOrResolve(string[] args, out Mutex? acquiredMutex)
    {
        acquiredMutex = null;
        bool isInitialInstance;

        try
        {
            _mutex = new Mutex(true, MutexName, out isInitialInstance);
        }
        catch (UnauthorizedAccessException)
        {
            // If mutex was created with different user permissions, fall back to local scope
            _mutex = new Mutex(true, @"Local\OmniPadServer_SingleInstance_Mutex", out isInitialInstance);
        }

        if (isInitialInstance)
        {
            acquiredMutex = _mutex;
            return true;
        }

        // Another instance is holding the mutex
        var currentProc = Process.GetCurrentProcess();
        var existingProc = Process.GetProcessesByName(currentProc.ProcessName)
            .FirstOrDefault(p => p.Id != currentProc.Id);

        // Check if command line specified forced restart / takeover
        bool forceKill = args.Any(a => a.Equals("--kill-existing", StringComparison.OrdinalIgnoreCase) ||
                                       a.Equals("--restart", StringComparison.OrdinalIgnoreCase) ||
                                       a.Equals("-f", StringComparison.OrdinalIgnoreCase) ||
                                       a.Equals("--force", StringComparison.OrdinalIgnoreCase));

        if (forceKill && existingProc != null)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[!] Terminating existing OmniPad instance (PID {existingProc.Id})...");
            Console.ResetColor();
            KillProcessAndChildren(existingProc);
            Thread.Sleep(600);

            // Re-attempt mutex acquisition
            try
            {
                _mutex?.Dispose();
                _mutex = new Mutex(true, MutexName, out isInitialInstance);
                if (isInitialInstance)
                {
                    acquiredMutex = _mutex;
                    return true;
                }
            }
            catch { }
        }

        // Display user-friendly collision screen instead of unhandled SocketException
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("========================================================================");
        Console.WriteLine("  [!] ANOTHER INSTANCE OF OMNIPAD SERVER IS ALREADY RUNNING");
        Console.WriteLine("========================================================================");
        Console.ResetColor();

        int existingPid = existingProc?.Id ?? 0;
        if (existingPid > 0)
        {
            Console.WriteLine($"  An active OmniPadServer instance (PID: {existingPid}) was detected.");
        }
        else
        {
            Console.WriteLine("  An active OmniPadServer instance was detected.");
        }

        Console.WriteLine("  Ports 27500–27503 and 26760 are currently in use by that process.");
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("  Options:");
        Console.WriteLine("    [K]  Terminate existing instance and start server here");
        if (existingProc != null && existingProc.MainWindowHandle != IntPtr.Zero)
        {
            Console.WriteLine("    [F]  Bring existing OmniPad window to foreground");
        }
        Console.WriteLine("    [ESC / Enter / Any key]  Close this window (Auto-exit in 8 seconds)");
        Console.ResetColor();
        Console.WriteLine("========================================================================");

        // If console input is redirected (e.g. background runner, automated tests), exit immediately
        if (Console.IsInputRedirected)
        {
            Console.WriteLine("  Input is redirected. Exiting safely to prevent port conflict.");
            _mutex?.Dispose();
            return false;
        }

        // Wait for user input with a countdown
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline)
        {
            int remaining = (int)Math.Ceiling((deadline - DateTime.UtcNow).TotalSeconds);
            Console.Write($"\r  Auto-closing in {remaining}s... Choose an option: ");

            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(intercept: true).Key;
                Console.WriteLine();

                if (key == ConsoleKey.K)
                {
                    if (existingProc != null && !existingProc.HasExited)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"  Stopping existing instance (PID {existingProc.Id})...");
                        Console.ResetColor();
                        KillProcessAndChildren(existingProc);
                        Thread.Sleep(800);
                    }

                    try
                    {
                        _mutex?.Dispose();
                        _mutex = new Mutex(true, MutexName, out isInitialInstance);
                        acquiredMutex = _mutex;
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("  [+] Ports freed successfully. Starting OmniPad Server...");
                        Console.ResetColor();
                        Console.WriteLine();
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  Error acquiring lock: {ex.Message}");
                    }
                }
                else if (key == ConsoleKey.F && existingProc != null && existingProc.MainWindowHandle != IntPtr.Zero)
                {
                    ShowWindow(existingProc.MainWindowHandle, SW_RESTORE);
                    SetForegroundWindow(existingProc.MainWindowHandle);
                    Console.WriteLine("  Brought existing OmniPad window to front. Exiting this window.");
                    _mutex?.Dispose();
                    return false;
                }
                else
                {
                    Console.WriteLine("  Exiting. Existing instance continues running.");
                    _mutex?.Dispose();
                    return false;
                }
            }

            Thread.Sleep(100);
        }

        Console.WriteLine("\n  Timed out. Exiting safely. Existing OmniPad instance continues running.");
        _mutex?.Dispose();
        return false;
    }

    private static void KillProcessAndChildren(Process proc)
    {
        try
        {
            proc.Kill(entireProcessTree: true);
            proc.WaitForExit(3000);
        }
        catch { }
    }
}
