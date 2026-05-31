using CodexUsageTray.Core.Models;
using CodexUsageTray.Core.Services;

namespace CodexUsageTray.Tests.Services;

[TestClass]
public sealed class UsagePageParserTests
{
    private readonly UsagePageParser _parser = new();

    [TestMethod]
    public void Parse_WithRemainingPercent_ReturnsAvailableSnapshot()
    {
        string pageText = """
            Codex usage
            64% remaining
            36% used
            Resets tomorrow at 9:00 AM
            """;

        UsageSnapshot snapshot = _parser.Parse(pageText, DateTimeOffset.UnixEpoch);

        Assert.AreEqual(UsageStatus.Available, snapshot.Status);
        Assert.AreEqual(64, snapshot.PercentRemaining);
        Assert.AreEqual("64% remaining", snapshot.RemainingText);
        Assert.AreEqual("36% used", snapshot.UsedText);
        Assert.AreEqual("Resets tomorrow at 9:00 AM", snapshot.ResetText);
    }

    [TestMethod]
    public void Parse_WithUsedPercent_ReturnsRemainingPercent()
    {
        string pageText = """
            Codex agentic usage
            73% used
            Renews in 2 days
            """;

        UsageSnapshot snapshot = _parser.Parse(pageText, DateTimeOffset.UnixEpoch);

        Assert.AreEqual(UsageStatus.Available, snapshot.Status);
        Assert.AreEqual(27, snapshot.PercentRemaining);
        Assert.AreEqual("27% remaining", snapshot.RemainingText);
    }

    [TestMethod]
    public void Parse_WithLeadingUsedPercent_ReturnsRemainingPercent()
    {
        string pageText = """
            Codex usage dashboard
            You have used 84% of your weekly Codex usage.
            Resets June 1, 2026
            """;

        UsageSnapshot snapshot = _parser.Parse(pageText, DateTimeOffset.UnixEpoch);

        Assert.AreEqual(UsageStatus.Available, snapshot.Status);
        Assert.AreEqual(16, snapshot.PercentRemaining);
    }

    [TestMethod]
    public void Parse_WithLeadingRemainingPercent_ReturnsRemainingPercent()
    {
        string pageText = """
            Codex usage dashboard
            Remaining this week: 42%
            """;

        UsageSnapshot snapshot = _parser.Parse(pageText, DateTimeOffset.UnixEpoch);

        Assert.AreEqual(UsageStatus.Available, snapshot.Status);
        Assert.AreEqual(42, snapshot.PercentRemaining);
    }

    [TestMethod]
    public void Parse_WithChineseRemainingCards_ReturnsLowestRemainingPercent()
    {
        string pageText = """
            Codex 分析
            5 小时使用限额
            86% 剩余
            重置时间： 12:39
            每周使用限额
            97% 剩余
            重置时间： 2026年6月7日 7:39
            GPT-5.3-Codex-Spark 5 小时使用限额
            100% 剩余
            GPT-5.3-Codex-Spark 每周使用限额
            100% 剩余
            """;

        UsageSnapshot snapshot = _parser.Parse(pageText, DateTimeOffset.UnixEpoch);

        Assert.AreEqual(UsageStatus.Available, snapshot.Status);
        Assert.AreEqual(86, snapshot.PercentRemaining);
        Assert.AreEqual("86% remaining", snapshot.RemainingText);
        Assert.AreEqual(string.Empty, snapshot.UsedText);
        Assert.AreEqual("重置时间： 12:39", snapshot.ResetText);
        Assert.IsNotNull(snapshot.FiveHourLimit);
        Assert.IsNotNull(snapshot.WeeklyLimit);
        Assert.AreEqual(86, snapshot.FiveHourLimit.PercentRemaining);
        Assert.AreEqual(97, snapshot.WeeklyLimit.PercentRemaining);
        Assert.AreEqual("重置时间： 12:39", snapshot.FiveHourLimit.ResetText);
        Assert.AreEqual("重置时间： 2026年6月7日 7:39", snapshot.WeeklyLimit.ResetText);
    }

    [TestMethod]
    public void Parse_WithUsageUrlAndChineseRemaining_DoesNotTreatUrlAsUsed()
    {
        string pageText = """
            Codex
            https://chatgpt.com/codex/settings/usage
            100% 剩余
            """;

        UsageSnapshot snapshot = _parser.Parse(pageText, DateTimeOffset.UnixEpoch);

        Assert.AreEqual(UsageStatus.Available, snapshot.Status);
        Assert.AreEqual(100, snapshot.PercentRemaining);
    }

