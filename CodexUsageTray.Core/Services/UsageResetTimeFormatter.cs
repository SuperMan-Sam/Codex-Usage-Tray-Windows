using System.Globalization;
using System.Text.RegularExpressions;
using CodexUsageTray.Core.Models;

namespace CodexUsageTray.Core.Services;

public static partial class UsageResetTimeFormatter
{
    public static string FormatHoursUntilReset(UsageLimitSnapshot? limit, DateTimeOffset capturedAt)
    {
        if (!TryGetResetTime(limit, capturedAt, out DateTimeOffset resetAt))
        {
            return "--";
        }

        TimeSpan remaining = resetAt - capturedAt.ToLocalTime();
        if (remaining <= TimeSpan.Zero)
        {
            return "0h";
        }

        int hours = Math.Max(1, (int)Math.Ceiling(remaining.TotalHours));
        return string.Create(CultureInfo.InvariantCulture, $"{hours}h");
    }

    public static bool TryGetFiveHourWindow(UsageSnapshot snapshot, out TokenUsageWindow window)
    {
        window = default!;
        if (!TryGetResetTime(snapshot.FiveHourLimit, snapshot.CapturedAt, out DateTimeOffset resetAt))
        {
            return false;
        }

        window = new TokenUsageWindow(resetAt - TimeSpan.FromHours(5), resetAt);
        return true;
    }

    public static bool TryGetResetTime(UsageLimitSnapshot? limit, DateTimeOffset capturedAt, out DateTimeOffset resetAt)
    {
        resetAt = default;
        return limit is not null
            && !string.IsNullOrWhiteSpace(limit.ResetText)
            && TryParseResetTime(limit.ResetText, capturedAt, out resetAt);
    }

    private static bool TryParseResetTime(string resetText, DateTimeOffset capturedAt, out DateTimeOffset resetAt)
    {
        resetAt = default;
        Match match = ChineseResetTimeRegex().Match(resetText);
        if (!match.Success)
        {
            return false;
        }

        DateTimeOffset localCapturedAt = capturedAt.ToLocalTime();
        int year = localCapturedAt.Year;
        int month = localCapturedAt.Month;
        int day = localCapturedAt.Day;

        if (match.Groups["year"].Success)
        {
            year = int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture);
            month = int.Parse(match.Groups["month"].Value, CultureInfo.InvariantCulture);
            day = int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture);
        }

        int hour = int.Parse(match.Groups["hour"].Value, CultureInfo.InvariantCulture);
        int minute = int.Parse(match.Groups["minute"].Value, CultureInfo.InvariantCulture);
        resetAt = new DateTimeOffset(year, month, day, hour, minute, 0, localCapturedAt.Offset);

        if (!match.Groups["year"].Success && resetAt <= localCapturedAt)
        {
            resetAt = resetAt.AddDays(1);
        }

        return true;
    }

    [GeneratedRegex(@"(?:(?<year>\d{4})年(?<month>\d{1,2})月(?<day>\d{1,2})日\s*)?(?<hour>\d{1,2}):(?<minute>\d{2})", RegexOptions.CultureInvariant)]
    private static partial Regex ChineseResetTimeRegex();
}
