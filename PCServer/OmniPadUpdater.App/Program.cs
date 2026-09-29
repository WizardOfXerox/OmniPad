using System;
using System.IO;
using System.Threading.Tasks;
using OmniPadServer.Core;
using OmniPadServer.ViGEm;

namespace OmniPadUpdater;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        string appDir = AppContext.BaseDirectory;
        string backupDir = Path.Combine(appDir, ".update_backup");

        // Parse CLI flags
        bool isCheck = false;
        bool isApply = false;
        bool isRollback = false;
        bool isInstallDrivers = false;
        bool isUninstallDrivers = false;
        bool isStatus = false;
        bool isSilent = false;
        int? pid = null;
        string? zipPath = null;

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i].ToLowerInvariant();
            if (a == "--check") isCheck = true;
            else if (a == "--apply") isApply = true;
            else if (a == "--rollback") isRollback = true;
            else if (a == "--install-drivers") isInstallDrivers = true;
            else if (a == "--uninstall-drivers") isUninstallDrivers = true;
            else if (a == "--status") isStatus = true;
            else if (a == "--silent") isSilent = true;
            else if (a.StartsWith("--pid=") && int.TryParse(a[6..], out int p1)) pid = p1;
            else if (a == "--pid" && i + 1 < args.Length && int.TryParse(args[++i], out int p2)) pid = p2;
            else if (a.StartsWith("--zip=")) zipPath = args[i][6..];
            else if (a == "--zip" && i + 1 < args.Length) zipPath = args[++i];
        }

        if (isCheck)
        {
            var info = await UpdateChecker.CheckForUpdatesAsync(appDir);
            if (!isSilent)
            {
                Console.WriteLine($"Current Version: {info.CurrentVersion}");
                Console.WriteLine($"Latest Version:  {info.LatestVersion}");
                Console.WriteLine($"Update Available: {info.IsUpdateAvailable}");
                if (info.IsUpdateAvailable)
                {
                    Console.WriteLine($"Title: {info.ReleaseTitle}");
                    Console.WriteLine($"Notes: {info.ReleaseNotes}");
                }
            }
            return info.IsUpdateAvailable ? 1 : 0;
        }

        if (isStatus)
        {
            PrintBanner();
            string curVer = UpdateChecker.GetInstalledVersion(appDir);
            Console.WriteLine($"Installed App Version: {curVer}");
            Console.WriteLine();
            var status = HIDOmniPadBusManager.DetectStatus();
            Console.WriteLine($"Primary Engine:       {status.PrimaryEngine}");
            Console.WriteLine($"ViGEmBus Installed:   {status.ViGEmInstalled} (Running: {status.ViGEmRunning})");
            Console.WriteLine($"HIDMaestro Installed: {status.HIDMaestroInstalled}");
            Console.WriteLine($"Hardware Profiles:    {status.TotalProfilesAvailable}");
            Console.WriteLine($"Summary:              {status.StatusSummary}");
            return 0;
        }

        if (isInstallDrivers)
        {
            PrintBanner();
            Console.WriteLine("[HIDOmniPadBus] Installing driver suite...");
            bool ok = HIDOmniPadBusManager.InstallAll();
            return ok ? 0 : 1;
        }

        if (isUninstallDrivers)
        {
            PrintBanner();
            Console.WriteLine("[HIDOmniPadBus] Purging driver suite...");
            bool ok = HIDOmniPadBusManager.UninstallAll();
            return ok ? 0 : 1;
        }

        if (isRollback)
        {
            PrintBanner();
            UpdateApplier.CloseServerProcess(pid);
            bool ok = UpdateApplier.Rollback(appDir, backupDir);
            if (ok) UpdateApplier.RelaunchServer(appDir);
            return ok ? 0 : 1;
        }

        if (isApply)
        {
            if (!isSilent) PrintBanner();
            string targetZip = zipPath ?? Path.Combine(appDir, "OmniPad-Update.zip");

            if (!File.Exists(targetZip))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[Updater] Update archive not found: {targetZip}");
                Console.ResetColor();
                return 1;
            }

            UpdateApplier.CloseServerProcess(pid);
            UpdateApplier.CreateBackup(appDir, backupDir);
            bool ok = UpdateApplier.ApplyUpdateFromZip(targetZip, appDir, backupDir);
            if (ok)
            {
                UpdateApplier.RelaunchServer(appDir);
                return 0;
            }
            return 1;
        }

        // Interactive Console Menu
        return await RunInteractiveMenuAsync(appDir, backupDir);
    }

    private static async Task<int> RunInteractiveMenuAsync(string appDir, string backupDir)
    {
        while (true)
        {
            Console.Clear();
            PrintBanner();

            string currentVersion = UpdateChecker.GetInstalledVersion(appDir);
            var status = HIDOmniPadBusManager.DetectStatus();

            Console.WriteLine($" Current Version: {currentVersion}");
            Console.WriteLine($" HIDOmniPadBus:   {status.PrimaryEngine}");
            Console.WriteLine(new string('-', 55));
            Console.WriteLine(" 1. Check for Software Updates");
            Console.WriteLine(" 2. Apply Staged Update Package (OmniPad-Update.zip)");
            Console.WriteLine(" 3. Rollback to Previous Version (.update_backup)");
            Console.WriteLine(" 4. Install / Repair HIDOmniPadBus Drivers");
            Console.WriteLine(" 5. Clean Uninstall HIDOmniPadBus Drivers");
            Console.WriteLine(" 6. Exit");
            Console.WriteLine(new string('-', 55));
            Console.Write(" Select an option (1-6): ");

            var key = Console.ReadKey();
            Console.WriteLine();
            Console.WriteLine();

            switch (key.KeyChar)
            {
                case '1':
                    Console.WriteLine($"Checking for updates from https://github.com/{UpdateChecker.GitHubRepoOwner}/{UpdateChecker.GitHubRepoName}...");
                    var info = await UpdateChecker.CheckForUpdatesAsync(appDir);
                    Console.WriteLine($"Current Version: {info.CurrentVersion}");
                    Console.WriteLine($"Latest Version:  {info.LatestVersion}");
                    if (info.IsUpdateAvailable)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"\n[UPDATE AVAILABLE] {info.ReleaseTitle}");
                        Console.ResetColor();
                        if (!string.IsNullOrWhiteSpace(info.ReleaseNotes))
                        {
                            Console.WriteLine(info.ReleaseNotes);
                        }

                        if (!string.IsNullOrEmpty(info.DownloadUrl))
                        {
                            Console.WriteLine($"\nDownload URL: {info.DownloadUrl}");
                            Console.Write("\nWould you like to download and install this update now? (y/N): ");
                            var ans = Console.ReadKey();
                            Console.WriteLine();
                            if (ans.KeyChar == 'y' || ans.KeyChar == 'Y')
                            {
                                string targetZip = Path.Combine(appDir, "OmniPad-Update.zip");
                                Console.Write("Downloading update package... ");
                                bool dlOk = await UpdateChecker.DownloadUpdateAsync(info.DownloadUrl, targetZip, pct =>
                                {
                                    Console.Write($"\rDownloading update package... {pct}%");
                                });
                                Console.WriteLine();

                                if (dlOk && File.Exists(targetZip))
                                {
                                    Console.ForegroundColor = ConsoleColor.Green;
                                    Console.WriteLine("Download complete. Applying update...");
                                    Console.ResetColor();

                                    UpdateApplier.CloseServerProcess();
                                    UpdateApplier.CreateBackup(appDir, backupDir);
                                    bool applied = UpdateApplier.ApplyUpdateFromZip(targetZip, appDir, backupDir);
                                    if (applied)
                                    {
                                        UpdateApplier.RelaunchServer(appDir);
                                        Console.WriteLine("Update complete!");
                                    }
                                }
                                else
                                {
                                    Console.ForegroundColor = ConsoleColor.Red;
                                    Console.WriteLine("Download failed or connection was interrupted.");
                                    Console.ResetColor();
                                }
                            }
                        }
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.WriteLine("OmniPad is already up to date.");
                        Console.ResetColor();
                    }
                    WaitForKey();
                    break;

                case '2':
                    string targetZip = Path.Combine(appDir, "OmniPad-Update.zip");
                    if (!File.Exists(targetZip))
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"No update file found at '{targetZip}'.");
                        Console.WriteLine("Place an 'OmniPad-Update.zip' in this directory or run with --zip <path>.");
                        Console.ResetColor();
                    }
                    else
                    {
                        UpdateApplier.CloseServerProcess();
                        UpdateApplier.CreateBackup(appDir, backupDir);
                        bool ok = UpdateApplier.ApplyUpdateFromZip(targetZip, appDir, backupDir);
                        if (ok) UpdateApplier.RelaunchServer(appDir);
                    }
                    WaitForKey();
                    break;

                case '3':
                    UpdateApplier.CloseServerProcess();
                    bool rbOk = UpdateApplier.Rollback(appDir, backupDir);
                    if (rbOk) UpdateApplier.RelaunchServer(appDir);
                    WaitForKey();
                    break;

                case '4':
                    HIDOmniPadBusManager.InstallAll();
                    WaitForKey();
                    break;

                case '5':
                    HIDOmniPadBusManager.UninstallAll();
                    WaitForKey();
                    break;

                case '6':
                    return 0;
            }
        }
    }

    private static void WaitForKey()
    {
        Console.WriteLine();
        Console.Write("Press any key to return to menu...");
        Console.ReadKey();
    }

    private static void PrintBanner()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(@"
   ___                  _ _____           _ _   _           _       _            
  / _ \ _ __ ___  _ __ (_|  __ \ __ _  __| | | | |_ __   __| | __ _| |_ ___ _ __ 
 | | | | '_ ` _ \| '_ \| | |__) / _` |/ _` | | | | '_ \ / _` |/ _` | __/ _ \ '__|
 | |_| | | | | | | | | | |  ___/ (_| | (_| | |_| | |_) | (_| | (_| | ||  __/ |   
  \___/|_| |_| |_|_| |_|_|_|    \__,_|\__,_|\___/| .__/ \__,_|\__,_|\__\___|_|   
                                                 |_|                             
   Universal Controller Engine & Safe Software Updater
");
        Console.ResetColor();
    }
}
