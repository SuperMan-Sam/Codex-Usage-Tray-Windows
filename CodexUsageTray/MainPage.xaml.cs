using System.Text.Json;
using CodexUsageTray.Core.Models;
using CodexUsageTray.Core.Services;
using CodexUsageTray.Core.Services.TokenMeter;
using CodexUsageTray.Services;
using CodexUsageTray.ViewModels;
using CodexUsageTray.Widgets;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace CodexUsageTray;

public sealed partial class MainPage : Page
{
    private static readonly TimeSpan UsageRenderTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan UsageRenderPollInterval = TimeSpan.FromSeconds(1);

    private readonly UsagePageParser _parser = new();
    private readonly TokenUsageMeter _tokenUsageMeter = new();
    private readonly DispatcherQueueTimer _refreshTimer;
    private TrayIconService? _trayIcon;
    private bool _isRefreshInProgress;
    private bool _isWebViewReady;

    public MainPage()
    {
        InitializeComponent();

        _refreshTimer = DispatcherQueue.CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromMinutes(1);
        _refreshTimer.Tick += OnRefreshTimerTick;

        Loaded += OnLoaded;
        LanguageSettingsService.LanguageChanged += OnLanguageChanged;
        ApplyLocalization();
    }

    public MainViewModel ViewModel { get; } = new();

    public event EventHandler<UsageSnapshot>? SnapshotChanged;

    internal void AttachTrayIcon(TrayIconService trayIcon)
    {
        _trayIcon = trayIcon;
        _trayIcon.Update(ViewModel.Snapshot);
    }

    public async Task RefreshAsync()
    {
        if (_isRefreshInProgress)
        {
            return;
        }

        _isRefreshInProgress = true;
        ViewModel.IsRefreshing = true;
        ViewModel.IsTokenRefreshing = true;

        try
        {
            await EnsureWebViewAsync();
            bool loaded = await NavigateToUsagePageAsync();
            if (!loaded)
            {
                UsageSnapshot unsupportedSnapshot = new("Login unsupported", string.Empty, string.Empty, null, DateTimeOffset.Now, UsageStatus.Unsupported);
                ApplySnapshot(unsupportedSnapshot);
                ApplyTokenSnapshot(await RefreshTokenUsageCoreAsync(unsupportedSnapshot));
                return;
            }

            UsageSnapshot usageSnapshot = await CaptureUsageSnapshotAsync();
            ApplySnapshot(usageSnapshot);
            ApplyTokenSnapshot(await RefreshTokenUsageCoreAsync(usageSnapshot));
        }
        catch (Exception)
        {
            UsageSnapshot unsupportedSnapshot = new("Login unsupported", string.Empty, string.Empty, null, DateTimeOffset.Now, UsageStatus.Unsupported);
            ApplySnapshot(unsupportedSnapshot);
            ApplyTokenSnapshot(await RefreshTokenUsageCoreAsync(unsupportedSnapshot));
        }
        finally
        {
            ViewModel.IsRefreshing = false;
            ViewModel.IsTokenRefreshing = false;
            _isRefreshInProgress = false;
        }
    }

    public async Task OpenUsagePageAsync()
    {
        ContentTabs.SelectedItem = UsagePageTab;
        ResizeUsageWebViewToHost();
        await EnsureWebViewAsync();
        UsageWebView.CoreWebView2.Navigate(AppText.UsageUrl);
    }

