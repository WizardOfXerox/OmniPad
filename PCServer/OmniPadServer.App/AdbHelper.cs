using System;
using System.Diagnostics;
using System.IO;

namespace OmniPadServer.App;

public static class AdbHelper
{
    private static string? _adbPath;

    public static string? FindAdb()
    {
        if (_adbPath != null) return _adbPath;

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

        // 2. Check Android SDK standard paths
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string sdkAdb = Path.Combine(localAppData, "Android", "Sdk", "platform-tools", "adb.exe");
        if (File.Exists(sdkAdb))
        {
            _adbPath = sdkAdb;
            return _adbPath;
        }

        return null;
    }

    public static bool TrySetupUsbReverse(int inputPort = 27500, int webPort = 27502)
    {
        string? adb = FindAdb();
        if (adb == null) return false;

        try
        {
            // Check connected devices
            var checkPsi = new ProcessStartInfo(adb, "devices")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var checkProcess = Process.Start(checkPsi);
            string output = checkProcess?.StandardOutput.ReadToEnd() ?? "";
            checkProcess?.WaitForExit(2000);

            // If output contains a device
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            bool hasDevice = false;
            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].Contains("device") && !lines[i].Contains("offline"))
                {
                    hasDevice = true;
                    break;
                }
            }

            if (!hasDevice) return false;

            // Run adb reverse for input port and web port
            RunCommand(adb, $"reverse tcp:{inputPort} tcp:{inputPort}");
            RunCommand(adb, $"reverse tcp:{webPort} tcp:{webPort}");

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"[USB ADB] Successfully reversed ports {inputPort} & {webPort} for connected phone!");
            Console.ResetColor();
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[USB ADB] Could not configure reverse tunnel: {ex.Message}");
            return false;
        }
    }

    private static void RunCommand(string adb, string args)
    {
        var psi = new ProcessStartInfo(adb, args)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi);
        p?.WaitForExit(2000);
    }
}
