namespace CodexUsageTray.Core.Models;

public sealed record UsageLimitSnapshot(
    string Name,
    int PercentRemaining,
    string ResetText);
