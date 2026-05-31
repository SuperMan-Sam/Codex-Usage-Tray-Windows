using CodexUsageTray.Core.Models;
using CodexUsageTray.Services;

namespace CodexUsageTray;

internal static class AppText
{
    public const string UsageUrl = "https://chatgpt.com/codex/settings/usage";

    public static string AppName => Text("Codex 用量托盘", "Codex Usage Tray");

    public static string CodexUsageTitle => Text("Codex 用量", "Codex Usage");

    public static string Refresh => Text("刷新", "Refresh");

    public static string RefreshUsageAutomationName => Text("刷新 Codex 用量", "Refresh Codex usage");

    public static string OpenUsagePage => Text("打开用量页面", "Open usage page");

    public static string OpenUsagePageAutomationName => Text("打开 Codex 用量页面", "Open Codex usage page");

    public static string Settings => Text("设置", "Settings");

    public static string OpenSettingsAutomationName => Text("打开设置", "Open settings");

    public static string Exit => Text("退出", "Exit");

    public static string Close => Text("关闭", "Close");

    public static string Status => Text("状态", "Status");

    public static string Remaining => Text("剩余", "Remaining");

    public static string FiveHourLimit => Text("5 小时限额", "5h limit");

    public static string WeeklyLimit => Text("每周限额", "Weekly limit");

    public static string ShortWeeklyLimit => Text("每周", "Week");

    public static string TaskbarFiveHourLabel => Text("5h", "5h");

    public static string TaskbarWeeklyLabel => Text("周", "7d");

    public static string Used => Text("已用", "Used");

    public static string Reset => Text("重置", "Reset");

    public static string LastUpdated => Text("最后更新", "Last updated");

    public static string Refreshing => Text("正在刷新...", "Refreshing...");

    public static string Idle => Text("空闲", "Idle");

    public static string TokenMeter => Text("Token 计量", "Token Meter");

    public static string TokenRange => Text("统计范围", "Range");

    public static string TokenRangeToday => Text("今天", "Today");

    public static string TokenRangeLast7Days => Text("7 天", "7 days");

    public static string TokenRangeCurrentMonth => Text("本月", "Month");

    public static string TokenRangeAllHistory => Text("全部历史", "All history");

    public static string TotalTokens => Text("总 tokens", "Total tokens");

    public static string FiveHourWindowTokens => Text("当前 5 小时窗口", "Current 5h window");

    public static string FiveHourWindowUnavailable => Text("等待 5 小时重置时间", "Waiting for 5h reset time");

    public static string TokenCost => Text("估算成本", "Estimated cost");

    public static string TokenBreakdown => Text("拆分", "Breakdown");

    public static string TokenClients => Text("客户端", "Clients");

    public static string TokenModels => Text("模型明细", "Models");

    public static string TokenClient => Text("客户端", "Client");

    public static string TokenModel => Text("模型", "Model");

    public static string TokenProvider => Text("Provider", "Provider");

    public static string TokenMessages => Text("消息", "Messages");

    public static string TokenUnknownCost => Text("部分成本不可用", "Some cost unavailable");

    public static string TokenPricing => Text("价格数据", "Pricing");

    public static string TokenFiles => Text("扫描文件", "Files scanned");

    public static string TokenRefreshState => Text("Token 刷新", "Token refresh");

    public static string StartWithWindows => Text("开机启动", "Start with Windows");

    public static string RefreshIntervalOneMinute => Text("刷新间隔：1 分钟", "Refresh interval: 1 minute");

    public static string WebViewData => Text("WebView2 数据", "WebView2 data");

    public static string NoCredentialFiles => Text(
        "本程序不会读取本地 Codex 认证文件或私有 ChatGPT 网络 API。",
        "The app does not read local Codex auth files or private ChatGPT network APIs.");

    public static string Language => Text("语言", "Language");

    public static string FollowSystemLanguage => Text("跟随系统语言", "Follow system language");

    public static string English => Text("英文", "English");

    public static string SimplifiedChinese => Text("简体中文", "Simplified Chinese");

    public static string TaskbarFontSize => Text("任务栏字体大小", "Taskbar font size");

    public static string FontSizeValue(double value)
    {
        return Text($"{value:0.#} 像素", $"{value:0.#} px");
    }

    public static string UsageLoaded => Text("用量已加载", "Usage loaded");

    public static string LoginRequired => Text("需要登录", "Login required");

    public static string LimitReached => Text("已达到限额", "Limit reached");

    public static string LoginUnsupported => Text("不支持登录", "Login unsupported");

    public static string LoginUnsupportedInEmbeddedBrowser => Text("嵌入式浏览器不支持登录", "Login unsupported in embedded browser");

    public static string UsageUnavailable => Text("用量不可用", "Usage unavailable");

    public static string TokenUsageUnavailable => Text("Token 计量不可用", "Token usage unavailable");

    public static string TokenUsageLoaded => Text("Token 计量已加载", "Token usage loaded");

    public static string NoTokenUsageDetected => Text("未检测到 token 用量", "No token usage detected");

    public static string NotRefreshedYet => Text("尚未刷新", "Not refreshed yet");

