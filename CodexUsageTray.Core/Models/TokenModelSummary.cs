namespace CodexUsageTray.Core.Models;

public sealed record TokenModelSummary(
    string Client,
    string Provider,
    string Model,
    TokenBreakdown Tokens,
    int MessageCount,
    decimal? Cost,
    int UnknownCostEntryCount)
{
    public long TotalTokens => Tokens.Total;
}
