using CodexUsageTray.Core.Models;

namespace CodexUsageTray.Core.Services;

public static class TrayIconStateResolver
{
    public static TrayIconState Resolve(UsageSnapshot snapshot)
    {
        if (snapshot.Status is UsageStatus.Unknown or UsageStatus.LoginRequired or UsageStatus.Unsupported)
        {
            return TrayIconState.Unknown;
        }

        if (snapshot.Status == UsageStatus.LimitReached || snapshot.PercentRemaining <= 10)
        {
            return TrayIconState.Critical;
        }

        if (snapshot.PercentRemaining <= 20)
        {
            return TrayIconState.Warning;
        }

        return TrayIconState.Normal;
    }
}