    public static string NoRemainingUsageDetected => Text("未检测到剩余用量", "No remaining usage detected");

    public static string NoFiveHourLimitDetected => Text("未检测到 5 小时限额", "No 5h limit detected");

    public static string NoWeeklyLimitDetected => Text("未检测到每周限额", "No weekly limit detected");

    public static string NoUsedUsageDetected => Text("未检测到已用用量", "No used usage detected");

    public static string NoResetTimeDetected => Text("未检测到重置时间", "No reset time detected");

    public static string OpenAppToSignIn => Text("打开 Codex Usage Tray 登录", "Open Codex Usage Tray to sign in");

    public static string Updated(DateTimeOffset capturedAt)
    {
        return string.Format(LanguageSettingsService.CurrentCulture, Text("更新于 {0:g}", "Updated {0:g}"), capturedAt.ToLocalTime());
    }

    public static string StatusText(UsageStatus status)
    {
        return status switch
        {
            UsageStatus.Available => UsageLoaded,
            UsageStatus.LoginRequired => LoginRequired,
            UsageStatus.LimitReached => LimitReached,
            UsageStatus.Unsupported => LoginUnsupported,
            _ => UsageUnavailable,
        };
    }

    public static string FormatRemainingPercent(int percent)
    {
        return Text($"{percent}% 剩余", $"{percent}% remaining");
    }

    public static string FormatCompactPercent(int percent)
    {
        return $"{percent}%";
    }

    public static string FormatTokenCount(long tokens)
    {
        if (LanguageSettingsService.CurrentLanguage == AppLanguage.ChineseSimplified)
        {
            return FormatChineseTokenCount(tokens);
        }

        return FormatCount(tokens);
    }

    public static string FormatCount(long count)
    {
        return string.Format(LanguageSettingsService.CurrentCulture, "{0:N0}", count);
    }

    public static string FormatTokenWindow(DateTimeOffset start, DateTimeOffset end, int messageCount)
    {
        return string.Format(
            LanguageSettingsService.CurrentCulture,
            Text("{0:t} - {1:t}，{2} 条记录", "{0:t} - {1:t}, {2} records"),
            start.ToLocalTime(),
            end.ToLocalTime(),
            FormatCount(messageCount));
    }

    public static string FormatUsdCost(decimal? cost)
    {
        return cost is null
            ? Text("不可用", "Unavailable")
            : string.Format(LanguageSettingsService.CurrentCulture, "${0:N4}", cost.Value);
    }

    public static string FormatTokenBreakdown(TokenBreakdown tokens)
    {
        return Text(
            $"输入 {FormatTokenCount(tokens.Input)} / 输出 {FormatTokenCount(tokens.Output)} / 缓存读 {FormatTokenCount(tokens.CacheRead)} / 缓存写 {FormatTokenCount(tokens.CacheWrite)} / 推理 {FormatTokenCount(tokens.Reasoning)}",
            $"In {FormatTokenCount(tokens.Input)} / Out {FormatTokenCount(tokens.Output)} / Cache read {FormatTokenCount(tokens.CacheRead)} / Cache write {FormatTokenCount(tokens.CacheWrite)} / Reasoning {FormatTokenCount(tokens.Reasoning)}");
    }

    public static string FormatTokenFiles(int scanned, int failed)
    {
        return failed <= 0
            ? Text($"{scanned} 个文件", $"{scanned} files")
            : Text($"{scanned} 个文件，{failed} 个失败", $"{scanned} files, {failed} failed");
    }

    public static string FormatUnknownCost(int count)
    {
        return count <= 0
            ? string.Empty
            : Text($"{count} 条记录成本不可用", $"{count} entries without pricing");
    }

    public static string TokenRangeText(TokenUsageRange range)
    {
        return range switch
        {
            TokenUsageRange.Today => TokenRangeToday,
            TokenUsageRange.Last7Days => TokenRangeLast7Days,
            TokenUsageRange.CurrentMonth => TokenRangeCurrentMonth,
            _ => TokenRangeAllHistory,
        };
    }

    public static string LocalizeKnownPageText(string value)
    {
        return value switch
        {
            "Not refreshed yet" => NotRefreshedYet,
            "Usage unavailable" => UsageUnavailable,
            "Login required" => LoginRequired,
            "Login unsupported" => LoginUnsupported,
            "Usage page changed" => UsageUnavailable,
            _ => value,
        };
    }

    private static string Text(string zh, string en)
    {
        return LanguageSettingsService.CurrentLanguage == AppLanguage.ChineseSimplified ? zh : en;
    }

    private static string FormatChineseTokenCount(long tokens)
    {
        long absoluteTokens = Math.Abs(tokens);
        if (absoluteTokens >= 100_000_000)
        {
            return $"{FormatCompactUnit(tokens / 100_000_000m)}亿";
        }

        if (absoluteTokens >= 10_000)
        {
            return $"{FormatCompactUnit(tokens / 10_000m)}万";
        }

        return FormatCount(tokens);
    }

    private static string FormatCompactUnit(decimal value)
    {
        return value.ToString("0.##", LanguageSettingsService.CurrentCulture);
    }
}
