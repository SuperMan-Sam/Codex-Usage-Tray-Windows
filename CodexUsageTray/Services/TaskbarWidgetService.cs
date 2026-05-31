using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using CodexUsageTray.Core.Models;
using CodexUsageTray.Core.Services;
using Microsoft.UI.Dispatching;

namespace CodexUsageTray.Services;

internal sealed class TaskbarWidgetService : IDisposable
{
    private const string WindowClassName = "CodexUsageTray.TaskbarWidget";
    private const int RefreshCommand = 2001;
    private const int OpenUsageCommand = 2002;
    private const int SettingsCommand = 2003;
    private const int ExitCommand = 2004;
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsExTopmost = 0x00000008;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExLayered = 0x00080000;
    private const int WsExNoActivate = 0x08000000;
    private const int WmPaint = 0x000F;
    private const int WmEraseBackground = 0x0014;
    private const int WmDisplayChange = 0x007E;
    private const int WmSettingChange = 0x001A;
    private const int WmRButtonUp = 0x0205;
    private const int WmLButtonDblClk = 0x0203;
    private const int SwHide = 0;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const uint MfString = 0x00000000;
    private const uint MfSeparator = 0x00000800;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCommand = 0x0100;
    private const uint AbmGetTaskbarPos = 0x00000005;
    private const uint UlwAlpha = 0x00000002;
    private const byte AcSrcOver = 0x00;
    private const byte AcSrcAlpha = 0x01;

    private static readonly ConcurrentDictionary<IntPtr, TaskbarWidgetService> Instances = new();
    private static readonly WindowProcedureDelegate SharedWindowProcedure = HandleWindowMessage;
    private static ushort _windowClass;

    private readonly DispatcherQueue _dispatcherQueue;
    private readonly Action _showWindow;
    private readonly Func<Task> _refreshAsync;
    private readonly Func<Task> _openUsagePageAsync;
    private readonly Func<Task> _showSettingsAsync;
    private readonly Action _exit;
    private readonly DispatcherQueueTimer _positionTimer;
    private UsageSnapshot _snapshot = UsageSnapshot.Unknown(DateTimeOffset.Now);
    private IntPtr _windowHandle;
    private bool _disposed;
    private int _lastWidth;
    private int _lastHeight;

    public TaskbarWidgetService(
        DispatcherQueue dispatcherQueue,
        Action showWindow,
        Func<Task> refreshAsync,
        Func<Task> openUsagePageAsync,
        Func<Task> showSettingsAsync,
        Action exit)
    {
        _dispatcherQueue = dispatcherQueue;
        _showWindow = showWindow;
        _refreshAsync = refreshAsync;
        _openUsagePageAsync = openUsagePageAsync;
        _showSettingsAsync = showSettingsAsync;
        _exit = exit;

        EnsureWindowClass();
        CreateWindow();
        LanguageSettingsService.LanguageChanged += OnLanguageChanged;
        LanguageSettingsService.TaskbarFontSizeChanged += OnTaskbarFontSizeChanged;

        _positionTimer = dispatcherQueue.CreateTimer();
        _positionTimer.Interval = TimeSpan.FromSeconds(3);
        _positionTimer.Tick += OnPositionTimerTick;
        _positionTimer.Start();

        PositionWindow();
    }

    public void Update(UsageSnapshot snapshot)
    {
        if (_disposed)
        {
            return;
        }

        _snapshot = snapshot;
        PositionWindow();
        Invalidate();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        LanguageSettingsService.LanguageChanged -= OnLanguageChanged;
        LanguageSettingsService.TaskbarFontSizeChanged -= OnTaskbarFontSizeChanged;
        _positionTimer.Stop();

        if (_windowHandle != IntPtr.Zero)
        {
            Instances.TryRemove(_windowHandle, out _);
            DestroyWindow(_windowHandle);
            _windowHandle = IntPtr.Zero;
        }
    }

    private static void EnsureWindowClass()
    {
        if (_windowClass != 0)
        {
            return;
        }

        IntPtr instance = GetModuleHandle(null);
        WndClassEx windowClass = new()
        {
            cbSize = Marshal.SizeOf<WndClassEx>(),
            style = 0x0002 | 0x0001,
            lpfnWndProc = SharedWindowProcedure,
            hInstance = instance,
            hCursor = LoadCursor(IntPtr.Zero, new IntPtr(32512)),
            lpszClassName = WindowClassName,
        };

        _windowClass = RegisterClassEx(ref windowClass);
        if (_windowClass == 0)
        {
            throw new InvalidOperationException("Could not register taskbar widget window class.");
        }
    }

