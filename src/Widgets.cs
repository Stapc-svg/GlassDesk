// GlassDesk - widgets: clock, system monitor, calendar, notes.
using System;
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
        protected Widget(App app, string id) : base(app, id) { }
        protected abstract Size BaseSize { get; }
        public override Size PreferredPanelSize(Size saved) { return new Size(T.S(BaseSize.Width), T.S(BaseSize.Height)); }
        protected Rectangle Inner { get { int m = T.S(18); return new Rectangle(Panel.X + m, Panel.Y + T.S(14), Panel.Width - 2 * m, Panel.Height - T.S(28)); } }
        protected void Txt(Graphics g, string s, Font f, Color c, RectangleF r, StringFormat sf)
        {
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

        protected override void PaintContent(Graphics g)
        {
            DateTime now = App.Now;
            Rectangle r = Inner;
            string greet = now.Hour < 5 ? "Good night" : now.Hour < 12 ? "Good morning" : now.Hour < 18 ? "Good afternoon" : "Good evening";
            Txt(g, greet, Gfx.F(T.FontName, T.Sf(13), FontStyle.Regular), T.SubText, new RectangleF(r.X, r.Y, r.Width, T.Sf(20)), Gfx.Left);

            string time = Cfg.Clock24 ? now.ToString("HH:mm", CultureInfo.InvariantCulture) : now.ToString("h:mm", CultureInfo.InvariantCulture);
            var big = Gfx.F(T.DisplayFont, T.Sf(58), FontStyle.Regular);
            float y = r.Y + T.Sf(18);
            SizeF ts = g.MeasureString(time, big, PointF.Empty, StringFormat.GenericTypographic);
            using (var b = new SolidBrush(T.Text)) g.DrawString(time, big, b, new PointF(r.X - T.Sf(2), y), StringFormat.GenericTypographic);
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
            Txt(g, date, Gfx.F(T.FontName, T.Sf(14.5f), FontStyle.Bold), T.Text, new RectangleF(r.X, r.Bottom - T.Sf(22), r.Width, T.Sf(22)), Gfx.Left);
            Txt(g, "Week " + CultureInfo.CurrentCulture.Calendar.GetWeekOfYear(now, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday),
                Gfx.F(T.FontName, T.Sf(12), FontStyle.Regular), T.SubText, new RectangleF(r.X, r.Bottom - T.Sf(22), r.Width, T.Sf(22)), Gfx.Right);
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
        public NoteEditor(App app, Rectangle bounds, string text)
        {
            Theme t = app.Theme;
            Result = text;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            Bounds = bounds; TopMost = true;
            BackColor = t.Dark ? Color.FromArgb(28, 30, 38) : Color.FromArgb(250, 250, 252);
            Padding = new Padding(t.S(16));
            var hint = new Label { Dock = DockStyle.Bottom, Height = t.S(22), Text = "Esc or click outside to save", ForeColor = t.Dark ? Color.FromArgb(150, 160, 175) : Color.Gray, Font = Gfx.SafeFont(t.FontName, t.Sf(11), FontStyle.Regular) };
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
                f.Load += delegate { Native.SetDarkTitleBar(f.Handle, t.Dark); };
                return f.ShowDialog() == DialogResult.OK ? tb.Text : null;
            }
        }
    }
}
