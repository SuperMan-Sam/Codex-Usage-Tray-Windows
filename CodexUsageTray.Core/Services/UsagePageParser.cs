using System.Globalization;
using System.Text.RegularExpressions;
using CodexUsageTray.Core.Models;

namespace CodexUsageTray.Core.Services;

public sealed partial class UsagePageParser
{
    public UsageSnapshot Parse(string? pageText, DateTimeOffset capturedAt)
    {
        if (string.IsNullOrWhiteSpace(pageText))
        {
            return UsageSnapshot.Unknown(capturedAt);
        }

        string normalizedText = NormalizeWhitespace(pageText);
        string lowerText = normalizedText.ToLowerInvariant();
        string[] lines = GetLines(pageText);

        if (IsLoginRequired(lowerText))
        {
            return new UsageSnapshot("Login required", string.Empty, string.Empty, null, capturedAt, UsageStatus.LoginRequired);
        }

        if (IsUnsupported(lowerText))
        {
            return new UsageSnapshot("Login unsupported", string.Empty, string.Empty, null, capturedAt, UsageStatus.Unsupported);
        }

        UsageLimitSnapshot? fiveHourLimit = TryFindFiveHourLimit(normalizedText);
        UsageLimitSnapshot? weeklyLimit = TryFindWeeklyLimit(normalizedText);
        int? percentRemaining = TryFindPercentRemaining(normalizedText);
        percentRemaining = ApplyLimitPercent(percentRemaining, fiveHourLimit);
        percentRemaining = ApplyLimitPercent(percentRemaining, weeklyLimit);
        string remainingText = FormatRemaining(percentRemaining);
        if (string.IsNullOrWhiteSpace(remainingText))
        {
            remainingText = FindLine(lines, RemainingLineRegex()) ?? string.Empty;
        }

        string usedText = FindUsedText(normalizedText, lines);
        string resetText = FindResetText(normalizedText, lines);
        UsageStatus status = IsLimitReached(lowerText, percentRemaining) ? UsageStatus.LimitReached : UsageStatus.Available;

        if (percentRemaining is null && string.IsNullOrWhiteSpace(remainingText) && string.IsNullOrWhiteSpace(usedText) && string.IsNullOrWhiteSpace(resetText))
        {
            return UsageSnapshot.Unknown(capturedAt, "Usage page changed");
        }

        return new UsageSnapshot(remainingText, usedText, resetText, percentRemaining, capturedAt, status)
        {
            FiveHourLimit = fiveHourLimit,
            WeeklyLimit = weeklyLimit,
        };
    }

    private static bool IsLoginRequired(string lowerText)
    {
        return (lowerText.Contains("log in", StringComparison.Ordinal) || lowerText.Contains("sign in", StringComparison.Ordinal))
            && (lowerText.Contains("sign up", StringComparison.Ordinal)
                || lowerText.Contains("continue with", StringComparison.Ordinal)
                || lowerText.Contains("email address", StringComparison.Ordinal)
                || lowerText.Contains("create account", StringComparison.Ordinal)
                || lowerText.Contains("where should we begin", StringComparison.Ordinal)
                || lowerText.Contains("get responses tailored to you", StringComparison.Ordinal));
    }

    private static bool IsUnsupported(string lowerText)
    {
        return lowerText.Contains("unsupported browser", StringComparison.Ordinal)
            || lowerText.Contains("login unsupported", StringComparison.Ordinal)
            || lowerText.Contains("unable to load chatgpt", StringComparison.Ordinal);
    }

    private static bool IsLimitReached(string lowerText, int? percentRemaining)
    {
        if (percentRemaining > 0)
        {
            return false;
        }

        return percentRemaining == 0
            || lowerText.Contains("limit reached", StringComparison.Ordinal)
            || lowerText.Contains("reached your codex limit", StringComparison.Ordinal)
            || lowerText.Contains("reached the codex limit", StringComparison.Ordinal)
            || lowerText.Contains("额度已用完", StringComparison.Ordinal)
            || ChineseLimitReachedRegex().IsMatch(lowerText);
    }

