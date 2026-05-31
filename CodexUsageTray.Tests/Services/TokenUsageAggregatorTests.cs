using CodexUsageTray.Core.Models;
using CodexUsageTray.Core.Services.TokenMeter;

namespace CodexUsageTray.Tests.Services;

[TestClass]
public sealed class TokenUsageAggregatorTests
{
    [TestMethod]
    public void BuildSnapshot_GroupsByClientAndModelAndFiltersRange()
    {
        DateTimeOffset now = new(2026, 5, 31, 12, 0, 0, TimeSpan.FromHours(10));
        TokenUsageEntry today = Entry("codex", "gpt-5.3-codex", now.AddHours(-1), 100, 0.10m);
        TokenUsageEntry yesterday = Entry("claude", "claude-sonnet-4", now.AddDays(-1), 50, 0.20m);
        TokenUsageEntry old = Entry("codex", "gpt-5.3-codex", now.AddDays(-20), 25, 0.30m);

        TokenUsageSnapshot snapshot = TokenUsageAggregator.BuildSnapshot(
            [today, yesterday, old],
            TokenUsageRange.Last7Days,
            now,
            null,
            scannedFileCount: 3,
            failedFileCount: 0);

        Assert.AreEqual(150, snapshot.TotalTokens.Total);
        Assert.AreEqual(0.30m, snapshot.KnownCost);
        Assert.AreEqual(2, snapshot.ClientSummaries.Count);
        Assert.AreEqual(2, snapshot.ModelSummaries.Count);
        Assert.AreEqual("codex", snapshot.ClientSummaries[0].Client);
    }

    [TestMethod]
    public void BuildSnapshot_WhenCostMissing_DoesNotTreatAsZero()
    {
        DateTimeOffset now = DateTimeOffset.Now;
        TokenUsageEntry unknownCost = Entry("codex", "unknown-model", now, 100, null);

        TokenUsageSnapshot snapshot = TokenUsageAggregator.BuildSnapshot(
            [unknownCost],
            TokenUsageRange.AllHistory,
            now,
            null,
            scannedFileCount: 1,
            failedFileCount: 0);

        Assert.IsNull(snapshot.KnownCost);
        Assert.AreEqual(1, snapshot.UnknownCostEntryCount);
    }

    [TestMethod]
    public void BuildSnapshot_WithFiveHourWindow_CountsOnlyCodexEntriesInsideWindow()
    {
        DateTimeOffset now = new(2026, 5, 31, 10, 0, 0, TimeSpan.FromHours(10));
        TokenUsageWindow window = new(now.AddHours(-2), now.AddHours(3));
        TokenUsageEntry codexInside = Entry("codex", "gpt-5.3-codex", now.AddHours(-1), 100, null);
        TokenUsageEntry codexBefore = Entry("codex", "gpt-5.3-codex", now.AddHours(-3), 50, null);
        TokenUsageEntry claudeInside = Entry("claude", "claude-sonnet-4", now.AddMinutes(-30), 200, null);

        TokenUsageSnapshot snapshot = TokenUsageAggregator.BuildSnapshot(
            [codexInside, codexBefore, claudeInside],
            TokenUsageRange.AllHistory,
            now,
            null,
            scannedFileCount: 3,
            failedFileCount: 0,
            fiveHourWindow: window);

        Assert.IsTrue(snapshot.HasFiveHourWindow);
        Assert.AreEqual(100, snapshot.FiveHourWindowTokens.Total);
        Assert.AreEqual(1, snapshot.FiveHourWindowMessageCount);
        Assert.AreEqual(window.Start, snapshot.FiveHourWindowStart);
        Assert.AreEqual(window.End, snapshot.FiveHourWindowEnd);
    }

    private static TokenUsageEntry Entry(string client, string model, DateTimeOffset timestamp, long input, decimal? cost)
    {
        return new TokenUsageEntry(
            client,
            model,
            "openai",
            "session",
            null,
            null,
            timestamp,
            new TokenBreakdown(input, 0, 0, 0, 0),
            1,
            null,
            cost,
            cost is null ? null : "test",
            "fixture");
    }
}