    [TestMethod]
    public void Parse_WithCollapsedChinesePageText_ExtractsResetSnippet()
    {
        string pageText = "Codex 分析 5 小时使用限额 84% 剩余 重置时间： 12:39 每周使用限额 97% 剩余 重置时间： 2026年6月7日 7:39";

        UsageSnapshot snapshot = _parser.Parse(pageText, DateTimeOffset.UnixEpoch);

        Assert.AreEqual(UsageStatus.Available, snapshot.Status);
        Assert.AreEqual(84, snapshot.PercentRemaining);
        Assert.AreEqual("84% remaining", snapshot.RemainingText);
        Assert.AreEqual("重置时间： 12:39", snapshot.ResetText);
        Assert.IsNotNull(snapshot.FiveHourLimit);
        Assert.IsNotNull(snapshot.WeeklyLimit);
        Assert.AreEqual(84, snapshot.FiveHourLimit.PercentRemaining);
        Assert.AreEqual(97, snapshot.WeeklyLimit.PercentRemaining);
    }

    [TestMethod]
    public void Parse_WithModelSpecificCards_UsesSharedLimitCardsForTaskbarValues()
    {
        string pageText = "Codex 分析 5 小时使用限额 82% 剩余 重置时间： 12:39 每周使用限额 96% 剩余 重置时间： 2026年6月7日 7:39 GPT-5.3-Codex-Spark 5 小时使用限额 100% 剩余 GPT-5.3-Codex-Spark 每周使用限额 100% 剩余";

        UsageSnapshot snapshot = _parser.Parse(pageText, DateTimeOffset.UnixEpoch);

        Assert.IsNotNull(snapshot.FiveHourLimit);
        Assert.IsNotNull(snapshot.WeeklyLimit);
        Assert.AreEqual(82, snapshot.FiveHourLimit.PercentRemaining);
        Assert.AreEqual(96, snapshot.WeeklyLimit.PercentRemaining);
        Assert.AreEqual(82, snapshot.PercentRemaining);
    }

    [TestMethod]
    public void Parse_WithFraction_ReturnsRemainingPercent()
    {
        string pageText = """
            Codex credits
            75 / 100 used
            Reset on Monday
            """;

        UsageSnapshot snapshot = _parser.Parse(pageText, DateTimeOffset.UnixEpoch);

        Assert.AreEqual(25, snapshot.PercentRemaining);
        Assert.AreEqual(UsageStatus.Available, snapshot.Status);
    }

    [TestMethod]
    public void Parse_WithLoggedOutChatGptPage_ReturnsLoginRequired()
    {
        string pageText = """
            ChatGPT
            Log in
            Sign up for free
            Where should we begin?
            """;

        UsageSnapshot snapshot = _parser.Parse(pageText, DateTimeOffset.UnixEpoch);

        Assert.AreEqual(UsageStatus.LoginRequired, snapshot.Status);
        Assert.IsNull(snapshot.PercentRemaining);
    }

    [TestMethod]
    public void Parse_WithSignInPage_ReturnsLoginRequired()
    {
        string pageText = """
            ChatGPT
            Sign in
            Continue with Google
            Email address
            """;

        UsageSnapshot snapshot = _parser.Parse(pageText, DateTimeOffset.UnixEpoch);

        Assert.AreEqual(UsageStatus.LoginRequired, snapshot.Status);
        Assert.IsNull(snapshot.PercentRemaining);
    }

    [TestMethod]
    public void Parse_WithUnknownPage_ReturnsUnknown()
    {
        string pageText = "A generic page with no usage details.";

        UsageSnapshot snapshot = _parser.Parse(pageText, DateTimeOffset.UnixEpoch);

        Assert.AreEqual(UsageStatus.Unknown, snapshot.Status);
        Assert.IsNull(snapshot.PercentRemaining);
    }

    [TestMethod]
    public void Parse_WithLimitReached_ReturnsLimitReached()
    {
        string pageText = """
            You have reached your Codex limit.
            0% remaining
            """;

        UsageSnapshot snapshot = _parser.Parse(pageText, DateTimeOffset.UnixEpoch);

        Assert.AreEqual(UsageStatus.LimitReached, snapshot.Status);
        Assert.AreEqual(0, snapshot.PercentRemaining);
    }
}
