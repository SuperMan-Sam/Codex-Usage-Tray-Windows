using CodexUsageTray.Core.Models;

namespace CodexUsageTray.Core.Services.TokenMeter;

public static class TokenUsageAggregator
{
    public static TokenUsageSnapshot BuildSnapshot(
        IEnumerable<TokenUsageEntry> allEntries,
        TokenUsageRange range,
        DateTimeOffset capturedAt,
        PricingSnapshot? pricing,
        int scannedFileCount,
        int failedFileCount,
        TokenUsageWindow? fiveHourWindow = null)
    {
        List<TokenUsageEntry> allEntryList = allEntries.ToList();
        List<TokenUsageEntry> entries = allEntryList
            .Where(entry => IsInRange(entry.Timestamp, range, capturedAt))
            .Where(entry => entry.Tokens.HasAnyTokens || entry.Cost is not null)
            .ToList();

        TokenBreakdown totalTokens = entries.Aggregate(TokenBreakdown.Empty, (current, entry) => current.Add(entry.Tokens));
        decimal knownCost = entries.Where(entry => entry.Cost is not null).Sum(entry => entry.Cost!.Value);
        int unknownCostCount = entries.Count(entry => entry.Tokens.HasAnyTokens && entry.Cost is null);
        int messageCount = entries.Sum(entry => Math.Max(1, entry.MessageCount));

        List<TokenClientSummary> clientSummaries = entries
            .GroupBy(entry => entry.Client, StringComparer.OrdinalIgnoreCase)
            .Select(group => new TokenClientSummary(
                group.Key,
                group.Aggregate(TokenBreakdown.Empty, (current, entry) => current.Add(entry.Tokens)),
                group.Sum(entry => Math.Max(1, entry.MessageCount)),
                SumNullableCost(group),
                group.Count(entry => entry.Tokens.HasAnyTokens && entry.Cost is null)))
            .OrderByDescending(summary => summary.TotalTokens)
            .ThenBy(summary => summary.Client, StringComparer.OrdinalIgnoreCase)
            .ToList();

        List<TokenModelSummary> modelSummaries = entries
            .GroupBy(entry => new ModelKey(entry.Client, entry.Provider, entry.Model))
            .Select(group => new TokenModelSummary(
                group.Key.Client,
                group.Key.Provider,
                group.Key.Model,
                group.Aggregate(TokenBreakdown.Empty, (current, entry) => current.Add(entry.Tokens)),
                group.Sum(entry => Math.Max(1, entry.MessageCount)),
                SumNullableCost(group),
                group.Count(entry => entry.Tokens.HasAnyTokens && entry.Cost is null)))
            .OrderByDescending(summary => summary.TotalTokens)
            .ThenBy(summary => summary.Client, StringComparer.OrdinalIgnoreCase)
            .ThenBy(summary => summary.Model, StringComparer.OrdinalIgnoreCase)
            .ToList();

        string status = entries.Count == 0 ? "No token usage detected" : "Token usage loaded";
        TokenUsageSnapshot snapshot = new(
            capturedAt,
            range,
            totalTokens,
            knownCost == 0 && unknownCostCount > 0 ? null : knownCost,
            unknownCostCount,
            messageCount,
            scannedFileCount,
            failedFileCount,
            clientSummaries,
            modelSummaries,
            pricing,
            status);

        if (fiveHourWindow is null)
        {
            return snapshot;
        }

        List<TokenUsageEntry> fiveHourEntries = allEntryList
            .Where(entry => string.Equals(entry.Client, "codex", StringComparison.OrdinalIgnoreCase))
            .Where(entry => entry.Timestamp >= fiveHourWindow.Start
                && entry.Timestamp < fiveHourWindow.End
                && entry.Timestamp <= capturedAt)
            .Where(entry => entry.Tokens.HasAnyTokens)
            .ToList();
        TokenBreakdown fiveHourTokens = fiveHourEntries.Aggregate(TokenBreakdown.Empty, (current, entry) => current.Add(entry.Tokens));

        return snapshot with
        {
            FiveHourWindowTokens = fiveHourTokens,
            FiveHourWindowMessageCount = fiveHourEntries.Sum(entry => Math.Max(1, entry.MessageCount)),
            FiveHourWindowStart = fiveHourWindow.Start,
            FiveHourWindowEnd = fiveHourWindow.End,
        };
    }

    private static bool IsInRange(DateTimeOffset timestamp, TokenUsageRange range, DateTimeOffset now)
    {
        DateOnly entryDate = DateOnly.FromDateTime(timestamp.ToLocalTime().DateTime);
        DateOnly today = DateOnly.FromDateTime(now.ToLocalTime().DateTime);

        return range switch
        {
            TokenUsageRange.Today => entryDate == today,
            TokenUsageRange.Last7Days => entryDate >= today.AddDays(-6) && entryDate <= today,
            TokenUsageRange.CurrentMonth => entryDate.Year == today.Year && entryDate.Month == today.Month,
            _ => true,
        };
    }

    private static decimal? SumNullableCost(IEnumerable<TokenUsageEntry> entries)
    {
        decimal total = 0;
        bool any = false;
        foreach (TokenUsageEntry entry in entries)
        {
            if (entry.Cost is not null)
            {
                any = true;
                total += entry.Cost.Value;
            }
        }

        return any ? total : null;
    }

    private sealed record ModelKey(string Client, string Provider, string Model);
}
