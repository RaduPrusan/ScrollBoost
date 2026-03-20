#nullable enable
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using WinForms = System.Windows.Forms;

namespace ScrollBoost.UI;

public class ThemedMenuRenderer : WinForms.ToolStripProfessionalRenderer
{
    private readonly bool _isDark;

    private readonly Color _menuBg;
    private readonly Color _menuBorder;
    private readonly Color _itemHover;
    private readonly Color _itemText;
    private readonly Color _separatorColor;

    public ThemedMenuRenderer(bool isDark) : base(new ThemedColorTable(isDark))
    {
        _isDark = isDark;
        RoundedEdges = false;

        if (isDark)
        {
            _menuBg = Color.FromArgb(44, 44, 44);
            _menuBorder = Color.FromArgb(56, 56, 56);
            _itemHover = Color.FromArgb(62, 62, 62);
            _itemText = Color.FromArgb(255, 255, 255);
            _separatorColor = Color.FromArgb(56, 56, 56);
        }
        else
        {
            _menuBg = Color.FromArgb(249, 249, 249);
            _menuBorder = Color.FromArgb(224, 224, 224);
            _itemHover = Color.FromArgb(230, 230, 230);
            _itemText = Color.FromArgb(26, 26, 26);
            _separatorColor = Color.FromArgb(224, 224, 224);
        }
    }

    protected override void OnRenderToolStripBackground(WinForms.ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(_menuBg);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(WinForms.ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(_menuBorder);
        var r = e.AffectedBounds;
        e.Graphics.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
    }

    protected override void OnRenderMenuItemBackground(WinForms.ToolStripItemRenderEventArgs e)
    {
        if (e.Item.Selected || e.Item.Pressed)
        {
            using var brush = new SolidBrush(_itemHover);
            var r = new Rectangle(2, 1, e.Item.Width - 4, e.Item.Height - 2);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            FillRoundedRect(e.Graphics, brush, r, 4);
        }
    }

    protected override void OnRenderItemText(WinForms.ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = _itemText;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(WinForms.ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.Height / 2;
        using var pen = new Pen(_separatorColor);
        e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
    }

    protected override void OnRenderImageMargin(WinForms.ToolStripRenderEventArgs e)
    {
        // Don't render the image margin background
    }

    private static void FillRoundedRect(Graphics g, Brush brush, Rectangle r, int radius)
    {
        using var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        g.FillPath(brush, path);
    }
}

internal class ThemedColorTable : WinForms.ProfessionalColorTable
{
    private readonly Color _bg;

    public ThemedColorTable(bool isDark)
    {
        _bg = isDark ? Color.FromArgb(44, 44, 44) : Color.FromArgb(249, 249, 249);
    }

    public override Color MenuItemSelected => _bg;
    public override Color MenuBorder => _bg;
    public override Color MenuItemBorder => Color.Transparent;
    public override Color ToolStripDropDownBackground => _bg;
    public override Color ImageMarginGradientBegin => _bg;
    public override Color ImageMarginGradientMiddle => _bg;
    public override Color ImageMarginGradientEnd => _bg;
}
