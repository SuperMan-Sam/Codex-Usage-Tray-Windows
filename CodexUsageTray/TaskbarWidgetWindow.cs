using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using CodexUsageTray.Core.Models;
using CodexUsageTray.Core.Services;
using Microsoft.UI.Dispatching;

namespace CodexUsageTray;

public sealed class TaskbarWidgetWindow : IDisposable
{
    private const string WindowClassName = "CodexUsageTrayTaskbarWidget";
    private const int WidgetWidthDip = 282;
    private const int WidgetHeightDip = 50;
    private const int WidgetMarginDip = 12;
    private const uint ExitCommand = 1001;
    private const int ClassAlreadyExists = 1410;
    private const int IdcArrow = 32512;
    private const uint WmNull = 0x0000;
    private const uint WmPaint = 0x000F;
    private const uint WmEraseBackground = 0x0014;
    private const uint WmContextMenu = 0x007B;
    private const uint WmLeftButtonUp = 0x0202;
    private const uint WmRightButtonUp = 0x0205;
    private const uint WsChild = 0x40000000;
    private const uint WsVisible = 0x10000000;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const uint MfString = 0x00000000;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCommand = 0x0100;

    private readonly Action _showMainWindow;
    private readonly Action _exit;
    private readonly DispatcherQueueTimer _positionTimer;
    private readonly WindowProcedure _windowProcedure;

    private IntPtr _windowHandle;
    private IntPtr _taskbarWindowHandle;
    private bool _classRegistered;
    private bool _disposed;
    private string _fiveHourPercent = "--";
    private string _fiveHourReset = "--";
    private string _weeklyPercent = "--";
    private string _weeklyReset = "--";
    private Color _dotColor = Color.Gray;

    public TaskbarWidgetWindow(Action showMainWindow, Action exit)
    {
        _showMainWindow = showMainWindow;
        _exit = exit;
        _windowProcedure = OnWindowMessage;

        _positionTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _positionTimer.Interval = TimeSpan.FromSeconds(1);
        _positionTimer.Tick += (_, _) => MaintainTaskbarPresence();
    }

    public IntPtr WindowHandle => _windowHandle;

    public void ShowWidget()
    {
        MaintainTaskbarPresence();
        _positionTimer.Start();
    }

    public void Update(UsageSnapshot snapshot)
    {
        _fiveHourPercent = FormatPercent(snapshot.FiveHourLimit);
        _fiveHourReset = UsageResetTimeFormatter.FormatHoursUntilReset(snapshot.FiveHourLimit, snapshot.CapturedAt);
        _weeklyPercent = FormatPercent(snapshot.WeeklyLimit);
        _weeklyReset = UsageResetTimeFormatter.FormatHoursUntilReset(snapshot.WeeklyLimit, snapshot.CapturedAt);
        _dotColor = ColorFor(TrayIconStateResolver.Resolve(snapshot));

        if (_windowHandle != IntPtr.Zero)
        {
            InvalidateRect(_windowHandle, IntPtr.Zero, erase: false);
        }
    }

    public void Close()
    {
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _positionTimer.Stop();

        if (_windowHandle != IntPtr.Zero && IsWindow(_windowHandle))
        {
            DestroyWindow(_windowHandle);
        }

        _windowHandle = IntPtr.Zero;
    }

    private void MaintainTaskbarPresence()
    {
        if (_disposed)
        {
            return;
        }

        _taskbarWindowHandle = FindWindow("Shell_TrayWnd", null);
        if (_taskbarWindowHandle == IntPtr.Zero)
        {
            return;
        }

        if (_windowHandle == IntPtr.Zero || !IsWindow(_windowHandle))
        {
            _windowHandle = CreateWidgetWindow(_taskbarWindowHandle);
        }
        else if (GetParent(_windowHandle) != _taskbarWindowHandle)
        {
            SetParent(_windowHandle, _taskbarWindowHandle);
        }

        PositionInTaskbar();
    }

