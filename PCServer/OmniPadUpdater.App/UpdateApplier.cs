using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace OmniPadUpdater;

public static class UpdateApplier
{
    public static bool CloseServerProcess(int? explicitPid = null, int timeoutMs = 5000)
    {
        try
        {
            if (explicitPid.HasValue && explicitPid.Value > 0)
            {
                try
                {
                    var proc = Process.GetProcessById(explicitPid.Value);
                    Console.WriteLine($"[Updater] Requesting shutdown of OmniPadServer (PID: {proc.Id})...");
                    proc.CloseMainWindow();
                    if (!proc.WaitForExit(timeoutMs))
                    {
                        proc.Kill(entireProcessTree: true);
                        proc.WaitForExit(2000);
                    }
                    return true;
                }
                catch { }
            }

            // Fallback: search for OmniPadServer processes
            var procs = Process.GetProcessesByName("OmniPadServer.App")
                .Concat(Process.GetProcessesByName("OmniPadServer"));
            foreach (var p in procs)
            {
                try
                {
                    Console.WriteLine($"[Updater] Stopping process {p.ProcessName} (PID: {p.Id})...");
                    p.Kill(entireProcessTree: true);
                    p.WaitForExit(timeoutMs);
                }
                catch { }
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Updater] Warning during process shutdown: {ex.Message}");
            return false;
        }
    }

    public static bool CreateBackup(string appDirectory, string backupDirectory)
    {
        try
        {
            if (Directory.Exists(backupDirectory))
            {
                Directory.Delete(backupDirectory, recursive: true);
            }
            Directory.CreateDirectory(backupDirectory);

            Console.WriteLine($"[Updater] Creating backup in {Path.GetFileName(backupDirectory)}...");

            // Backup critical executables and libraries
            foreach (var file in Directory.EnumerateFiles(appDirectory, "*.*", SearchOption.TopDirectoryOnly))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext is ".exe" or ".dll" or ".json" or ".bat")
                {
                    string dest = Path.Combine(backupDirectory, Path.GetFileName(file));
                    File.Copy(file, dest, overwrite: true);
                }
            }

            // Backup wwwroot
            string wwwrootDir = Path.Combine(appDirectory, "wwwroot");
            if (Directory.Exists(wwwrootDir))
            {
                string destWwwroot = Path.Combine(backupDirectory, "wwwroot");
                CopyDirectory(wwwrootDir, destWwwroot);
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[Updater] Backup creation failed: {ex.Message}");
            Console.ResetColor();
            return false;
        }
    }

    public static bool ApplyUpdateFromZip(string zipPath, string appDirectory, string backupDirectory)
    {
        if (!File.Exists(zipPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[Updater] Update archive not found: {zipPath}");
            Console.ResetColor();
            return false;
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"[Updater] Extracting package {Path.GetFileName(zipPath)}...");
        Console.ResetColor();

        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue; // Directory entry

                string destPath = Path.Combine(appDirectory, entry.FullName);
                string? destDir = Path.GetDirectoryName(destPath);
                if (destDir != null && !Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                // Extract with overwrite
                entry.ExtractToFile(destPath, overwrite: true);
                Console.WriteLine($"  -> Updated: {entry.FullName}");
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[Updater] File extraction completed successfully!");
            Console.ResetColor();
            return true;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[Updater] Error applying update: {ex.Message}. Initiating automatic rollback...");
            Console.ResetColor();

            Rollback(appDirectory, backupDirectory);
            return false;
        }
    }

    public static bool Rollback(string appDirectory, string backupDirectory)
    {
        if (!Directory.Exists(backupDirectory))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[Updater] No backup directory found to restore from.");
            Console.ResetColor();
            return false;
        }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("[Updater] Restoring files from backup...");
        Console.ResetColor();

        try
        {
            CopyDirectory(backupDirectory, appDirectory);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[Updater] Rollback successful. Original files restored.");
            Console.ResetColor();
            return true;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[Updater] Fatal error during rollback: {ex.Message}");
            Console.ResetColor();
            return false;
        }
    }

    public static bool RelaunchServer(string appDirectory)
    {
        string serverExe = Path.Combine(appDirectory, "OmniPadServer.App.exe");
        if (!File.Exists(serverExe))
        {
            serverExe = Path.Combine(appDirectory, "OmniPadServer.exe");
        }

        if (!File.Exists(serverExe))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[Updater] OmniPadServer executable not found to relaunch.");
            Console.ResetColor();
            return false;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[Updater] Relaunching OmniPadServer: {Path.GetFileName(serverExe)}...");
        Console.ResetColor();

        try
        {
            var psi = new ProcessStartInfo(serverExe)
            {
                WorkingDirectory = appDirectory,
                UseShellExecute = true
            };
            Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Updater] Could not relaunch server: {ex.Message}");
            return false;
        }
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            string dest = Path.Combine(targetDir, Path.GetFileName(file));
            File.Copy(file, dest, overwrite: true);
        }

        foreach (var dir in Directory.EnumerateDirectories(sourceDir))
        {
            string dest = Path.Combine(targetDir, Path.GetFileName(dir));
            CopyDirectory(dir, dest);
        }
    }
}
