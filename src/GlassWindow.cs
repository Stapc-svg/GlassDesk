// GlassDesk - base class for every glass panel on the desktop.
// Panels are "layered" windows: we paint one bitmap with real per-pixel transparency
// and hand it to Windows only when something changes. No animation loops = ~0% CPU when idle.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GlassDesk
{
    internal static class Gfx
    {
        public static GraphicsPath Round(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
        /// <summary>A soft round glow: color at the center fading to nothing at the edge.</summary>
        public static void Glow(Graphics g, PointF c, float radius, Color color)
        {
            if (radius < 2 || color.A == 0) return;
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(c.X - radius, c.Y - radius, radius * 2, radius * 2);
                using (var b = new PathGradientBrush(path) { CenterPoint = c, CenterColor = color, SurroundColors = new[] { Color.FromArgb(0, color) } })
                {
                    var blend = new Blend(3) { Factors = new[] { 0f, 0.55f, 1f }, Positions = new[] { 0f, 0.5f, 1f } };
                    b.Blend = blend;
                    g.FillPath(b, path);
                }
            }
        }

        public static void FillRound(Graphics g, Color c, RectangleF r, float radius)
        {
            using (var p = Round(r, radius)) using (var b = new SolidBrush(c)) g.FillPath(b, p);
        }
        public static Color WithAlpha(Color c, int a) { return Color.FromArgb(Math.Max(0, Math.Min(255, a)), c.R, c.G, c.B); }

        static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();
        /// <summary>Cached font, size in pixels.</summary>
        public static Font F(string name, float px, FontStyle st)
        {
            string key = name + "|" + px.ToString("0.0") + "|" + (int)st;
            Font f;
            if (!fonts.TryGetValue(key, out f))
            {
                f = SafeFont(name, px, st);
                fonts[key] = f;
            }
            return f;
        }
        /// <summary>Creates a font without ever throwing (some fonts reject some styles in GDI+).</summary>
        public static Font SafeFont(string name, float px, FontStyle st)
        {
            string[] names = { name, "Segoe UI", "Arial" };
            FontStyle[] styles = { st, FontStyle.Regular };
            foreach (string n in names)
                foreach (FontStyle s in styles)
                {
                    try { return new Font(n, px, s, GraphicsUnit.Pixel); } catch { }
                }
            return new Font(FontFamily.GenericSansSerif, px, GraphicsUnit.Pixel);
        }

        public static void ClearFonts() { foreach (var f in fonts.Values) f.Dispose(); fonts.Clear(); }

        public static readonly StringFormat Left = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        public static readonly StringFormat Right = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
        public static readonly StringFormat Center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
        public static readonly StringFormat TopCenterWrap = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near, Trimming = StringTrimming.EllipsisCharacter };
        public static readonly StringFormat TopLeftWrap = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Near, Trimming = StringTrimming.EllipsisWord };

        public static void Setup(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        }
    }

    internal abstract class GlassWindow : Form
    {
        protected readonly App App;
        public readonly string Id;
        /// <summary>Widgets can be zoomed by dragging their corner; everything inside is drawn at that size.</summary>
        public float Zoom = 1f;
        protected bool ZoomResize;
        Theme zoomed, zoomedFrom; float zoomedAt;
        protected Theme T
        {
            get
            {
                Theme b = App.Theme;
                if (Math.Abs(Zoom - 1f) < 0.001f) return b;
                if (zoomed == null || !ReferenceEquals(zoomedFrom, b) || zoomedAt != Zoom) { zoomed = b.Zoomed(Zoom); zoomedFrom = b; zoomedAt = Zoom; }
                return zoomed;
            }
        }
        protected Settings Cfg { get { return App.Settings; } }

        int pad;                       // room for the soft shadow around the panel
        protected Rectangle Panel;     // panel rectangle inside this window
        public bool Resizable;
        public bool Wanted = true;     // should this window be shown with current settings
        protected Size MinPanel = new Size(150, 50);

        bool armed, dragging, resizing, hoverGrip, hoverPanel;
        Point downScreen; Rectangle downBounds; float downZoom = 1f;

        protected GlassWindow(App app, string id)
        {
            App = app; Id = id;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Text = "GlassDesk";
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80000 /*LAYERED*/ | 0x80 /*TOOLWINDOW*/ | 0x08000000 /*NOACTIVATE*/;
                return cp;
            }
        }
        protected override bool ShowWithoutActivation { get { return true; } }

        public Rectangle PanelBounds
        {
            get { return new Rectangle(Left + pad, Top + pad, Width - 2 * pad, Height - 2 * pad); }
        }

        public void SetPanelBounds(Rectangle r)
        {
            pad = App.Theme.S(14);   // GlassDesk paints its own blur now, so every style keeps its soft shadow
            Bounds = new Rectangle(r.X - pad, r.Y - pad, r.Width + 2 * pad, r.Height + 2 * pad);
            Panel = new Rectangle(pad, pad, r.Width, r.Height);
            ApplyStyle();
            Redraw();
        }

        /// <summary>Window size the panel wants (widgets have a fixed size, fences use saved size).</summary>
        public abstract Size PreferredPanelSize(Size saved);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyStyle();
        }

        public void ApplyStyle()
        {
            if (!Native.IsWindows || !IsHandleCreated) return;
            SetGlass(false);
            UpdateRegion();
            Native.SetOwner(Handle, Cfg.KeepBehindWindows ? Native.DesktopOwner() : IntPtr.Zero);
            if (Cfg.KeepBehindWindows)
                Native.SetWindowPos(Handle, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        /// <summary>With glass on, the window is cut to the panel's rounded shape so the blur doesn't show past the corners.</summary>
        void UpdateRegion()
        {
            if (!Native.IsWindows || !IsHandleCreated) return;
            // no window cut-out any more: our blur is drawn inside the panel's own rounded shape
            Native.SetWindowRgn(Handle, IntPtr.Zero, true);
        }

        /// <summary>Windows draws the real blur of whatever is behind the panel; we only paint tint, light and grain on top.
        /// While a panel is being dragged, acrylic is swapped for the lighter blur because acrylic lags when moved.</summary>
        void SetGlass(bool moving)
        {
            int mode = 0;    // Windows' blur stays off; it didn't show on many PCs. See Backdrop.cs.
            Color c = T.Dark ? Color.FromArgb(18, 20, 28) : Color.FromArgb(242, 244, 248);
            int tint = (0x30 << 24) | (c.B << 16) | (c.G << 8) | c.R;
            Native.SetGlass(Handle, mode, tint);
        }

        int lastMoveDraw;
        bool UsesBackdrop { get { return !T.Solid && (Cfg.GlassStyle == "frosted" || Cfg.GlassStyle == "blur"); } }

        /// <summary>Paints the matching piece of the blurred wallpaper inside the panel: the actual glass blur.</summary>
        void DrawBackdrop(Graphics g, GraphicsPath path)
        {
            if (!UsesBackdrop) return;
            try
            {
                Rectangle scr = App.Preview ? new Rectangle(0, 0, 1920, 1080) : Screen.FromRectangle(PanelBounds).Bounds;
                Bitmap blur = Backdrop.For(scr, T.LookId == "acrylic" ? 3 : Cfg.GlassStyle == "frosted" ? 2 : 1);
                var src = new Rectangle(Left + Panel.X - scr.X, Top + Panel.Y - scr.Y, Panel.Width, Panel.Height);
                var st = g.Save();
                g.SetClip(path, CombineMode.Intersect);
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.DrawImage(blur, new Rectangle(Panel.X, Panel.Y, Panel.Width, Panel.Height), src, GraphicsUnit.Pixel);
                g.Restore(st);
            }
            catch (Exception ex) { Program.Log(ex); }
        }

        /// <summary>A color washed over the glass; fences can have their own.</summary>
        protected virtual Color PanelColor { get { return Color.Empty; } }

        // ---------------- painting ----------------
        protected abstract void PaintContent(Graphics g);
        public bool DropHint;   // another panel is being dragged onto this one

        // ---- groups: dots along the bottom; click the dots or the top of the panel (widgets: or scroll) to switch ----
        RectangleF Dots(int count)
        {
            float d = App.Theme.Sf(6), gap = App.Theme.Sf(5), w = count * d + (count - 1) * gap;
            return new RectangleF(Panel.X + (Panel.Width - w) / 2, Panel.Bottom - App.Theme.Sf(11), w, d);
        }
        protected virtual void PaintOverlay(Graphics g)
        {
            int i, n;
            if (App.GroupInfo(Id, out i, out n))
            {
                var r = Dots(n);
                float d = r.Height, gap = App.Theme.Sf(5);
                Gfx.FillRound(g, Color.FromArgb(T.Dark ? 70 : 40, 0, 0, 0), RectangleF.Inflate(r, App.Theme.Sf(5), App.Theme.Sf(3)), App.Theme.Sf(6));
                for (int k = 0; k < n; k++)
                    using (var b = new SolidBrush(k == i ? T.Accent : Color.FromArgb(150, 255, 255, 255)))
                        g.FillEllipse(b, r.X + k * (d + gap), r.Y, d, d);
            }
            if (DropHint)
            {
                var pr = new RectangleF(Panel.X + 1.5f, Panel.Y + 1.5f, Panel.Width - 3, Panel.Height - 3);
                using (var path = Gfx.Round(pr, Math.Max(2, T.S(Cfg.Radius) - 1.5f)))
                {
                    using (var b = new SolidBrush(Gfx.WithAlpha(T.Accent, 50))) g.FillPath(b, path);
                    using (var pen = new Pen(T.Accent, T.Sf(3))) g.DrawPath(pen, path);
                }
                var f = Gfx.F(T.FontName, T.Sf(13), FontStyle.Bold);
                var lr = new RectangleF(Panel.X + Panel.Width / 2f - T.Sf(80), Panel.Y + Panel.Height / 2f - T.Sf(17), T.Sf(160), T.Sf(34));
                Gfx.FillRound(g, T.Accent, lr, T.Sf(17));
                using (var b = new SolidBrush(Color.White)) g.DrawString("Drop to stack", f, b, lr, Gfx.Center);
            }
        }
        /// <summary>The strip along the top you can click to flip to the next panel of a group.</summary>
        protected virtual Rectangle GroupClickArea { get { return new Rectangle(Panel.X, Panel.Y, Panel.Width * 6 / 10, T.S(34)); } }
        protected virtual bool OnPreClick(Point p)
        {
            int i, n;
            if (!App.GroupInfo(Id, out i, out n)) return false;
            if (!RectangleF.Inflate(Dots(n), App.Theme.Sf(6), App.Theme.Sf(6)).Contains(p) && !GroupClickArea.Contains(p)) return false;
            App.SwitchGroup(this, 1); return true;
        }

        public Bitmap RenderBitmap()
        {
            var bmp = new Bitmap(Math.Max(1, Width), Math.Max(1, Height), PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                Gfx.Setup(g);
                g.Clear(Color.Transparent);
                DrawChrome(g);
                PaintContent(g);
                PaintOverlay(g);
                if (Resizable && (hoverPanel || resizing) && !Cfg.LockPositions) DrawGrip(g);
            }
            return bmp;
        }

        /// <summary>Two little curved lines in the corner, shown on hover, so you can see where to resize.</summary>
        void DrawGrip(Graphics g)
        {
            float r = T.S(Cfg.Radius);
            float x = Panel.Right, y = Panel.Bottom;
            int a = hoverGrip || resizing ? 230 : 120;
            using (var pen = new Pen(Color.FromArgb(a, hoverGrip || resizing ? T.Accent : T.Text), App.Theme.Sf(2.2f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                for (int i = 0; i < 2; i++)
                {
                    float o = App.Theme.Sf(5 + i * 5);
                    float rr = Math.Max(App.Theme.Sf(6), r - o);
                    var box = new RectangleF(x - o - rr * 2, y - o - rr * 2, rr * 2, rr * 2);
                    g.DrawArc(pen, box, 10, 70);
                }
            }
        }

        void DrawChrome(Graphics g)
        {
            float r = T.S(Cfg.Radius);
            if (pad > 0)
            {
                for (int i = pad; i >= 1; i--)
                {
                    float f = 1f - (float)i / pad;
                    var rr = new RectangleF(Panel.X - i, Panel.Y - i + T.Sf(3), Panel.Width + 2 * i, Panel.Height + 2 * i);
                    using (var p = Gfx.Round(rr, r + i)) using (var pen = new Pen(Color.FromArgb((int)(f * f * 26), 0, 0, 0), 1.2f))
                        g.DrawPath(pen, p);
                }
            }
            var pr = new RectangleF(Panel.X, Panel.Y, Panel.Width, Panel.Height);
            if (pr.Width < 4 || pr.Height < 4) return;
            bool glass = Cfg.GlassOn;
            using (var path = Gfx.Round(pr, r))
            {
                // 1. tint: how much the glass colors what's behind it
                DrawBackdrop(g, path);
                if (T.TintTop.ToArgb() == T.TintBottom.ToArgb())
                    using (var b = new SolidBrush(T.TintTop)) g.FillPath(b, path);
                else
                    using (var b = new LinearGradientBrush(new PointF(0, pr.Y - 1), new PointF(0, pr.Bottom + 1), T.TintTop, T.TintBottom)) g.FillPath(b, path);
                if (!T.Glow.IsEmpty || !T.Glow2.IsEmpty)
                {
                    var gs = g.Save();
                    g.SetClip(path, CombineMode.Intersect);
                    float big = Math.Max(pr.Width, pr.Height);
                    if (!T.Glow.IsEmpty) Gfx.Glow(g, new PointF(pr.Right - pr.Width * 0.08f, pr.Y - pr.Height * 0.05f), big * 0.75f, T.Glow);
                    if (!T.Glow2.IsEmpty) Gfx.Glow(g, new PointF(pr.X - pr.Width * 0.05f, pr.Bottom + pr.Height * 0.05f), big * 0.5f, T.Glow2);
                    g.Restore(gs);
                }
                if (!PanelColor.IsEmpty) using (var b = new SolidBrush(Color.FromArgb(T.Dark ? 52 : 44, PanelColor))) g.FillPath(b, path);

                var st = g.Save();
                g.SetClip(path, CombineMode.Intersect);
                // 2. frosted grain, the fine texture real frosted glass has
                // (grain removed in 1.7.1: it showed up as a grid of little squares)

                // 3. light falling across the pane from the top-left (flat themes skip the glass effects)
                if (T.Flat) { g.Restore(st); goto rim; }
                var a = new PointF(pr.X, pr.Y);
                var z = new PointF(pr.Right + 2, pr.Bottom + 2);
                using (var lg = new LinearGradientBrush(new PointF(a.X - 2, a.Y - 2), z, Color.White, Color.White))
                {
                    lg.WrapMode = WrapMode.TileFlipXY;
                    var cb = new ColorBlend(4);
                    cb.Colors = new[] { Color.FromArgb(T.Dark ? 34 : 70, 255, 255, 255), Color.FromArgb(T.Dark ? 10 : 24, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), Color.FromArgb(T.Dark ? 18 : 10, 0, 0, 0) };
                    cb.Positions = new[] { 0f, 0.22f, 0.5f, 1f };
                    lg.InterpolationColors = cb;
                    g.FillPath(lg, path);
                }
                // 4. soft glow just inside the edge, like light trapped in thick glass
                using (var pen = new Pen(Color.FromArgb(T.Dark ? 10 : 18, 255, 255, 255), T.Sf(8))) g.DrawPath(pen, path);
                using (var pen = new Pen(Color.FromArgb(T.Dark ? 12 : 22, 255, 255, 255), T.Sf(3))) g.DrawPath(pen, path);
                g.Restore(st);
            }
            rim:
            // 5. the rim: bright where light hits (top-left and bottom-right corners), faint along the sides
            var br = new RectangleF(pr.X + 0.6f, pr.Y + 0.6f, pr.Width - 1.2f, pr.Height - 1.2f);
            if (T.Flat)
            {
                using (var p = Gfx.Round(br, Math.Max(0, r - 0.6f))) using (var pen = new Pen(T.Border, 1f)) g.DrawPath(pen, p);
                return;
            }
            if (T.Neon)
            {
                using (var p = Gfx.Round(br, Math.Max(0, r - 0.6f)))
                {
                    for (int i = 4; i >= 1; i--)
                        using (var pen = new Pen(Color.FromArgb(18 * (5 - i), T.Accent), T.Sf(1.2f + i * 1.6f))) g.DrawPath(pen, p);
                    using (var lg = new LinearGradientBrush(new PointF(br.X, br.Y), new PointF(br.Right, br.Bottom), T.Accent, T.Glow2.IsEmpty ? T.Accent : Color.FromArgb(255, T.Glow2)))
                    using (var pen = new Pen(lg, T.Sf(1.4f))) g.DrawPath(pen, p);
                }
                return;
            }
            using (var p = Gfx.Round(br, Math.Max(0, r - 0.6f)))
            using (var lg = new LinearGradientBrush(new PointF(br.X - 1, br.Y - 1), new PointF(br.Right + 1, br.Bottom + 1), Color.White, Color.White))
            {
                lg.WrapMode = WrapMode.TileFlipXY;
                var cb = new ColorBlend(4);
                int k = T.Dark ? 1 : 0;
                cb.Colors = new[] { Color.FromArgb(200 - 20 * k, 255, 255, 255), Color.FromArgb(46, 255, 255, 255), Color.FromArgb(34, 255, 255, 255), Color.FromArgb(130 - 20 * k, 255, 255, 255) };
                cb.Positions = new[] { 0f, 0.35f, 0.7f, 1f };
                lg.InterpolationColors = cb;
                using (var pen = new Pen(lg, T.Sf(1.25f))) g.DrawPath(pen, p);
            }
            // 6. a hairline highlight along the top edge, just inside the rim
            if (r < pr.Width / 2 - 2)
                using (var lg = new LinearGradientBrush(new PointF(pr.X + r, 0), new PointF(pr.Right - r, 0), Color.FromArgb(90, 255, 255, 255), Color.FromArgb(0, 255, 255, 255)))
                using (var pen = new Pen(lg, 1f))
                    g.DrawLine(pen, pr.X + r * 0.8f, pr.Y + 1.8f, pr.Right - r, pr.Y + 1.8f);
        }

        static Bitmap grainDark, grainLight;
        /// <summary>A small tile of random light and dark specks, made once and reused everywhere.</summary>
        static Bitmap Grain(bool dark)
        {
            Bitmap b = dark ? grainDark : grainLight;
            if (b != null) return b;
            b = new Bitmap(96, 96, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var rnd = new Random(7);
            for (int y = 0; y < 96; y++)
                for (int x = 0; x < 96; x++)
                {
                    int v = rnd.Next(256);
                    int a = (int)(Math.Abs(v - 128) / 128.0 * (dark ? 16 : 12));
                    b.SetPixel(x, y, v > 128 ? Color.FromArgb(a, 255, 255, 255) : Color.FromArgb(a, 0, 0, 0));
                }
            if (dark) grainDark = b; else grainLight = b;
            return b;
        }

        public void Redraw()
        {
            if (!Native.IsWindows || !IsHandleCreated || !Visible || Width <= 0 || Height <= 0) return;
            if (App.Paused) { App.MarkDirty(this); return; }
            using (var bmp = RenderBitmap()) Push(bmp);
        }

        void Push(Bitmap bmp)
        {
            IntPtr screen = Native.GetDC(IntPtr.Zero);
            IntPtr mem = Native.CreateCompatibleDC(screen);
            IntPtr hb = IntPtr.Zero, old = IntPtr.Zero;
            try
            {
                hb = bmp.GetHbitmap(Color.FromArgb(0));
                old = Native.SelectObject(mem, hb);
                var size = new Native.SIZE(bmp.Width, bmp.Height);
                var src = new Native.POINT(0, 0);
                var dst = new Native.POINT(Left, Top);
                var blend = new Native.BLENDFUNCTION { BlendOp = Native.AC_SRC_OVER, SourceConstantAlpha = 255, AlphaFormat = Native.AC_SRC_ALPHA };
                Native.UpdateLayeredWindow(Handle, screen, ref dst, ref size, mem, ref src, 0, ref blend, Native.ULW_ALPHA);
            }
            finally
            {
                Native.ReleaseDC(IntPtr.Zero, screen);
                if (hb != IntPtr.Zero) { Native.SelectObject(mem, old); Native.DeleteObject(hb); }
                Native.DeleteDC(mem);
            }
        }

        protected override void OnPaint(PaintEventArgs e) { }
        protected override void OnPaintBackground(PaintEventArgs e) { }

        // ---------------- input ----------------
        protected virtual bool IsDragArea(Point p) { return true; }
        protected virtual void OnClickAt(Point p, MouseButtons b) { }
        protected virtual void OnDoubleClickAt(Point p) { }
        protected virtual void OnHover(Point p) { }
        protected virtual void OnWheel(int delta) { }
        protected virtual void OnResized() { }

        protected bool InGrip(Point p) { return ResizeZone(p) != 0; }

        /// <summary>Where a resize would start: 1 = right edge, 2 = bottom edge, 3 = bottom-right corner, 0 = not on an edge.</summary>
        protected int ResizeZone(Point p)
        {
            if (!Resizable || p.X < Panel.X || p.Y < Panel.Y || p.X > Panel.Right + 1 || p.Y > Panel.Bottom + 1) return 0;
            int corner = App.Theme.S(20), edge = App.Theme.S(7);
            bool right = p.X >= Panel.Right - edge, bottom = p.Y >= Panel.Bottom - edge;
            if ((p.X >= Panel.Right - corner && p.Y >= Panel.Bottom - corner) || (right && bottom)) return 3;
            if (right) return 1;
            if (bottom) return 2;
            return 0;
        }
        int zone;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || Cfg.LockPositions) return;
            downScreen = Cursor.Position; downBounds = PanelBounds; downZoom = Zoom;
            zone = ResizeZone(e.Location);
            if (zone != 0) { resizing = true; Capture = true; if (IsHandleCreated) SetGlass(true); }
            else if (IsDragArea(e.Location)) { armed = true; Capture = true; }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Point s = Cursor.Position;
            int dx = s.X - downScreen.X, dy = s.Y - downScreen.Y;
            if (resizing && ZoomResize)
            {
                // keep the widget's shape and scale everything inside it
                float bw = downBounds.Width / downZoom, bh = downBounds.Height / downZoom;
                float zx = (downBounds.Width + dx) / bw, zy = (downBounds.Height + dy) / bh;
                float z = zone == 1 ? zx : zone == 2 ? zy : Math.Max(zx, zy);
                Zoom = Math.Max(0.6f, Math.Min(2.2f, (float)Math.Round(z * 50) / 50f));
                int zw = (int)Math.Round(bw * Zoom), zh = (int)Math.Round(bh * Zoom);
                Size = new Size(zw + 2 * pad, zh + 2 * pad);
                Panel = new Rectangle(pad, pad, zw, zh);
                OnResized();
                Redraw();
                return;
            }
            if (resizing)
            {
                int w = zone == 2 ? downBounds.Width : Math.Max(T.S(MinPanel.Width), downBounds.Width + dx);
                int h = zone == 1 ? downBounds.Height : Math.Max(T.S(MinPanel.Height), downBounds.Height + dy);
                // never wider or taller than the screen it is on
                Rectangle wa = Screen.FromRectangle(downBounds).WorkingArea;
                w = Math.Min(w, wa.Right - downBounds.X); h = Math.Min(h, wa.Bottom - downBounds.Y);
                Size = new Size(w + 2 * pad, h + 2 * pad);
                Panel = new Rectangle(pad, pad, w, h);
                UpdateRegion();
                OnResized();
                Redraw();
                return;
            }
            if (armed && !dragging && (Math.Abs(dx) > 4 || Math.Abs(dy) > 4)) { dragging = true; if (IsHandleCreated) SetGlass(true); }
            if (dragging)
            {
                App.DragOver(this);
                var r = new Rectangle(downBounds.X + dx, downBounds.Y + dy, downBounds.Width, downBounds.Height);
                r = Snap(r);
                Location = new Point(r.X - pad, r.Y - pad);
                // the blurred wallpaper behind the panel has to follow it while it moves
                if (UsesBackdrop && Environment.TickCount - lastMoveDraw > 30) { lastMoveDraw = Environment.TickCount; Redraw(); }
                return;
            }
            int zn = Cfg.LockPositions ? 0 : ResizeZone(e.Location);
            bool g = zn != 0;
            Cursor = zn == 1 ? Cursors.SizeWE : zn == 2 ? Cursors.SizeNS : zn == 3 ? Cursors.SizeNWSE : Cursors.Default;
            if (Resizable && !hoverPanel) { hoverPanel = true; hoverGrip = g; Redraw(); }
            else if (g != hoverGrip) { hoverGrip = g; Redraw(); }
            OnHover(e.Location);
        }

        Rectangle Snap(Rectangle r)
        {
            Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            int m = T.S(16), th = T.S(14);
            if (Math.Abs(r.Left - (wa.Left + m)) < th) r.X = wa.Left + m;
            if (Math.Abs(r.Top - (wa.Top + m)) < th) r.Y = wa.Top + m;
            if (Math.Abs(r.Right - (wa.Right - m)) < th) r.X = wa.Right - m - r.Width;
            if (Math.Abs(r.Bottom - (wa.Bottom - m)) < th) r.Y = wa.Bottom - m - r.Height;
            // align with other panels
            foreach (var o in App.Windows)
            {
                if (o == this || !o.Visible) continue;
                Rectangle b = o.PanelBounds;
                if (Math.Abs(r.Left - b.Left) < th) r.X = b.Left;
                else if (Math.Abs(r.Left - (b.Right + m)) < th) r.X = b.Right + m;
                if (Math.Abs(r.Top - b.Top) < th) r.Y = b.Top;
                else if (Math.Abs(r.Top - (b.Bottom + m)) < th) r.Y = b.Bottom + m;
            }
            return r;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            bool wasMoving = dragging || resizing, wasDragging = dragging;
            if (resizing && ZoomResize) App.SaveZoom(Id, Zoom);
            if (wasMoving) { App.SavePosition(Id, PanelBounds); if (IsHandleCreated) SetGlass(false); }
            armed = dragging = resizing = false;
            Capture = false;
            if (wasDragging) App.DragEnded(this);
            if (!wasMoving && !(e.Button == MouseButtons.Left && OnPreClick(e.Location))) OnClickAt(e.Location, e.Button);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left) OnDoubleClickAt(e.Location);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hoverPanel && !resizing) { hoverPanel = hoverGrip = false; Redraw(); }
            OnHover(new Point(-1, -1));
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int gi, gn;
            if (this is Widget && App.GroupInfo(Id, out gi, out gn)) { App.SwitchGroup(this, e.Delta < 0 ? 1 : -1); return; }
            OnWheel(e.Delta);
        }

        const int WM_WINDOWPOSCHANGING = 0x46, WM_MOUSEACTIVATE = 0x21;
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_MOUSEACTIVATE) { m.Result = new IntPtr(3); return; } // MA_NOACTIVATE
            if (m.Msg == WM_WINDOWPOSCHANGING && Cfg.KeepBehindWindows && Native.IsWindows)
            {
                var wp = (Native.WINDOWPOS)Marshal.PtrToStructure(m.LParam, typeof(Native.WINDOWPOS));
                wp.hwndInsertAfter = Native.HWND_BOTTOM;
                wp.flags &= ~Native.SWP_NOZORDER;
                Marshal.StructureToPtr(wp, m.LParam, false);
            }
            base.WndProc(ref m);
        }

        // ------------- small drawing helpers for subclasses -------------
        protected void Title(Graphics g, string text, Rectangle r)
        {
            var f = Gfx.F(T.FontName, T.Sf(11), FontStyle.Bold);
            if (Cfg.GlassStyle == "transparent")
                using (var sh = new SolidBrush(Color.FromArgb(140, 0, 0, 0)))
                    g.DrawString(text.ToUpperInvariant(), f, sh, new RectangleF(r.X, r.Y + T.Sf(1.2f), r.Width, r.Height), Gfx.Left);
            using (var b = new SolidBrush(T.SubText))
                g.DrawString(text.ToUpperInvariant(), f, b, r, Gfx.Left);
        }
        protected void Bar(Graphics g, RectangleF r, float frac, Color c)
        {
            Gfx.FillRound(g, T.Track, r, r.Height / 2);
            float w = Math.Max(r.Height, r.Width * Math.Max(0, Math.Min(1, frac)));
            if (frac > 0.001f) Gfx.FillRound(g, c, new RectangleF(r.X, r.Y, w, r.Height), r.Height / 2);
        }
    }
}
