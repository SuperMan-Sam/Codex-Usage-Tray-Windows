namespace CodexUsageTray.Core.Models;

public sealed record TokenUsageEntry(
    string Client,
    string Model,
    string Provider,
    string SessionId,
    string? WorkspaceKey,
    string? WorkspaceLabel,
    DateTimeOffset Timestamp,
    TokenBreakdown Tokens,
    int MessageCount,
    string? Agent,
    decimal? Cost,
    string? CostSource,
    string? SourcePath);
