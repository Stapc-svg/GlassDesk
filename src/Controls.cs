// GlassDesk - small modern controls for the settings window (toggle, slider, segmented, swatch, card, nav item)
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace GlassDesk
{
    internal class Palette
    {
        public bool Dark;
        public Color Bg, Side, Card, Text, Sub, Line, Accent, Off, Hover;
        public static Palette From(Theme t)
        {
            var p = new Palette { Dark = t.Dark, Accent = t.Accent };
            if (t.Dark)
            {
                p.Bg = Color.FromArgb(24, 25, 32); p.Side = Color.FromArgb(18, 19, 25); p.Card = Color.FromArgb(34, 36, 45);
                p.Text = Color.FromArgb(236, 238, 245); p.Sub = Color.FromArgb(150, 156, 172); p.Line = Color.FromArgb(46, 48, 58);
                p.Off = Color.FromArgb(70, 73, 86); p.Hover = Color.FromArgb(38, 40, 50);
            }
            else
            {
                p.Bg = Color.FromArgb(243, 244, 248); p.Side = Color.FromArgb(234, 236, 242); p.Card = Color.White;
                p.Text = Color.FromArgb(24, 26, 32); p.Sub = Color.FromArgb(104, 110, 124); p.Line = Color.FromArgb(228, 230, 236);
                p.Off = Color.FromArgb(196, 200, 210); p.Hover = Color.FromArgb(222, 225, 233);
            }
            return p;
        }
    }

    internal abstract class SmoothControl : Control
    {
        protected SmoothControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected Graphics Prep(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(BackColor);
            return g;
        }
    }

    internal class ToggleSwitch : SmoothControl
    {
        bool on;
        public Palette P;
        public event EventHandler CheckedChanged;
        public ToggleSwitch(Palette p, float k) { P = p; Size = new Size((int)(44 * k), (int)(24 * k)); Cursor = Cursors.Hand; }
        public bool Checked
        {
            get { return on; }
            set { if (on == value) return; on = value; Invalidate(); if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty); }
        }
        public void SetSilently(bool v) { on = v; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prep(e);
            var r = new RectangleF(1, 1, Width - 2, Height - 2);
            Gfx.FillRound(g, on ? P.Accent : P.Off, r, r.Height / 2);
            float d = r.Height - 6;
            float x = on ? r.Right - d - 3 : r.X + 3;
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, x, r.Y + 3, d, d);
        }
        protected override void OnClick(EventArgs e) { base.OnClick(e); Checked = !Checked; }
    }

    internal class Slider : SmoothControl
    {
        public int Min, Max, Step = 1; int val;
        public Palette P;
        public event EventHandler ValueChanged;
        bool down;
        public Slider(Palette p, float k, int min, int max, int v) { P = p; Min = min; Max = max; val = v; Size = new Size((int)(200 * k), (int)(24 * k)); Cursor = Cursors.Hand; }
        public int Value
        {
            get { return val; }
            set { int v = Math.Max(Min, Math.Min(Max, (int)Math.Round(value / (double)Step) * Step)); if (v == val) return; val = v; Invalidate(); if (ValueChanged != null) ValueChanged(this, EventArgs.Empty); }
        }
        float Knob { get { return Height - 6; } }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prep(e);
            float k = Knob, x0 = k / 2 + 1, x1 = Width - k / 2 - 1, cy = Height / 2f;
            float f = (val - Min) / (float)Math.Max(1, Max - Min);
            float kx = x0 + (x1 - x0) * f;
            Gfx.FillRound(g, P.Off, new RectangleF(x0, cy - 2, x1 - x0, 4), 2);
            Gfx.FillRound(g, P.Accent, new RectangleF(x0, cy - 2, kx - x0, 4), 2);
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, kx - k / 2, cy - k / 2, k, k);
            using (var pen = new Pen(P.Accent, Math.Max(2f, k * 0.18f))) g.DrawEllipse(pen, kx - k / 2 + 1, cy - k / 2 + 1, k - 2, k - 2);
        }
        void SetFromX(int x)
        {
            float k = Knob, x0 = k / 2 + 1, x1 = Width - k / 2 - 1;
            float f = Math.Max(0, Math.Min(1, (x - x0) / (x1 - x0)));
            Value = Min + (int)Math.Round(f * (Max - Min));
        }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); down = true; SetFromX(e.X); }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (down) SetFromX(e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); down = false; }
    }

    internal class Segmented : SmoothControl
    {
        public string[] Options; int sel;
        public Palette P;
        public event EventHandler SelectionChanged;
        public Segmented(Palette p, float k, string[] options, int selected) { P = p; Options = options; sel = selected; Size = new Size((int)(84 * k * options.Length), (int)(34 * k)); Cursor = Cursors.Hand; }
        public int SelectedIndex
        {
            get { return sel; }
            set { if (value == sel) return; sel = value; Invalidate(); if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty); }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prep(e);
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Gfx.FillRound(g, P.Dark ? Color.FromArgb(24, 25, 32) : Color.FromArgb(236, 238, 243), r, Height / 2f);
            float w = r.Width / Options.Length;
            Gfx.FillRound(g, P.Accent, new RectangleF(r.X + sel * w + 3, r.Y + 3, w - 6, r.Height - 6), (Height - 6) / 2f);
            for (int i = 0; i < Options.Length; i++)
                using (var b = new SolidBrush(i == sel ? Color.White : P.Text))
                    g.DrawString(Options[i], Font, b, new RectangleF(r.X + i * w, r.Y, w, r.Height), Gfx.Center);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            SelectedIndex = Math.Max(0, Math.Min(Options.Length - 1, e.X * Options.Length / Math.Max(1, Width)));
        }
    }

    internal class Swatch : SmoothControl
    {
        public Color Color; public bool Selected; public Palette P; public bool IsCustom;
        public Swatch(Palette p, float k, Color c) { P = p; Color = c; Size = new Size((int)(30 * k), (int)(30 * k)); Cursor = Cursors.Hand; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prep(e);
            float pad = Width * 0.16f;
            if (IsCustom)
            {
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(pad, pad, Width - 2 * pad, Height - 2 * pad);
                    using (var pgb = new PathGradientBrush(path) { CenterColor = Color.White, SurroundColors = new[] { Color.FromArgb(255, 120, 120) } })
                        g.FillPath(pgb, path);
                }
                using (var b = new SolidBrush(Color.FromArgb(60, 60, 70))) g.DrawString("+", Font, b, new RectangleF(0, 0, Width, Height), Gfx.Center);
            }
            else using (var b = new SolidBrush(Color)) g.FillEllipse(b, pad, pad, Width - 2 * pad, Height - 2 * pad);
            if (Selected) using (var pen = new Pen(P.Text, 2f)) g.DrawEllipse(pen, 2, 2, Width - 4, Height - 4);
        }
    }

    /// <summary>A theme in the gallery: a tiny glass panel drawn in that theme's colors, with its name.</summary>
    internal class LookCard : SmoothControl
    {
        readonly Palette P; readonly float k; readonly Themes.Look L; readonly bool dark; public bool Selected; bool hover;
        public LookCard(Palette p, float k, Themes.Look look, bool dark, bool selected)
        {
            P = p; this.k = k; L = look; this.dark = dark; Selected = selected; Cursor = Cursors.Hand;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prep(e);
            bool d = L.Mode == "Light" ? false : L.Mode == "Dark" ? true : dark;
            float nameH = 40 * k;
            var stage = new RectangleF(1.5f, 1.5f, Width - 3, Height - nameH - 3);
            // wallpaper behind the glass
            using (var path = Gfx.Round(stage, 12 * k))
            {
                using (var lg = new LinearGradientBrush(stage, d ? Color.FromArgb(40, 52, 120) : Color.FromArgb(150, 190, 250),
                                                        d ? Color.FromArgb(110, 36, 90) : Color.FromArgb(244, 182, 210), 35f))
                    g.FillPath(lg, path);
                var st = g.Save();
                g.SetClip(path, CombineMode.Intersect);
                Gfx.Glow(g, new PointF(stage.Right, stage.Y), stage.Width * 0.8f, Color.FromArgb(120, d ? 90 : 255, d ? 130 : 220, 255));
                Gfx.Glow(g, new PointF(stage.X, stage.Bottom), stage.Width * 0.7f, Color.FromArgb(110, 255, 120, 170));
                // the glass panel
                var pr = new RectangleF(stage.X + stage.Width * 0.12f, stage.Y + stage.Height * 0.16f, stage.Width * 0.76f, stage.Height * 0.68f);
                float r = Math.Min(L.Radius, 22) * k * 0.6f;
                int a = L.Solid ? 255 : Math.Min(255, (int)(255 * L.Tint / 100f) + 70);
                Color top = d ? L.DarkTop : L.LightTop, bot = d ? L.DarkBottom : L.LightBottom;
                using (var pp = Gfx.Round(pr, r))
                {
                    using (var lg = new LinearGradientBrush(new PointF(0, pr.Y - 1), new PointF(0, pr.Bottom + 1), Color.FromArgb(a, top), Color.FromArgb(a, bot)))
                        g.FillPath(lg, pp);
                    var s2 = g.Save();
                    g.SetClip(pp, CombineMode.Intersect);
                    if (L.Glow) Gfx.Glow(g, new PointF(pr.Right, pr.Y), pr.Width * 0.7f, Color.FromArgb(d ? 110 : 70, L.Accent));
                    if (!L.Glow2.IsEmpty) Gfx.Glow(g, new PointF(pr.X, pr.Bottom), pr.Width * 0.5f, Color.FromArgb(d ? 70 : 50, L.Glow2));
                    if (!L.Flat)
                        using (var sheen = new LinearGradientBrush(pr, Color.FromArgb(d ? 34 : 80, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 60f)) g.FillPath(sheen, pp);
                    g.Restore(s2);
                    Color ink = L.Id == "paper" && !d ? Color.FromArgb(46, 40, 32) : d ? Color.FromArgb(240, 244, 246, 252) : Color.FromArgb(230, 18, 20, 28);
                    float px = pr.X + pr.Width * 0.12f, lh = pr.Height * 0.12f;
                    using (var b = new SolidBrush(L.Accent)) g.FillEllipse(b, px, pr.Y + pr.Height * 0.16f, lh, lh);
                    Gfx.FillRound(g, Color.FromArgb(150, ink), new RectangleF(px + lh * 1.6f, pr.Y + pr.Height * 0.16f + lh * 0.15f, pr.Width * 0.38f, lh * 0.7f), lh * 0.35f);
                    Gfx.FillRound(g, Color.FromArgb(230, ink), new RectangleF(px, pr.Y + pr.Height * 0.42f, pr.Width * 0.5f, lh * 1.25f), lh * 0.4f);
                    Gfx.FillRound(g, Color.FromArgb(d ? 40 : 30, ink), new RectangleF(px, pr.Y + pr.Height * 0.72f, pr.Width * 0.76f, lh * 0.55f), lh * 0.3f);
                    Gfx.FillRound(g, L.Accent, new RectangleF(px, pr.Y + pr.Height * 0.72f, pr.Width * 0.46f, lh * 0.55f), lh * 0.3f);
                    if (L.Neon)
                    {
                        for (int i = 3; i >= 1; i--) using (var pen = new Pen(Color.FromArgb(28 * (4 - i), L.Accent), 1f + i * 1.3f)) g.DrawPath(pen, pp);
                        using (var pen = new Pen(L.Accent, 1.2f)) g.DrawPath(pen, pp);
                    }
                    else if (L.Flat)
                        using (var pen = new Pen(d ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(26, 0, 0, 0), 1f)) g.DrawPath(pen, pp);
                    else
                        using (var lg = new LinearGradientBrush(pr, Color.FromArgb(200, 255, 255, 255), Color.FromArgb(40, 255, 255, 255), 70f))
                        using (var pen = new Pen(lg, 1.1f)) g.DrawPath(pen, pp);
                }
                g.Restore(st);
            }
            // name and description
            using (var b = new SolidBrush(P.Text)) using (var f = new Font(Font.FontFamily, Font.Size, FontStyle.Bold))
                g.DrawString(L.Name, f, b, new RectangleF(2, stage.Bottom + 6 * k, Width - 4, 18 * k), Gfx.Left);
            using (var b = new SolidBrush(P.Sub))
                g.DrawString(L.Desc, Font, b, new RectangleF(2, stage.Bottom + 22 * k, Width - 4, 16 * k), new StringFormat(Gfx.Left) { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
            // selection ring
            if (Selected || hover)
                using (var path = Gfx.Round(new RectangleF(1, 1, Width - 2, stage.Height + 1), 12.5f * k))
                using (var pen = new Pen(Selected ? P.Accent : Color.FromArgb(90, P.Text), Selected ? 2.5f * k : 1.2f * k)) g.DrawPath(pen, path);
            if (Selected)
            {
                float cs = 20 * k, cx = Width - cs - 8 * k, cy = 8 * k;
                using (var b = new SolidBrush(P.Accent)) g.FillEllipse(b, cx, cy, cs, cs);
                using (var pen = new Pen(Color.White, 2f * k) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    g.DrawLines(pen, new[] { new PointF(cx + cs * 0.27f, cy + cs * 0.52f), new PointF(cx + cs * 0.44f, cy + cs * 0.68f), new PointF(cx + cs * 0.74f, cy + cs * 0.34f) });
            }
        }
    }

    /// <summary>A rounded card that holds setting rows separated by thin lines.</summary>
    internal class Card : Panel
    {
        public Palette P; public float Radius;
        public System.Collections.Generic.List<int> Dividers = new System.Collections.Generic.List<int>();
        public Card(Palette p, float k)
        {
            P = p; Radius = 12 * k;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : P.Bg);
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Gfx.FillRound(g, P.Card, r, Radius);
            using (var path = Gfx.Round(r, Radius)) using (var pen = new Pen(P.Line)) g.DrawPath(pen, path);
            g.SmoothingMode = SmoothingMode.None;
            using (var pen = new Pen(P.Line)) foreach (int y in Dividers) g.DrawLine(pen, (int)(16 * Radius / 12), y, Width - (int)(16 * Radius / 12), y);
        }
    }

    internal class NavItem : SmoothControl
    {
        public string Glyph, Label; public bool Selected; bool hot;
        public Palette P; public Font GlyphFont;
        public NavItem(Palette p, float k, string glyph, string label, Font glyphFont)
        {
            P = p; Glyph = glyph; Label = label; GlyphFont = glyphFont;
            Size = new Size((int)(186 * k), (int)(40 * k)); Cursor = Cursors.Hand;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prep(e);
            float k = Height / 40f;
            if (Selected || hot) Gfx.FillRound(g, Selected ? (P.Dark ? Color.FromArgb(40, 43, 55) : Color.White) : P.Hover, new RectangleF(0, 0, Width, Height), 8 * k);
            if (Selected) Gfx.FillRound(g, P.Accent, new RectangleF(0, Height * 0.28f, 3 * k, Height * 0.44f), 1.5f * k);
            float x = 14 * k;
            if (GlyphFont != null)
            {
                using (var b = new SolidBrush(Selected ? P.Accent : P.Sub)) g.DrawString(Glyph, GlyphFont, b, new RectangleF(x, 0, 22 * k, Height), Gfx.Center);
                x += 32 * k;
            }
            using (var b = new SolidBrush(Selected ? P.Text : P.Sub)) g.DrawString(Label, Font, b, new RectangleF(x, 0, Width - x, Height), Gfx.Left);
        }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hot = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hot = false; Invalidate(); }
    }

    internal class FlatBtn : SmoothControl
    {
        public Palette P; public bool Primary, Danger; bool hot;
        public FlatBtn(Palette p, float k, string text, bool primary) { P = p; Text = text; Primary = primary; Height = (int)(34 * k); Cursor = Cursors.Hand; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prep(e);
            Color bg = Danger ? Color.FromArgb(220, 70, 80) : Primary ? P.Accent : (P.Dark ? Color.FromArgb(48, 51, 63) : Color.FromArgb(232, 234, 240));
            if (hot) bg = ControlPaint.Light(bg, 0.12f);
            Gfx.FillRound(g, bg, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), Height / 4f);
            using (var b = new SolidBrush(Primary || Danger ? Color.White : P.Text)) g.DrawString(Text, Font, b, new RectangleF(0, 0, Width, Height), Gfx.Center);
        }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hot = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hot = false; Invalidate(); }
    }

    /// <summary>Dark / light renderer for context menus.</summary>
    internal class MenuRenderer : ToolStripProfessionalRenderer
    {
        readonly Palette p;
        public MenuRenderer(Palette p) : base(new MenuColors(p)) { this.p = p; RoundedEdges = true; }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? p.Text : p.Sub;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Gfx.FillRound(e.Graphics, p.Hover, new RectangleF(4, 1, e.Item.Width - 8, e.Item.Height - 2), 5);
        }
        class MenuColors : ProfessionalColorTable
        {
            readonly Palette p;
            public MenuColors(Palette p) { this.p = p; UseSystemColors = false; }
            Color Bg { get { return p.Dark ? Color.FromArgb(36, 38, 48) : Color.FromArgb(252, 252, 254); } }
            public override Color ToolStripDropDownBackground { get { return Bg; } }
            public override Color ImageMarginGradientBegin { get { return Bg; } }
            public override Color ImageMarginGradientMiddle { get { return Bg; } }
            public override Color ImageMarginGradientEnd { get { return Bg; } }
            public override Color MenuBorder { get { return p.Line; } }
            public override Color MenuItemBorder { get { return Color.Transparent; } }
            public override Color MenuItemSelected { get { return p.Hover; } }
            public override Color SeparatorDark { get { return p.Line; } }
            public override Color SeparatorLight { get { return p.Line; } }
        }
    }
}
