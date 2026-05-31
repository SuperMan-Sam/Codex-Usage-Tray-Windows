using Microsoft.Win32;

namespace CodexUsageTray.Services;

internal static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CodexUsageTray";
    private const string LauncherScriptPath = @"F:\Tools\StartCodexUsageTray.ps1";
    private const string LauncherCommandPath = @"F:\Tools\StartCodexUsageTray.cmd";

    public static bool IsEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    public static void SetEnabled(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (enabled)
        {
            key.SetValue(ValueName, StartupCommand, RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    private static string StartupCommand
    {
        get
        {
            if (File.Exists(LauncherScriptPath))
            {
                return $"powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{LauncherScriptPath}\"";
            }

            if (File.Exists(LauncherCommandPath))
            {
                return $"\"{LauncherCommandPath}\"";
            }

            string executablePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "CodexUsageTray.exe");
            return $"\"{executablePath}\"";
        }
    }
}
