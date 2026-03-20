using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace ScrollBoost.Profiles;

public static class AutoStartManager
{
    private const string RegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RegistryValue = "ScrollBoost";
    private const string TaskName = "ScrollBoost";

    // startupMode: "none", "registry", "scheduler"
    public static void Apply(string startupMode)
    {
        // Always clean up both methods first, then apply the selected one
        RemoveRegistry();
        RemoveScheduledTask();

        switch (startupMode?.ToLowerInvariant())
        {
            case "registry":
                SetRegistry();
                break;
            case "scheduler":
                CreateScheduledTask();
                break;
            // "none" or anything else: just remove both (already done)
        }
    }

    public static string Detect()
    {
        // Check what's currently configured
        if (HasScheduledTask()) return "scheduler";
        if (HasRegistry()) return "registry";
        return "none";
    }

    private static void SetRegistry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, writable: true);
            if (key == null) return;
            string exePath = Environment.ProcessPath ?? "";
            key.SetValue(RegistryValue, $"\"{exePath}\"");
        }
        catch { }
    }

    private static void RemoveRegistry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, writable: true);
            key?.DeleteValue(RegistryValue, throwOnMissingValue: false);
        }
        catch { }
    }

    private static bool HasRegistry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey);
            return key?.GetValue(RegistryValue) != null;
        }
        catch { return false; }
    }

    private static void CreateScheduledTask()
    {
        try
        {
            string exePath = Environment.ProcessPath ?? "";
            if (string.IsNullOrEmpty(exePath)) return;

            // Use schtasks.exe to create a task that runs at logon with highest privileges
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/Create /TN \"{TaskName}\" /TR \"\\\"{exePath}\\\"\" /SC ONLOGON /RL HIGHEST /F",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            process?.WaitForExit(5000);
        }
        catch { }
    }

    private static void RemoveScheduledTask()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/Delete /TN \"{TaskName}\" /F",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            process?.WaitForExit(5000);
        }
        catch { }
    }

    private static bool HasScheduledTask()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/Query /TN \"{TaskName}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            process?.WaitForExit(3000);
            return process?.ExitCode == 0;
        }
        catch { return false; }
    }
}
