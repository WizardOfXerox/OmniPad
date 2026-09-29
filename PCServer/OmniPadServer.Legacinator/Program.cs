#if !WINDOWS
using System;

namespace OmniPadServer.Legacinator;

public class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("OmniPad Legacinator is a Windows-only driver cleanup utility.");
    }
}
#else
using System;
using System.IO;
using System.Linq;

namespace OmniPadServer.Legacinator;

public class Program
{
    public static void Main(string[] args)
    {
        Console.Title = "OmniPad Legacinator - System & Driver Cleaner";
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // Auto-elevate if not admin
        if (!DiagnosticScanner.IsAdministrator())
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[!] Administrator privileges required to manage Windows Driver Store and Firewall.");
            Console.WriteLine("[*] Requesting UAC elevation...");
            Console.ResetColor();

            if (DiagnosticScanner.RelaunchAsAdmin(args))
            {
                return;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[-] Failed to elevate privileges. Please right click and select 'Run as administrator'.");
                Console.ResetColor();
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
                return;
            }
        }

        string baseDir = AppContext.BaseDirectory;
        string? localSetupExe = Path.Combine(baseDir, "ViGEmBus_Setup.exe");
        if (!File.Exists(localSetupExe))
        {
            // Check portable parent
            string parentSetup = Path.GetFullPath(Path.Combine(baseDir, "..", "ViGEmBus_Setup.exe"));
            if (File.Exists(parentSetup)) localSetupExe = parentSetup;
            else localSetupExe = null;
        }

        // Handle CLI arguments
        if (args.Length > 0)
        {
            var report = DiagnosticScanner.RunDiagnosticScan();
            if (args.Any(a => a.Equals("--scan", StringComparison.OrdinalIgnoreCase)))
            {
                PrintDashboard(report);
                return;
            }
            if (args.Any(a => a.Equals("--clean-gamepads", StringComparison.OrdinalIgnoreCase)))
            {
                int removed = DriverCleaner.CleanPhantomDevices(report);
                Console.WriteLine($"Removed {removed} phantom device(s).");
                return;
            }
            if (args.Any(a => a.Equals("--clean-firewall", StringComparison.OrdinalIgnoreCase)))
            {
                DriverCleaner.CleanFirewallRules();
                Console.WriteLine("Firewall rules cleaned.");
                return;
            }
            if (args.Any(a => a.Equals("--uninstall-driver", StringComparison.OrdinalIgnoreCase)))
            {
                DriverCleaner.UninstallDriverStandard(report, localSetupExe);
                return;
            }
            if (args.Any(a => a.Equals("--purge", StringComparison.OrdinalIgnoreCase)))
            {
                DriverCleaner.ForcePurgeDriverStore(report);
                return;
            }
            if (args.Any(a => a.Equals("--purge-all", StringComparison.OrdinalIgnoreCase)))
            {
                DriverCleaner.CleanAllVirtualGamepads(report);
                DriverCleaner.CleanFirewallRules();
                DriverCleaner.ForcePurgeDriverStore(report);
                Console.WriteLine("Full purge completed.");
                return;
            }
        }

