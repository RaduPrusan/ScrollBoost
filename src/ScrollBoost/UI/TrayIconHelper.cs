using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using Microsoft.Win32;

namespace ScrollBoost.UI;

public static class TrayIconHelper
{
    public static Icon CreateIcon()
    {
        bool isLightTheme = IsLightTheme();
        return GenerateScrollIcon(isLightTheme);
    }

    private static bool IsLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("SystemUsesLightTheme");
            return value is int i && i == 1;
        }
        catch
        {
            return false; // Default to dark theme
        }
    }

    private static Icon GenerateScrollIcon(bool lightTheme)
    {
        // Draw at 32x32 for better quality, then the icon handle carries the image.
        // System tray will scale to the DPI-appropriate size.
        const int size = 16;

        using var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);

        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        // Theme colour: dark gray for light theme, white for dark theme.
        Color lineColor = lightTheme ? Color.FromArgb(255, 0x33, 0x33, 0x33)
                                     : Color.FromArgb(255, 0xFF, 0xFF, 0xFF);

        using var pen = new Pen(lineColor, 1.5f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        // ---------------------------------------------------------------
        // Layout (all coordinates in 16x16 space):
        //
        //   Up chevron (^):   apex at (8, 3), legs spread to (5, 6) and (11, 6)
        //   Gap of ~2px
        //   Down chevron (v): apex at (8, 13), legs spread to (5, 10) and (11, 10)
        //
        // This gives a clean double-arrow indicating scroll up/down.
        // ---------------------------------------------------------------

        float cx = size / 2f; // 8.0

        // Up chevron  ^
        float upApexY  = 3.5f;
        float upBaseY  = 6.5f;
        float arrowHalf = 3.0f; // half-width of the chevron

        g.DrawLine(pen, cx - arrowHalf, upBaseY,  cx,             upApexY);
        g.DrawLine(pen, cx,             upApexY,  cx + arrowHalf, upBaseY);

        // Down chevron  v
        float downApexY = 12.5f;
        float downBaseY = 9.5f;

        g.DrawLine(pen, cx - arrowHalf, downBaseY,  cx,             downApexY);
        g.DrawLine(pen, cx,             downApexY,  cx + arrowHalf, downBaseY);

        // Convert Bitmap → Icon
        IntPtr hIcon = bitmap.GetHicon();
        Icon icon = Icon.FromHandle(hIcon);

        // Clone the icon so we can safely destroy the GDI handle after
        // (FromHandle does NOT take ownership of the handle).
        Icon safeIcon = (Icon)icon.Clone();
        DestroyIcon(hIcon);
        return safeIcon;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
