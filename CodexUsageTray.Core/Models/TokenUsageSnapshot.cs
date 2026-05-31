namespace CodexUsageTray.Core.Models;

public sealed record TokenUsageSnapshot(
    DateTimeOffset CapturedAt,
    TokenUsageRange Range,
    TokenBreakdown TotalTokens,
    decimal? KnownCost,
    int UnknownCostEntryCount,
    int MessageCount,
    int ScannedFileCount,
    int FailedFileCount,
    IReadOnlyList<TokenClientSummary> ClientSummaries,
    IReadOnlyList<TokenModelSummary> ModelSummaries,
    PricingSnapshot? Pricing,
    string Status)
{
    public TokenBreakdown FiveHourWindowTokens { get; init; } = TokenBreakdown.Empty;

    public int FiveHourWindowMessageCount { get; init; }

    public DateTimeOffset? FiveHourWindowStart { get; init; }

    public DateTimeOffset? FiveHourWindowEnd { get; init; }

    public bool HasFiveHourWindow => FiveHourWindowStart is not null && FiveHourWindowEnd is not null;

    public static TokenUsageSnapshot Empty(DateTimeOffset capturedAt, TokenUsageRange range, string status = "No token usage detected")
    {
        return new TokenUsageSnapshot(
            capturedAt,
            range,
            TokenBreakdown.Empty,
            null,
            0,
            0,
            0,
            0,
            Array.Empty<TokenClientSummary>(),
            Array.Empty<TokenModelSummary>(),
            null,
            status);
    }
}