    public async Task ShowSettingsDialogAsync()
    {
        CheckBox startupCheckBox = new()
        {
            Content = AppText.StartWithWindows,
            IsChecked = StartupService.IsEnabled(),
        };
        startupCheckBox.Checked += (_, _) => StartupService.SetEnabled(true);
        startupCheckBox.Unchecked += (_, _) => StartupService.SetEnabled(false);

        List<LanguageOption> languageOptions = CreateLanguageOptions();
        ComboBox languageComboBox = new()
        {
            ItemsSource = languageOptions,
            DisplayMemberPath = nameof(LanguageOption.DisplayName),
            SelectedItem = languageOptions.First(option => option.Mode == LanguageSettingsService.CurrentMode),
            MinWidth = 220,
        };
        languageComboBox.SelectionChanged += (_, _) =>
        {
            if (languageComboBox.SelectedItem is LanguageOption option)
            {
                LanguageSettingsService.SetMode(option.Mode);
            }
        };

        StackPanel languagePanel = new()
        {
            Spacing = 4,
        };
        languagePanel.Children.Add(new TextBlock { Text = AppText.Language });
        languagePanel.Children.Add(languageComboBox);

        TextBlock fontSizeValueText = new()
        {
            Text = AppText.FontSizeValue(LanguageSettingsService.TaskbarFontSize),
        };
        Slider fontSizeSlider = new()
        {
            Minimum = LanguageSettingsService.MinimumTaskbarFontSize,
            Maximum = LanguageSettingsService.MaximumTaskbarFontSize,
            StepFrequency = 0.5,
            Value = LanguageSettingsService.TaskbarFontSize,
            SmallChange = 0.5,
            LargeChange = 1.0,
            Width = 220,
        };
        fontSizeSlider.ValueChanged += (_, args) =>
        {
            double fontSize = Math.Round(args.NewValue * 2, MidpointRounding.AwayFromZero) / 2;
            fontSizeValueText.Text = AppText.FontSizeValue(fontSize);
            LanguageSettingsService.SetTaskbarFontSize(fontSize);
        };

        StackPanel fontSizePanel = new()
        {
            Spacing = 4,
        };
        fontSizePanel.Children.Add(new TextBlock { Text = AppText.TaskbarFontSize });
        fontSizePanel.Children.Add(fontSizeSlider);
        fontSizePanel.Children.Add(fontSizeValueText);

        StackPanel content = new()
        {
            Spacing = 8,
        };
        content.Children.Add(languagePanel);
        content.Children.Add(fontSizePanel);
        content.Children.Add(startupCheckBox);
        content.Children.Add(new TextBlock { Text = AppText.RefreshIntervalOneMinute });
        content.Children.Add(new TextBlock { Text = $"{AppText.WebViewData}: {ViewModel.WebViewDataFolder}", TextWrapping = TextWrapping.WrapWholeWords });
        content.Children.Add(new TextBlock { Text = AppText.NoCredentialFiles, TextWrapping = TextWrapping.WrapWholeWords });

        ContentDialog dialog = new()
        {
            Title = AppText.Settings,
            Content = content,
            CloseButtonText = AppText.Close,
            XamlRoot = XamlRoot,
        };

        await dialog.ShowAsync();
    }

    public void Shutdown()
    {
        _refreshTimer.Stop();
        LanguageSettingsService.LanguageChanged -= OnLanguageChanged;
    }

    private void ApplyLocalization()
    {
        RefreshButton.Label = AppText.Refresh;
        AutomationProperties.SetName(RefreshButton, AppText.RefreshUsageAutomationName);
        OpenUsagePageButton.Label = AppText.OpenUsagePage;
        AutomationProperties.SetName(OpenUsagePageButton, AppText.OpenUsagePageAutomationName);
        SettingsButton.Label = AppText.Settings;
        AutomationProperties.SetName(SettingsButton, AppText.OpenSettingsAutomationName);

        PageTitleText.Text = AppText.CodexUsageTitle;
        StatusLabel.Text = AppText.Status;
        RemainingLabel.Text = AppText.Remaining;
        FiveHourLimitLabel.Text = AppText.FiveHourLimit;
        WeeklyLimitLabel.Text = AppText.WeeklyLimit;
        UsedLabel.Text = AppText.Used;
        ResetLabel.Text = AppText.Reset;
        LastUpdatedLabel.Text = AppText.LastUpdated;
        AutomationProperties.SetName(UsageWebView, AppText.OpenUsagePageAutomationName);
        UsagePageTab.Header = AppText.OpenUsagePage;
        TokenMeterTab.Header = AppText.TokenMeter;
        TokenMeterTitleText.Text = AppText.TokenMeter;
        TokenRangeLabel.Text = AppText.TokenRange;
        TotalTokensLabel.Text = AppText.TotalTokens;
        TokenCostLabel.Text = AppText.TokenCost;
        FiveHourWindowTokensLabel.Text = AppText.FiveHourWindowTokens;
        TokenBreakdownLabel.Text = AppText.TokenBreakdown;
        TokenClientsLabel.Text = AppText.TokenClients;
        TokenMetadataLabel.Text = AppText.TokenPricing;
        TokenModelsLabel.Text = AppText.TokenModels;
        ApplyTokenRangeComboText();

        ViewModel.RefreshLocalization();
        _trayIcon?.Update(ViewModel.Snapshot);
        CodexUsageWidgetProvider.UpdateAllFromSnapshot(ViewModel.Snapshot);
    }

    private void OnLanguageChanged(object? sender, EventArgs args)
    {
        DispatcherQueue.TryEnqueue(ApplyLocalization);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        ContentTabs.SelectedItem = UsagePageTab;
        ResizeUsageWebViewToHost();
        await RefreshAsync();
        ContentTabs.SelectedItem = UsagePageTab;
        ResizeUsageWebViewToHost();
        _refreshTimer.Start();
    }

    private async void OnRefreshTimerTick(DispatcherQueueTimer sender, object args)
    {
        await RefreshAsync();
    }