    private static int? TryFindPercentRemaining(string text)
    {
        int? explicitRemaining = FindLowestPercent(
            RemainingPercentRegex().Matches(text),
            LeadingRemainingPercentRegex().Matches(text),
            ChineseRemainingPercentRegex().Matches(text),
            LeadingChineseRemainingPercentRegex().Matches(text));
        if (explicitRemaining is not null)
        {
            return explicitRemaining;
        }

        int? usedPercent = FindHighestPercent(
            UsedPercentRegex().Matches(text),
            LeadingUsedPercentRegex().Matches(text),
            ChineseUsedPercentRegex().Matches(text),
            LeadingChineseUsedPercentRegex().Matches(text));
        if (usedPercent is not null)
        {
            return Math.Clamp(100 - usedPercent.Value, 0, 100);
        }

        Match fractionMatch = FractionRegex().Match(text);
        if (fractionMatch.Success
            && double.TryParse(fractionMatch.Groups["used"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double used)
            && double.TryParse(fractionMatch.Groups["total"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double total)
            && total > 0)
        {
            return Math.Clamp((int)Math.Round(((total - used) / total) * 100, MidpointRounding.AwayFromZero), 0, 100);
        }

        return null;
    }

    private static int? ApplyLimitPercent(int? percentRemaining, UsageLimitSnapshot? limit)
    {
        if (limit is null)
        {
            return percentRemaining;
        }

        return percentRemaining is null ? limit.PercentRemaining : Math.Min(percentRemaining.Value, limit.PercentRemaining);
    }

    private static UsageLimitSnapshot? TryFindFiveHourLimit(string text)
    {
        return TryFindLimit(text, FiveHourLimitRegex(), "5h");
    }

    private static UsageLimitSnapshot? TryFindWeeklyLimit(string text)
    {
        return TryFindLimit(text, WeeklyLimitRegex(), "Week");
    }

    private static UsageLimitSnapshot? TryFindLimit(string text, Regex regex, string name)
    {
        foreach (Match match in regex.Matches(text))
        {
            if (IsModelSpecificLimit(text, match.Index))
            {
                continue;
            }

            int? percent = ClampPercent(match.Groups["value"].Value);
            if (percent is null)
            {
                continue;
            }

            string resetText = match.Groups["reset"].Success ? match.Groups["reset"].Value.Trim() : string.Empty;
            return new UsageLimitSnapshot(name, percent.Value, resetText);
        }

        return null;
    }

    private static bool IsModelSpecificLimit(string text, int matchIndex)
    {
        int start = Math.Max(0, matchIndex - 64);
        string prefix = text[start..matchIndex].ToLowerInvariant();
        return prefix.Contains("gpt-", StringComparison.Ordinal)
            || prefix.Contains("codex-spark", StringComparison.Ordinal);
    }

    private static int? FindLowestPercent(params MatchCollection[] matchCollections)
    {
        int? lowest = null;
        foreach (MatchCollection matches in matchCollections)
        {
            foreach (Match match in matches)
            {
                int? percent = ClampPercent(match.Groups["value"].Value);
                if (percent is null)
                {
                    continue;
                }

                lowest = lowest is null ? percent.Value : Math.Min(lowest.Value, percent.Value);
            }
        }

        return lowest;
    }

    private static int? FindHighestPercent(params MatchCollection[] matchCollections)
    {
        int? highest = null;
        foreach (MatchCollection matches in matchCollections)
        {
            foreach (Match match in matches)
            {
                int? percent = ClampPercent(match.Groups["value"].Value);
                if (percent is null)
                {
                    continue;
                }

                highest = highest is null ? percent.Value : Math.Max(highest.Value, percent.Value);
            }
        }

        return highest;
    }

    private static int? ClampPercent(string value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int percent)
            ? Math.Clamp(percent, 0, 100)
            : null;
    }

    private static string FormatRemaining(int? percentRemaining)
    {
        return percentRemaining is null ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"{percentRemaining}% remaining");
    }

    private static string? FindLine(string[] lines, Regex regex)
    {
        return lines.FirstOrDefault(line => regex.IsMatch(line));
    }

    private static string FindResetText(string normalizedText, string[] lines)
    {
        Match chineseResetMatch = ChineseResetTextRegex().Match(normalizedText);
        if (chineseResetMatch.Success)
        {
            return chineseResetMatch.Value.Trim();
        }

        return FindLine(lines, ResetLineRegex()) ?? string.Empty;
    }

