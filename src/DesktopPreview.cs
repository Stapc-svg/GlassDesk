// GlassDesk - a live, small picture of your desktop inside Settings: wallpaper, fences and widgets
// exactly as they look right now, so every switch you flip shows up straight away.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GlassDesk
{
    internal class DesktopPreview : Control
    {
        readonly App app;
        readonly Timer timer;
        Bitmap shot;                 // the last drawn picture
        string hoverName = "";
        readonly List<KeyValuePair<RectangleF, GlassWindow>> spots = new List<KeyValuePair<RectangleF, GlassWindow>>();

        // previews never redraw while the Settings page is scrolling: that is what made scrolling stutter
        static int scrolledAt = -100000;
        public static void Scrolled() { scrolledAt = Environment.TickCount; }
        public static bool Busy { get { return Environment.TickCount - scrolledAt < 600; } }
        /// <summary>True when the control is actually on screen inside its scrolled page.</summary>
        public static bool OnScreen(Control c)
        {
            if (!c.Visible || c.Parent == null) return false;
            var f = c.FindForm(); if (f == null || !f.Visible || f.WindowState == FormWindowState.Minimized) return false;
            var r = c.Parent.RectangleToScreen(c.Bounds);
            Control p = c.Parent;
            while (p != null) { if (!p.RectangleToScreen(p.ClientRectangle).IntersectsWith(r)) return false; p = p.Parent; }
            return true;
        }

        public DesktopPreview(App app, float k, int width)
        {
            this.app = app;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            Rectangle scr = Screen.PrimaryScreen.Bounds;
            Width = width;
            Height = Math.Max(120, (int)(width * (float)scr.Height / Math.Max(1, scr.Width)));
            // redraw now and then, so changes made in Settings (or on the desktop) appear in the picture
            timer = new Timer { Interval = 1500 };
            timer.Tick += delegate { if (!Busy && OnScreen(this)) Snap(); };
            timer.Start();
            Snap();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { timer.Dispose(); if (shot != null) shot.Dispose(); }
            base.Dispose(disposing);
        }

        /// <summary>Paints the wallpaper and every visible panel, scaled down to fit.</summary>
        void Snap()
        {
            try
            {
                Rectangle scr = app.Preview ? new Rectangle(0, 0, 1920, 1080) : Screen.PrimaryScreen.Bounds;
                float s = (float)Width / scr.Width;
                var bmp = new Bitmap(Math.Max(1, Width), Math.Max(1, Height));
                spots.Clear();
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    using (var path = Gfx.Round(new RectangleF(0, 0, Width - 1, Height - 1), 12))
                    {
                        g.SetClip(path);
                        g.InterpolationMode = InterpolationMode.Bilinear;
                        Bitmap wall = null;
                        try { wall = Backdrop.For(scr, 0); } catch { }
                        if (wall != null) g.DrawImage(wall, new Rectangle(0, 0, Width, Height));
                        else using (var lg = new LinearGradientBrush(new Point(0, 0), new Point(0, Height), Color.FromArgb(40, 60, 110), Color.FromArgb(120, 90, 140))) g.FillRectangle(lg, 0, 0, Width, Height);

                        foreach (var w in app.Windows.ToArray())
                        {
                            if (w == null || w.IsDisposed || !(app.Preview ? app.WantsShown(w) : w.Visible) || w.Width < 4) continue;
                            var b = w.Bounds;
                            if (!b.IntersectsWith(scr)) continue;   // on another monitor
                            Bitmap img = w.Snapshot(1400);   // reused unless the panel changed
                            if (img == null) continue;
                            {
                                var dst = new RectangleF((b.X - scr.X) * s, (b.Y - scr.Y) * s, b.Width * s, b.Height * s);
                                g.DrawImage(img, dst);
                                var pb = w.PanelBounds;
                                spots.Add(new KeyValuePair<RectangleF, GlassWindow>(new RectangleF((pb.X - scr.X) * s, (pb.Y - scr.Y) * s, pb.Width * s, pb.Height * s), w));
                            }
                        }
                        g.ResetClip();
                    }
                    using (var path = Gfx.Round(new RectangleF(0.5f, 0.5f, Width - 2, Height - 2), 12))
                    using (var pen = new Pen(Color.FromArgb(70, 255, 255, 255), 1f)) g.DrawPath(pen, path);
                }
                var old = shot; shot = bmp; if (old != null) old.Dispose();
                Invalidate();
            }
            catch (Exception ex) { Program.Log(ex); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (shot != null) e.Graphics.DrawImageUnscaled(shot, 0, 0);
            if (hoverName.Length > 0)
            {
                var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
                var f = Gfx.F(Font.Name, 9f, FontStyle.Bold);
                var sz = g.MeasureString(hoverName, f);
                var r = new RectangleF(10, Height - sz.Height - 16, sz.Width + 16, sz.Height + 6);
                using (var b = new SolidBrush(Color.FromArgb(190, 0, 0, 0))) Gfx.FillRound(g, b.Color, r, r.Height / 2);
                using (var b = new SolidBrush(Color.White)) g.DrawString(hoverName, f, b, r.X + 8, r.Y + 3);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            string n = "";
            for (int i = spots.Count - 1; i >= 0; i--)
                if (spots[i].Key.Contains(e.Location)) { n = app.PanelName(spots[i].Value); break; }
            if (n != hoverName) { hoverName = n; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { hoverName = ""; Invalidate(); base.OnMouseLeave(e); }
    }
}

namespace GlassDesk
{
    /// <summary>A small live picture of one widget, shown next to its switch in Settings.
    /// Faded while the widget is off, so you can see what turning it on adds.</summary>
    internal class WidgetThumb : Control
    {
        readonly App app; readonly GlassWindow w;
        readonly Timer timer;
        Bitmap small;        // already shrunk and faded: painting it is just a copy, so scrolling stays smooth
        bool shownOn;

        public WidgetThumb(App app, GlassWindow w, int width, int height)
        {
            this.app = app; this.w = w;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            Size = new Size(width, height);
            timer = new Timer { Interval = 3000 };
            timer.Tick += delegate { if (!DesktopPreview.Busy && DesktopPreview.OnScreen(this)) Make(); };
            timer.Start();
            HandleCreated += delegate { Make(); };
        }
        protected override void Dispose(bool disposing) { if (disposing) { timer.Dispose(); if (small != null) small.Dispose(); } base.Dispose(disposing); }

        void Make()
        {
            try
            {
                bool on = app.WantsShown(w);
                var shot = w.IsDisposed ? null : w.Snapshot(2500);
                var b = new Bitmap(Math.Max(1, Width), Math.Max(1, Height), System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(b))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
                    using (var p = Gfx.Round(r, 10))
                    {
                        using (var lg = new LinearGradientBrush(r, Color.FromArgb(70, 60, 80, 130), Color.FromArgb(70, 140, 100, 160), 45f)) g.FillPath(lg, p);
                        if (shot != null)
                        {
                            float sc = Math.Min((Width - 6f) / shot.Width, (Height - 6f) / shot.Height);
                            var dst = new RectangleF((Width - shot.Width * sc) / 2, (Height - shot.Height * sc) / 2, shot.Width * sc, shot.Height * sc);
                            using (var ia = new System.Drawing.Imaging.ImageAttributes())
                            {
                                ia.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = on ? 1f : 0.4f });
                                g.DrawImage(shot, Rectangle.Round(dst), 0, 0, shot.Width, shot.Height, GraphicsUnit.Pixel, ia);
                            }
                        }
                    }
                }
                var old = small; small = b; shownOn = on; if (old != null) old.Dispose();
                Invalidate();
            }
            catch (Exception ex) { Program.Log(ex); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (small == null) return;
            if (shownOn != app.WantsShown(w)) { BeginInvoke(new Action(Make)); }   // the switch was flipped: fade or brighten right away
            e.Graphics.DrawImageUnscaled(small, 0, 0);
        }
    }
}
