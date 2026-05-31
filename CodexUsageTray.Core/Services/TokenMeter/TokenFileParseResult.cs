using CodexUsageTray.Core.Models;

namespace CodexUsageTray.Core.Services.TokenMeter;

public sealed record TokenFileParseResult(
    IReadOnlyList<TokenUsageEntry> Entries,
    bool Succeeded,
    string? ErrorMessage = null);
