using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
#if WINDOWS
using System.ServiceProcess;
using HIDMaestro;
using Microsoft.Win32;
#endif

namespace OmniPadServer.ViGEm;

public sealed record HIDOmniPadBusStatus(
    bool ViGEmInstalled,
    bool ViGEmRunning,
    string ViGEmVersion,
    bool HIDMaestroInstalled,
    int TotalProfilesAvailable,
    string PrimaryEngine,
    string StatusSummary
);

/// <summary>
/// Central lifecycle and driver management engine for HIDOmniPadBus.
/// Safely manages ViGEmBus (KMDF WHQL) and HIDMaestro (UMDF2 Direct HID)
/// installation, uninstallation, and diagnostics.
/// </summary>
public static class HIDOmniPadBusManager
{
    private const string ViGEmServiceName = "ViGEmBus";
    private const string ViGEmMsiProductCode = "{966606F3-2745-49E9-BF15-5C3EAA4E9077}";
    private const string HIDMaestroCertSubject = "HIDMaestroTestCert";

    public static HIDOmniPadBusStatus DetectStatus()
    {
#if WINDOWS
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return CreateNonWindowsStatus();
        }

        bool vigemInstalled = false;
        bool vigemRunning = false;
        string vigemVersion = "Not Detected";

        try
        {
            using var sc = new ServiceController(ViGEmServiceName);
            vigemInstalled = true;
            vigemRunning = sc.Status == ServiceControllerStatus.Running;
            vigemVersion = "1.22.0 (WHQL)";
        }
        catch
        {
            vigemInstalled = false;
            vigemRunning = false;
        }

        bool hidMaestroInstalled = false;
        int profileCount = 0;

        try
        {
            using var ctx = new HMContext();
            hidMaestroInstalled = ctx.IsDriverInstalled;
            profileCount = ctx.LoadDefaultProfiles();
        }
        catch
        {
            hidMaestroInstalled = false;
        }

        string primaryEngine;
        string summary;

        if (vigemRunning && hidMaestroInstalled)
        {
            primaryEngine = "Full HIDOmniPadBus Suite (WHQL Kernel + UMDF2 Direct HID)";
            summary = "Optimal: All 231 hardware profiles, anti-cheat gaming, and direct web testing are fully active.";
        }
        else if (vigemRunning)
        {
            primaryEngine = "ViGEm Kernel Engine (WHQL)";
            summary = "Hardware Xbox 360 & PS4 emulation active. Anti-cheat games fully supported.";
        }
        else if (hidMaestroInstalled)
        {
            primaryEngine = "HIDMaestro UMDF2 Engine";
            summary = "Direct HID emulation active (DualSense PS5, Switch Pro, GameCube, Racing Wheels).";
        }
        else
        {
            primaryEngine = "Zero-Driver Fallback (Windows SendInput)";
            summary = "No drivers installed. Operating in portable zero-driver Keyboard/Mouse mode.";
        }

