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
        protected Theme T { get { return App.Theme; } }
        protected Settings Cfg { get { return App.Settings; } }

        int pad;                       // room for the soft shadow around the panel
        protected Rectangle Panel;     // panel rectangle inside this window
        public bool Resizable;
        public bool Wanted = true;     // should this window be shown with current settings
        protected Size MinPanel = new Size(150, 50);

        bool armed, dragging, resizing;
        Point downScreen; Rectangle downBounds;

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
            pad = Cfg.Blur ? 0 : T.S(14);
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
            Native.SetBlur(Handle, Cfg.Blur);
            if (Cfg.Blur)
            {
                int d = T.S(Cfg.Radius) * 2 + 1;
                Native.SetWindowRgn(Handle, Native.CreateRoundRectRgn(0, 0, Width + 1, Height + 1, d, d), true);
            }
            else Native.SetWindowRgn(Handle, IntPtr.Zero, true);
            Native.SetOwner(Handle, Cfg.KeepBehindWindows ? Native.DesktopOwner() : IntPtr.Zero);
            if (Cfg.KeepBehindWindows)
                Native.SetWindowPos(Handle, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        // ---------------- painting ----------------
        protected abstract void PaintContent(Graphics g);

        public Bitmap RenderBitmap()
        {
            var bmp = new Bitmap(Math.Max(1, Width), Math.Max(1, Height), PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                Gfx.Setup(g);
                g.Clear(Color.Transparent);
                DrawChrome(g);
                PaintContent(g);
            }
            return bmp;
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
            using (var path = Gfx.Round(pr, r))
            {
                using (var b = new SolidBrush(T.Tint)) g.FillPath(b, path);
                // glass sheen
                var sheen = new RectangleF(pr.X, pr.Y, pr.Width, Math.Max(2, pr.Height * 0.55f));
                var st = g.Save();
                g.SetClip(sheen);
                using (var lg = new LinearGradientBrush(new PointF(0, sheen.Top - 1), new PointF(0, sheen.Bottom + 1),
                    Color.FromArgb(T.Dark ? 22 : 70, 255, 255, 255), Color.FromArgb(0, 255, 255, 255)))
                    g.FillPath(lg, path);
                g.Restore(st);
            }
            var br = new RectangleF(pr.X + 0.5f, pr.Y + 0.5f, pr.Width - 1, pr.Height - 1);
            using (var p = Gfx.Round(br, Math.Max(0, r - 0.5f))) using (var pen = new Pen(T.Border, 1f)) g.DrawPath(pen, p);
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

        protected bool InGrip(Point p)
        {
            int g = T.S(18);
            return Resizable && p.X >= Panel.Right - g && p.Y >= Panel.Bottom - g && p.X <= Panel.Right && p.Y <= Panel.Bottom;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || Cfg.LockPositions) return;
            downScreen = Cursor.Position; downBounds = PanelBounds;
            if (InGrip(e.Location)) { resizing = true; Capture = true; }
            else if (IsDragArea(e.Location)) { armed = true; Capture = true; }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Point s = Cursor.Position;
            int dx = s.X - downScreen.X, dy = s.Y - downScreen.Y;
            if (resizing)
            {
                int w = Math.Max(T.S(MinPanel.Width), downBounds.Width + dx);
                int h = Math.Max(T.S(MinPanel.Height), downBounds.Height + dy);
                Size = new Size(w + 2 * pad, h + 2 * pad);
                Panel = new Rectangle(pad, pad, w, h);
                if (Cfg.Blur) ApplyStyle();
                OnResized();
                Redraw();
                return;
            }
            if (armed && !dragging && (Math.Abs(dx) > 4 || Math.Abs(dy) > 4)) dragging = true;
            if (dragging)
            {
                var r = new Rectangle(downBounds.X + dx, downBounds.Y + dy, downBounds.Width, downBounds.Height);
                r = Snap(r);
                Location = new Point(r.X - pad, r.Y - pad);
                return;
            }
            Cursor = InGrip(e.Location) && !Cfg.LockPositions ? Cursors.SizeNWSE : Cursors.Default;
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
            bool wasMoving = dragging || resizing;
            if (wasMoving) App.SavePosition(Id, PanelBounds);
            armed = dragging = resizing = false;
            Capture = false;
            if (!wasMoving) OnClickAt(e.Location, e.Button);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left) OnDoubleClickAt(e.Location);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            OnHover(new Point(-1, -1));
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
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
            using (var b = new SolidBrush(T.SubText))
                g.DrawString(text.ToUpperInvariant(), Gfx.F(T.FontName, T.Sf(11), FontStyle.Bold), b, r, Gfx.Left);
        }
        protected void Bar(Graphics g, RectangleF r, float frac, Color c)
        {
            Gfx.FillRound(g, T.Track, r, r.Height / 2);
            float w = Math.Max(r.Height, r.Width * Math.Max(0, Math.Min(1, frac)));
            if (frac > 0.001f) Gfx.FillRound(g, c, new RectangleF(r.X, r.Y, w, r.Height), r.Height / 2);
        }
    }
}
