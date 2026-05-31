using CodexUsageTray.Core.Models;

namespace CodexUsageTray.Core.Services.TokenMeter;

public sealed record TokenUsageScanResult(
    IReadOnlyList<TokenUsageEntry> Entries,
    int ScannedFileCount,
    int FailedFileCount);