    private void CreateWindow()
    {
        IntPtr taskbarHandle = FindTaskbar();
        if (taskbarHandle == IntPtr.Zero)
        {
            return;
        }

        _windowHandle = CreateWindowEx(
            WsExTopmost | WsExToolWindow | WsExLayered | WsExNoActivate,
            WindowClassName,
            AppText.AppName,
            WsPopup,
            0,
            0,
            1,
            1,
            IntPtr.Zero,
            IntPtr.Zero,
            GetModuleHandle(null),
            IntPtr.Zero);

        if (_windowHandle == IntPtr.Zero)
        {
            return;
        }

        Instances[_windowHandle] = this;
    }

    private void PositionWindow()
    {
        if (_disposed)
        {
            return;
        }

        if (_windowHandle == IntPtr.Zero)
        {
            CreateWindow();
            if (_windowHandle == IntPtr.Zero)
            {
                return;
            }
        }

        IntPtr taskbarHandle = FindTaskbar();
        if (taskbarHandle == IntPtr.Zero)
        {
            ShowWindow(_windowHandle, SwHide);
            return;
        }

        if (!TryGetTaskbarRect(taskbarHandle, out Rect taskbarRect))
        {
            ShowWindow(_windowHandle, SwHide);
            return;
        }

        int taskbarHeight = taskbarRect.Bottom - taskbarRect.Top;
        if (taskbarHeight <= 0)
        {
            ShowWindow(_windowHandle, SwHide);
            return;
        }

        int trayLeft = taskbarRect.Right;
        IntPtr trayHandle = FindChildWindow(taskbarHandle, "TrayNotifyWnd");
        if (trayHandle != IntPtr.Zero && GetWindowRect(trayHandle, out Rect trayRect))
        {
            trayLeft = trayRect.Left;
        }

        int taskListRight = taskbarRect.Left;
        foreach (string className in new[] { "MSTaskSwWClass", "ReBarWindow32" })
        {
            IntPtr taskListHandle = FindChildWindow(taskbarHandle, className);
            if (taskListHandle != IntPtr.Zero
                && IsWindowVisible(taskListHandle)
                && GetWindowRect(taskListHandle, out Rect taskListRect))
            {
                taskListRight = Math.Max(taskListRight, taskListRect.Right);
            }
        }

        int margin = Math.Max(4, taskbarHeight / 8);
        int availableLeft = taskListRight + margin;
        int availableRight = trayLeft - margin;
        int availableWidth = availableRight - availableLeft;
        int maxHeight = Math.Max(20, taskbarHeight - (margin * 2));
        double taskbarFontSize = LanguageSettingsService.TaskbarFontSize;
        int desiredHeight = Math.Min(maxHeight, Math.Max(24, (int)Math.Ceiling((taskbarFontSize * 2) + 8)));
        int desiredWidth = Math.Clamp(MeasureDesiredWidth(taskbarFontSize), 52, 130);

        if (availableWidth < desiredWidth + 8)
        {
            ShowWindow(_windowHandle, SwHide);
            return;
        }

        int width = Math.Min(desiredWidth, availableWidth);
        int height = desiredHeight;
        int screenX = availableRight - width;
        int screenY = taskbarRect.Top + ((taskbarHeight - height) / 2);

        _lastWidth = width;
        _lastHeight = height;
        SetWindowPos(_windowHandle, new IntPtr(-1), screenX, screenY, width, height, SwpShowWindow | SwpNoActivate);
        RenderLayered(screenX, screenY, width, height);
    }

    private static bool TryGetTaskbarRect(IntPtr taskbarHandle, out Rect rect)
    {
        AppBarData data = new()
        {
            cbSize = Marshal.SizeOf<AppBarData>(),
            hWnd = taskbarHandle,
        };

        if (SHAppBarMessage(AbmGetTaskbarPos, ref data) != IntPtr.Zero)
        {
            rect = data.rc;
            return true;
        }

        return GetWindowRect(taskbarHandle, out rect);
    }

    private void Paint(IntPtr deviceContext)
    {
        int width = Math.Max(_lastWidth, 1);
        int height = Math.Max(_lastHeight, 1);

        using Graphics graphics = Graphics.FromHdc(deviceContext);
        DrawWidget(graphics, width, height);
    }

