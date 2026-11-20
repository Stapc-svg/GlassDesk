// GlassDesk - Clipboard widget: the last few things you copied; click one to copy it again.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GlassDesk
{
    /// <summary>
    /// Remembers the last texts you copied (only in memory: nothing is written to disk, and it's
    /// forgotten when GlassDesk closes). Password managers mark what they copy as private, and those
    /// copies are skipped.
    /// </summary>
    internal class ClipWidget : Widget
    {
        const int Keep = 20, Show = 5;
        readonly List<KeyValuePair<string, DateTime>> items = new List<KeyValuePair<string, DateTime>>();
        string last;
        bool paused;
        System.Windows.Forms.Timer watch;
        readonly List<RectangleF> rows = new List<RectangleF>();
        int hot = -1, flash = -1; int flashAt;

        protected override Color MacColor { get { return Color.FromArgb(255, 142, 142, 147); } }

        public ClipWidget(App app) : base(app, "clip") { }
        protected override Size BaseSize { get { return new Size(320, 252); } }

        public void SetSample()
        {
            var now = App.Now;
            foreach (var s in new[] { "Meeting moved to 15:30 – room 4B", "https://github.com/Stapc-svg/GlassDesk", "#7C9CFF", "Thanks so much, see you tomorrow!", "C:\\Users\\Me\\Documents\\Report final.docx" })
                items.Add(new KeyValuePair<string, DateTime>(s, now.AddMinutes(-items.Count * 7 - 1)));
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (App.Preview) return;
            watch = new System.Windows.Forms.Timer { Interval = 900 };
            watch.Tick += delegate { Check(); };
            watch.Start();
        }
        protected override void Dispose(bool disposing) { if (disposing && watch != null) watch.Dispose(); base.Dispose(disposing); }

        void Check()
        {
            if (paused || App.Paused) return;
            try
            {
                if (!Clipboard.ContainsText()) return;
                var data = Clipboard.GetDataObject();
                // password managers and private apps ask clipboard tools to look away
                if (data != null && (data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing") || data.GetDataPresent("Clipboard Viewer Ignore") || data.GetDataPresent("CanIncludeInClipboardHistory") && IsZero(data.GetData("CanIncludeInClipboardHistory"))))
                    return;
                string s = Clipboard.GetText();
                if (string.IsNullOrEmpty(s) || s == last) return;
                last = s;
                if (s.Trim().Length == 0 || s.Length > 20000) return;
                Add(s);
            }
            catch { }   // another app is holding the clipboard; try again next time
        }
        static bool IsZero(object o)
        {
            var ms = o as System.IO.MemoryStream;
            if (ms == null) return false;
            var b = ms.ToArray();
            return b.Length >= 4 && b[0] == 0 && b[1] == 0 && b[2] == 0 && b[3] == 0;
        }

        void Add(string s)
        {
            items.RemoveAll(x => x.Key == s);
            items.Insert(0, new KeyValuePair<string, DateTime>(s, App.Now));
            if (items.Count > Keep) items.RemoveRange(Keep, items.Count - Keep);
            Redraw();
        }

        public void Clear() { items.Clear(); Redraw(); }
        public bool Paused { get { return paused; } }
        public void TogglePause() { paused = !paused; Redraw(); }

        string Ago(DateTime t)
        {
            var d = App.Now - t;
            if (d.TotalSeconds < 60) return "now";
            if (d.TotalMinutes < 60) return (int)d.TotalMinutes + "m";
            if (d.TotalHours < 24) return (int)d.TotalHours + "h";
            return (int)d.TotalDays + "d";
        }
        static string OneLine(string s)
        {
            s = s.Replace("\r\n", " ⏎ ").Replace('\n', ' ').Replace('\t', ' ').Trim();
            return s.Length > 200 ? s.Substring(0, 200) : s;
        }

        protected override void PaintContent(Graphics g)
        {
            Rectangle r = Inner;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Title(g, Lang.L("Clipboard"), new Rectangle(r.X, r.Y, r.Width, T.S(20)));
            string right = paused ? "paused" : items.Count == 0 ? "" : items.Count + (items.Count == 1 ? " item" : " items");
            Txt(g, right, Gfx.F(T.FontName, T.Sf(11), FontStyle.Regular), paused ? Color.FromArgb(255, 255, 170, 70) : T.SubText, new RectangleF(r.X, r.Y, r.Width, T.Sf(20)), Gfx.Right);
            rows.Clear();
            if (items.Count == 0)
            {
                Txt(g, Lang.L("Copy some text anywhere and it shows up here.\nClick it later to copy it again."), Gfx.F(T.FontName, T.Sf(12.5f), FontStyle.Regular), T.SubText,
                    new RectangleF(r.X, r.Y + T.Sf(34), r.Width, T.Sf(60)), Gfx.TopLeftWrap);
                return;
            }
            float y = r.Y + T.Sf(28), rh = T.Sf(38), gap = T.Sf(4);
            int n = Math.Min(Show, Math.Max(1, (int)((r.Bottom - y + gap) / (rh + gap))));
            for (int i = 0; i < n && i < items.Count; i++)
            {
                var row = new RectangleF(r.X - T.Sf(6), y, r.Width + T.Sf(12), rh);
                rows.Add(row);
                bool isFlash = i == flash && Environment.TickCount - flashAt < 900;
                int a = isFlash ? 70 : i == hot ? 34 : 14;
                using (var path = Gfx.Round(row, T.Sf(10)))
                using (var b = new SolidBrush(isFlash ? Color.FromArgb(a, T.Accent) : Color.FromArgb(a, T.Text))) g.FillPath(b, path);
                // a small colored mark for links, colors and file paths
                string s = items[i].Key, t = s.Trim();
                Color mark = t.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? Color.FromArgb(255, 90, 170, 255)
                    : (t.Length == 7 && t[0] == '#') ? SafeColor(t)
                    : (t.Length > 2 && t[1] == ':' && t[2] == '\\') ? Color.FromArgb(255, 250, 190, 70) : T.Accent;
                using (var b = new SolidBrush(mark)) g.FillEllipse(b, row.X + T.Sf(10), row.Y + rh / 2 - T.Sf(4), T.Sf(8), T.Sf(8));
                Txt(g, isFlash ? "Copied!" : OneLine(s), Gfx.F(T.FontName, T.Sf(12.5f), isFlash ? FontStyle.Bold : FontStyle.Regular), T.Text,
                    new RectangleF(row.X + T.Sf(26), row.Y, row.Width - T.Sf(70), rh), Gfx.Left);
                Txt(g, Ago(items[i].Value), Gfx.F(T.FontName, T.Sf(10.5f), FontStyle.Regular), T.SubText, new RectangleF(row.Right - T.Sf(44), row.Y, T.Sf(36), rh), Gfx.Right);
                y += rh + gap;
            }
        }
        static Color SafeColor(string hex)
        {
            try { return ColorTranslator.FromHtml(hex); } catch { return Color.Gray; }
        }

        protected override void OnHover(Point p)
        {
            int h = rows.FindIndex(r => r.Contains(p));
            if (h != hot) { hot = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Redraw(); }
        }

        protected override void OnClickAt(Point p, MouseButtons b)
        {
            int i = rows.FindIndex(r => r.Contains(p));
            if (b == MouseButtons.Left && i >= 0 && i < items.Count)
            {
                string s = items[i].Key;
                try { Clipboard.SetText(s); last = s; } catch { return; }
                Add(s);              // moves it back to the top
                flash = 0; flashAt = Environment.TickCount;
                var t = new System.Windows.Forms.Timer { Interval = 950 };
                t.Tick += delegate { t.Stop(); t.Dispose(); flash = -1; Redraw(); };
                t.Start();
                return;
            }
            base.OnClickAt(p, b);
        }
    }
}