    private async void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
    }

    private async void OnOpenUsagePageClicked(object sender, RoutedEventArgs e)
    {
        await OpenUsagePageAsync();
    }

    private async void OnSettingsClicked(object sender, RoutedEventArgs e)
    {
        await ShowSettingsDialogAsync();
    }

    private void OnUsageWebViewHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ResizeUsageWebViewToHost();
    }

    private void OnContentTabsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(ContentTabs.SelectedItem as TabViewItem, UsagePageTab))
        {
            ResizeUsageWebViewToHost();
        }
    }

    private async void OnTokenRangeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TokenRangeComboBox.SelectedItem is not ComboBoxItem item
            || item.Tag is not string rawRange
            || !Enum.TryParse(rawRange, out TokenUsageRange range)
            || ViewModel.TokenRange == range)
        {
            return;
        }

        ViewModel.TokenRange = range;
        TokenMeterSettingsService.SetRange(range);
        ViewModel.IsTokenRefreshing = true;
        try
        {
            ApplyTokenSnapshot(await RefreshTokenUsageCoreAsync(ViewModel.Snapshot));
        }
        finally
        {
            ViewModel.IsTokenRefreshing = false;
        }
    }

    private async Task EnsureWebViewAsync()
    {
        if (_isWebViewReady)
        {
            return;
        }

        Directory.CreateDirectory(ViewModel.WebViewDataFolder);
        CoreWebView2Environment environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, ViewModel.WebViewDataFolder, null);
        await UsageWebView.EnsureCoreWebView2Async(environment);
        UsageWebView.CoreWebView2.NavigationCompleted += OnCoreNavigationCompleted;
        _isWebViewReady = true;
        ResizeUsageWebViewToHost();
    }

    private void ResizeUsageWebViewToHost()
    {
        double width = UsageWebViewHost.ActualWidth;
        double height = UsageWebViewHost.ActualHeight;
        if (width > 1)
        {
            UsageWebView.Width = width;
        }

        if (height > 1)
        {
            UsageWebView.Height = height;
        }
    }

    private async Task<bool> NavigateToUsagePageAsync()
    {
        TaskCompletionSource<bool> navigationCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            UsageWebView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
            navigationCompletion.TrySetResult(args.IsSuccess);
        }

        UsageWebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
        UsageWebView.CoreWebView2.Navigate(AppText.UsageUrl);

        try
        {
            return await navigationCompletion.Task.WaitAsync(TimeSpan.FromSeconds(45));
        }
        catch (TimeoutException)
        {
            UsageWebView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
            return false;
        }
    }

    private async void OnCoreNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (_isRefreshInProgress || !args.IsSuccess)
        {
            return;
        }

        ApplySnapshot(await CaptureUsageSnapshotAsync());
    }

    private async Task<UsageSnapshot> CaptureUsageSnapshotAsync()
    {
        DateTimeOffset startedAt = DateTimeOffset.Now;
        UsageSnapshot latestSnapshot = UsageSnapshot.Unknown(startedAt);
        UsageSnapshot bestSnapshot = latestSnapshot;

        while (DateTimeOffset.Now - startedAt < UsageRenderTimeout)
        {
            string visibleText = await ReadVisiblePageTextAsync();
            latestSnapshot = _parser.Parse(visibleText, DateTimeOffset.Now);
            if (IsBetterSnapshot(latestSnapshot, bestSnapshot))
            {
                bestSnapshot = latestSnapshot;
            }

            if (IsReadySnapshot(latestSnapshot, visibleText))
            {
                return latestSnapshot;
            }

            await Task.Delay(UsageRenderPollInterval);
        }

        return bestSnapshot.Status == UsageStatus.Unknown ? latestSnapshot : bestSnapshot;
    }

    private static bool IsReadySnapshot(UsageSnapshot snapshot, string visibleText)
    {
        return snapshot.Status switch
        {
            UsageStatus.Available => snapshot.PercentRemaining is not null
                || !string.IsNullOrWhiteSpace(snapshot.RemainingText)
                || !string.IsNullOrWhiteSpace(snapshot.ResetText),
            UsageStatus.LimitReached => HasExplicitLimitReachedText(visibleText),
            UsageStatus.LoginRequired or UsageStatus.Unsupported => true,
            _ => false,
        };
    }

    private static bool IsBetterSnapshot(UsageSnapshot candidate, UsageSnapshot current)
    {
        return SnapshotScore(candidate) > SnapshotScore(current);
    }

    private static int SnapshotScore(UsageSnapshot snapshot)
    {
        int score = snapshot.Status switch
        {
            UsageStatus.Available => 60,
            UsageStatus.LoginRequired or UsageStatus.Unsupported => 50,
            UsageStatus.LimitReached => 30,
            _ => 0,
        };

        if (snapshot.PercentRemaining is not null)
        {
            score += 20;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.ResetText))
        {
            score += 10;
        }

        return score;
    }

    private static bool HasExplicitLimitReachedText(string visibleText)
    {
        return visibleText.Contains("limit reached", StringComparison.OrdinalIgnoreCase)
            || visibleText.Contains("reached your codex limit", StringComparison.OrdinalIgnoreCase)
            || visibleText.Contains("reached the codex limit", StringComparison.OrdinalIgnoreCase)
            || visibleText.Contains("额度已用完", StringComparison.Ordinal)
            || visibleText.Contains("达到限制", StringComparison.Ordinal)
            || visibleText.Contains("已达限制", StringComparison.Ordinal)
            || visibleText.Contains("达到限额", StringComparison.Ordinal)
            || visibleText.Contains("已达限额", StringComparison.Ordinal);
    }

    private async Task<string> ReadVisiblePageTextAsync()
    {
        string scriptResult = await UsageWebView.ExecuteScriptAsync(
            """
            (() => {
                const values = new Set();
                const add = (value) => {
                    if (typeof value !== 'string') {
                        return;
                    }

                    const text = value.replace(/\s+/g, ' ').trim();
                    if (text.length > 0) {
                        values.add(text);
                    }
                };

                add(document.title);

                if (document.body) {
                    add(document.body.innerText || document.body.textContent || '');
                    const isVisible = (element) => {
                        if (!element || typeof element.getBoundingClientRect !== 'function') {
                            return false;
                        }

                        const style = getComputedStyle(element);
                        if (style.display === 'none' || style.visibility === 'hidden' || style.opacity === '0') {
                            return false;
                        }

                        const rect = element.getBoundingClientRect();
                        return rect.width > 0 && rect.height > 0;
                    };
                    const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_ELEMENT);
                    let node = walker.currentNode;
                    while (node) {
                        if (isVisible(node)) {
                            add(node.getAttribute && node.getAttribute('aria-label'));
                            add(node.getAttribute && node.getAttribute('title'));
                        }

                        if (node.shadowRoot && isVisible(node)) {
                            add(node.shadowRoot.textContent || '');
                        }

                        node = walker.nextNode();
                    }
                }

                return Array.from(values).join('\n').slice(0, 30000);
            })();
            """);
        string visibleText = JsonSerializer.Deserialize<string>(scriptResult) ?? string.Empty;
        return visibleText;
    }

    private void ApplySnapshot(UsageSnapshot snapshot)
    {
        ViewModel.ApplySnapshot(snapshot);
        _trayIcon?.Update(snapshot);
        SnapshotChanged?.Invoke(this, snapshot);
    }

    private void ApplyTokenSnapshot(TokenUsageSnapshot snapshot)
    {
        ViewModel.ApplyTokenSnapshot(snapshot);
        SelectTokenRange(snapshot.Range);
    }

    private Task<TokenUsageSnapshot> RefreshTokenUsageCoreAsync(UsageSnapshot? usageSnapshot = null)
    {
        TokenUsageWindow? fiveHourWindow = UsageResetTimeFormatter.TryGetFiveHourWindow(usageSnapshot ?? ViewModel.Snapshot, out TokenUsageWindow resolvedWindow)
            ? resolvedWindow
            : null;
        return _tokenUsageMeter.RefreshAsync(ViewModel.TokenRange, AppPaths.PricingCacheFolder, fiveHourWindow);
    }

    private void SelectTokenRange(TokenUsageRange range)
    {
        foreach (object item in TokenRangeComboBox.Items)
        {
            if (item is ComboBoxItem comboBoxItem
                && comboBoxItem.Tag is string rawRange
                && Enum.TryParse(rawRange, out TokenUsageRange itemRange)
                && itemRange == range)
            {
                TokenRangeComboBox.SelectedItem = comboBoxItem;
                return;
            }
        }
    }

    private void ApplyTokenRangeComboText()
    {
        foreach (object item in TokenRangeComboBox.Items)
        {
            if (item is ComboBoxItem comboBoxItem
                && comboBoxItem.Tag is string rawRange
                && Enum.TryParse(rawRange, out TokenUsageRange range))
            {
                comboBoxItem.Content = AppText.TokenRangeText(range);
            }
        }

        SelectTokenRange(ViewModel.TokenRange);
    }

    private static List<LanguageOption> CreateLanguageOptions()
    {
        return
        [
            new(LanguageMode.System, AppText.FollowSystemLanguage),
            new(LanguageMode.ChineseSimplified, AppText.SimplifiedChinese),
            new(LanguageMode.English, AppText.English),
        ];
    }

    private sealed record LanguageOption(LanguageMode Mode, string DisplayName);
}