    private static string FindUsedText(string normalizedText, string[] lines)
    {
        Match usedTextMatch = UsedTextRegex().Match(normalizedText);
        if (usedTextMatch.Success)
        {
            return usedTextMatch.Value.Trim();
        }

        return lines.FirstOrDefault(line => line.Length <= 120 && UsedLineRegex().IsMatch(line)) ?? string.Empty;
    }

    private static string[] GetLines(string text)
    {
        return text
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
    }

    private static string NormalizeWhitespace(string text)
    {
        return WhitespaceRegex().Replace(text, " ").Trim();
    }

    [GeneratedRegex(@"(?<value>\d{1,3})\s*%\s*(?:remaining|left|available|remain(?:s|ing)?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RemainingPercentRegex();

    [GeneratedRegex(@"(?<!%\s)(?:remaining|left|available|remain(?:s|ing)?)[^\d%]{0,40}(?<value>\d{1,3})\s*%", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LeadingRemainingPercentRegex();

    [GeneratedRegex(@"(?<value>\d{1,3})\s*%\s*(?:剩余|剩余额度|可用|可用额度)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChineseRemainingPercentRegex();

    [GeneratedRegex(@"(?<!%\s)(?:剩余|剩余额度|可用|可用额度)[^\d%]{0,40}(?<value>\d{1,3})\s*%", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LeadingChineseRemainingPercentRegex();

    [GeneratedRegex(@"(?<value>\d{1,3})\s*%\s*used", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UsedPercentRegex();

    [GeneratedRegex(@"\bused\b[^\d%]{0,40}(?<value>\d{1,3})\s*%", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LeadingUsedPercentRegex();

    [GeneratedRegex(@"(?<value>\d{1,3})\s*%\s*(?:已用|已使用|使用了)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChineseUsedPercentRegex();

    [GeneratedRegex(@"(?:已用|已使用|使用了)[^\d%]{0,40}(?<value>\d{1,3})\s*%", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LeadingChineseUsedPercentRegex();

    [GeneratedRegex(@"(?<used>\d+(?:\.\d+)?)\s*/\s*(?<total>\d+(?:\.\d+)?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FractionRegex();

    [GeneratedRegex(@"(?<name>(?:5\s*小时使用限额|5\s*(?:hour|hr|h)\s*(?:usage\s*)?limit))\s*(?<value>\d{1,3})\s*%\s*(?:剩余|remaining|left)(?:\s*(?<reset>重置时间\s*[:：]\s*(?:\d{4}年\d{1,2}月\d{1,2}日\s*)?\d{1,2}:\d{2}))?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FiveHourLimitRegex();

    [GeneratedRegex(@"(?<name>(?:每周使用限额|weekly\s*(?:usage\s*)?limit))\s*(?<value>\d{1,3})\s*%\s*(?:剩余|remaining|left)(?:\s*(?<reset>重置时间\s*[:：]\s*(?:\d{4}年\d{1,2}月\d{1,2}日\s*)?\d{1,2}:\d{2}))?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WeeklyLimitRegex();

    [GeneratedRegex(@"(remaining|left|available|credit|剩余|剩余额度|可用额度)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RemainingLineRegex();

    [GeneratedRegex(@"\bused\b|limit used", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UsedLineRegex();

    [GeneratedRegex(@"(?:\d{1,3}\s*%\s*(?:used|已用|已使用|使用了)|(?:\bused\b|已用|已使用|使用了)[^\d%]{0,40}\d{1,3}\s*%)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UsedTextRegex();

    [GeneratedRegex(@"(reset|resets|renews|refreshes|available again|重置时间|重置|刷新时间)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ResetLineRegex();

    [GeneratedRegex(@"重置时间\s*[:：]\s*(?:\d{4}年\d{1,2}月\d{1,2}日\s*)?\d{1,2}:\d{2}", RegexOptions.CultureInvariant)]
    private static partial Regex ChineseResetTextRegex();

    [GeneratedRegex(@"(?:达到|已达).{0,8}(?:限制|限额|额度)", RegexOptions.CultureInvariant)]
    private static partial Regex ChineseLimitReachedRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
