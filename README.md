# Codex Usage Tray for Windows

Codex Usage Tray is a compact Windows utility that shows ChatGPT Codex usage in a small taskbar display, tray app, and official Windows 11 Widget. It reads the official Codex usage page in an embedded WebView2 session, caches the latest safe snapshot locally, renders the 5-hour and weekly remaining limits, and includes a main-window token meter for local AI coding assistant logs.

## 中文快速开始
0.如果你有codex就不用看下面的步骤了，直接告诉他“帮我部署https://github.com/SuperMan-Sam/Codex-Usage-Tray-Windows即可”
1. 运行 `.\RegisterCodexUsageWidget.ps1` 注册 Windows 11 Widget。
2. 运行 `.\StartCodexUsageTray.cmd` 打开主程序。
3. 在主程序的 WebView2 页面登录 ChatGPT，然后点击 Refresh 获取用量。
4. 按 `Win+W` 打开 Windows Widgets，点击添加小组件，选择 `Codex Usage`。

Widget 会显示最近一次缓存的 Codex `5h limit` 和 `Weekly limit`。如果 Widget 显示未知状态，先回到主程序刷新一次。
可以在 Settings 中把语言设置为跟随系统语言、简体中文或英文。
可以在 Settings 中调整任务栏字体大小，范围为 7 到 28，改动会立即应用到任务栏小显示块。
任务栏内的小显示块只显示百分数，上方是 `5h84%`，下方是 `周97%`；它会自动放在任务栏按钮区域和系统托盘之间，如果空间不足会自动隐藏，避免遮挡其他程序块。
主窗口默认显示官方 Codex 用量页；`Token Meter` 页会扫描本机支持的 AI 编程助手会话日志，显示总 tokens、估算成本、客户端汇总和模型明细。Token 数量会使用 `万`、`亿` 等中文单位缩短显示。成本使用公开 LiteLLM/OpenRouter 价格数据估算，未知模型不会按 0 计算。

## Release 安装

从 GitHub Releases 下载 `CodexUsageTray-*-win-x64.zip`，解压后运行 `Install-CodexUsageTray.cmd`。安装脚本会注册随包附带的 packaged app 布局并启动程序，不需要本机安装 .NET SDK 或导入私有凭据。

发布包只包含应用、安装脚本、许可证和第三方说明；不会包含本机 WebView2 登录缓存、价格缓存、截图、tokens 日志或任何 `%LOCALAPPDATA%\CodexUsageTray` 运行时数据。

## Images
<img width="247" height="81" alt="image" src="https://github.com/user-attachments/assets/a75717a4-5db3-4271-bac8-21feb3ad2fde" />
<img width="2087" height="1386" alt="image" src="https://github.com/user-attachments/assets/2f46b309-62e3-4bae-a04a-a93af8aa8d84" />
<img width="1995" height="1463" alt="image" src="https://github.com/user-attachments/assets/76d58802-0bd1-4336-aab7-13363a92f20d" />
<img width="2608" height="1618" alt="image" src="https://github.com/user-attachments/assets/93e6901d-6187-4be9-bbf1-524f4def79c5" />
<img width="2608" height="1618" alt="image" src="https://github.com/user-attachments/assets/15992523-999e-4c9c-84cc-83f43e147550" />


## Features

- Shows Codex 5-hour and weekly remaining usage as compact taskbar percentages without progress bars.
- Places the taskbar widget between the running-app button area and the notification area, and hides it if there is not enough free space.
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
- Supports language selection in Settings: follow system language, Simplified Chinese, or English.
- Supports taskbar font size adjustment from Settings, with a 7 to 28 range.
- Includes a Token Meter tab in the main window:
  - scans supported local AI coding assistant logs every minute
  - supports Today, 7 days, current month, and all history ranges
  - shows total tokens, input/output/cache/reasoning breakdown, estimated USD cost, client summaries, and model details
  - shows tokens with compact units such as `万` and `亿` in Chinese mode
  - tracks Codex tokens used inside the current 5-hour usage window when the official reset time is available
  - uses public LiteLLM/OpenRouter pricing data with local cache fallback
- Includes a one-click launcher: `StartCodexUsageTray.cmd`.
- Includes a widget registration helper: `RegisterCodexUsageWidget.ps1`.

## Language Support / 语言支持

The app currently supports:

| Mode | Locale | Scope |
| --- | --- | --- |
| Follow system language | Auto | Uses Simplified Chinese when the Windows UI culture is Chinese, otherwise English. |
| Simplified Chinese | `zh-CN` | Main window, settings, tray menu, taskbar display, and widget text. |
| English | `en-US` | Main window, settings, tray menu, taskbar display, and widget text. |

You can change the language in Settings. The embedded ChatGPT/Codex usage page is loaded from `chatgpt.com` and may use the language selected by ChatGPT or the WebView2 session, not necessarily the app UI language.