    private void RenderLayered(int x, int y, int width, int height)
    {
        if (_windowHandle == IntPtr.Zero || width <= 0 || height <= 0)
        {
            return;
        }

        using Bitmap bitmap = new(width, height, PixelFormat.Format32bppPArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            DrawWidget(graphics, width, height);
        }

        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memoryDc = CreateCompatibleDC(screenDc);
        IntPtr bitmapHandle = bitmap.GetHbitmap(Color.FromArgb(0));
        IntPtr oldObject = SelectObject(memoryDc, bitmapHandle);

        try
        {
            Point destination = new() { X = x, Y = y };
            Size size = new() { Width = width, Height = height };
            Point source = new() { X = 0, Y = 0 };
            BlendFunction blend = new()
            {
                BlendOp = AcSrcOver,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = AcSrcAlpha,
            };

            if (!UpdateLayeredWindow(_windowHandle, screenDc, ref destination, ref size, memoryDc, ref source, 0, ref blend, UlwAlpha))
            {
                WriteDebugLog($"UpdateLayeredWindow failed: {Marshal.GetLastWin32Error()}");
            }
        }
        finally
        {
            SelectObject(memoryDc, oldObject);
            DeleteObject(bitmapHandle);
            DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private void DrawWidget(Graphics graphics, int width, int height)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        graphics.Clear(Color.Transparent);

        using SolidBrush backgroundBrush = new(Color.FromArgb(238, 31, 31, 31));
        graphics.FillRoundedRectangle(backgroundBrush, new Rectangle(0, 0, width, height), 7);

        DrawStackedUsage(graphics, width, height);
    }

    private void DrawStackedUsage(Graphics graphics, int width, int height)
    {
        (string fiveHourText, string weeklyText, int fiveHourPercent, int weeklyPercent) = GetStackedUsageText();

        using Font font = CreateFittingFont(graphics, width, height, fiveHourText, weeklyText);
        using SolidBrush fiveHourBrush = new(GetAccentColor(fiveHourPercent));
        using SolidBrush weeklyBrush = new(GetAccentColor(weeklyPercent));

        DrawCenteredLine(graphics, fiveHourText, font, fiveHourBrush, new RectangleF(3, 1, width - 6, height / 2.0f));
        DrawCenteredLine(graphics, weeklyText, font, weeklyBrush, new RectangleF(3, height / 2.0f - 1, width - 6, height / 2.0f));
    }

    private (string FiveHourText, string WeeklyText, int FiveHourPercent, int WeeklyPercent) GetStackedUsageText()
    {
        int fiveHourPercent = _snapshot.FiveHourLimit?.PercentRemaining ?? _snapshot.PercentRemaining ?? -1;
        int weeklyPercent = _snapshot.WeeklyLimit?.PercentRemaining ?? -1;

        return (
            $"{AppText.TaskbarFiveHourLabel}{FormatCompactPercent(fiveHourPercent)}",
            $"{AppText.TaskbarWeeklyLabel}{FormatCompactPercent(weeklyPercent)}",
            fiveHourPercent,
            weeklyPercent);
    }

    private int MeasureDesiredWidth(double fontSize)
    {
        (string fiveHourText, string weeklyText, _, _) = GetStackedUsageText();

        try
        {
            using Bitmap bitmap = new(1, 1);
            using Graphics graphics = Graphics.FromImage(bitmap);
            using Font font = new("Segoe UI", (float)fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            using StringFormat format = (StringFormat)StringFormat.GenericTypographic.Clone();
            format.FormatFlags |= StringFormatFlags.NoWrap;

            float fiveHourWidth = graphics.MeasureString(fiveHourText, font, int.MaxValue, format).Width;
            float weeklyWidth = graphics.MeasureString(weeklyText, font, int.MaxValue, format).Width;
            return (int)Math.Ceiling(Math.Max(fiveHourWidth, weeklyWidth) + 12);
        }
        catch
        {
            return 72;
        }
    }

    private static Font CreateFittingFont(Graphics graphics, int width, int height, string firstLine, string secondLine)
    {
        const float MinimumFontSize = (float)LanguageSettingsService.MinimumTaskbarFontSize;
        float preferredFontSize = (float)LanguageSettingsService.TaskbarFontSize;
        const float MaximumFontSize = (float)LanguageSettingsService.MaximumTaskbarFontSize;
        float maximumFontSize = Math.Clamp(Math.Min(preferredFontSize, (height - 4) / 2.0f), MinimumFontSize, MaximumFontSize);
        using StringFormat format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.FormatFlags |= StringFormatFlags.NoWrap;
        float availableWidth = Math.Max(1, width - 8);

        for (float size = maximumFontSize; size >= MinimumFontSize; size -= 0.5f)
        {
            Font candidate = new("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Pixel);
            SizeF firstSize = graphics.MeasureString(firstLine, candidate, int.MaxValue, format);
            SizeF secondSize = graphics.MeasureString(secondLine, candidate, int.MaxValue, format);
            if (firstSize.Width <= availableWidth && secondSize.Width <= availableWidth)
            {
                return candidate;
            }

            candidate.Dispose();
        }

        return new Font("Segoe UI", MinimumFontSize, FontStyle.Bold, GraphicsUnit.Pixel);
    }

    private static void DrawCenteredLine(Graphics graphics, string text, Font font, Brush brush, RectangleF bounds)
    {
        using StringFormat format = new()
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap,
            Trimming = StringTrimming.None,
        };

        graphics.DrawString(text, font, brush, bounds, format);
    }

    private static string FormatCompactPercent(int percent)
    {
        return percent >= 0 ? $"{percent}%" : "--";
    }

    private static Color GetAccentColor(int percent)
    {
        return percent switch
        {
            < 0 => Color.FromArgb(150, 150, 150),
            <= 10 => Color.FromArgb(239, 68, 68),
            <= 20 => Color.FromArgb(245, 184, 70),
            _ => Color.FromArgb(34, 197, 94),
        };
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

    private void OnLanguageChanged(object? sender, EventArgs args)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            PositionWindow();
            Invalidate();
        });
    }