        // Interactive Loop
        while (true)
        {
            Console.Clear();
            PrintBanner();

            var report = DiagnosticScanner.RunDiagnosticScan();
            PrintDashboard(report);

            PrintMenu();

            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("Select an option [1-6, Q]: ");
            Console.ResetColor();

            var key = Console.ReadKey(intercept: true);
            Console.WriteLine();

            if (key.Key == ConsoleKey.Q || key.Key == ConsoleKey.Escape)
            {
                Console.WriteLine("Exiting OmniPad Legacinator. Goodbye!");
                break;
            }

            switch (key.KeyChar)
            {
                case '1':
                    // Refresh
                    break;
                case '2':
                    Console.WriteLine();
                    int removed = DriverCleaner.CleanPhantomDevices(report);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"[Done] Successfully cleared {removed} phantom gamepad device node(s)!");
                    Console.ResetColor();
                    Pause();
                    break;
                case '3':
                    Console.WriteLine();
                    DriverCleaner.CleanFirewallRules();
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("[Done] OmniPad Windows Firewall rules successfully removed.");
                    Console.ResetColor();
                    Pause();
                    break;
                case '4':
                    Console.WriteLine();
                    DriverCleaner.UninstallDriverStandard(report, localSetupExe);
                    Pause();
                    break;
                case '5':
                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write("Are you sure you want to FORCE PURGE ViGEmBus from Windows Driver Store? [y/N]: ");
                    Console.ResetColor();
                    var confirm = Console.ReadLine();
                    if (confirm?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        DriverCleaner.ForcePurgeDriverStore(report);
                    }
                    else
                    {
                        Console.WriteLine("Action canceled.");
                    }
                    Pause();
                    break;
                case '6':
                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("WARNING: This will remove all virtual gamepads, firewall rules, and force purge ViGEmBus!");
                    Console.Write("Type 'RESET' to confirm full system clean: ");
                    Console.ResetColor();
                    var confirmReset = Console.ReadLine();
                    if (confirmReset?.Trim().Equals("RESET", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        DriverCleaner.CleanAllVirtualGamepads(report);
                        DriverCleaner.CleanFirewallRules();
                        DriverCleaner.ForcePurgeDriverStore(report);
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("\n[Done] Complete system cleanup finished! 0% residue remaining.");
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.WriteLine("Action canceled.");
                    }
                    Pause();
                    break;
                default:
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine("Invalid option.");
                    Console.ResetColor();
                    System.Threading.Thread.Sleep(500);
                    break;
            }
        }
    }

    private static void PrintBanner()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(@"
   ___                  _ _____           _ 
  / _ \ _ __ ___  _ __ (_|  __ \ __ _  __| |
 | | | | '_ ` _ \| '_ \| | |__) / _` |/ _` |
 | |_| | | | | | | | | | |  ___/ (_| | (_| |
  \___/|_| |_| |_|_| |_|_|_|    \__,_|\__,_|
          L E G A C I N A T O R   v1.0
  Universal Driver Cleaner & System Reset Utility
========================================================================");
        Console.ResetColor();
    }

    private static void PrintDashboard(ScanReport report)
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("--- SYSTEM HEALTH & DIAGNOSTIC DASHBOARD ---");
        Console.ResetColor();

        // 1. ViGEm Service
        Console.Write("  ViGEmBus Driver Service : ");
        if (report.ServiceStatus.Equals("Running", StringComparison.OrdinalIgnoreCase))
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"RUNNING (Active Kernel Driver v{report.InstalledVersion ?? "1.22.0"})");
        }
        else if (report.ServiceStatus.Equals("Stopped", StringComparison.OrdinalIgnoreCase))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("STOPPED (Installed but inactive)");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("NOT INSTALLED (Driverless fallback active in OmniPad)");
        }
        Console.ResetColor();

        // 2. Driver Store Packages
        Console.Write("  Windows Driver Store    : ");
        if (report.DriverStorePackages.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            var names = string.Join(", ", report.DriverStorePackages.Select(p => $"{p.PublishedName} ({p.OriginalName})"));
            Console.WriteLine($"{report.DriverStorePackages.Count} package(s) found -> {names}");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("No ViGEm packages in Driver Store");
        }
        Console.ResetColor();

        // 3. Virtual Gamepads
        Console.Write("  Virtual Gamepad Nodes   : ");
        int active = report.GamepadDevices.Count(d => !d.IsPhantom && !d.InstanceId.StartsWith(@"ROOT\SYSTEM", StringComparison.OrdinalIgnoreCase));
        int phantom = report.GamepadDevices.Count(d => d.IsPhantom && !d.InstanceId.StartsWith(@"ROOT\SYSTEM", StringComparison.OrdinalIgnoreCase));
        if (active > 0 || phantom > 0)
        {
            Console.ForegroundColor = phantom > 0 ? ConsoleColor.Yellow : ConsoleColor.Green;
            Console.WriteLine($"{active} Active, {phantom} Disconnected / Phantom Node(s)");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("None (Device Manager clean)");
        }
        Console.ResetColor();

        // 4. Firewall Rules
        Console.Write("  Windows Firewall Rules  : ");
        if (report.FirewallRules.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"{report.FirewallRules.Count} OmniPad rule(s) configured");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("None");
        }
        Console.ResetColor();

        Console.WriteLine("========================================================================");
    }

    private static void PrintMenu()
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("AVAILABLE ACTIONS:");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("  [1] Refresh Diagnostics");
        Console.WriteLine("  [2] Clean Phantom / Zombie Gamepads (Removes disconnected controller nodes)");
        Console.WriteLine("  [3] Clean OmniPad Windows Firewall Rules");
        Console.WriteLine("  [4] Standard Uninstall of ViGEmBus Driver (Runs official installer/MSI)");
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("  [5] Force Purge ViGEmBus from Windows Driver Store (Nuclear Cleaner)");
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("  [6] Full System Reset (Purge Gamepads + Firewall + Driver - 0% Residue)");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  [Q] Exit Legacinator");
        Console.ResetColor();
        Console.WriteLine("========================================================================");
    }

    private static void Pause()
    {
        Console.WriteLine("\nPress any key to return to dashboard...");
        Console.ReadKey();
    }
}
#endif
