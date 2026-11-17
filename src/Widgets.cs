// GlassDesk - widgets: clock, system monitor, calendar, notes.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace GlassDesk
{
    /// <summary>CPU / RAM / disk / battery sampling with cheap Win32 calls (no PerformanceCounter).</summary>
    internal class SysStats
    {
        public float Cpu;                       // 0..1
        public float[] CpuHistory = new float[40];
        public ulong RamTotal, RamUsed;
        public long DiskTotal, DiskFree; public string DiskName = "C:";
        public int Battery = -1; public bool Charging;
        long lastIdle, lastTotal;

        public void Sample()
        {
            long idle, kernel, user;
            if (Native.IsWindows && Native.GetSystemTimes(out idle, out kernel, out user))
            {
                long total = kernel + user;
                if (lastTotal != 0 && total != lastTotal)
                    Cpu = Math.Max(0, Math.Min(1, 1f - (float)(idle - lastIdle) / (total - lastTotal)));
                lastIdle = idle; lastTotal = total;
            }
            Array.Copy(CpuHistory, 1, CpuHistory, 0, CpuHistory.Length - 1);
            CpuHistory[CpuHistory.Length - 1] = Cpu;

            ulong t, a;
            if (Native.Memory(out t, out a)) { RamTotal = t; RamUsed = t - a; }
            try
            {
                string root = Path.GetPathRoot(Environment.SystemDirectory);
                var d = new DriveInfo(string.IsNullOrEmpty(root) ? "/" : root);
                DiskTotal = d.TotalSize; DiskFree = d.AvailableFreeSpace; DiskName = d.Name.TrimEnd('\\');
            }
            catch { }
            try
            {
                var ps = SystemInformation.PowerStatus;
                if ((ps.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) != 0 || ps.BatteryChargeStatus == BatteryChargeStatus.Unknown) Battery = -1;
                else { Battery = (int)Math.Round(ps.BatteryLifePercent * 100); Charging = ps.PowerLineStatus == PowerLineStatus.Online; }
            }
            catch { Battery = -1; }
        }
    }

    internal abstract class Widget : GlassWindow
    {
        protected Widget(App app, string id) : base(app, id) { Resizable = true; ZoomResize = true; }
        protected abstract Size BaseSize { get; }
        public override Size PreferredPanelSize(Size saved)
        {
            int pct;
            Zoom = Cfg.WidgetZoom.TryGetValue(Id, out pct) ? pct / 100f : 1f;
            Size gs = App.GroupSize(Id);   // stacked widgets all take the size of the biggest one
            if (!gs.IsEmpty) return new Size(gs.Width, gs.Height + TabH);
            return new Size(T.S(BaseSize.Width), T.S(BaseSize.Height) + TabH);
        }
        protected Rectangle Inner { get { int m = T.S(18), t = TabH; return new Rectangle(Panel.X + m, Panel.Y + T.S(14) + t, Panel.Width - 2 * m, Panel.Height - T.S(28) - t); } }
        /// <summary>Height of the tab strip a stacked widget shows on top (0 when not stacked).</summary>
        protected int TabH { get { int i, n; return App.GroupInfo(Id, out i, out n) ? T.S(40) : 0; } }
        protected override int AnimTop { get { return Panel.Y + TabH; } }
        /// <summary>This widget's own size at its zoom, without a tab strip.</summary>
        public Size NaturalSize() { int pct; float z = Cfg.WidgetZoom.TryGetValue(Id, out pct) ? pct / 100f : 1f; var t = App.Theme; return new Size((int)(t.S(BaseSize.Width) * z), (int)(t.S(BaseSize.Height) * z)); }
        protected override void PaintOverlay(Graphics g)
        {
            if (TabH > 0)
            {
                var strip = new RectangleF(Panel.X, Panel.Y, Panel.Width, TabH);
                using (var path = Gfx.Round(new RectangleF(Panel.X, Panel.Y, Panel.Width, Panel.Height), T.S(Cfg.Radius)))
                {
                    var st = g.Save(); g.SetClip(path, System.Drawing.Drawing2D.CombineMode.Intersect);
                    using (var b = new SolidBrush(Color.FromArgb(T.Dark ? 40 : 30, T.Dark ? Color.Black : Color.White))) g.FillRectangle(b, strip);
                    g.Restore(st);
                }
                using (var pen = new Pen(T.Border)) g.DrawLine(pen, Panel.X + T.S(12), Panel.Y + TabH, Panel.Right - T.S(12), Panel.Y + TabH);
                DrawTabs(g, new RectangleF(Panel.X + T.S(12), Panel.Y + T.S(4), Panel.Width - T.S(24), TabH - T.S(4)));
            }
            else tabRects.Clear();
            base.PaintOverlay(g);
        }
        protected void Txt(Graphics g, string s, Font f, Color c, RectangleF r, StringFormat sf)
        {
            if (Cfg.GlassStyle == "transparent")
                using (var sh = new SolidBrush(Color.FromArgb(T.Dark ? 150 : 110, 0, 0, 0)))
                    g.DrawString(s, f, sh, new RectangleF(r.X, r.Y + T.Sf(1.2f), r.Width, r.Height), sf);
            using (var b = new SolidBrush(c)) g.DrawString(s, f, b, r, sf);
        }
        protected override void OnClickAt(Point p, MouseButtons b)
        {
            if (b == MouseButtons.Right) App.ShowWidgetMenu(this);
        }

    }

    // ---------------------------------------------------------------- Clock
    internal class ClockWidget : Widget
    {
        public ClockWidget(App app) : base(app, "clock") { }
        protected override Size BaseSize { get { return new Size(320, 150); } }

        static readonly System.Collections.Generic.Dictionary<string, Bitmap> photos = new System.Collections.Generic.Dictionary<string, Bitmap>();
        static Bitmap Photo(string name)
        {
            Bitmap b;
            if (photos.TryGetValue(name, out b)) return b;
            try
            {
                using (var st = typeof(ClockWidget).Assembly.GetManifestResourceStream("GlassDesk.scene." + name + ".jpg"))
                    b = st == null ? null : new Bitmap(st);
            }
            catch { b = null; }
            photos[name] = b;
            return b;
        }

        /// <summary>A free (CC0) photo behind the clock that matches the time of day.</summary>
        bool PaintPhoto(Graphics g, DateTime now)
        {
            int h = now.Hour;
            string name = h >= 5 && h < 10 ? "morning" : h >= 10 && h < 17 ? "day" : h >= 17 && h < 20 ? "sunset" : "night";
            var img = Photo(name);
            if (img == null) return false;
            int t = TabH;
            var pr = new RectangleF(Panel.X + 1, Panel.Y + 1 + t, Panel.Width - 2, Panel.Height - 2 - t);
            if (pr.Width < 10 || pr.Height < 10) return true;
            var st = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var clip = Gfx.Round(pr, Math.Max(2, T.S(Cfg.Radius) - 1.5f)))
            {
                g.SetClip(clip, CombineMode.Intersect);
                // cover-crop: fill the panel, keep the picture's middle
                float sc = Math.Max(pr.Width / img.Width, pr.Height / img.Height);
                float sw = pr.Width / sc, sh = pr.Height / sc;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(img, pr, new RectangleF((img.Width - sw) / 2, (img.Height - sh) / 2, sw, sh), GraphicsUnit.Pixel);
                // shade top and bottom so the text stays readable on any photo
                using (var lg = new LinearGradientBrush(new PointF(0, pr.Top - 1), new PointF(0, pr.Bottom + 1), Color.FromArgb(90, 0, 0, 0), Color.FromArgb(130, 0, 0, 0)))
                {
                    lg.InterpolationColors = new ColorBlend { Colors = new[] { Color.FromArgb(95, 0, 0, 0), Color.FromArgb(25, 0, 0, 0), Color.FromArgb(140, 0, 0, 0) }, Positions = new[] { 0f, 0.5f, 1f } };
                    g.FillRectangle(lg, pr);
                }
            }
            g.Restore(st);
            return true;
        }

        /// <summary>A little landscape behind the clock: the sky's colors, the sun or moon and the stars follow
        /// the real time of day; two layers of mountains sit at the bottom.</summary>
        void PaintScene(Graphics g, DateTime now)
        {
            int t = TabH;
            var pr = new RectangleF(Panel.X + 1, Panel.Y + 1 + t, Panel.Width - 2, Panel.Height - 2 - t);
            if (pr.Width < 10 || pr.Height < 10) return;
            float h = (float)now.TimeOfDay.TotalHours;
            // sky colors through the day
            float[] at = { 0, 5, 6.5f, 9, 16.5f, 18.5f, 20.5f, 24 };
            Color[][] sky = {
                new[] { Color.FromArgb(10, 14, 40), Color.FromArgb(30, 38, 84) },     // night
                new[] { Color.FromArgb(18, 22, 60), Color.FromArgb(70, 60, 120) },    // before dawn
                new[] { Color.FromArgb(90, 110, 200), Color.FromArgb(255, 150, 110) },// sunrise
                new[] { Color.FromArgb(56, 140, 236), Color.FromArgb(160, 210, 255) },// day
                new[] { Color.FromArgb(56, 140, 236), Color.FromArgb(160, 210, 255) },// day
                new[] { Color.FromArgb(90, 70, 170), Color.FromArgb(255, 130, 90) },  // sunset
                new[] { Color.FromArgb(14, 18, 50), Color.FromArgb(50, 44, 100) },    // dusk
                new[] { Color.FromArgb(10, 14, 40), Color.FromArgb(30, 38, 84) } };
            int i = 0; while (i < at.Length - 2 && h >= at[i + 1]) i++;
            float f = (h - at[i]) / (at[i + 1] - at[i]);
            Func<Color, Color, float, Color> mix = (a, b, k) => Color.FromArgb((int)(a.R + (b.R - a.R) * k), (int)(a.G + (b.G - a.G) * k), (int)(a.B + (b.B - a.B) * k));
            Color top = mix(sky[i][0], sky[i + 1][0], f), bot = mix(sky[i][1], sky[i + 1][1], f);
            float night = h < 5.5f || h > 19.5f ? 1f : h < 7 ? (7 - h) / 1.5f : h > 18 ? (h - 18) / 1.5f : 0f;
            night = Math.Max(0, Math.Min(1, night));
            var st = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var clip = Gfx.Round(pr, Math.Max(2, T.S(Cfg.Radius) - 1.5f)))
            {
                g.SetClip(clip, CombineMode.Intersect);
                using (var lg = new LinearGradientBrush(new PointF(0, pr.Top - 1), new PointF(0, pr.Bottom + 1), Color.FromArgb(235, top), Color.FromArgb(235, bot))) g.FillRectangle(lg, pr);
                // stars
                if (night > 0.05f)
                {
                    var rnd = new Random(7);
                    for (int s = 0; s < 46; s++)
                    {
                        float sx = pr.X + (float)rnd.NextDouble() * pr.Width, sy = pr.Y + (float)rnd.NextDouble() * pr.Height * 0.65f, sz = T.Sf(0.8f + (float)rnd.NextDouble() * 1.6f);
                        using (var b = new SolidBrush(Color.FromArgb((int)(night * (90 + rnd.Next(150))), 255, 255, 255))) g.FillEllipse(b, sx, sy, sz, sz);
                    }
                }
                // sun by day, moon by night, travelling across the sky
                bool sun = h >= 6 && h < 19;
                float p = sun ? (h - 6) / 13f : ((h + 24 - 19) % 24) / 11f;
                float bx = pr.X + pr.Width * (0.56f + 0.38f * p), by = pr.Bottom - pr.Height * (0.34f + 0.42f * (float)Math.Sin(Math.PI * p));   // right side, away from the time
                float br = T.Sf(sun ? 15 : 12);
                if (sun)
                {
                    Gfx.Glow(g, new PointF(bx, by), br * 4.5f, Color.FromArgb(140, 255, 214, 140));
                    using (var b = new SolidBrush(Color.FromArgb(255, 255, 236, 180))) g.FillEllipse(b, bx - br, by - br, br * 2, br * 2);
                }
                else
                {
                    Gfx.Glow(g, new PointF(bx, by), br * 3.5f, Color.FromArgb(70, 200, 210, 255));
                    using (var b = new SolidBrush(Color.FromArgb(255, 236, 240, 255))) g.FillEllipse(b, bx - br, by - br, br * 2, br * 2);
                    using (var b = new SolidBrush(Color.FromArgb(255, top))) g.FillEllipse(b, bx - br + br * 0.55f, by - br - br * 0.2f, br * 2, br * 2);   // crescent
                }
                // mountains: a far, lighter range and a near, darker one
                DrawRange(g, pr, 0.62f, 0.20f, 3, mix(bot, top, 0.55f), 200);
                DrawRange(g, pr, 0.78f, 0.16f, 11, mix(top, Color.Black, 0.55f), 235);
                // darken the top a little so the time stays readable
                using (var lg = new LinearGradientBrush(new PointF(0, pr.Top - 1), new PointF(0, pr.Top + pr.Height * 0.6f), Color.FromArgb(60, 0, 0, 0), Color.FromArgb(0, 0, 0, 0))) g.FillRectangle(lg, pr.X, pr.Y, pr.Width, pr.Height * 0.6f);
            }
            g.Restore(st);
        }
        void DrawRange(Graphics g, RectangleF pr, float baseY, float amp, int seed, Color c, int alpha)
        {
            var rnd = new Random(seed);
            var pts = new List<PointF> { new PointF(pr.X, pr.Bottom) };
            int n = 9;
            for (int k = 0; k <= n; k++)
            {
                float x = pr.X + pr.Width * k / n, y = pr.Y + pr.Height * (baseY - amp * (float)rnd.NextDouble());
                pts.Add(new PointF(x, y));
            }
            pts.Add(new PointF(pr.Right, pr.Bottom));
            using (var path = new GraphicsPath())
            {
                path.AddCurve(pts.GetRange(1, pts.Count - 2).ToArray(), 0.35f);
                path.AddLine(pr.Right, pr.Bottom, pr.X, pr.Bottom);
                path.CloseFigure();
                using (var b = new SolidBrush(Color.FromArgb(alpha, c))) g.FillPath(b, path);
            }
        }

        protected override void PaintContent(Graphics g)
        {
            DateTime now = App.Now;
            Rectangle r = Inner;
            bool scene = Cfg.ClockStyle == "photo" || Cfg.ClockStyle == "drawn";
            if (Cfg.ClockStyle == "photo") { if (!PaintPhoto(g, now)) PaintScene(g, now); }
            else if (scene) PaintScene(g, now);
            string greet = now.Hour < 5 ? "Good night" : now.Hour < 12 ? "Good morning" : now.Hour < 18 ? "Good afternoon" : "Good evening";
            Color ink = scene ? Color.White : T.Text, sub = scene ? Color.FromArgb(225, 255, 255, 255) : T.SubText;
            Txt(g, greet, Gfx.F(T.FontName, T.Sf(13), FontStyle.Regular), sub, new RectangleF(r.X, r.Y, r.Width, T.Sf(20)), Gfx.Left);

            string time = Cfg.Clock24 ? now.ToString("HH:mm", CultureInfo.InvariantCulture) : now.ToString("h:mm", CultureInfo.InvariantCulture);
            var big = Gfx.F(T.DisplayFont, T.Sf(58), FontStyle.Regular);
            float y = r.Y + T.Sf(18);
            SizeF ts = g.MeasureString(time, big, PointF.Empty, StringFormat.GenericTypographic);
            if (scene) using (var b = new SolidBrush(Color.FromArgb(70, 0, 0, 0))) g.DrawString(time, big, b, new PointF(r.X - T.Sf(1), y + T.Sf(2)), StringFormat.GenericTypographic);
            using (var b = new SolidBrush(ink)) g.DrawString(time, big, b, new PointF(r.X - T.Sf(2), y), StringFormat.GenericTypographic);
            string extra = "";
            if (Cfg.ShowSeconds) extra = now.ToString(":ss", CultureInfo.InvariantCulture);
            if (!Cfg.Clock24) extra += (extra.Length > 0 ? " " : " ") + now.ToString("tt", CultureInfo.InvariantCulture);
            if (extra.Length > 0)
            {
                var sm = Gfx.F(T.DisplayFont, T.Sf(22), FontStyle.Regular);
                using (var b = new SolidBrush(T.Accent))
                    g.DrawString(extra.Trim(), sm, b, new PointF(r.X + ts.Width + T.Sf(6), y + ts.Height - T.Sf(32)), StringFormat.GenericTypographic);
            }
            string date = now.ToString("dddd, MMMM d", CultureInfo.CurrentCulture);
            Txt(g, date, Gfx.F(T.FontName, T.Sf(14.5f), FontStyle.Bold), ink, new RectangleF(r.X, r.Bottom - T.Sf(22), r.Width, T.Sf(22)), Gfx.Left);
            Txt(g, "Week " + CultureInfo.CurrentCulture.Calendar.GetWeekOfYear(now, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday),
                Gfx.F(T.FontName, T.Sf(12), FontStyle.Regular), sub, new RectangleF(r.X, r.Bottom - T.Sf(22), r.Width, T.Sf(22)), Gfx.Right);
        }
    }

    // ---------------------------------------------------------------- System
    internal class SystemWidget : Widget
    {
        public SystemWidget(App app) : base(app, "system") { }
        protected override Size BaseSize { get { return new Size(320, App.Stats.Battery >= 0 ? 226 : 188); } }

        static string GB(double bytes) { return (bytes / 1073741824.0).ToString(bytes >= 107374182400.0 ? "0" : "0.0", CultureInfo.InvariantCulture) + " GB"; }

        protected override void PaintContent(Graphics g)
        {
            var st = App.Stats;
            Rectangle r = Inner;
            Title(g, "System", new Rectangle(r.X, r.Y, r.Width, T.S(20)));
            Txt(g, "live", Gfx.F(T.FontName, T.Sf(11), FontStyle.Regular), T.SubText, new RectangleF(r.X, r.Y, r.Width - T.Sf(12), T.Sf(20)), Gfx.Right);
            using (var b = new SolidBrush(Color.FromArgb(255, 70, 210, 130))) g.FillEllipse(b, r.Right - T.Sf(7), r.Y + T.Sf(7), T.Sf(6), T.Sf(6));

            float y = r.Y + T.Sf(30);
            var label = Gfx.F(T.FontName, T.Sf(13), FontStyle.Bold);
            var value = Gfx.F(T.FontName, T.Sf(12.5f), FontStyle.Regular);
            float rowH = T.Sf(38);

            // CPU with sparkline
            Txt(g, "CPU", label, T.Text, new RectangleF(r.X, y, r.Width, T.Sf(18)), Gfx.Left);
            Txt(g, (st.Cpu * 100).ToString("0") + "%", value, T.SubText, new RectangleF(r.X, y, r.Width, T.Sf(18)), Gfx.Right);
            var spark = new RectangleF(r.X, y + T.Sf(20), r.Width, T.Sf(16));
            DrawSpark(g, spark, st.CpuHistory);
            y += rowH + T.Sf(4);

            Row(g, "Memory", st.RamTotal > 0 ? GB(st.RamUsed) + " / " + GB(st.RamTotal) : "—", st.RamTotal > 0 ? (float)st.RamUsed / st.RamTotal : 0, ref y, label, value, r);
            Row(g, "Disk " + st.DiskName, st.DiskTotal > 0 ? GB(st.DiskFree) + " free" : "—", st.DiskTotal > 0 ? 1f - (float)st.DiskFree / st.DiskTotal : 0, ref y, label, value, r);
            if (st.Battery >= 0)
                Row(g, "Battery", st.Battery + "%" + (st.Charging ? " · charging" : ""), st.Battery / 100f, ref y, label, value, r);
        }

        void Row(Graphics g, string name, string val, float frac, ref float y, Font label, Font value, Rectangle r)
        {
            Txt(g, name, label, T.Text, new RectangleF(r.X, y, r.Width, T.Sf(18)), Gfx.Left);
            Txt(g, val, value, T.SubText, new RectangleF(r.X, y, r.Width, T.Sf(18)), Gfx.Right);
            Color c = frac > 0.9f ? Color.FromArgb(255, 240, 96, 96) : T.Accent;
            Bar(g, new RectangleF(r.X, y + T.Sf(23), r.Width, T.Sf(6)), frac, c);
            y += T.Sf(38);
        }

        void DrawSpark(Graphics g, RectangleF r, float[] h)
        {
            Gfx.FillRound(g, T.Track, new RectangleF(r.X, r.Bottom - T.Sf(1), r.Width, T.Sf(1)), 0);
            var pts = new PointF[h.Length];
            for (int i = 0; i < h.Length; i++)
                pts[i] = new PointF(r.X + r.Width * i / (h.Length - 1), r.Bottom - 1 - (r.Height - 2) * h[i]);
            using (var path = new GraphicsPath())
            {
                path.AddLines(pts);
                path.AddLine(r.Right, r.Bottom, r.X, r.Bottom);
                path.CloseFigure();
                using (var lg = new LinearGradientBrush(new PointF(0, r.Y - 1), new PointF(0, r.Bottom + 1), Gfx.WithAlpha(T.Accent, 110), Gfx.WithAlpha(T.Accent, 0)))
                    g.FillPath(lg, path);
            }
            using (var pen = new Pen(T.Accent, T.Sf(1.6f)) { LineJoin = LineJoin.Round }) g.DrawLines(pen, pts);
        }
    }

    // ---------------------------------------------------------------- Calendar
    internal class CalendarWidget : Widget
    {
        int monthOffset;
        public CalendarWidget(App app) : base(app, "calendar") { }
        protected override Size BaseSize { get { return new Size(320, 288); } }

        Rectangle PrevBtn { get { Rectangle r = Inner; return new Rectangle(r.Right - T.S(56), r.Y - T.S(2), T.S(26), T.S(26)); } }
        Rectangle NextBtn { get { Rectangle r = Inner; return new Rectangle(r.Right - T.S(26), r.Y - T.S(2), T.S(26), T.S(26)); } }

        protected override bool IsDragArea(Point p) { return !PrevBtn.Contains(p) && !NextBtn.Contains(p); }

        protected override void PaintContent(Graphics g)
        {
            DateTime today = App.Now.Date;
            DateTime first = new DateTime(today.Year, today.Month, 1).AddMonths(monthOffset);
            Rectangle r = Inner;
            string head = first.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
            Txt(g, head, Gfx.F(T.FontName, T.Sf(15), FontStyle.Bold), T.Text, new RectangleF(r.X, r.Y - T.Sf(2), r.Width - T.Sf(60), T.Sf(26)), Gfx.Left);
            Arrow(g, PrevBtn, true); Arrow(g, NextBtn, false);

            DayOfWeek fdow = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
            float top = r.Y + T.Sf(34);
            float cw = r.Width / 7f, ch = T.Sf(31);
            var small = Gfx.F(T.FontName, T.Sf(11), FontStyle.Bold);
            var dayF = Gfx.F(T.FontName, T.Sf(13), FontStyle.Regular);
            var dayB = Gfx.F(T.FontName, T.Sf(13), FontStyle.Bold);
            for (int i = 0; i < 7; i++)
            {
                var dow = (DayOfWeek)(((int)fdow + i) % 7);
                string n = CultureInfo.CurrentCulture.DateTimeFormat.GetShortestDayName(dow);
                if (n.Length > 2) n = n.Substring(0, 2);
                Txt(g, n, small, T.SubText, new RectangleF(r.X + i * cw, top, cw, T.Sf(18)), Gfx.Center);
            }
            top += T.Sf(24);
            int lead = ((int)first.DayOfWeek - (int)fdow + 7) % 7;
            DateTime d = first.AddDays(-lead);
            for (int row = 0; row < 6; row++)
                for (int col = 0; col < 7; col++, d = d.AddDays(1))
                {
                    var cell = new RectangleF(r.X + col * cw, top + row * ch, cw, ch);
                    bool inMonth = d.Month == first.Month;
                    bool isToday = d == today;
                    if (isToday)
                    {
                        float s = Math.Min(cw, ch) - T.Sf(4);
                        using (var b = new SolidBrush(T.Accent)) g.FillEllipse(b, cell.X + (cw - s) / 2, cell.Y + (ch - s) / 2, s, s);
                        Txt(g, d.Day.ToString(), dayB, Color.White, cell, Gfx.Center);
                    }
                    else
                    {
                        bool weekend = d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday;
                        Color c = !inMonth ? Gfx.WithAlpha(T.SubText, 70) : weekend ? T.SubText : T.Text;
                        Txt(g, d.Day.ToString(), dayF, c, cell, Gfx.Center);
                    }
                }
        }

        void Arrow(Graphics g, Rectangle b, bool left)
        {
            Gfx.FillRound(g, T.Hover, b, T.Sf(8));
            float cx = b.X + b.Width / 2f, cy = b.Y + b.Height / 2f, s = T.Sf(3.5f);
            using (var pen = new Pen(T.Text, T.Sf(1.6f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                if (left) g.DrawLines(pen, new[] { new PointF(cx + s / 2, cy - s), new PointF(cx - s / 2, cy), new PointF(cx + s / 2, cy + s) });
                else g.DrawLines(pen, new[] { new PointF(cx - s / 2, cy - s), new PointF(cx + s / 2, cy), new PointF(cx - s / 2, cy + s) });
            }
        }

        protected override void OnClickAt(Point p, MouseButtons b)
        {
            if (b == MouseButtons.Left && PrevBtn.Contains(p)) { monthOffset--; Redraw(); }
            else if (b == MouseButtons.Left && NextBtn.Contains(p)) { monthOffset++; Redraw(); }
            else base.OnClickAt(p, b);
        }
        protected override void OnDoubleClickAt(Point p)
        {
            if (!PrevBtn.Contains(p) && !NextBtn.Contains(p)) { monthOffset = 0; Redraw(); }
        }
    }

    // ---------------------------------------------------------------- Notes
    internal class NotesWidget : Widget
    {
        public NotesWidget(App app) : base(app, "notes") { }
        protected override Size BaseSize { get { return new Size(320, 220); } }

        protected override void PaintContent(Graphics g)
        {
            Rectangle r = Inner;
            Title(g, "Notes", new Rectangle(r.X, r.Y, r.Width, T.S(20)));
            Txt(g, "double-click to edit", Gfx.F(T.FontName, T.Sf(11), FontStyle.Regular), T.SubText, new RectangleF(r.X, r.Y, r.Width, T.Sf(20)), Gfx.Right);
            var body = new RectangleF(r.X, r.Y + T.Sf(30), r.Width, r.Height - T.Sf(30));
            string text = string.IsNullOrEmpty(Cfg.NotesText) ? "Empty note" : Cfg.NotesText;
            Txt(g, text, Gfx.F(T.FontName, T.Sf(14), FontStyle.Regular), string.IsNullOrEmpty(Cfg.NotesText) ? T.SubText : T.Text, body, Gfx.TopLeftWrap);
        }

        protected override void OnDoubleClickAt(Point p) { Edit(); }

        public void Edit()
        {
            var f = new NoteEditor(App, PanelBounds, Cfg.NotesText);
            f.FormClosed += delegate { Cfg.NotesText = f.Result; Cfg.Save(); Redraw(); };
            f.Show();
            f.Activate();
        }
    }

    internal class NoteEditor : Form
    {
        readonly TextBox box;
        public string Result;
        bool closing;
        public NoteEditor(App app, Rectangle bounds, string text) : this(app, bounds, text, "Esc or click outside to save") { }
        public NoteEditor(App app, Rectangle bounds, string text, string hintText)
        {
            Theme t = app.Theme;
            Result = text;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            Bounds = bounds; TopMost = true;
            BackColor = t.Dark ? Color.FromArgb(28, 30, 38) : Color.FromArgb(250, 250, 252);
            Padding = new Padding(t.S(16));
            var hint = new Label { Dock = DockStyle.Bottom, Height = t.S(22), Text = hintText, ForeColor = t.Dark ? Color.FromArgb(150, 160, 175) : Color.Gray, Font = Gfx.SafeFont(t.FontName, t.Sf(11), FontStyle.Regular) };
            box = new TextBox
            {
                Multiline = true, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, Text = (text ?? "").Replace("\n", "\r\n"),
                BackColor = BackColor, ForeColor = t.Dark ? Color.FromArgb(240, 242, 248) : Color.FromArgb(20, 22, 28),
                Font = Gfx.SafeFont(t.FontName, t.Sf(14), FontStyle.Regular), ScrollBars = ScrollBars.Vertical, AcceptsReturn = true
            };
            Controls.Add(box); Controls.Add(hint);
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Finish(); };
            Deactivate += delegate { Finish(); };
            Load += delegate { if (Native.IsWindows) Native.SetRoundCorners(Handle); box.SelectionStart = box.TextLength; };
        }
        void Finish()
        {
            if (closing) return;
            closing = true;
            Result = box.Text.Replace("\r\n", "\n");
            Close();
        }
    }

    /// <summary>Tiny themed text prompt.</summary>
    internal static class InputBox
    {
        public static string Ask(string title, string value, Theme t)
        {
            using (var f = new Form())
            {
                f.Text = title; f.FormBorderStyle = FormBorderStyle.FixedDialog; f.MinimizeBox = f.MaximizeBox = false;
                f.StartPosition = FormStartPosition.CenterScreen; f.ClientSize = new Size(t.S(360), t.S(120)); f.TopMost = true;
                f.BackColor = t.Dark ? Color.FromArgb(32, 34, 42) : Color.FromArgb(246, 247, 250);
                f.ForeColor = t.Dark ? Color.White : Color.Black;
                f.Font = Gfx.SafeFont(t.FontName, t.Sf(13), FontStyle.Regular);
                var tb = new TextBox { Text = value, Left = t.S(16), Top = t.S(18), Width = t.S(328), BorderStyle = BorderStyle.FixedSingle };
                var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, Left = t.S(170), Top = t.S(68), Width = t.S(84), Height = t.S(32), FlatStyle = FlatStyle.Flat, BackColor = t.Accent, ForeColor = Color.White };
                var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = t.S(262), Top = t.S(68), Width = t.S(84), Height = t.S(32), FlatStyle = FlatStyle.Flat };
                ok.FlatAppearance.BorderSize = 0;
                f.Controls.Add(tb); f.Controls.Add(ok); f.Controls.Add(cancel);
                f.AcceptButton = ok; f.CancelButton = cancel;
                f.Load += delegate { Native.ThemeTitleBar(f.Handle, t.Dark, f.BackColor, f.ForeColor); };
                return f.ShowDialog() == DialogResult.OK ? tb.Text : null;
            }
        }
    }
}
