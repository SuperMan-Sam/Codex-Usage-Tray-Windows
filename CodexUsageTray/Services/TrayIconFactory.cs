using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using CodexUsageTray.Core.Models;

namespace CodexUsageTray.Services;

internal static class TrayIconFactory
{
    public static Icon Create(TrayIconState state)
    {
        using Bitmap bitmap = new(32, 32);
        using Graphics graphics = Graphics.FromImage(bitmap);

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        Color fill = state switch
        {
            TrayIconState.Normal => Color.FromArgb(43, 172, 102),
            TrayIconState.Warning => Color.FromArgb(232, 171, 38),
            TrayIconState.Critical => Color.FromArgb(218, 54, 51),
            _ => Color.FromArgb(132, 139, 148),
        };

        using SolidBrush brush = new(fill);
        using Pen outline = new(Color.White, 2);

        graphics.FillEllipse(brush, 5, 5, 22, 22);
        graphics.DrawEllipse(outline, 5, 5, 22, 22);

        IntPtr handle = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
