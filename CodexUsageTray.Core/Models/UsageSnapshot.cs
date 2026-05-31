namespace CodexUsageTray.Core.Models;

public sealed record UsageSnapshot(
    string RemainingText,
    string UsedText,
    string ResetText,
    int? PercentRemaining,
    DateTimeOffset CapturedAt,
    UsageStatus Status)
{
    public UsageLimitSnapshot? FiveHourLimit { get; init; }

    public UsageLimitSnapshot? WeeklyLimit { get; init; }

    public static UsageSnapshot Unknown(DateTimeOffset capturedAt, string message = "Usage unavailable")
    {
        return new UsageSnapshot(message, string.Empty, string.Empty, null, capturedAt, UsageStatus.Unknown);
    }
}
