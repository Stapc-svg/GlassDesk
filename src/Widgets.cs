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
        /// <summary>Every drive to show: the Windows drive first, then other internal and USB drives.</summary>
        public System.Collections.Generic.List<Tuple<string, long, long>> Drives = new System.Collections.Generic.List<Tuple<string, long, long>>();
        int driveTick;
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
            // other drives change rarely, so look at them every 5th sample
            if (driveTick++ % 5 == 0 || Drives.Count == 0)
            {
                var list = new System.Collections.Generic.List<Tuple<string, long, long>>();
                list.Add(Tuple.Create(DiskName, DiskTotal, DiskFree));
                try
                {
                    foreach (var d in DriveInfo.GetDrives())
                        try
                        {
                            if (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable) continue;
                            if (!d.IsReady || d.TotalSize <= 0) continue;
                            string n = d.Name.TrimEnd('\\');
                            if (n == DiskName || n == "/" && DiskName == "/") continue;
                            string label = n;
                            try { if (!string.IsNullOrEmpty(d.VolumeLabel) && d.VolumeLabel.Length <= 14) label = n + " " + d.VolumeLabel; } catch { }
                            list.Add(Tuple.Create(label, d.TotalSize, d.AvailableFreeSpace));
                        }
                        catch { }
                }
                catch { }
                if (list.Count > 6) list.RemoveRange(6, list.Count - 6);
                Drives = list;
            }
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
        protected override bool MacLook { get { return Cfg.StyleOf(Id) == "mac"; } }
        /// <summary>Each widget's color in the macOS style, like the app it stands for (Reminders blue, Notes yellow...).</summary>
        protected virtual Color MacColor { get { return Color.Empty; } }
        Theme macMine, macFrom;
        protected override Theme MacBase()
        {
            Theme b = App.MacTheme;
            if (MacColor.IsEmpty) return b;
            if (macMine == null || !ReferenceEquals(macFrom, b)) { macMine = b.WithAccent(MacColor); macFrom = b; }
            return macMine;
        }
        public bool IsMac { get { return MacLook; } }
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
                using (var path = Gfx.Round(new RectangleF(Panel.X, Panel.Y, Panel.Width, Panel.Height), T.S(PanelRadius)))
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
            if (Cfg.GlassStyle == "transparent" || MacLook && T.Dark)
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
        protected override Color MacColor { get { return Color.FromArgb(255, 255, 149, 0); } }
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
            // half picture, half glass: the photo fills the top and fades into the panel below
            SceneArt.DrawFaded(g, pr, Math.Max(2, T.S(PanelRadius) - 1.5f), "clock:" + name, (pw, ph) =>
            {
                var c = SceneArt.Cover(img, pw, ph);
                using (var cg = Graphics.FromImage(c))
                using (var lg = new LinearGradientBrush(new Point(0, -1), new Point(0, ph / 2 + 1), Color.FromArgb(80, 0, 0, 0), Color.FromArgb(0, 0, 0, 0)))
                    cg.FillRectangle(lg, 0, 0, pw, ph / 2);
                return c;
            }, 0.40f, 0.86f);
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
            using (var clip = Gfx.Round(pr, Math.Max(2, T.S(PanelRadius) - 1.5f)))
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

        /// <summary>macOS style: a round clock face with an orange second hand, the time and date beside it.</summary>
        void PaintMac(Graphics g, DateTime now)
        {
            Rectangle r = Inner;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float d = Math.Min(r.Height + T.Sf(8), r.Width * 0.46f);
            var face = new RectangleF(r.X - T.Sf(4), r.Y + (r.Height - d) / 2f, d, d);
            float cx = face.X + d / 2, cy = face.Y + d / 2, rad = d / 2;
            Color dial = T.Dark ? Color.FromArgb(255, 18, 18, 20) : Color.FromArgb(255, 246, 246, 248);
            Color ink = T.Dark ? Color.White : Color.FromArgb(255, 29, 29, 31);
            using (var b = new SolidBrush(dial)) g.FillEllipse(b, face);
            using (var pen = new Pen(T.Dark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(24, 0, 0, 0), T.Sf(1))) g.DrawEllipse(pen, face);
            var nf = Gfx.F(T.DisplayFont, Math.Max(6f, rad * 0.2f), FontStyle.Bold);
            for (int i = 1; i <= 12; i++)
            {
                double a = Math.PI * 2 * i / 12 - Math.PI / 2;
                float nx = cx + (float)Math.Cos(a) * rad * 0.76f, ny = cy + (float)Math.Sin(a) * rad * 0.76f;
                Txt(g, i.ToString(), nf, ink, new RectangleF(nx - rad * 0.2f, ny - rad * 0.16f, rad * 0.4f, rad * 0.32f), Gfx.Center);
            }
            for (int i = 0; i < 60; i++)
            {
                if (i % 5 == 0) continue;
                double a = Math.PI * 2 * i / 60;
                float x1 = cx + (float)Math.Cos(a) * rad * 0.93f, y1 = cy + (float)Math.Sin(a) * rad * 0.93f;
                using (var b = new SolidBrush(Color.FromArgb(110, ink))) g.FillEllipse(b, x1 - T.Sf(0.7f), y1 - T.Sf(0.7f), T.Sf(1.4f), T.Sf(1.4f));
            }
            double sec = now.Second, min = now.Minute + sec / 60, hr = now.Hour % 12 + min / 60;
            Action<double, float, float, Color> hand = (frac, len, w, c) =>
            {
                double a = Math.PI * 2 * frac - Math.PI / 2;
                using (var pen = new Pen(c, w) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(pen, cx - (float)Math.Cos(a) * rad * 0.1f, cy - (float)Math.Sin(a) * rad * 0.1f, cx + (float)Math.Cos(a) * rad * len, cy + (float)Math.Sin(a) * rad * len);
            };
            hand(hr / 12, 0.5f, Math.Max(2f, rad * 0.075f), ink);
            hand(min / 60, 0.78f, Math.Max(1.6f, rad * 0.055f), ink);
            Color orange = Color.FromArgb(255, 255, 149, 0);
            if (Cfg.ShowSeconds) hand(sec / 60, 0.86f, Math.Max(1f, rad * 0.02f), orange);
            using (var b = new SolidBrush(Cfg.ShowSeconds ? orange : ink)) g.FillEllipse(b, cx - rad * 0.06f, cy - rad * 0.06f, rad * 0.12f, rad * 0.12f);
            using (var b = new SolidBrush(dial)) g.FillEllipse(b, cx - rad * 0.025f, cy - rad * 0.025f, rad * 0.05f, rad * 0.05f);

            // right side: day, big time, date
            float x = face.Right + T.Sf(16), tw = r.Right - x;
            Txt(g, now.ToString("dddd", CultureInfo.CurrentCulture).ToUpper(CultureInfo.CurrentCulture), Gfx.F(T.FontName, T.Sf(11.5f), FontStyle.Bold), orange,
                new RectangleF(x, r.Y + T.Sf(4), tw, T.Sf(18)), Gfx.Left);
            string time = Cfg.Clock24 ? now.ToString("HH:mm", CultureInfo.InvariantCulture) : now.ToString("h:mm", CultureInfo.InvariantCulture);
            Txt(g, time, Gfx.F(T.DisplayFont, T.Sf(38), FontStyle.Regular), T.Text, new RectangleF(x - T.Sf(3), r.Y + T.Sf(20), tw + T.Sf(6), T.Sf(52)), Gfx.Left);
            string date = now.ToString("d MMMM", CultureInfo.CurrentCulture) + (Cfg.Clock24 ? "" : "  " + now.ToString("tt", CultureInfo.InvariantCulture));
            Txt(g, date, Gfx.F(T.FontName, T.Sf(13), FontStyle.Regular), T.SubText, new RectangleF(x, r.Y + T.Sf(74), tw, T.Sf(20)), Gfx.Left);
        }

        protected override void PaintContent(Graphics g)
        {
            DateTime now = App.Now;
            if (MacLook) { PaintMac(g, now); return; }
            Rectangle r = Inner;
            bool scene = Cfg.ClockStyle == "photo" || Cfg.ClockStyle == "drawn";
            if (Cfg.ClockStyle == "photo") { if (!PaintPhoto(g, now)) PaintScene(g, now); }
            else if (scene) PaintScene(g, now);
            string greet = Lang.L(now.Hour < 5 ? "Good night" : now.Hour < 12 ? "Good morning" : now.Hour < 18 ? "Good afternoon" : "Good evening");
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
            Color low = Cfg.ClockStyle == "photo" ? T.Text : ink, lowSub = Cfg.ClockStyle == "photo" ? T.SubText : sub;
            Txt(g, date, Gfx.F(T.FontName, T.Sf(14.5f), FontStyle.Bold), low, new RectangleF(r.X, r.Bottom - T.Sf(22), r.Width, T.Sf(22)), Gfx.Left);
            Txt(g, string.Format(Lang.L("Week {0}"), CultureInfo.CurrentCulture.Calendar.GetWeekOfYear(now, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday)),
                Gfx.F(T.FontName, T.Sf(12), FontStyle.Regular), lowSub, new RectangleF(r.X, r.Bottom - T.Sf(22), r.Width, T.Sf(22)), Gfx.Right);
        }
    }

    // ---------------------------------------------------------------- System
    internal class SystemWidget : Widget
    {
        protected override Color MacColor { get { return Color.FromArgb(255, 48, 209, 88); } }
        public SystemWidget(App app) : base(app, "system") { }
        int ExtraDrives { get { return App.Settings.AllDrives ? Math.Max(0, App.Stats.Drives.Count - 1) : 0; } }
        int MacCount { get { return 2 + (App.Settings.AllDrives ? Math.Max(1, App.Stats.Drives.Count) : 1) + (App.Stats.Battery >= 0 ? 1 : 0); } }
        protected override Size BaseSize { get { return MacLook ? new Size(320, 52 + 98 * ((MacCount + 3) / 4)) : new Size(320, (App.Stats.Battery >= 0 ? 226 : 188) + 38 * ExtraDrives); } }

        /// <summary>macOS style: round gauges like the Batteries widget on a Mac.</summary>
        void PaintMac(Graphics g)
        {
            var st = App.Stats;
            Rectangle r = Inner;
            Title(g, Lang.L("System"), new Rectangle(r.X, r.Y, r.Width, T.S(20)));
            var items = new List<Tuple<string, string, float, Color>>();
            items.Add(Tuple.Create(Lang.L("CPU"), (st.Cpu * 100).ToString("0") + "%", st.Cpu, Color.FromArgb(255, 48, 209, 88)));
            items.Add(Tuple.Create(Lang.L("Memory"), st.RamTotal > 0 ? ((float)st.RamUsed / st.RamTotal * 100).ToString("0") + "%" : "—", st.RamTotal > 0 ? (float)st.RamUsed / st.RamTotal : 0, Color.FromArgb(255, 10, 132, 255)));
            var drives = App.Settings.AllDrives && st.Drives.Count > 0 ? st.Drives : new List<Tuple<string, long, long>> { Tuple.Create(st.DiskName, st.DiskTotal, st.DiskFree) };
            foreach (var d in drives)
            {
                float used = d.Item2 > 0 ? 1f - (float)d.Item3 / d.Item2 : 0;
                items.Add(Tuple.Create(d.Item1.Split(' ')[0], GB(d.Item3), used, Color.FromArgb(255, 255, 159, 10)));
            }
            if (st.Battery >= 0) items.Add(Tuple.Create(st.Charging ? "⚡" : Lang.L("Battery"), st.Battery + "%", st.Battery / 100f, st.Battery <= 20 ? Color.FromArgb(255, 255, 69, 58) : Color.FromArgb(255, 48, 209, 88)));
            float cw = r.Width / 4f, d0 = Math.Min(T.Sf(58), cw - T.Sf(12));
            for (int i = 0; i < items.Count; i++)
            {
                int row = i / 4, col = i % 4;
                float cx = r.X + cw * col + cw / 2, top = r.Y + T.Sf(32) + row * T.Sf(98);
                var ring = new RectangleF(cx - d0 / 2, top, d0, d0);
                float pw = T.Sf(5.5f);
                var inner = RectangleF.Inflate(ring, -pw / 2, -pw / 2);
                var it = items[i];
                using (var pen = new Pen(Color.FromArgb(T.Dark ? 50 : 34, it.Item4), pw)) g.DrawEllipse(pen, inner);
                float sweep = 360 * Math.Max(0, Math.Min(1, it.Item3));
                Color c = it.Item3 > 0.9f && i > 0 && i < items.Count - (st.Battery >= 0 ? 1 : 0) ? Color.FromArgb(255, 255, 69, 58) : it.Item4;
                if (sweep > 1) using (var pen = new Pen(c, pw) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(pen, inner, -90, sweep);
                Txt(g, it.Item2, Gfx.F(T.FontName, T.Sf(it.Item2.Length > 5 ? 9.5f : 12), FontStyle.Bold), T.Text, ring, Gfx.Center);
                Txt(g, it.Item1, Gfx.F(T.FontName, T.Sf(11), FontStyle.Regular), T.SubText, new RectangleF(cx - cw / 2, ring.Bottom + T.Sf(4), cw, T.Sf(18)), Gfx.Center);
            }
        }
        int shownExtra = -1;

        static string GB(double bytes) { return (bytes / 1073741824.0).ToString(bytes >= 107374182400.0 ? "0" : "0.0", CultureInfo.InvariantCulture) + " GB"; }

        protected override void PaintContent(Graphics g)
        {
            if (MacLook) { PaintMac(g); if (shownExtra != MacCount + 100) { bool first = shownExtra < 0; shownExtra = MacCount + 100; if (!first) BeginInvoke(new Action(delegate { App.PlaceWindow(this); })); } return; }
            var st = App.Stats;
            Rectangle r = Inner;
            Title(g, Lang.L("System"), new Rectangle(r.X, r.Y, r.Width, T.S(20)));
            Txt(g, Lang.L("live"), Gfx.F(T.FontName, T.Sf(11), FontStyle.Regular), T.SubText, new RectangleF(r.X, r.Y, r.Width - T.Sf(12), T.Sf(20)), Gfx.Right);
            using (var b = new SolidBrush(Color.FromArgb(255, 70, 210, 130))) g.FillEllipse(b, r.Right - T.Sf(7), r.Y + T.Sf(7), T.Sf(6), T.Sf(6));

            float y = r.Y + T.Sf(30);
            var label = Gfx.F(T.FontName, T.Sf(13), FontStyle.Bold);
            var value = Gfx.F(T.FontName, T.Sf(12.5f), FontStyle.Regular);
            float rowH = T.Sf(38);

            // CPU with sparkline
            Txt(g, Lang.L("CPU"), label, T.Text, new RectangleF(r.X, y, r.Width, T.Sf(18)), Gfx.Left);
            Txt(g, (st.Cpu * 100).ToString("0") + "%", value, T.SubText, new RectangleF(r.X, y, r.Width, T.Sf(18)), Gfx.Right);
            var spark = new RectangleF(r.X, y + T.Sf(20), r.Width, T.Sf(16));
            DrawSpark(g, spark, st.CpuHistory);
            y += rowH + T.Sf(4);

            Row(g, "Memory", st.RamTotal > 0 ? GB(st.RamUsed) + " / " + GB(st.RamTotal) : "—", st.RamTotal > 0 ? (float)st.RamUsed / st.RamTotal : 0, ref y, label, value, r);
            if (!App.Settings.AllDrives || st.Drives.Count == 0)
                Row(g, Lang.L("Disk") + " " + st.DiskName, st.DiskTotal > 0 ? GB(st.DiskFree) + " " + Lang.L("free") : "—", st.DiskTotal > 0 ? 1f - (float)st.DiskFree / st.DiskTotal : 0, ref y, label, value, r);
            else
                foreach (var d in st.Drives)
                    Row(g, Lang.L("Disk") + " " + d.Item1, GB(d.Item3) + " " + Lang.L("free"), d.Item2 > 0 ? 1f - (float)d.Item3 / d.Item2 : 0, ref y, label, value, r);
            // a drive was plugged in or taken out: grow or shrink to fit
            if (shownExtra != ExtraDrives) { bool first = shownExtra < 0; shownExtra = ExtraDrives; if (!first) BeginInvoke(new Action(delegate { App.PlaceWindow(this); })); }
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
        List<CalEvent> events = new List<CalEvent>();
        string feedStatus = "";
        System.Windows.Forms.Timer feedTimer;
        protected override Color MacColor { get { return Color.FromArgb(255, 255, 59, 48); } }
        public CalendarWidget(App app) : base(app, "calendar") { }
        bool HasFeed { get { return Cfg.CalendarFeeds.Count > 0 || App.Preview && events.Count > 0; } }
        protected override Size BaseSize { get { return new Size(320, HasFeed ? 356 : 288); } }

        public void SetSample()
        {
            var t = App.Now.Date;
            events = new List<CalEvent> {
                new CalEvent { Start = t.AddHours(15).AddMinutes(30), Title = "Team call" },
                new CalEvent { Start = t.AddDays(2), AllDay = true, Title = "Mom's birthday" },
                new CalEvent { Start = t.AddDays(6).AddHours(10), Title = "Dentist" } };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (App.Preview) return;
            feedTimer = new System.Windows.Forms.Timer { Interval = 15 * 60 * 1000 };
            feedTimer.Tick += delegate { Fetch(); };
            feedTimer.Start();
            Fetch();
        }
        protected override void Dispose(bool disposing) { if (disposing && feedTimer != null) feedTimer.Dispose(); base.Dispose(disposing); }

        /// <summary>Download the connected calendars in the background.</summary>
        public void Fetch()
        {
            var urls = Cfg.CalendarFeeds;
            if (urls.Count == 0) { events = new List<CalEvent>(); feedStatus = ""; return; }
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                List<CalEvent> got = null; string err = "";
                try { got = CalFeed.Load(urls); }
                catch (FormatException) { err = Lang.L("That link isn't a calendar (.ics) link"); }
                catch { err = Lang.L("Can't reach your calendar – trying again soon"); }
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        if (got != null) events = got;
                        feedStatus = err;
                        App.PlaceWindow(this); Redraw();
                    }));
                }
                catch { }
            });
        }

        public void ChooseFeed()
        {
            string u = InputBox.Ask(Lang.L("Paste your calendar's iCal (.ics) link — several links can be separated by spaces"), Cfg.CalendarIcs, App.Theme);
            if (u == null) return;
            Cfg.CalendarIcs = u.Trim(); Cfg.Save();
            Fetch(); App.PlaceWindow(this); Redraw();
        }
        public void Disconnect() { Cfg.CalendarIcs = ""; Cfg.Save(); events = new List<CalEvent>(); feedStatus = ""; App.PlaceWindow(this); Redraw(); }
        public bool Connected { get { return Cfg.CalendarFeeds.Count > 0; } }

        bool HasEvent(DateTime day) { foreach (var e in events) if (e.Start.Date == day) return true; return false; }

        Rectangle PrevBtn { get { Rectangle r = Inner; return new Rectangle(r.Right - T.S(56), r.Y - T.S(2), T.S(26), T.S(26)); } }
        Rectangle NextBtn { get { Rectangle r = Inner; return new Rectangle(r.Right - T.S(26), r.Y - T.S(2), T.S(26), T.S(26)); } }

        protected override bool IsDragArea(Point p) { return !PrevBtn.Contains(p) && !NextBtn.Contains(p); }

        protected override void PaintContent(Graphics g)
        {
            DateTime today = App.Now.Date;
            DateTime first = new DateTime(today.Year, today.Month, 1).AddMonths(monthOffset);
            Rectangle r = Inner;
            string head = first.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
            Color red = Color.FromArgb(255, 255, 59, 48);
            if (MacLook) head = head.ToUpper(CultureInfo.CurrentCulture);
            Txt(g, head, Gfx.F(T.FontName, T.Sf(MacLook ? 13 : 15), FontStyle.Bold), MacLook ? red : T.Text, new RectangleF(r.X, r.Y - T.Sf(2), r.Width - T.Sf(60), T.Sf(26)), Gfx.Left);
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
                        using (var b = new SolidBrush(MacLook ? red : T.Accent)) g.FillEllipse(b, cell.X + (cw - s) / 2, cell.Y + (ch - s) / 2, s, s);
                        Txt(g, d.Day.ToString(), dayB, Color.White, cell, Gfx.Center);
                    }
                    else
                    {
                        bool weekend = d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday;
                        Color c = !inMonth ? Gfx.WithAlpha(T.SubText, 70) : weekend ? T.SubText : T.Text;
                        Txt(g, d.Day.ToString(), dayF, c, cell, Gfx.Center);
                    }
                    if (events.Count > 0 && HasEvent(d))
                    {
                        float ds = T.Sf(4);
                        using (var b = new SolidBrush(isToday ? Color.White : Gfx.WithAlpha(T.Accent, inMonth ? 255 : 90)))
                            g.FillEllipse(b, cell.X + cw / 2 - ds / 2, cell.Bottom - T.Sf(6.5f), ds, ds);
                    }
                }
            if (!HasFeed) return;
            // the next two events from the connected calendars
            float ey = top + 6 * ch + T.Sf(8);
            Gfx.FillRound(g, T.Track, new RectangleF(r.X, ey - T.Sf(4), r.Width, T.Sf(1)), 0);
            var ef = Gfx.F(T.FontName, T.Sf(12), FontStyle.Regular);
            var eb = Gfx.F(T.FontName, T.Sf(12), FontStyle.Bold);
            int shown = 0;
            DateTime now = App.Now;
            foreach (var e in events)
            {
                if (shown == 2) break;
                if (e.AllDay ? e.Start.Date < now.Date : e.Start < now.AddMinutes(-30)) continue;
                string when = e.Start.Date == now.Date ? Lang.L("Today") : e.Start.Date == now.Date.AddDays(1) ? Lang.L("Tomorrow") : e.Start.ToString("ddd d MMM", CultureInfo.CurrentCulture);
                if (!e.AllDay) when += " " + e.Start.ToString(Cfg.Clock24 ? "HH:mm" : "h:mm tt", CultureInfo.CurrentCulture);
                var row = new RectangleF(r.X, ey + shown * T.Sf(24), r.Width, T.Sf(22));
                using (var b = new SolidBrush(T.Accent)) g.FillEllipse(b, row.X, row.Y + row.Height / 2 - T.Sf(3), T.Sf(6), T.Sf(6));
                Txt(g, e.Title.Length == 0 ? "(no title)" : e.Title, eb, T.Text, new RectangleF(row.X + T.Sf(14), row.Y, row.Width * 0.58f, row.Height), Gfx.Left);
                Txt(g, when, ef, T.SubText, new RectangleF(row.X + row.Width * 0.5f, row.Y, row.Width * 0.5f, row.Height), Gfx.Right);
                shown++;
            }
            if (shown == 0)
                Txt(g, feedStatus.Length > 0 ? feedStatus : events.Count == 0 ? Lang.L("Loading…") : Lang.L("Nothing coming up"), ef, T.SubText, new RectangleF(r.X, ey, r.Width, T.Sf(22)), Gfx.Left);
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
        protected override Color MacColor { get { return Color.FromArgb(255, 255, 204, 0); } }
        public NotesWidget(App app) : base(app, "notes") { }
        protected override Size BaseSize { get { return new Size(320, 220); } }

        /// <summary>macOS style: a yellow paper note with faint lines and a folded corner.</summary>
        protected override void PaintMacBack(Graphics g, GraphicsPath path, RectangleF pr)
        {
            LiquidBack(g);
            using (var lg = new LinearGradientBrush(new PointF(0, pr.Top - 1), new PointF(0, pr.Bottom + 1), Color.FromArgb(190, 255, 236, 140), Color.FromArgb(190, 255, 214, 90))) g.FillPath(lg, path);
            // ruled lines, like a notepad
            float lh = T.Sf(24), y0 = pr.Y + T.Sf(58);
            using (var pen = new Pen(Color.FromArgb(46, 150, 110, 20), 1f))
                for (float y = y0; y < pr.Bottom - T.Sf(10); y += lh) g.DrawLine(pen, pr.X + T.Sf(14), y, pr.Right - T.Sf(14), y);
            // the folded bottom-right corner
            float c = T.Sf(26);
            var fold = new[] { new PointF(pr.Right - c, pr.Bottom), new PointF(pr.Right, pr.Bottom - c), new PointF(pr.Right - c, pr.Bottom - c) };
            using (var b = new SolidBrush(Color.FromArgb(255, 236, 190, 60))) g.FillPolygon(b, fold);
            using (var b = new SolidBrush(Color.FromArgb(40, 0, 0, 0))) g.FillPolygon(b, new[] { fold[0], fold[2], new PointF(pr.Right - c + T.Sf(3), pr.Bottom) });
            MacSheen(g, path, pr);
        }
        void PaintMac(Graphics g)
        {
            Rectangle r = Inner;
            Color ink = Color.FromArgb(255, 74, 56, 16), soft = Color.FromArgb(170, 110, 84, 24);
            string text = (Cfg.NotesText ?? "").Replace("\r", "");
            string first = text, rest = "";
            int nl = text.IndexOf('\n'); if (nl >= 0) { first = text.Substring(0, nl); rest = text.Substring(nl + 1).TrimStart('\n'); }
            if (text.Trim().Length == 0) { first = Lang.L("Notes"); rest = Lang.L("double-click to edit"); }
            Txt(g, first, Gfx.F(T.DisplayFont, T.Sf(17), FontStyle.Bold), ink, new RectangleF(r.X, r.Y - T.Sf(2), r.Width, T.Sf(26)), Gfx.Left);
            Txt(g, App.Now.ToString("d MMM", CultureInfo.CurrentCulture), Gfx.F(T.FontName, T.Sf(11), FontStyle.Regular), soft, new RectangleF(r.X, r.Y + T.Sf(22), r.Width, T.Sf(16)), Gfx.Left);
            var body = new RectangleF(r.X, r.Y + T.Sf(44), r.Width - T.Sf(10), r.Height - T.Sf(44));
            Txt(g, rest, Gfx.F(T.FontName, T.Sf(13.5f), FontStyle.Regular), ink, body, Gfx.TopLeftWrap);
        }

        protected override void PaintContent(Graphics g)
        {
            if (MacLook) { PaintMac(g); return; }
            Rectangle r = Inner;
            Title(g, Lang.L("Notes"), new Rectangle(r.X, r.Y, r.Width, T.S(20)));
            Txt(g, Lang.L("double-click to edit"), Gfx.F(T.FontName, T.Sf(11), FontStyle.Regular), T.SubText, new RectangleF(r.X, r.Y, r.Width, T.Sf(20)), Gfx.Right);
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
