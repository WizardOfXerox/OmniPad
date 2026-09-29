#if WINDOWS
using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace OmniPadServer.Legacinator;

public static class DriverCleaner
{
    public static int CleanPhantomDevices(ScanReport report)
    {
        int count = 0;
        foreach (var dev in report.GamepadDevices)
        {
            if (dev.IsPhantom && !dev.InstanceId.StartsWith(@"ROOT\SYSTEM", StringComparison.OrdinalIgnoreCase))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[Cleaning] Removing phantom device node: {dev.InstanceId} ({dev.Description})...");
                Console.ResetColor();

                bool ok = RunCommand("pnputil.exe", $"/remove-device \"{dev.InstanceId}\"");
                if (ok) count++;
            }
        }
        return count;
    }

    public static int CleanAllVirtualGamepads(ScanReport report)
    {
        int count = 0;
        foreach (var dev in report.GamepadDevices)
        {
            if (!dev.InstanceId.StartsWith(@"ROOT\SYSTEM", StringComparison.OrdinalIgnoreCase))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[Cleaning] Removing virtual gamepad device node: {dev.InstanceId} ({dev.Description})...");
                Console.ResetColor();

                bool ok = RunCommand("pnputil.exe", $"/remove-device \"{dev.InstanceId}\"");
                if (ok) count++;
            }
        }
        return count;
    }

    public static bool CleanFirewallRules()
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("[Firewall] Deleting all OmniPad firewall rules...");
        Console.ResetColor();

        // 1. Netsh wildcard rule delete
        RunCommand("netsh.exe", "advfirewall firewall delete rule name=\"OmniPadServer.App\"");
        RunCommand("netsh.exe", "advfirewall firewall delete rule name=\"OmniPad Gamepad Server\"");
        RunCommand("netsh.exe", "advfirewall firewall delete rule name=\"OmniPad Web PWA Server\"");

        // 2. PowerShell wildcard cleanup for any other OmniPad-named rules
        RunCommand("powershell.exe", "-NoProfile -Command \"Get-NetFirewallRule | Where-Object { $_.DisplayName -like '*OmniPad*' -or $_.Name -like '*OmniPad*' } | Remove-NetFirewallRule -ErrorAction SilentlyContinue\"");

        return true;
    }

    public static bool UninstallDriverStandard(ScanReport report, string? localSetupExe)
    {
        // 1. Try bundled setup exe first if present
        if (!string.IsNullOrEmpty(localSetupExe) && File.Exists(localSetupExe))
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"[Uninstall] Launching {Path.GetFileName(localSetupExe)} /uninstall...");
            Console.ResetColor();
            return RunProcessInteractive(localSetupExe, "/uninstall");
        }

        // 2. Try MSI uninstaller
        if (!string.IsNullOrEmpty(report.MsiUninstallString))
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"[Uninstall] Running Windows Installer: {report.MsiUninstallString}...");
            Console.ResetColor();

            // Typically: MsiExec.exe /X{GUID}
            string msiArgs = report.MsiUninstallString;
            if (msiArgs.StartsWith("MsiExec.exe", StringComparison.OrdinalIgnoreCase))
            {
                msiArgs = msiArgs.Substring("MsiExec.exe".Length).Trim();
            }

            return RunProcessInteractive("msiexec.exe", msiArgs);
        }

        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("[Error] No standard uninstaller found. Consider using Force Purge (Option 5).");
        Console.ResetColor();
        return false;
    }

    public static bool ForcePurgeDriverStore(ScanReport report)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("[Force Purge] Initiating Legacinator Driver Store Purge...");
        Console.ResetColor();

        // 1. Stop ViGEmBus service
        Console.WriteLine(" -> Stopping ViGEmBus kernel driver service...");
        RunCommand("sc.exe", "stop ViGEmBus");
        System.Threading.Thread.Sleep(500);

        // 2. Remove all virtual gamepads and root bus device
        CleanAllVirtualGamepads(report);

        Console.WriteLine(" -> Removing Root Virtual Gamepad Emulation Bus device node...");
        RunCommand("pnputil.exe", "/remove-device \"ROOT\\SYSTEM\\0001\"");

        // 3. Force delete driver packages from Windows Driver Store
        if (report.DriverStorePackages.Count == 0)
        {
            // Re-scan pnputil in case
            var outText = RunCommandOutput("pnputil.exe", "/enum-drivers");
            var pkgs = DiagnosticScanner.RunDiagnosticScan().DriverStorePackages;
            report.DriverStorePackages = pkgs;
        }

        foreach (var pkg in report.DriverStorePackages)
        {
            Console.WriteLine($" -> Purging driver package '{pkg.PublishedName}' ({pkg.OriginalName}) from Driver Store...");
            RunCommand("pnputil.exe", $"/delete-driver {pkg.PublishedName} /uninstall /force");
        }

        // 4. Clean Registry service entry
        try
        {
            Console.WriteLine(" -> Cleaning service registry entries...");
            Registry.LocalMachine.DeleteSubKeyTree(@"SYSTEM\CurrentControlSet\Services\ViGEmBus", throwOnMissingSubKey: false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    (Registry notice: {ex.Message})");
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("[Force Purge] Completed driver store removal and service cleanup.");
        Console.ResetColor();
        return true;
    }

    private static bool RunCommand(string file, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(10000);
            return p?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string RunCommandOutput(string file, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return "";
            var text = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            return text;
        }
        catch
        {
            return "";
        }
    }

    private static bool RunProcessInteractive(string file, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args)
            {
                UseShellExecute = true
            };
            using var p = Process.Start(psi);
            p?.WaitForExit();
            return p?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
#endif
