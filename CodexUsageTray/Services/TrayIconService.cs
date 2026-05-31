using System.Runtime.InteropServices;
using System.Drawing;
using CodexUsageTray.Core.Models;
using CodexUsageTray.Core.Services;
using Microsoft.UI.Dispatching;

namespace CodexUsageTray.Services;

internal sealed class TrayIconService : IDisposable
{
    private const int IconId = 1;
    private const int MaxTooltipLength = 127;
    private const int RefreshCommand = 1001;
    private const int OpenUsageCommand = 1002;
    private const int SettingsCommand = 1003;
    private const int ExitCommand = 1004;
    private const uint CallbackMessage = 0x8001;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NimAdd = 0x00000000;
    private const uint NimModify = 0x00000001;
    private const uint NimDelete = 0x00000002;
    private const uint NimSetVersion = 0x00000004;
    private const uint NotifyIconVersion4 = 4;
    private const uint WmContextMenu = 0x007B;
    private const uint WmLButtonDblClk = 0x0203;
    private const uint WmRButtonUp = 0x0205;
    private const uint MfString = 0x00000000;
    private const uint MfSeparator = 0x00000800;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCommand = 0x0100;

    private readonly DispatcherQueue _dispatcherQueue;
    private readonly IntPtr _windowHandle;
    private readonly Action _showWindow;
    private readonly Func<Task> _refreshAsync;
    private readonly Func<Task> _openUsagePageAsync;
    private readonly Func<Task> _showSettingsAsync;
    private readonly Action _exit;
    private readonly SubclassProcedure _subclassProcedure;
    private Icon? _currentIcon;
    private bool _disposed;

    public TrayIconService(
        DispatcherQueue dispatcherQueue,
        IntPtr windowHandle,
        Action showWindow,
        Func<Task> refreshAsync,
        Func<Task> openUsagePageAsync,
        Func<Task> showSettingsAsync,
        Action exit)
    {
        _dispatcherQueue = dispatcherQueue;
        _windowHandle = windowHandle;
        _showWindow = showWindow;
        _refreshAsync = refreshAsync;
        _openUsagePageAsync = openUsagePageAsync;
        _showSettingsAsync = showSettingsAsync;
        _exit = exit;
        _subclassProcedure = WindowProcedure;

        SetWindowSubclass(_windowHandle, _subclassProcedure, new UIntPtr(1), UIntPtr.Zero);
        SetIcon(TrayIconState.Unknown, AppText.AppName, NimAdd);
        SetNotifyIconVersion();
    }

    public void Update(UsageSnapshot snapshot)
    {
        if (_disposed)
        {
            return;
        }

        SetIcon(TrayIconStateResolver.Resolve(snapshot), BuildTooltip(snapshot), NimModify);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        NotifyIconData data = CreateNotifyIconData();
        ShellNotifyIcon(NimDelete, ref data);
        RemoveWindowSubclass(_windowHandle, _subclassProcedure, new UIntPtr(1));
        _currentIcon?.Dispose();
    }

    private void SetIcon(TrayIconState state, string tooltip, uint message)
    {
        Icon nextIcon = TrayIconFactory.Create(state);
        Icon? oldIcon = _currentIcon;
        _currentIcon = nextIcon;

        NotifyIconData data = CreateNotifyIconData();
        data.uFlags = NifMessage | NifIcon | NifTip;
        data.uCallbackMessage = CallbackMessage;
        data.hIcon = nextIcon.Handle;
        data.szTip = Truncate(tooltip);
        ShellNotifyIcon(message, ref data);

        oldIcon?.Dispose();
    }

    private void SetNotifyIconVersion()
    {
        NotifyIconData data = CreateNotifyIconData();
        data.uVersion = NotifyIconVersion4;
        ShellNotifyIcon(NimSetVersion, ref data);
    }

    private IntPtr WindowProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, UIntPtr subclassId, UIntPtr refData)
    {
        if (message == CallbackMessage)
        {
            uint eventCode = unchecked((uint)lParam.ToInt64());
            if (eventCode is WmRButtonUp or WmContextMenu)
            {
                ShowContextMenu();
                return IntPtr.Zero;
            }

            if (eventCode == WmLButtonDblClk)
            {
                Queue(_showWindow);
                return IntPtr.Zero;
            }
        }

        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        IntPtr menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            AppendMenu(menu, MfString, RefreshCommand, AppText.Refresh);
            AppendMenu(menu, MfString, OpenUsageCommand, AppText.OpenUsagePage);
            AppendMenu(menu, MfString, SettingsCommand, AppText.Settings);
            AppendMenu(menu, MfSeparator, 0, string.Empty);
            AppendMenu(menu, MfString, ExitCommand, AppText.Exit);

            GetCursorPos(out Point point);
            SetForegroundWindow(_windowHandle);
            uint command = TrackPopupMenu(menu, TpmRightButton | TpmReturnCommand, point.X, point.Y, 0, _windowHandle, IntPtr.Zero);
            DispatchMenuCommand(command);
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private void DispatchMenuCommand(uint command)
    {
        switch (command)
        {
            case RefreshCommand:
                QueueAsync(_refreshAsync);
                break;
            case OpenUsageCommand:
                QueueAsync(_openUsagePageAsync);
                break;
            case SettingsCommand:
                QueueAsync(_showSettingsAsync);
                break;
            case ExitCommand:
                Queue(_exit);
                break;
        }
    }

    private void Queue(Action action)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (!_disposed)
            {
                action();
            }
        });
    }

    private void QueueAsync(Func<Task> action)
    {
        _dispatcherQueue.TryEnqueue(async () =>
        {
            if (!_disposed)
            {
                await action();
            }
        });
    }

    private NotifyIconData CreateNotifyIconData()
    {
        return new NotifyIconData
        {
            cbSize = Marshal.SizeOf<NotifyIconData>(),
            hWnd = _windowHandle,
            uID = IconId,
        };
    }

    private static string BuildTooltip(UsageSnapshot snapshot)
    {
        if (snapshot.FiveHourLimit is not null || snapshot.WeeklyLimit is not null)
        {
            string fiveHour = snapshot.FiveHourLimit is null ? "5h --" : $"5h {snapshot.FiveHourLimit.PercentRemaining}%";
            string weekly = snapshot.WeeklyLimit is null ? "Week --" : $"Week {snapshot.WeeklyLimit.PercentRemaining}%";
            return $"Codex {fiveHour} | {weekly}";
        }

        if (snapshot.PercentRemaining is int percent)
        {
            return $"Codex {percent}% remaining";
        }

        return snapshot.Status switch
        {
            UsageStatus.LoginRequired => "Codex login required",
            UsageStatus.Unsupported => "Codex login unsupported",
            UsageStatus.LimitReached => "Codex limit reached",
            _ => "Codex usage unavailable",
        };
    }

    private static string Truncate(string text)
    {
        return text.Length <= MaxTooltipLength ? text : text[..MaxTooltipLength];
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProcedure procedure, UIntPtr subclassId, UIntPtr refData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(IntPtr hwnd, SubclassProcedure procedure, UIntPtr subclassId);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, uint newItemId, string newItem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rectangle);

    private delegate IntPtr SubclassProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, UIntPtr subclassId, UIntPtr refData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;

        public uint dwState;
        public uint dwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;

        public uint uVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;

        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }
}