    private IntPtr CreateWidgetWindow(IntPtr parentWindow)
    {
        if (!EnsureWindowClass())
        {
            return IntPtr.Zero;
        }

        int width = ScaleToPixels(WidgetWidthDip, parentWindow);
        int height = ScaleToPixels(WidgetHeightDip, parentWindow);
        return CreateWindowEx(
            0,
            WindowClassName,
            "Codex Usage Widget",
            WsChild | WsVisible,
            0,
            0,
            width,
            height,
            parentWindow,
            IntPtr.Zero,
            GetModuleHandle(null),
            IntPtr.Zero);
    }

    private bool EnsureWindowClass()
    {
        if (_classRegistered)
        {
            return true;
        }

        WindowClassEx windowClass = new()
        {
            cbSize = Marshal.SizeOf<WindowClassEx>(),
            style = 0,
            lpfnWndProc = _windowProcedure,
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = GetModuleHandle(null),
            hIcon = IntPtr.Zero,
            hCursor = LoadCursor(IntPtr.Zero, new IntPtr(IdcArrow)),
            hbrBackground = IntPtr.Zero,
            lpszMenuName = null,
            lpszClassName = WindowClassName,
            hIconSm = IntPtr.Zero,
        };

        ushort atom = RegisterClassEx(ref windowClass);
        int error = Marshal.GetLastWin32Error();
        _classRegistered = atom != 0 || error == ClassAlreadyExists;
        return _classRegistered;
    }

    private void PositionInTaskbar()
    {
        if (_windowHandle == IntPtr.Zero || _taskbarWindowHandle == IntPtr.Zero || !GetWindowRect(_taskbarWindowHandle, out NativeRect taskbarRect))
        {
            return;
        }

        int widgetWidth = ScaleToPixels(WidgetWidthDip, _taskbarWindowHandle);
        int widgetHeight = ScaleToPixels(WidgetHeightDip, _taskbarWindowHandle);
        int margin = ScaleToPixels(WidgetMarginDip, _taskbarWindowHandle);
        int taskbarWidth = taskbarRect.Right - taskbarRect.Left;
        int taskbarHeight = taskbarRect.Bottom - taskbarRect.Top;
        int x = margin;
        int y = Math.Max(0, (taskbarHeight - widgetHeight) / 2);

        if (taskbarWidth < taskbarHeight)
        {
            x = Math.Max(0, (taskbarWidth - widgetWidth) / 2);
            y = margin;
        }

        SetWindowPos(_windowHandle, IntPtr.Zero, x, y, widgetWidth, widgetHeight, SwpNoActivate | SwpShowWindow);
    }

