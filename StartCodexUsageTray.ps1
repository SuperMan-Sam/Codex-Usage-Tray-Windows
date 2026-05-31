$ErrorActionPreference = "Stop"

$projectDir = Join-Path $PSScriptRoot "CodexUsageTray"
$platform = if ($env:PROCESSOR_ARCHITECTURE -eq "AMD64") { "x64" } else { $env:PROCESSOR_ARCHITECTURE }
$targetFramework = "net10.0-windows10.0.26100.0"
$builtExe = Join-Path $projectDir "bin\$platform\Debug\$targetFramework\win-$($platform.ToLowerInvariant())\AppX\CodexUsageTray.exe"

Add-Type -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class WindowTools
{
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    public static IntPtr[] FindWindowsForProcess(int targetProcessId)
    {
        List<IntPtr> handles = new List<IntPtr>();
        EnumWindows((hWnd, lParam) =>
        {
            uint processId;
            GetWindowThreadProcessId(hWnd, out processId);
            if (processId == targetProcessId)
            {
                handles.Add(hWnd);
            }

            return true;
        }, IntPtr.Zero);

        return handles.ToArray();
    }

    public static string GetWindowTitle(IntPtr hWnd)
    {
        StringBuilder text = new StringBuilder(256);
        GetWindowText(hWnd, text, text.Capacity);
        return text.ToString();
    }

    public static void RestoreWindow(IntPtr hWnd)
    {
        const int SW_RESTORE = 9;
        ShowWindow(hWnd, SW_RESTORE);
        SetForegroundWindow(hWnd);
    }
}
"@

$runningProcesses = @(Get-Process -Name "CodexUsageTray" -ErrorAction SilentlyContinue | Sort-Object StartTime -Descending)
foreach ($process in $runningProcesses) {
    $windows = [WindowTools]::FindWindowsForProcess($process.Id)
    if ($windows.Count -gt 0) {
        $mainWindow = $windows | Where-Object { [WindowTools]::GetWindowTitle($_) -eq "Codex Usage Tray" } | Select-Object -First 1
        if ($null -eq $mainWindow) {
            $mainWindow = $windows[0]
        }

        [WindowTools]::RestoreWindow($mainWindow)
        exit 0
    }
}

if (-not (Test-Path -LiteralPath $builtExe)) {
    dotnet build -c Debug "-p:Platform=$platform" $projectDir | Out-Null
}

Start-Process -FilePath "dotnet" `
    -ArgumentList @("run", "--no-build", "-c", "Debug", "-p:Platform=$platform") `
    -WorkingDirectory $projectDir `
    -WindowStyle Hidden
