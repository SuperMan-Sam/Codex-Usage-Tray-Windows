# Codex Usage Tray for Windows

Codex Usage Tray is a compact Windows utility that shows ChatGPT Codex usage in the Windows taskbar. It reads the official Codex usage page in an embedded WebView2 session and displays the 5-hour and weekly remaining limits directly inside the taskbar.

## Features

- Shows Codex 5-hour and weekly remaining usage in a taskbar-embedded widget.
- Displays reset time as remaining hours, for example `2h` or `165h`.
- Updates usage every minute.
- Uses a tray icon as a fallback status indicator:
  - green: normal
  - yellow: 20% or less remaining
  - red: 10% or less remaining
  - gray: unknown, login required, or unsupported
- Provides a WebView2 login/session window for the official usage page.
- Includes context menus for refresh, opening the usage page, settings, and exit.
- Supports optional start-with-Windows registration from the settings dialog.
- Includes a one-click launcher: `StartCodexUsageTray.cmd`.

## Privacy and Data Source

The source of truth is the official Codex usage page:

```text
https://chatgpt.com/codex/settings/usage
```

The app does not read local Codex credential files, does not read `auth.json`, and does not call private ChatGPT network APIs. WebView2 cookies and session data are stored under the current user's local app data folder so you can sign in through the app window.

If ChatGPT blocks WebView2 login or changes the usage page structure, the app reports an unknown or unsupported state instead of extracting local tokens.

## Requirements

- Windows 10 1809 or later, with Windows App SDK support.
- .NET 10 SDK.
- WebView2 Runtime.
- WinUI project tooling for build/development.

The application is a packaged WinUI 3 app targeting:

```text
net10.0-windows10.0.26100.0
```

## Build

From the repository root:

```powershell
$platform = if ($env:PROCESSOR_ARCHITECTURE -eq "AMD64") { "x64" } else { $env:PROCESSOR_ARCHITECTURE }
dotnet build .\CodexUsageTray\CodexUsageTray.csproj -c Debug "-p:Platform=$platform"
```

## Run

Use the launcher:

```powershell
.\StartCodexUsageTray.cmd
```

Or run the WinUI project directly after building:

```powershell
$platform = if ($env:PROCESSOR_ARCHITECTURE -eq "AMD64") { "x64" } else { $env:PROCESSOR_ARCHITECTURE }
dotnet run --no-build -c Debug "-p:Platform=$platform" --project .\CodexUsageTray\CodexUsageTray.csproj
```

On first run, sign in through the app window if needed, then use Refresh to capture usage.

## Test

```powershell
$platform = if ($env:PROCESSOR_ARCHITECTURE -eq "AMD64") { "x64" } else { $env:PROCESSOR_ARCHITECTURE }
dotnet test .\CodexUsageTray.Tests\CodexUsageTray.Tests.csproj -c Debug "-p:Platform=$platform"
```

The test suite covers parser behavior, reset-hour formatting, and tray icon state thresholds.

## Project Layout

```text
CodexUsageTray/          WinUI 3 app, WebView2 host, tray icon, taskbar widget
CodexUsageTray.Core/     Usage parser and shared models/services
CodexUsageTray.Tests/    MSTest coverage for parsing and state logic
StartCodexUsageTray.*    One-click Windows launcher scripts
```

## Notes

- The taskbar usage display is implemented as a native Win32 child window under `Shell_TrayWnd`, not as a normal top-level floating window.
- The tray icon uses native `Shell_NotifyIconW` interop.
- The taskbar widget is tuned for the primary Windows taskbar and may need adjustment for unusual taskbar replacements or heavily customized Explorer shells.
