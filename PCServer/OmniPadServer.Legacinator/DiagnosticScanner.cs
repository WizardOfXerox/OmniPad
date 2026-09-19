using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace OmniPadServer.Legacinator;

public record DriverPackage(string PublishedName, string OriginalName, string ProviderName, string ClassName);
public record DeviceNode(string InstanceId, string Description, string Status, bool IsPhantom);
public record FirewallRule(string Name, string DisplayName, string Direction, string Action);

public class ScanReport
{
    public bool IsAdmin { get; set; }
    public string ServiceStatus { get; set; } = "Not Installed";
    public string? MsiUninstallString { get; set; }
    public string? InstalledVersion { get; set; }
    public List<DriverPackage> DriverStorePackages { get; set; } = new();
    public List<DeviceNode> GamepadDevices { get; set; } = new();
    public List<FirewallRule> FirewallRules { get; set; } = new();
    public bool RootBusExists { get; set; }
}

public static class DiagnosticScanner
{
    public static bool IsAdministrator()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(id);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public static bool RelaunchAsAdmin(string[] args)
    {
        try
        {
            var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath)) return false;

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = string.Join(" ", args),
                Verb = "runas",
                UseShellExecute = true
            };
            Process.Start(psi);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static ScanReport RunDiagnosticScan()
    {
        var report = new ScanReport
        {
            IsAdmin = IsAdministrator()
        };

        // 1. Check Service
        try
        {
            using var sc = new ServiceController("ViGEmBus");
            report.ServiceStatus = sc.Status.ToString();
        }
        catch
        {
            report.ServiceStatus = "Not Installed";
        }

        // 2. Check Registry for Installed App & UninstallString
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (key != null)
            {
                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    using var subKey = key.OpenSubKey(subKeyName);
                    var dispName = subKey?.GetValue("DisplayName")?.ToString() ?? "";
                    if (dispName.Contains("ViGEm", StringComparison.OrdinalIgnoreCase))
                    {
                        report.InstalledVersion = subKey?.GetValue("DisplayVersion")?.ToString();
                        report.MsiUninstallString = subKey?.GetValue("UninstallString")?.ToString();
                        break;
                    }
                }
            }
        }
        catch { }

        // 3. Scan Driver Store (pnputil /enum-drivers)
        try
        {
            var output = RunPnpUtil("/enum-drivers");
            report.DriverStorePackages = ParseDriverStorePackages(output);
        }
        catch { }

        // 4. Scan Virtual Gamepad Devices
        try
        {
            var devOutput = RunPnpUtil("/enum-devices");
            report.GamepadDevices = ParseGamepadDevices(devOutput);
            report.RootBusExists = devOutput.Contains(@"ROOT\SYSTEM\0001", StringComparison.OrdinalIgnoreCase) ||
                                  devOutput.Contains("Nefarius Virtual Gamepad", StringComparison.OrdinalIgnoreCase);
        }
        catch { }

        // 5. Scan Firewall Rules
        try
        {
            report.FirewallRules = ScanFirewallRules();
        }
        catch { }

        return report;
    }

    private static string RunPnpUtil(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("pnputil.exe", args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return "";
            var outText = p.StandardOutput.ReadToEnd();
            p.WaitForExit(3000);
            return outText;
        }
        catch
        {
            return "";
        }
    }

    private static List<DriverPackage> ParseDriverStorePackages(string pnpOutput)
    {
        var list = new List<DriverPackage>();
        var blocks = pnpOutput.Split(new[] { "Published Name:" }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var block in blocks)
        {
            if (block.Contains("vigembus.inf", StringComparison.OrdinalIgnoreCase) ||
                block.Contains("Nefarius", StringComparison.OrdinalIgnoreCase))
            {
                var pubMatch = Regex.Match(block, @"^\s*([^\r\n]+)");
                var origMatch = Regex.Match(block, @"Original Name:\s*([^\r\n]+)");
                var provMatch = Regex.Match(block, @"Provider Name:\s*([^\r\n]+)");
                var classMatch = Regex.Match(block, @"Class Name:\s*([^\r\n]+)");

                string published = pubMatch.Success ? pubMatch.Groups[1].Value.Trim() : "Unknown";
                string original = origMatch.Success ? origMatch.Groups[1].Value.Trim() : "vigembus.inf";
                string provider = provMatch.Success ? provMatch.Groups[1].Value.Trim() : "Nefarius";
                string className = classMatch.Success ? classMatch.Groups[1].Value.Trim() : "System";

                list.Add(new DriverPackage(published, original, provider, className));
            }
        }

        return list;
    }

    private static List<DeviceNode> ParseGamepadDevices(string devOutput)
    {
        var list = new List<DeviceNode>();
        var blocks = devOutput.Split(new[] { "Instance ID:" }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var block in blocks)
        {
            // Match Xbox 360 virtual pads or DualShock 4 virtual pads spawned by ViGEm
            if (block.Contains(@"USB\VID_045E&PID_028E", StringComparison.OrdinalIgnoreCase) ||
                block.Contains(@"USB\VID_054C&PID_05C4", StringComparison.OrdinalIgnoreCase) ||
                block.Contains(@"ROOT\SYSTEM\0001", StringComparison.OrdinalIgnoreCase))
            {
                var idMatch = Regex.Match(block, @"^\s*([^\r\n]+)");
                var descMatch = Regex.Match(block, @"Device Description:\s*([^\r\n]+)");
                var statusMatch = Regex.Match(block, @"Status:\s*([^\r\n]+)");

                string instanceId = idMatch.Success ? idMatch.Groups[1].Value.Trim() : "";
                string desc = descMatch.Success ? descMatch.Groups[1].Value.Trim() : "Virtual Controller";
                string status = statusMatch.Success ? statusMatch.Groups[1].Value.Trim() : "Unknown";

                bool isPhantom = !status.Equals("Started", StringComparison.OrdinalIgnoreCase) &&
                                 !status.Equals("OK", StringComparison.OrdinalIgnoreCase);

                if (!string.IsNullOrEmpty(instanceId))
                {
                    list.Add(new DeviceNode(instanceId, desc, status, isPhantom));
                }
            }
        }

        return list;
    }

    private static List<FirewallRule> ScanFirewallRules()
    {
        var list = new List<FirewallRule>();
        try
        {
            var psi = new ProcessStartInfo("netsh.exe", "advfirewall firewall show rule name=all")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return list;
            var outText = p.StandardOutput.ReadToEnd();
            p.WaitForExit(3000);

            var blocks = outText.Split(new[] { "Rule Name:" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var b in blocks)
            {
                if (b.Contains("OmniPad", StringComparison.OrdinalIgnoreCase))
                {
                    var nameMatch = Regex.Match(b, @"^\s*([^\r\n]+)");
                    var dirMatch = Regex.Match(b, @"Direction:\s*([^\r\n]+)");
                    var actMatch = Regex.Match(b, @"Action:\s*([^\r\n]+)");

                    string name = nameMatch.Success ? nameMatch.Groups[1].Value.Trim() : "OmniPad Rule";
                    string dir = dirMatch.Success ? dirMatch.Groups[1].Value.Trim() : "In";
                    string act = actMatch.Success ? actMatch.Groups[1].Value.Trim() : "Allow";

                    list.Add(new FirewallRule(name, name, dir, act));
                }
            }
        }
        catch { }

        return list;
    }
}
