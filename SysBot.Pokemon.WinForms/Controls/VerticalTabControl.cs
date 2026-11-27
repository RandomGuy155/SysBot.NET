using System;
using System.Drawing;
using System.Windows.Forms;
using PKHeX.Drawing.PokeSprite;

namespace SysBot.Pokemon.WinForms;

/// <summary>
/// Custom-painted vertical <see cref="TabControl"/> that eliminates the native Win32 white border/background
/// in dark mode and colors tabs top-to-bottom using PKHeX's <see cref="ContestColor"/> palette.
/// </summary>
public sealed class VerticalTabControl : TabControl
{
    private int _hoverIndex = -1;

    public VerticalTabControl()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        Alignment = TabAlignment.Left;
        SizeMode = TabSizeMode.Fixed;
    }

    public override Rectangle DisplayRectangle
    {
        get
        {
            var tabWidth = Alignment is TabAlignment.Left or TabAlignment.Right ? ItemSize.Height : 0;
            return new Rectangle(tabWidth + 1, 0, Math.Max(0, Width - tabWidth - 1), Height);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hovered = -1;
        for (int i = 0; i < TabCount; i++)
        {
            var rect = GetTabRect(i);
            rect = new Rectangle(0, rect.Top, ItemSize.Height, rect.Height);
            if (rect.Contains(e.Location))
            {
                hovered = i;
                break;
            }
        }

        if (hovered != _hoverIndex)
        {
            _hoverIndex = hovered;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverIndex != -1)
        {
            _hoverIndex = -1;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var dark = Application.IsDarkModeEnabled;

        var stripBack = dark ? Color.FromArgb(32, 32, 32) : SystemColors.Control;
        var selectedBack = dark ? Color.FromArgb(48, 48, 52) : SystemColors.Window;
        var hoverBack = dark ? Color.FromArgb(40, 40, 44) : Color.FromArgb(229, 241, 251);
        var dividerColor = dark ? Color.FromArgb(58, 58, 62) : SystemColors.ControlDark;
        var selectedFore = dark ? Color.White : SystemColors.ControlText;
        var normalFore = dark ? Color.FromArgb(200, 200, 205) : SystemColors.ControlText;

        using (var bg = new SolidBrush(stripBack))
            g.FillRectangle(bg, ClientRectangle);

        var tabWidth = ItemSize.Height;
        using (var pen = new Pen(dividerColor))
            g.DrawLine(pen, tabWidth, 0, tabWidth, Height);

        for (int i = 0; i < TabCount; i++)
        {
            var raw = GetTabRect(i);
            var rect = new Rectangle(0, raw.Top, tabWidth, raw.Height);
            var selected = i == SelectedIndex;
            var hovered = i == _hoverIndex;

            if (selected)
            {
                using var fill = new SolidBrush(selectedBack);
                g.FillRectangle(fill, rect);
            }
            else if (hovered)
            {
                using var fill = new SolidBrush(hoverBack);
                g.FillRectangle(fill, rect);
            }

            var contest = ContestColor.GetColor(i % 5);
            var barColor = selected ? contest : Blend(contest, stripBack, dark ? 0.45 : 0.6);
            var barWidth = selected ? 4 : 3;
            using (var accent = new SolidBrush(barColor))
                g.FillRectangle(accent, rect.Left + 2, rect.Top + 5, barWidth, Math.Max(4, rect.Height - 10));

            var textRect = new Rectangle(rect.Left + 8, rect.Top, rect.Width - 10, rect.Height);
            TextRenderer.DrawText(
                g,
                TabPages[i].Text,
                Font,
                textRect,
                selected ? selectedFore : normalFore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    private static Color Blend(Color foreground, Color background, double amount)
    {
        byte r = (byte)((foreground.R * amount) + (background.R * (1 - amount)));
        byte g = (byte)((foreground.G * amount) + (background.G * (1 - amount)));
        byte b = (byte)((foreground.B * amount) + (background.B * (1 - amount)));
        return Color.FromArgb(r, g, b);
    }
}
