using System.ComponentModel;
using System.Collections.ObjectModel;
using CodexUsageTray.Core.Models;
using CodexUsageTray.Services;

namespace CodexUsageTray.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private UsageSnapshot _snapshot = UsageSnapshot.Unknown(DateTimeOffset.Now, "Not refreshed yet");
    private TokenUsageSnapshot _tokenSnapshot = TokenUsageSnapshot.Empty(DateTimeOffset.Now, TokenMeterSettingsService.CurrentRange);
    private bool _isRefreshing;
    private bool _isTokenRefreshing;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string StatusText => AppText.StatusText(_snapshot.Status);

    public string RemainingText => ValueOrFallback(AppText.LocalizeKnownPageText(_snapshot.RemainingText), AppText.NoRemainingUsageDetected);

    public string FiveHourLimitText => FormatLimit(_snapshot.FiveHourLimit, AppText.NoFiveHourLimitDetected);

    public string WeeklyLimitText => FormatLimit(_snapshot.WeeklyLimit, AppText.NoWeeklyLimitDetected);

    public string UsedText => ValueOrFallback(_snapshot.UsedText, AppText.NoUsedUsageDetected);

    public string ResetText => ValueOrFallback(_snapshot.ResetText, AppText.NoResetTimeDetected);

    public string LastUpdatedText => _snapshot.CapturedAt.ToLocalTime().ToString("g", LanguageSettingsService.CurrentCulture);

    public string RefreshStateText => IsRefreshing ? AppText.Refreshing : AppText.Idle;

    public string TokenRefreshStateText => IsTokenRefreshing ? AppText.Refreshing : AppText.Idle;

    public string TokenStatusText => _tokenSnapshot.Status switch
    {
        "Token usage loaded" => AppText.TokenUsageLoaded,
        "No token usage detected" => AppText.NoTokenUsageDetected,
        _ when _tokenSnapshot.Status.StartsWith("Token usage unavailable", StringComparison.OrdinalIgnoreCase) => AppText.TokenUsageUnavailable,
        _ => _tokenSnapshot.Status,
    };

    public string TokenRangeText => AppText.TokenRangeText(TokenRange);

    public string TokenTotalText => AppText.FormatTokenCount(_tokenSnapshot.TotalTokens.Total);

    public string TokenFiveHourWindowText => _tokenSnapshot.HasFiveHourWindow
        ? AppText.FormatTokenCount(_tokenSnapshot.FiveHourWindowTokens.Total)
        : AppText.FiveHourWindowUnavailable;

    public string TokenFiveHourWindowRangeText => _tokenSnapshot.HasFiveHourWindow
        ? AppText.FormatTokenWindow(_tokenSnapshot.FiveHourWindowStart!.Value, _tokenSnapshot.FiveHourWindowEnd!.Value, _tokenSnapshot.FiveHourWindowMessageCount)
        : AppText.FiveHourWindowUnavailable;

    public string TokenCostText => AppText.FormatUsdCost(_tokenSnapshot.KnownCost);

    public string TokenBreakdownText => AppText.FormatTokenBreakdown(_tokenSnapshot.TotalTokens);

    public string TokenFilesText => AppText.FormatTokenFiles(_tokenSnapshot.ScannedFileCount, _tokenSnapshot.FailedFileCount);

    public string TokenUnknownCostText => AppText.FormatUnknownCost(_tokenSnapshot.UnknownCostEntryCount);

    public bool HasTokenUnknownCost => _tokenSnapshot.UnknownCostEntryCount > 0;

    public string TokenPricingText => _tokenSnapshot.Pricing is null
        ? AppText.FormatUsdCost(null)
        : $"{_tokenSnapshot.Pricing.Status} - {AppText.Updated(_tokenSnapshot.Pricing.CapturedAt)}";

    public string TokenLastUpdatedText => _tokenSnapshot.CapturedAt.ToLocalTime().ToString("g", LanguageSettingsService.CurrentCulture);

    public TokenUsageRange TokenRange
    {
        get => _tokenSnapshot.Range;
        set
        {
            if (_tokenSnapshot.Range == value)
            {
                return;
            }

            TokenMeterSettingsService.SetRange(value);
            _tokenSnapshot = _tokenSnapshot with { Range = value };
            RefreshTokenLocalizedProperties();
            OnPropertyChanged(nameof(TokenSnapshot));
        }
    }

    public ObservableCollection<TokenClientSummaryRow> TokenClientSummaries { get; } = [];

    public ObservableCollection<TokenModelSummaryRow> TokenModelSummaries { get; } = [];

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

    public bool IsTokenRefreshing
    {
        get => _isTokenRefreshing;
        set
        {
            if (_isTokenRefreshing == value)
            {
                return;
            }

            _isTokenRefreshing = value;
            OnPropertyChanged(nameof(IsTokenRefreshing));
            OnPropertyChanged(nameof(TokenRefreshStateText));
        }
    }

    public UsageSnapshot Snapshot => _snapshot;

    public TokenUsageSnapshot TokenSnapshot => _tokenSnapshot;

    public void ApplySnapshot(UsageSnapshot snapshot)
    {
        _snapshot = snapshot;
        RefreshLocalizedProperties();
        OnPropertyChanged(nameof(Snapshot));
    }

    public void ApplyTokenSnapshot(TokenUsageSnapshot snapshot)
    {
        _tokenSnapshot = snapshot;
        ReplaceRows(TokenClientSummaries, snapshot.ClientSummaries.Select(TokenClientSummaryRow.FromSummary));
        ReplaceRows(TokenModelSummaries, snapshot.ModelSummaries.Select(TokenModelSummaryRow.FromSummary));
        RefreshTokenLocalizedProperties();
        OnPropertyChanged(nameof(TokenSnapshot));
    }

    public void RefreshLocalization()
    {
        RefreshLocalizedProperties();
    }

    private void RefreshLocalizedProperties()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(RemainingText));
        OnPropertyChanged(nameof(FiveHourLimitText));
        OnPropertyChanged(nameof(WeeklyLimitText));
        OnPropertyChanged(nameof(UsedText));
        OnPropertyChanged(nameof(ResetText));
        OnPropertyChanged(nameof(LastUpdatedText));
        OnPropertyChanged(nameof(RefreshStateText));
        RefreshTokenLocalizedProperties();
    }

    private void RefreshTokenLocalizedProperties()
    {
        OnPropertyChanged(nameof(TokenRefreshStateText));
        OnPropertyChanged(nameof(TokenStatusText));
        OnPropertyChanged(nameof(TokenRangeText));
        OnPropertyChanged(nameof(TokenTotalText));
        OnPropertyChanged(nameof(TokenFiveHourWindowText));
        OnPropertyChanged(nameof(TokenFiveHourWindowRangeText));
        OnPropertyChanged(nameof(TokenCostText));
        OnPropertyChanged(nameof(TokenBreakdownText));
        OnPropertyChanged(nameof(TokenFilesText));
        OnPropertyChanged(nameof(TokenUnknownCostText));
        OnPropertyChanged(nameof(HasTokenUnknownCost));
        OnPropertyChanged(nameof(TokenPricingText));
        OnPropertyChanged(nameof(TokenLastUpdatedText));

        foreach (TokenClientSummaryRow row in TokenClientSummaries)
        {
            row.RefreshLocalization();
        }

        foreach (TokenModelSummaryRow row in TokenModelSummaries)
        {
            row.RefreshLocalization();
        }
    }

    private static void ReplaceRows<T>(ObservableCollection<T> rows, IEnumerable<T> values)
    {
        rows.Clear();
        foreach (T value in values)
        {
            rows.Add(value);
        }
    }

    private static string FormatLimit(UsageLimitSnapshot? limit, string fallback)
    {
        if (limit is null)
        {
            return fallback;
        }

        string text = AppText.FormatRemainingPercent(limit.PercentRemaining);
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

    public sealed class TokenClientSummaryRow : INotifyPropertyChanged
    {
        private readonly TokenClientSummary _summary;

        private TokenClientSummaryRow(TokenClientSummary summary)
        {
            _summary = summary;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Client => _summary.Client;

        public string TotalTokens => AppText.FormatTokenCount(_summary.TotalTokens);

        public string Messages => AppText.FormatCount(_summary.MessageCount);

        public string Cost => AppText.FormatUsdCost(_summary.Cost);

        public string Breakdown => AppText.FormatTokenBreakdown(_summary.Tokens);

        public static TokenClientSummaryRow FromSummary(TokenClientSummary summary)
        {
            return new TokenClientSummaryRow(summary);
        }

        public void RefreshLocalization()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalTokens)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Messages)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Cost)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Breakdown)));
        }
    }

    public sealed class TokenModelSummaryRow : INotifyPropertyChanged
    {
        private readonly TokenModelSummary _summary;

        private TokenModelSummaryRow(TokenModelSummary summary)
        {
            _summary = summary;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Client => _summary.Client;

        public string Provider => string.IsNullOrWhiteSpace(_summary.Provider) ? "-" : _summary.Provider;

        public string Model => _summary.Model;

        public string TotalTokens => AppText.FormatTokenCount(_summary.TotalTokens);

        public string Messages => AppText.FormatCount(_summary.MessageCount);

        public string Cost => AppText.FormatUsdCost(_summary.Cost);

        public string Breakdown => AppText.FormatTokenBreakdown(_summary.Tokens);

        public static TokenModelSummaryRow FromSummary(TokenModelSummary summary)
        {
            return new TokenModelSummaryRow(summary);
        }

        public void RefreshLocalization()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalTokens)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Messages)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Cost)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Breakdown)));
        }
    }
}
