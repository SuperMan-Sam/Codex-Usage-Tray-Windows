using CodexUsageTray.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace CodexUsageTray;

public partial class App : Application
{
    private MainWindow? _window;
    private TaskbarWidgetWindow? _taskbarWidget;
    private TrayIconService? _trayIcon;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    public static new App Current => (App)Application.Current;

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        try
        {
            _window = new MainWindow();
            _trayIcon = new TrayIconService(
                DispatcherQueue.GetForCurrentThread(),
                _window.WindowHandle,
                ShowWindow,
                RefreshAsync,
                OpenUsagePageAsync,
                ShowSettingsAsync,
                ExitApplication);
            _window.MainPage.AttachTrayIcon(_trayIcon);
            _window.MainPage.SnapshotChanged += OnSnapshotChanged;
            _taskbarWidget = new TaskbarWidgetWindow(ShowWindow, ExitApplication);
            _taskbarWidget.Update(_window.MainPage.ViewModel.Snapshot);
            _taskbarWidget.ShowWidget();
            _window.Activate();
        }
        catch (Exception ex)
        {
            WriteCrashLog(ex);
            throw;
        }
    }

    private void ShowWindow()
    {
        _window?.ShowWindow();
    }

    private Task RefreshAsync()
    {
        return _window?.MainPage.RefreshAsync() ?? Task.CompletedTask;
    }

    private async Task OpenUsagePageAsync()
    {
        ShowWindow();

        if (_window is not null)
        {
            await _window.MainPage.OpenUsagePageAsync();
        }
    }

    private async Task ShowSettingsAsync()
    {
        ShowWindow();

        if (_window is not null)
        {
            await _window.MainPage.ShowSettingsDialogAsync();
        }
    }

    private void ExitApplication()
    {
        _trayIcon?.Dispose();
        _trayIcon = null;
        _taskbarWidget?.Close();
        _taskbarWidget = null;
        _window?.MainPage.Shutdown();
        _window?.CloseForExit();
        Exit();
    }

    private void OnSnapshotChanged(object? sender, CodexUsageTray.Core.Models.UsageSnapshot snapshot)
    {
        _taskbarWidget?.Update(snapshot);
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        WriteCrashLog(e.Exception);
    }

    private static void OnCurrentDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            WriteCrashLog(ex);
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteCrashLog(e.Exception);
    }

    private static void WriteCrashLog(Exception ex)
    {
        Directory.CreateDirectory(AppPaths.AppDataFolder);
        File.AppendAllText(
            Path.Combine(AppPaths.AppDataFolder, "crash.log"),
            $"{DateTimeOffset.Now:O}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
    }
}
