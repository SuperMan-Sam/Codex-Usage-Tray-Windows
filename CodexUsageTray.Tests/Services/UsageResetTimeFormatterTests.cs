using CodexUsageTray.Core.Models;
using CodexUsageTray.Core.Services;

namespace CodexUsageTray.Tests.Services;

[TestClass]
public sealed class UsageResetTimeFormatterTests
{
    [TestMethod]
    public void FormatHoursUntilReset_WithSameDayReset_ReturnsCeilingHours()
    {
        UsageLimitSnapshot limit = new("5h", 74, "重置时间： 12:39");
        DateTimeOffset capturedAt = new(2026, 5, 31, 10, 42, 0, TimeSpan.FromHours(10));

        string text = UsageResetTimeFormatter.FormatHoursUntilReset(limit, capturedAt);

        Assert.AreEqual("2h", text);
    }

    [TestMethod]
    public void FormatHoursUntilReset_WithDatedReset_ReturnsCeilingHours()
    {
        UsageLimitSnapshot limit = new("Week", 95, "重置时间： 2026年6月7日 7:39");
        DateTimeOffset capturedAt = new(2026, 5, 31, 10, 42, 0, TimeSpan.FromHours(10));

        string text = UsageResetTimeFormatter.FormatHoursUntilReset(limit, capturedAt);

        Assert.AreEqual("165h", text);
    }

    [TestMethod]
    public void FormatHoursUntilReset_WhenUndatedTimeAlreadyPassed_UsesTomorrow()
    {
        UsageLimitSnapshot limit = new("5h", 74, "重置时间： 09:15");
        DateTimeOffset capturedAt = new(2026, 5, 31, 10, 42, 0, TimeSpan.FromHours(10));

        string text = UsageResetTimeFormatter.FormatHoursUntilReset(limit, capturedAt);

        Assert.AreEqual("23h", text);
    }

    [TestMethod]
    public void FormatHoursUntilReset_WhenMissingReset_ReturnsFallback()
    {
        UsageLimitSnapshot limit = new("5h", 74, string.Empty);

        string text = UsageResetTimeFormatter.FormatHoursUntilReset(limit, DateTimeOffset.UnixEpoch);

        Assert.AreEqual("--", text);
    }

    [TestMethod]
    public void TryGetFiveHourWindow_WithResetTime_ReturnsCurrentLimitWindow()
    {
        DateTimeOffset capturedAt = new(2026, 5, 31, 10, 42, 0, TimeSpan.FromHours(10));
        UsageSnapshot snapshot = new("84% remaining", string.Empty, string.Empty, 84, capturedAt, UsageStatus.Available)
        {
            FiveHourLimit = new UsageLimitSnapshot("5h", 84, "重置时间： 12:39"),
        };

        bool found = UsageResetTimeFormatter.TryGetFiveHourWindow(snapshot, out TokenUsageWindow window);

        Assert.IsTrue(found);
        Assert.AreEqual(new DateTimeOffset(2026, 5, 31, 7, 39, 0, TimeSpan.FromHours(10)), window.Start);
        Assert.AreEqual(new DateTimeOffset(2026, 5, 31, 12, 39, 0, TimeSpan.FromHours(10)), window.End);
    }
}