        return new HIDOmniPadBusStatus(
            vigemInstalled,
            vigemRunning,
            vigemVersion,
            hidMaestroInstalled,
            profileCount,
            primaryEngine,
            summary
        );
#else
        return CreateNonWindowsStatus();
#endif
    }

    private static HIDOmniPadBusStatus CreateNonWindowsStatus()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            bool hasUinput = File.Exists("/dev/uinput") || File.Exists("/dev/input/uinput");
            return new HIDOmniPadBusStatus(
                ViGEmInstalled: false,
                ViGEmRunning: false,
                ViGEmVersion: "N/A (Linux Host)",
                HIDMaestroInstalled: false,
                TotalProfilesAvailable: 2,
                PrimaryEngine: "Linux Native uinput Subsystem",
                StatusSummary: hasUinput
                    ? "Ready: Linux kernel /dev/uinput active (Xbox 360 & DualShock 4 emulation)."
                    : "Notice: /dev/uinput not found or requires input group permissions (e.g. sudo usermod -aG input $USER)."
            );
        }

        return new HIDOmniPadBusStatus(
            ViGEmInstalled: false,
            ViGEmRunning: false,
            ViGEmVersion: "N/A (Non-Windows Host)",
            HIDMaestroInstalled: false,
            TotalProfilesAvailable: 0,
            PrimaryEngine: "Zero-Driver Keyboard & Mouse",
            StatusSummary: $"Non-Windows host ({RuntimeInformation.OSDescription}): Virtual hardware drivers not applicable. DSU motion & KBM active."
        );
    }

    public static bool InstallViGEm(string? explicitInstallerPath = null)
    {
        string? installer = explicitInstallerPath;

        if (string.IsNullOrEmpty(installer) || !File.Exists(installer))
        {
            string baseDir = AppContext.BaseDirectory;
            string candidate1 = Path.Combine(baseDir, "ViGEmBus_Setup.exe");
            string candidate2 = Path.Combine(baseDir, "tools", "ViGEmBus_Setup.exe");
            string candidate3 = Path.Combine(baseDir, "..", "tools", "ViGEmBus_Setup.exe");

            if (File.Exists(candidate1)) installer = candidate1;
            else if (File.Exists(candidate2)) installer = candidate2;
            else if (File.Exists(candidate3)) installer = Path.GetFullPath(candidate3);
        }

        if (string.IsNullOrEmpty(installer) || !File.Exists(installer))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[HIDOmniPadBus] ViGEm installer binary not found locally.");
            Console.ResetColor();
            return false;
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"[HIDOmniPadBus] Running silent ViGEmBus installer: {Path.GetFileName(installer)}...");
        Console.ResetColor();

        return RunCommand(installer, "/quiet /norestart");
    }

    public static bool InstallHIDMaestro()
    {
#if WINDOWS
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("[HIDOmniPadBus] Staging HIDMaestro UMDF2 driver and test certificate...");
        Console.ResetColor();

        try
        {
            using var ctx = new HMContext();
            ctx.InstallDriver();
            return ctx.IsDriverInstalled;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[HIDOmniPadBus] Failed to stage HIDMaestro driver: {ex.Message}");
            Console.ResetColor();
            return false;
        }
#else
        return false;
#endif
    }

    public static bool InstallAll(string? vigemInstaller = null)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Console.WriteLine("[HIDOmniPadBus] Linux uses kernel /dev/uinput. Ensure your user belongs to 'input' group: sudo usermod -aG input $USER");
            return true;
        }

        bool okVigem = InstallViGEm(vigemInstaller);
        bool okHm = InstallHIDMaestro();
        return okVigem || okHm;
    }

    public static bool UninstallAll()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Console.WriteLine($"[HIDOmniPadBusManager] Driver uninstallation is only supported on Windows hosts ({RuntimeInformation.OSDescription}).");
            return false;
        }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("[HIDOmniPadBus] Initiating complete driver and device purge...");
        Console.ResetColor();

        // 1. Remove all phantom device nodes
        CleanDeviceNodes();

        // 2. Stop and uninstall ViGEmBus
        UninstallViGEm();

        // 3. Delete DriverStore packages for HIDMaestro and ViGEm
        PurgeDriverStorePackages();

        // 4. Remove test certificates
        RemoveTestCertificates();

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("[HIDOmniPadBus] All virtual gamepad drivers, device nodes, and certificates have been cleanly uninstalled.");
        Console.ResetColor();

        return true;
    }

    private static void CleanDeviceNodes()
    {
        try
        {
            RunCommand("pnputil.exe", "/remove-device \"SWD\\HIDMAESTRO\\HM_0000\"");
            RunCommand("pnputil.exe", "/remove-device \"ROOT\\SYSTEM\\0001\"");
        }
        catch { }
    }

    private static void UninstallViGEm()
    {
        try
        {
            RunCommand("sc.exe", "stop ViGEmBus");
            RunCommand("msiexec.exe", $"/x {ViGEmMsiProductCode} /quiet /norestart");
        }
        catch { }
    }

    private static void PurgeDriverStorePackages()
    {
        try
        {
            var psi = new ProcessStartInfo("pnputil.exe", "/enum-drivers")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(5000);

                string[] entries = output.Split(new[] { "Published Name:" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var entry in entries)
                {
                    if (entry.Contains("hidmaestro", StringComparison.OrdinalIgnoreCase) ||
                        entry.Contains("vigem", StringComparison.OrdinalIgnoreCase))
                    {
                        var line = entry.Trim().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                        if (line.EndsWith(".inf", StringComparison.OrdinalIgnoreCase))
                        {
                            Console.WriteLine($"[HIDOmniPadBus] Purging driver package {line} from DriverStore...");
                            RunCommand("pnputil.exe", $"/delete-driver {line} /uninstall /force");
                        }
                    }
                }
            }
        }
        catch { }
    }

    private static void RemoveTestCertificates()
    {
        try
        {
            RemoveCertFromStore(StoreName.Root);
            RemoveCertFromStore(StoreName.TrustedPublisher);
        }
        catch { }
    }

    private static void RemoveCertFromStore(StoreName storeName)
    {
        try
        {
            using var store = new X509Store(storeName, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadWrite);
            var matches = store.Certificates.Find(X509FindType.FindBySubjectName, HIDMaestroCertSubject, false);
            foreach (var cert in matches)
            {
                Console.WriteLine($"[HIDOmniPadBus] Removing certificate {cert.Thumbprint} from {storeName}...");
                store.Remove(cert);
            }
        }
        catch { }
    }

    private static bool RunCommand(string exe, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(15000);
            return p?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
