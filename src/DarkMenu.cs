using System;
using System.Drawing;
using System.Windows.Forms;

namespace DeskFlow;

/// <summary>Helpers for building dark-themed WinForms context menus (used by the tray and the player).</summary>
public static class DarkMenu
{
    private static readonly Color Bg = Color.FromArgb(30, 31, 40);
    private static readonly Color Hover = Color.FromArgb(52, 55, 74);
    private static readonly Color Border = Color.FromArgb(62, 64, 82);
    private static readonly Color Fg = Color.FromArgb(236, 238, 246);
    private static readonly Color Dim = Color.FromArgb(118, 121, 140);
    private static readonly Color Accent = Color.FromArgb(124, 140, 255);

    private sealed class Colors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Bg;
        public override Color ImageMarginGradientBegin => Bg;
        public override Color ImageMarginGradientMiddle => Bg;
        public override Color ImageMarginGradientEnd => Bg;
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => Hover;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color MenuItemPressedGradientBegin => Hover;
        public override Color MenuItemPressedGradientEnd => Hover;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Bg;
        public override Color CheckBackground => Color.FromArgb(60, 68, 120);
        public override Color CheckSelectedBackground => Color.FromArgb(72, 82, 140);
        public override Color CheckPressedBackground => Color.FromArgb(72, 82, 140);
    }

    private sealed class Renderer : ToolStripProfessionalRenderer
    {
        public Renderer() : base(new Colors()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Fg : Dim;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item?.Enabled == false ? Dim : Fg;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var r = e.ImageRectangle;
            using var b = new SolidBrush(Accent);
            var radio = e.Item.Tag as string == "radio";
            if (radio)
            {
                var d = Math.Min(r.Width, r.Height) / 2.2f;
                g.FillEllipse(b, r.X + (r.Width - d) / 2, r.Y + (r.Height - d) / 2, d, d);
            }
            else
            {
                using var pen = new Pen(Accent, Math.Max(1.6f, r.Height / 9f)) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
                float x = r.X, y = r.Y, w = r.Width, h = r.Height;
                g.DrawLines(pen, new[] { new PointF(x + w * .25f, y + h * .52f), new PointF(x + w * .43f, y + h * .7f), new PointF(x + w * .76f, y + h * .32f) });
            }
        }
    }

    private static readonly Font MenuFont = new("Yu Gothic UI", 9.5f);

    public static ContextMenuStrip Create()
    {
        var m = new ContextMenuStrip { Renderer = new Renderer(), ShowImageMargin = true, ShowCheckMargin = false, Font = MenuFont };
        return m;
    }

    public static ToolStripMenuItem Item(string text, Action? click = null, bool enabled = true)
    {
        var it = new ToolStripMenuItem(text) { Enabled = enabled, ForeColor = Fg };
        if (click != null) it.Click += (_, _) => click();
        return it;
    }

    public static ToolStripMenuItem Check(string text, bool on, Action click, bool enabled = true)
    {
        var it = Item(text, click, enabled);
        it.Checked = on;
        return it;
    }

    public static ToolStripMenuItem Radio(string text, bool on, Action click, bool enabled = true)
    {
        var it = Check(text, on, click, enabled);
        it.Tag = "radio";
        return it;
    }

    public static ToolStripMenuItem Sub(string text, bool enabled, params ToolStripItem[] items)
    {
        var it = Item(text, null, enabled);
        it.DropDown = Create();
        it.DropDownItems.AddRange(items);
        return it;
    }

    public static ToolStripSeparator Sep() => new();
}