    private void OnTaskbarFontSizeChanged(object? sender, EventArgs args)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            PositionWindow();
            Invalidate();
        });
    }

    private void OnPositionTimerTick(DispatcherQueueTimer sender, object args)
    {
        PositionWindow();
    }

    private void Invalidate()
    {
        if (_windowHandle != IntPtr.Zero)
        {
            InvalidateRect(_windowHandle, IntPtr.Zero, true);
            UpdateWindow(_windowHandle);
        }
    }

    private static void WriteDebugLog(string message)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.AppDataFolder);
            File.AppendAllText(
                Path.Combine(AppPaths.AppDataFolder, "taskbar-widget.log"),
                $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never affect the tray app.
        }
    }

    private static IntPtr HandleWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam)
    {
        if (!Instances.TryGetValue(hwnd, out TaskbarWidgetService? service))
        {
            return DefWindowProc(hwnd, message, wParam, lParam);
        }

        switch (message)
        {
            case WmPaint:
                PaintStruct paintStruct = new()
                {
                    rgbReserved = new byte[32],
                };
                IntPtr deviceContext = BeginPaint(hwnd, ref paintStruct);
                service.Paint(deviceContext);
                EndPaint(hwnd, ref paintStruct);
                return IntPtr.Zero;
            case WmEraseBackground:
                return new IntPtr(1);
            case WmDisplayChange:
            case WmSettingChange:
                service.PositionWindow();
                service.Invalidate();
                return IntPtr.Zero;
            case WmRButtonUp:
                service.ShowContextMenu();
                return IntPtr.Zero;
            case WmLButtonDblClk:
                service.Queue(service._showWindow);
                return IntPtr.Zero;
            default:
                return DefWindowProc(hwnd, message, wParam, lParam);
        }
    }

    private static IntPtr FindTaskbar()
    {
        return FindWindow("Shell_TrayWnd", null);
    }

    private static IntPtr FindChildWindow(IntPtr parent, string className)
    {
        return FindWindowEx(parent, IntPtr.Zero, className, null);
    }

    private delegate IntPtr WindowProcedureDelegate(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WndClassEx windowClass);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        int extendedStyle,
        string className,
        string windowName,
        int style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr param);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "FindWindowW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string className, string? windowName);

    [DllImport("user32.dll", EntryPoint = "FindWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string? windowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr hwndInsertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool InvalidateRect(IntPtr hwnd, IntPtr rect, bool erase);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr BeginPaint(IntPtr hwnd, ref PaintStruct paint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EndPaint(IntPtr hwnd, ref PaintStruct paint);

    [DllImport("user32.dll", EntryPoint = "LoadCursorW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadCursor(IntPtr instance, IntPtr cursorName);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern IntPtr SHAppBarMessage(uint message, ref AppBarData data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(
        IntPtr hwnd,
        IntPtr destinationDeviceContext,
        ref Point destination,
        ref Size size,
        IntPtr sourceDeviceContext,
        ref Point source,
        uint colorKey,
        ref BlendFunction blend,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteDC(IntPtr deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr gdiObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr gdiObject);

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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public int cbSize;
        public int style;
        public WindowProcedureDelegate lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PaintStruct
    {
        public IntPtr hdc;
        public int fErase;
        public Rect rcPaint;
        public int fRestore;
        public int fIncUpdate;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] rgbReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public Rect rc;
        public IntPtr lParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Size
    {
        public int Width;
        public int Height;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }
}

internal static class GraphicsExtensions
{
    public static void FillRoundedRectangle(this Graphics graphics, Brush brush, Rectangle bounds, int radius)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        using GraphicsPath path = new();
        int diameter = Math.Max(1, radius * 2);
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        graphics.FillPath(brush, path);
    }
}
