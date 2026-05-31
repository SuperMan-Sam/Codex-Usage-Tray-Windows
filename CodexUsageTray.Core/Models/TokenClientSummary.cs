namespace CodexUsageTray.Core.Models;

public sealed record TokenClientSummary(
    string Client,
    TokenBreakdown Tokens,
    int MessageCount,
    decimal? Cost,
    int UnknownCostEntryCount)
{
    public long TotalTokens => Tokens.Total;
}
