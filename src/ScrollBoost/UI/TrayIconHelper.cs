using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using Microsoft.Win32;

namespace ScrollBoost.UI;

public static class TrayIconHelper
{
    public static Icon CreateIcon()
    {
        bool isLightTheme = IsLightTheme();
        Color lineColor = isLightTheme
            ? Color.FromArgb(255, 0x33, 0x33, 0x33)
            : Color.White;

        // Render at DPI-appropriate size for crisp tray icons on high-DPI displays
        int size = GetTrayIconSize();
        return GenerateMouseIcon(lineColor, size);
    }

    private static int GetTrayIconSize()
    {
        try
        {
            using var g = Graphics.FromHwnd(IntPtr.Zero);
            int dpi = (int)g.DpiX;
            // 96 DPI = 100% = 16px, 120 DPI = 125% = 20px,
            // 144 DPI = 150% = 24px, 192 DPI = 200% = 32px
            return Math.Max(16, 16 * dpi / 96);
        }
        catch
        {
            return 32;
        }
    }

    public static void SaveMultiSizeIco(string path)
    {
        var sizes = new[] { 16, 32, 48 };
        var darkColor = Color.FromArgb(255, 0x33, 0x33, 0x33);

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // ICO header
        writer.Write((short)0);             // reserved
        writer.Write((short)1);             // type = ICO
        writer.Write((short)sizes.Length);  // number of images

        // Render each size to PNG bytes
        var pngDatas = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++)
        {
            using var bmp = new Bitmap(sizes[i], sizes[i], PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            DrawMouse(g, darkColor, sizes[i]);

            using var pngMs = new MemoryStream();
            bmp.Save(pngMs, ImageFormat.Png);
            pngDatas[i] = pngMs.ToArray();
        }

        // Directory entries — image data starts right after header + all entries
        int dataOffset = 6 + sizes.Length * 16;
        for (int i = 0; i < sizes.Length; i++)
        {
            writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); // width
            writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); // height
            writer.Write((byte)0);    // color palette count (0 = no palette)
            writer.Write((byte)0);    // reserved
            writer.Write((short)1);   // color planes
            writer.Write((short)32);  // bits per pixel
            writer.Write(pngDatas[i].Length);  // image data size in bytes
            writer.Write(dataOffset);           // offset to image data
            dataOffset += pngDatas[i].Length;
        }

        // Write image data blocks
        for (int i = 0; i < sizes.Length; i++)
            writer.Write(pngDatas[i]);

        File.WriteAllBytes(path, ms.ToArray());
    }

    public static bool IsLightTheme()
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
            return false; // default to dark theme
        }
    }

    private static Icon GenerateMouseIcon(Color lineColor, int size)
    {
        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        DrawMouse(g, lineColor, size);

        IntPtr hIcon = bitmap.GetHicon();
        Icon icon = Icon.FromHandle(hIcon);
        Icon safeIcon = (Icon)icon.Clone();
        DestroyIcon(hIcon);
        return safeIcon;
    }

    private static void DrawMouse(Graphics g, Color lineColor, int size)
    {
        float penWidth = size / 16f; // ~2 px at 32, scales with size
        using var pen = new Pen(lineColor, penWidth)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        // Mouse body: vertical capsule centered in the canvas
        float bodyW = size * 0.44f;   // ~14 px at 32
        float bodyH = size * 0.69f;   // ~22 px at 32
        float bodyX = (size - bodyW) / 2f;
        float bodyY = (size - bodyH) / 2f;
        float radius = bodyW / 2f;    // fully rounded ends

        using var path = new GraphicsPath();
        // Top semicircle (left → right across the top)
        path.AddArc(bodyX, bodyY, bodyW, bodyW, 180, 180);
        // Right side straight down
        path.AddLine(bodyX + bodyW, bodyY + radius, bodyX + bodyW, bodyY + bodyH - radius);
        // Bottom semicircle (right → left across the bottom)
        path.AddArc(bodyX, bodyY + bodyH - bodyW, bodyW, bodyW, 0, 180);
        // Left side straight up
        path.AddLine(bodyX, bodyY + bodyH - radius, bodyX, bodyY + radius);
        path.CloseFigure();

        g.DrawPath(pen, path);

        // Scroll wheel: short vertical line centered horizontally, in the upper third of the body
        float wheelX = size / 2f;
        float wheelTop    = bodyY + bodyH * 0.25f;
        float wheelBottom = bodyY + bodyH * 0.42f;
        g.DrawLine(pen, wheelX, wheelTop, wheelX, wheelBottom);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