## Privacy and Data Source

The source of truth is the official Codex usage page:

```text
https://chatgpt.com/codex/settings/usage
```

The app does not read local Codex credential files, does not read `auth.json`, and does not call private ChatGPT network APIs. WebView2 cookies and session data are stored under the current user's local app data folder so you can sign in through the app window.

If ChatGPT blocks WebView2 login or changes the usage page structure, the app reports an unknown or unsupported state instead of extracting local auth tokens.

The Token Meter scans local session logs for supported coding assistants. It reads usage metadata such as model name, timestamp, and token counts. It does not upload these logs. Cost estimation fetches public model pricing from LiteLLM and OpenRouter, then caches pricing under `%LOCALAPPDATA%\CodexUsageTray\Pricing`.

Supported local token sources currently match the tokscale client set: OpenCode, Claude Code, Codex, Cursor, Gemini, Amp, Codebuff, Droid, OpenClaw, Pi, Kimi, Qwen, RooCode, KiloCode, Mux, Kilo, Crush, Hermes, Copilot, Goose, and Antigravity.

For Codex token counting, the app scans both:

```text
%USERPROFILE%\.codex\sessions
%USERPROFILE%\.codex\archived_sessions
```

It uses `last_token_usage` as the increment source and uses `total_token_usage` only for deduplication and monotonicity checks. This matches tokscale's Codex counting behavior and avoids over-counting resumed or compacted sessions.

## Local Files and Git Safety

Runtime state stays outside the repository or is ignored by Git:

```text
%LOCALAPPDATA%\CodexUsageTray\WebView2
%LOCALAPPDATA%\CodexUsageTray\Pricing
%LOCALAPPDATA%\CodexUsageTray\usage-snapshot.json
%LOCALAPPDATA%\CodexUsageTray\settings.json
%LOCALAPPDATA%\CodexUsageTray\token-meter-settings.json
```

Local build output, screenshots, logs, WebView2 profile data, pricing cache, and settings files are excluded by `.gitignore`. They should not be committed or uploaded to GitHub.

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

The app window opens on the official usage page by default. Use the `Token Meter` tab for local token analytics, then switch back to `Open usage page` to inspect the embedded ChatGPT Codex usage page.

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

The test suite covers parser behavior, reset-hour formatting, tray icon state thresholds, token scanner fixtures for every supported client, token aggregation, pricing cache fallback, unknown-price handling, and corrupt-file resilience.

## Troubleshooting

- If the taskbar display is too small, open Settings and increase `Taskbar font size`.
- If the taskbar display disappears, there may not be enough free space between running-app buttons and the notification area. The app hides the display instead of covering other taskbar items.
- If the usage page is blank or not visible, click `Open usage page`; the app switches to the usage tab and reloads `https://chatgpt.com/codex/settings/usage`.
- If official usage stays unknown, sign in again through the WebView2 page and press Refresh.
- If Token Meter is lower or higher than another tool, compare scan scope and counting rules. Codex counting includes `sessions` and `archived_sessions`, and counts `last_token_usage` rather than full carried totals.

## License / 许可证

This project is released under the [Codex Usage Tray Non-Commercial License](LICENSE).

You may use, copy, modify, and share this software for free for personal, learning, research, and evaluation purposes.

Commercial use is not allowed without separate written permission. This includes selling the software, charging for it, bundling it with a commercial product, offering it as part of a paid service, or using it primarily for commercial advantage or monetary compensation.

本项目采用 [Codex Usage Tray 非商用许可证](LICENSE)。

本软件可免费用于个人、学习、研究和评估等非商业用途。未经单独书面授权，不允许任何商业用途，包括销售、收费分发、集成到商业产品或付费服务中，或以商业利益、金钱报酬为主要目的使用。

The token metering implementation is based on behavior and compatibility work from [tokscale](https://github.com/junhoyeo/tokscale), which is MIT licensed. Third-party notices are included in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## Project Layout

```text
CodexUsageTray/          WinUI 3 app, WebView2 host, tray icon, Windows Widget provider
CodexUsageTray.Core/     Usage parser, token meter, pricing cache, and shared models/services
CodexUsageTray.Tests/    MSTest coverage for parsing, token metering, pricing, and state logic
StartCodexUsageTray.*    One-click Windows launcher scripts
RegisterCodexUsageWidget.ps1
```

## Notes

- The taskbar display uses a small native no-activate tool window positioned over free taskbar space. It does not register as a normal taskbar button and does not reserve or resize the taskbar's running-app button area.
- The Windows 11 Widget appears in the Widgets board.
- The tray icon uses native `Shell_NotifyIconW` interop.
- The widget provider reads only the app's cached usage snapshot. It does not read cookies, local Codex credentials, `auth.json`, or private ChatGPT APIs.
- Token cost is an estimate from public model price tables, not a bill.
