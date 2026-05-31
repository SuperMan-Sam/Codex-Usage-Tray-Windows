using System.ComponentModel;
using System.Globalization;
using CodexUsageTray.Core.Models;

namespace CodexUsageTray.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private UsageSnapshot _snapshot = UsageSnapshot.Unknown(DateTimeOffset.Now, "Not refreshed yet");
    private bool _isRefreshing;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string StatusText => _snapshot.Status switch
    {
        UsageStatus.Available => "Usage loaded",
        UsageStatus.LoginRequired => "Login required",
        UsageStatus.LimitReached => "Limit reached",
        UsageStatus.Unsupported => "Login unsupported",
        _ => "Usage unavailable",
    };

    public string RemainingText => ValueOrFallback(_snapshot.RemainingText, "No remaining usage detected");

    public string FiveHourLimitText => FormatLimit(_snapshot.FiveHourLimit, "No 5h limit detected");

    public string WeeklyLimitText => FormatLimit(_snapshot.WeeklyLimit, "No weekly limit detected");

    public string UsedText => ValueOrFallback(_snapshot.UsedText, "No used usage detected");

    public string ResetText => ValueOrFallback(_snapshot.ResetText, "No reset time detected");

    public string LastUpdatedText => _snapshot.CapturedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    public string RefreshStateText => IsRefreshing ? "Refreshing..." : "Idle";

    public string WebViewDataFolder => AppPaths.WebViewUserDataFolder;

    public bool IsRefreshing
    {
        get => _isRefreshing;
        set
        {
            if (_isRefreshing == value)
            {
                return;
            }

            _isRefreshing = value;
            OnPropertyChanged(nameof(IsRefreshing));
            OnPropertyChanged(nameof(RefreshStateText));
        }
    }

    public UsageSnapshot Snapshot => _snapshot;

    public void ApplySnapshot(UsageSnapshot snapshot)
    {
        _snapshot = snapshot;
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(RemainingText));
        OnPropertyChanged(nameof(FiveHourLimitText));
        OnPropertyChanged(nameof(WeeklyLimitText));
        OnPropertyChanged(nameof(UsedText));
        OnPropertyChanged(nameof(ResetText));
        OnPropertyChanged(nameof(LastUpdatedText));
        OnPropertyChanged(nameof(Snapshot));
    }

    private static string FormatLimit(UsageLimitSnapshot? limit, string fallback)
    {
        if (limit is null)
        {
            return fallback;
        }

        string text = string.Create(CultureInfo.InvariantCulture, $"{limit.PercentRemaining}% remaining");
        return string.IsNullOrWhiteSpace(limit.ResetText) ? text : $"{text} - {limit.ResetText}";
    }

    private static string ValueOrFallback(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