    private IntPtr OnWindowMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case WmPaint:
                Paint(hwnd);
                return IntPtr.Zero;
            case WmEraseBackground:
                return new IntPtr(1);
            case WmLeftButtonUp:
                _showMainWindow();
                return IntPtr.Zero;
            case WmRightButtonUp:
            case WmContextMenu:
                ShowContextMenu();
                return IntPtr.Zero;
            default:
                return DefWindowProc(hwnd, message, wParam, lParam);
        }
    }

    private void Paint(IntPtr hwnd)
    {
        IntPtr hdc = BeginPaint(hwnd, out PaintStruct paint);
        try
        {
            GetClientRect(hwnd, out NativeRect clientRect);
            using Graphics graphics = Graphics.FromHdc(hdc);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            float scale = GetDpiForWindow(hwnd) / 96f;
            RectangleF bounds = new(0.5f, 0.5f, Math.Max(0, clientRect.Right - clientRect.Left - 1), Math.Max(0, clientRect.Bottom - clientRect.Top - 1));
            using GraphicsPath backgroundPath = CreateRoundedRectangle(bounds, 8f * scale);
            using SolidBrush backgroundBrush = new(Color.FromArgb(242, 31, 26, 32));
            using Pen borderPen = new(Color.FromArgb(77, 255, 255, 255), Math.Max(1f, scale));
            graphics.FillPath(backgroundBrush, backgroundPath);
            graphics.DrawPath(borderPen, backgroundPath);

            using SolidBrush dotBrush = new(_dotColor);
            float dotSize = 7f * scale;
            graphics.FillEllipse(dotBrush, 10f * scale, bounds.Height / 2f - dotSize / 2f, dotSize, dotSize);

            using Font labelFont = new("Segoe UI", 12f * scale, FontStyle.Bold, GraphicsUnit.Pixel);
            using Font valueFont = new("Segoe UI", 16f * scale, FontStyle.Bold, GraphicsUnit.Pixel);
            using Font smallFont = new("Segoe UI", 10f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            using SolidBrush whiteBrush = new(Color.White);
            using SolidBrush mutedBrush = new(Color.FromArgb(207, 207, 207));

            DrawLimit(graphics, "5h", _fiveHourPercent, _fiveHourReset, 27f * scale, 6f * scale, labelFont, valueFont, smallFont, whiteBrush, mutedBrush);
            DrawLimit(graphics, "Week", _weeklyPercent, _weeklyReset, 138f * scale, 6f * scale, labelFont, valueFont, smallFont, whiteBrush, mutedBrush);
        }
        finally
        {
            EndPaint(hwnd, ref paint);
        }
    }

    private static void DrawLimit(
        Graphics graphics,
        string label,
        string percent,
        string reset,
        float x,
        float y,
        Font labelFont,
        Font valueFont,
        Font smallFont,
        Brush whiteBrush,
        Brush mutedBrush)
    {
        graphics.DrawString(label, labelFont, whiteBrush, x, y + 5f);
        float percentX = x + (label == "Week" ? 42f : 25f);
        graphics.DrawString(percent, valueFont, whiteBrush, percentX, y + 1f);
        graphics.DrawString(reset, smallFont, mutedBrush, x, y + 25f);
    }

    private void ShowContextMenu()
    {
        if (_windowHandle == IntPtr.Zero)
        {
            return;
        }

        IntPtr menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            AppendMenu(menu, MfString, ExitCommand, "关闭程序");
            GetCursorPos(out NativePoint point);
            SetForegroundWindow(_windowHandle);
            uint command = TrackPopupMenu(menu, TpmRightButton | TpmReturnCommand, point.X, point.Y, 0, _windowHandle, IntPtr.Zero);
            PostMessage(_windowHandle, WmNull, IntPtr.Zero, IntPtr.Zero);

            if (command == ExitCommand)
            {
                _exit();
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private static GraphicsPath CreateRoundedRectangle(RectangleF bounds, float radius)
    {
        float diameter = radius * 2f;
        GraphicsPath path = new();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static string FormatPercent(UsageLimitSnapshot? limit)
    {
        return limit is null ? "--" : $"{limit.PercentRemaining}%";
    }

    private static Color ColorFor(TrayIconState state)
    {
        return state switch
        {
            TrayIconState.Normal => Color.LimeGreen,
            TrayIconState.Warning => Color.Gold,
            TrayIconState.Critical => Color.OrangeRed,
            _ => Color.Gray,
        };
    }

    private static int ScaleToPixels(int value, IntPtr hwnd)
    {
        uint dpi = hwnd == IntPtr.Zero ? 96 : GetDpiForWindow(hwnd);
        return (int)Math.Round(value * (dpi / 96.0), MidpointRounding.AwayFromZero);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WindowClassEx windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint exStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr param);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string className, string? windowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetParent(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr hwndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool InvalidateRect(IntPtr hwnd, IntPtr rect, bool erase);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr BeginPaint(IntPtr hwnd, out PaintStruct paint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EndPaint(IntPtr hwnd, ref PaintStruct paint);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, uint newItemId, string newItem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rectangle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr LoadCursor(IntPtr instance, IntPtr cursorName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    private delegate IntPtr WindowProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClassEx
    {
        public int cbSize;
        public uint style;
        public WindowProcedure lpfnWndProc;
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
    private struct PaintStruct
    {
        public IntPtr hdc;
        public bool fErase;
        public NativeRect rcPaint;
        public bool fRestore;
        public bool fIncUpdate;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] rgbReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
