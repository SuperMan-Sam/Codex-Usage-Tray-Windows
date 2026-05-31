# Codex Usage Tray for Windows

Codex Usage Tray is a compact Windows utility that shows ChatGPT Codex usage in a tray app and an official Windows 11 Widget. It reads the official Codex usage page in an embedded WebView2 session, caches the latest safe snapshot locally, and renders the 5-hour and weekly remaining limits in the Widgets board.

## Features

- Shows Codex 5-hour and weekly remaining usage in an official Windows 11 Widget.
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
- Includes a widget registration helper: `RegisterCodexUsageWidget.ps1`.

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

## Register the Windows 11 Widget

Windows Widgets for Win32 apps require a packaged app registration. Build and register the debug package with:

```powershell
.\RegisterCodexUsageWidget.ps1
```

Then open Widgets with `Win+W`, choose Add widgets, and pin `Codex Usage`.

The widget displays the latest cached snapshot from the tray app. Run `StartCodexUsageTray.cmd`, sign in through WebView2 if needed, and refresh once before expecting live data in the widget.

## Test

```powershell
$platform = if ($env:PROCESSOR_ARCHITECTURE -eq "AMD64") { "x64" } else { $env:PROCESSOR_ARCHITECTURE }
dotnet test .\CodexUsageTray.Tests\CodexUsageTray.Tests.csproj -c Debug "-p:Platform=$platform"
```

The test suite covers parser behavior, reset-hour formatting, and tray icon state thresholds.

## Project Layout

```text
CodexUsageTray/          WinUI 3 app, WebView2 host, tray icon, Windows Widget provider
CodexUsageTray.Core/     Usage parser and shared models/services
CodexUsageTray.Tests/    MSTest coverage for parsing and state logic
StartCodexUsageTray.*    One-click Windows launcher scripts
RegisterCodexUsageWidget.ps1
```

## Notes

- The Windows 11 Widget appears in the Widgets board, not as a first-party weather-style live taskbar surface. Windows 11 does not expose a public third-party API for embedding arbitrary app UI directly inside the taskbar.
- The tray icon uses native `Shell_NotifyIconW` interop.
- The widget provider reads only the app's cached usage snapshot. It does not read cookies, local Codex credentials, `auth.json`, or private ChatGPT APIs.
