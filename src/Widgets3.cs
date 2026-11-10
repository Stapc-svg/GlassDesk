// GlassDesk 1.7 - GitHub stats, network meter and focus timer widgets.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Threading;
using System.Windows.Forms;

namespace GlassDesk
{
    // ================================================================ GitHub stats
    internal class GitHubWidget : Widget
    {
        public class Stats
        {
            public string Login = "", Name = "";
            public int Repos, Followers, Following, Stars, Forks;
            public string TopRepo = ""; public int TopStars;
            public string LastRepo = "";
            public int[] Days = new int[14];      // pushes per day, oldest first
        }
        Stats st; string status = ""; bool busy;
        System.Windows.Forms.Timer refresh;

        public GitHubWidget(App app) : base(app, "github") { }
        protected override Size BaseSize { get { return new Size(320, 236); } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            refresh = new System.Windows.Forms.Timer { Interval = 30 * 60 * 1000 };   // GitHub allows 60 lookups an hour; we use 3 every 30 minutes
            refresh.Tick += delegate { if (Visible && !App.Paused) Fetch(); };
            refresh.Start();
            Fetch();
        }
        protected override void Dispose(bool disposing) { if (disposing && refresh != null) refresh.Dispose(); base.Dispose(disposing); }

        public void SetSample()
        {
            st = new Stats { Login = "glassdesk-dev", Name = "GlassDesk Dev", Repos = 24, Followers = 318, Following = 41, Stars = 1270, Forks = 96, TopRepo = "GlassDesk", TopStars = 842, LastRepo = "GlassDesk" };
            st.Days = new[] { 2, 0, 5, 3, 7, 1, 0, 4, 9, 6, 2, 8, 5, 11 };
            status = "";
        }

        public void Fetch()
        {
            if (busy || App.Preview) return;
            string user = (Cfg.GitHubUser ?? "").Trim();
            if (user.Length == 0) { status = "Double-click to add your GitHub username"; Redraw(); return; }
            busy = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                Stats s = null; string err = null;
                try { s = LoadStats(user); }
                catch (System.Net.WebException ex)
                {
                    var r = ex.Response as System.Net.HttpWebResponse;
                    err = r != null && (int)r.StatusCode == 404 ? "No GitHub user called \"" + user + "\"" :
                          r != null && (int)r.StatusCode == 403 ? "GitHub says: too many lookups, trying again later" : "Can't reach GitHub right now";
                }
                catch { err = "GitHub sent something unexpected"; }
                try { BeginInvoke(new Action(delegate { busy = false; if (s != null) { st = s; status = ""; } else status = err; Redraw(); })); } catch { busy = false; }
            });
        }

        public static Stats LoadStats(string user)
        {
            string u = Uri.EscapeDataString(user);
            var s = Parse(Http.GetJson("https://api.github.com/users/" + u),
                          Json.Parse(Http.Get("https://api.github.com/users/" + u + "/repos?per_page=100&sort=pushed")) as List<object>,
                          Json.Parse(Http.Get("https://api.github.com/users/" + u + "/events/public?per_page=100")) as List<object>,
                          DateTime.UtcNow.Date);
            return s;
        }

        /// <summary>Turns GitHub's answers into the numbers the widget shows (separate so it can be tested).</summary>
        public static Stats Parse(Dictionary<string, object> user, List<object> repos, List<object> events, DateTime todayUtc)
        {
            var s = new Stats();
            s.Login = Json.Str(user, "login"); s.Name = Json.Str(user, "name");
            s.Repos = (int)Num(user, "public_repos"); s.Followers = (int)Num(user, "followers"); s.Following = (int)Num(user, "following");
            if (repos != null)
                foreach (var o in repos)
                {
                    var r = o as Dictionary<string, object>; if (r == null) continue;
                    object fork; if (r.TryGetValue("fork", out fork) && fork is bool && (bool)fork) continue;   // count your own work, not copies
                    int stars = (int)Num(r, "stargazers_count");
                    s.Stars += stars; s.Forks += (int)Num(r, "forks_count");
                    if (stars > s.TopStars || s.TopRepo.Length == 0) { s.TopStars = stars; s.TopRepo = Json.Str(r, "name"); }
                    if (s.LastRepo.Length == 0) s.LastRepo = Json.Str(r, "name");
                }
            if (events != null)
                foreach (var o in events)
                {
                    var e = o as Dictionary<string, object>; if (e == null) continue;
                    DateTime t;
                    if (!DateTime.TryParse(Json.Str(e, "created_at"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out t)) continue;
                    int ago = (int)(todayUtc - t.Date).TotalDays;
                    if (ago < 0 || ago > 13) continue;
                    string type = Json.Str(e, "type");
                    int n = 1;
                    if (type == "PushEvent")
                    {
                        var p = Json.Obj(e, "payload");
                        double size = Num(p, "size");
                        n = double.IsNaN(size) || size < 1 ? 1 : (int)size;   // commits in that push
                    }
                    s.Days[13 - ago] += n;
                }
            return s;
        }
        static double Num(Dictionary<string, object> o, string k) { double v = Json.Num(o, k); return double.IsNaN(v) ? 0 : v; }

        static string K(int n) { return n >= 10000 ? (n / 1000) + "k" : n >= 1000 ? (n / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + "k" : n.ToString(CultureInfo.InvariantCulture); }

        protected override void PaintContent(Graphics g)
        {
            Rectangle r = Inner;
            Title(g, "GitHub", new Rectangle(r.X, r.Y, r.Width, T.S(20)));
            // the cat-shaped mark is GitHub's; we just show a simple octagon-free dot instead
            if (st == null || status.Length > 0)
            {
                Txt(g, status.Length > 0 ? status : busy ? "Loading…" : "Double-click to add your GitHub username", Gfx.F(T.FontName, T.Sf(13.5f), FontStyle.Regular), T.SubText, new RectangleF(r.X, r.Y + T.Sf(32), r.Width, T.Sf(60)), Gfx.TopLeftWrap);
                if (st == null) return;
            }
            Txt(g, "@" + st.Login, Gfx.F(T.FontName, T.Sf(11.5f), FontStyle.Regular), T.SubText, new RectangleF(r.X + T.Sf(60), r.Y, r.Width - T.Sf(60), T.Sf(20)), Gfx.Right);

            // three big numbers
            string[] vals = { K(st.Stars), K(st.Followers), K(st.Repos) };
            string[] labs = { "stars", "followers", "repos" };
            float cw = r.Width / 3f, top = r.Y + T.Sf(26);
            for (int i = 0; i < 3; i++)
            {
                var cell = new RectangleF(r.X + i * cw, top, cw, T.Sf(34));
                Txt(g, vals[i], Gfx.F(T.DisplayFont, T.Sf(28), FontStyle.Regular), i == 0 ? T.Accent : T.Text, cell, Gfx.Left);
                Txt(g, labs[i], Gfx.F(T.FontName, T.Sf(11.5f), FontStyle.Regular), T.SubText, new RectangleF(cell.X, cell.Bottom, cw, T.Sf(16)), Gfx.Left);
            }
            // most-starred repo
            float y = top + T.Sf(60);
            Txt(g, "★ " + (st.TopRepo.Length > 0 ? st.TopRepo : "—"), Gfx.F(T.FontName, T.Sf(13), FontStyle.Bold), T.Text, new RectangleF(r.X, y, r.Width - T.Sf(70), T.Sf(20)), Gfx.Left);
            Txt(g, K(st.TopStars) + " stars", Gfx.F(T.FontName, T.Sf(12), FontStyle.Regular), T.SubText, new RectangleF(r.X, y, r.Width, T.Sf(20)), Gfx.Right);

            // 14-day activity bars
            y += T.Sf(28);
            int max = 1, total = 0; foreach (int d in st.Days) { max = Math.Max(max, d); total += d; }
            Txt(g, "Last 14 days", Gfx.F(T.FontName, T.Sf(11), FontStyle.Bold), T.SubText, new RectangleF(r.X, y, r.Width, T.Sf(16)), Gfx.Left);
            Txt(g, total + (total == 1 ? " contribution" : " contributions"), Gfx.F(T.FontName, T.Sf(11), FontStyle.Regular), T.SubText, new RectangleF(r.X, y, r.Width, T.Sf(16)), Gfx.Right);
            float bh = T.Sf(40), by = y + T.Sf(20), gap = T.Sf(4), bw = (r.Width - gap * 13) / 14f;
            for (int i = 0; i < 14; i++)
            {
                float h = st.Days[i] == 0 ? T.Sf(3) : Math.Max(T.Sf(5), bh * st.Days[i] / max);
                var bar = new RectangleF(r.X + i * (bw + gap), by + bh - h, bw, h);
                Gfx.FillRound(g, st.Days[i] == 0 ? T.Track : Gfx.WithAlpha(T.Accent, 120 + 135 * st.Days[i] / max), bar, Math.Min(bw / 2, T.Sf(3)));
            }
        }

        protected override void OnDoubleClickAt(Point p) { ChooseUser(); }
        public void ChooseUser()
        {
            string u = InputBox.Ask("Your GitHub username", Cfg.GitHubUser, App.Theme);
            if (u == null) return;
            u = u.Trim().TrimStart('@').Replace("https://github.com/", "").Trim('/');
            if (u.Length == 0) return;
            Cfg.GitHubUser = u; Cfg.Save(); st = null; status = ""; Redraw(); Fetch();
        }
        public void OpenProfile()
        {
            if ((Cfg.GitHubUser ?? "").Length > 0) try { System.Diagnostics.Process.Start("https://github.com/" + Uri.EscapeDataString(Cfg.GitHubUser)); } catch { }
        }
    }

    // ================================================================ Network meter
    internal class NetworkWidget : Widget
    {
        long lastRx = -1, lastTx = -1; DateTime lastAt;
        public double Down, Up;                  // bytes per second
        readonly float[] histDown = new float[40], histUp = new float[40];
        System.Windows.Forms.Timer timer;
        string adapter = "";

        public NetworkWidget(App app) : base(app, "network") { }
        protected override Size BaseSize { get { return new Size(320, 168); } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            timer = new System.Windows.Forms.Timer { Interval = 2000 };
            timer.Tick += delegate { if (Visible && !App.Paused) { Sample(); Redraw(); } };
            timer.Start();
            Sample();
        }
        protected override void Dispose(bool disposing) { if (disposing && timer != null) timer.Dispose(); base.Dispose(disposing); }

        public void SetSample()
        {
            var rnd = new Random(3);
            for (int i = 0; i < 40; i++) { histDown[i] = (float)(0.25 + 0.2 * Math.Sin(i * .4) + rnd.NextDouble() * .25); histUp[i] = (float)(0.08 + rnd.NextDouble() * .1); }
            histDown[39] = 0.6f; Down = 4.8 * 1048576; Up = 0.6 * 1048576; adapter = "Wi-Fi";
        }

        void Sample()
        {
            try
            {
                long rx = 0, tx = 0; string name = "";
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    var s = ni.GetIPv4Statistics();
                    rx += s.BytesReceived; tx += s.BytesSent;
                    if (name.Length == 0 || ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) name = ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "Wi-Fi" : ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? "Ethernet" : ni.Name;
                }
                adapter = name;
                DateTime now = DateTime.UtcNow;
                if (lastRx >= 0)
                {
                    double sec = Math.Max(0.5, (now - lastAt).TotalSeconds);
                    Down = Math.Max(0, (rx - lastRx) / sec); Up = Math.Max(0, (tx - lastTx) / sec);
                    Push(histDown, Down); Push(histUp, Up);
                }
                lastRx = rx; lastTx = tx; lastAt = now;
            }
            catch { }
        }
        static void Push(float[] h, double bps)
        {
            Array.Copy(h, 1, h, 0, h.Length - 1);
            // log scale so both 50 KB/s and 50 MB/s are visible: 1 KB/s .. 100 MB/s
            double v = bps <= 1024 ? 0 : Math.Log10(bps / 1024) / 5;
            h[h.Length - 1] = (float)Math.Max(0, Math.Min(1, v));
        }

        public static string Speed(double bps)
        {
            if (bps < 1024) return "0 KB/s";
            if (bps < 1048576) return (bps / 1024).ToString(bps < 10240 ? "0.0" : "0", CultureInfo.InvariantCulture) + " KB/s";
            return (bps / 1048576).ToString(bps < 10485760 ? "0.0" : "0", CultureInfo.InvariantCulture) + " MB/s";
        }

        protected override void PaintContent(Graphics g)
        {
            Rectangle r = Inner;
            Title(g, "Network", new Rectangle(r.X, r.Y, r.Width, T.S(20)));
            Txt(g, adapter.Length > 0 ? adapter : "offline", Gfx.F(T.FontName, T.Sf(11.5f), FontStyle.Regular), T.SubText, new RectangleF(r.X, r.Y, r.Width, T.Sf(20)), Gfx.Right);
            float half = r.Width / 2f - T.Sf(8);
            Row(g, "↓ Download", Speed(Down), histDown, new RectangleF(r.X, r.Y + T.Sf(28), half, T.Sf(90)), T.Accent);
            Row(g, "↑ Upload", Speed(Up), histUp, new RectangleF(r.X + half + T.Sf(16), r.Y + T.Sf(28), half, T.Sf(90)), Color.FromArgb(255, 46, 200, 140));
        }
        void Row(Graphics g, string label, string value, float[] h, RectangleF b, Color c)
        {
            Txt(g, label, Gfx.F(T.FontName, T.Sf(11.5f), FontStyle.Bold), T.SubText, new RectangleF(b.X, b.Y, b.Width, T.Sf(16)), Gfx.Left);
            Txt(g, value, Gfx.F(T.DisplayFont, T.Sf(22), FontStyle.Regular), T.Text, new RectangleF(b.X, b.Y + T.Sf(16), b.Width, T.Sf(30)), Gfx.Left);
            var sp = new RectangleF(b.X, b.Y + T.Sf(52), b.Width, T.Sf(34));
            var pts = new PointF[h.Length];
            for (int i = 0; i < h.Length; i++) pts[i] = new PointF(sp.X + sp.Width * i / (h.Length - 1), sp.Bottom - 1 - (sp.Height - 2) * h[i]);
            using (var path = new GraphicsPath())
            {
                path.AddLines(pts); path.AddLine(sp.Right, sp.Bottom, sp.X, sp.Bottom); path.CloseFigure();
                using (var lg = new LinearGradientBrush(new PointF(0, sp.Y - 1), new PointF(0, sp.Bottom + 1), Gfx.WithAlpha(c, 110), Gfx.WithAlpha(c, 0))) g.FillPath(lg, path);
            }
            using (var pen = new Pen(c, T.Sf(1.6f)) { LineJoin = LineJoin.Round }) g.DrawLines(pen, pts);
        }
    }

    // ================================================================ Focus timer (Pomodoro)
    internal class FocusWidget : Widget
    {
        bool running, onBreak;
        int left;                                // seconds left
        int sessions;                            // focus blocks finished today
        System.Windows.Forms.Timer timer;

        public FocusWidget(App app) : base(app, "focus") { left = Math.Max(1, app.Settings.FocusMinutes) * 60; }
        protected override Size BaseSize { get { return new Size(320, 150); } }
        int Total { get { return (onBreak ? Math.Max(1, Cfg.BreakMinutes) : Math.Max(1, Cfg.FocusMinutes)) * 60; } }

        public void SetSample() { running = true; left = 14 * 60 + 32; sessions = 3; }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            timer = new System.Windows.Forms.Timer { Interval = 1000 };   // only ticks while the timer is running
            timer.Tick += delegate { Tick(); };
        }
        protected override void Dispose(bool disposing) { if (disposing && timer != null) timer.Dispose(); base.Dispose(disposing); }

        void Tick()
        {
            if (!running) { timer.Stop(); return; }
            left--;
            if (left <= 0)
            {
                if (!onBreak) sessions++;
                onBreak = !onBreak; left = Total; running = false; timer.Stop();
                try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
                App.Notify(onBreak ? "Focus done — take a break" : "Break's over", onBreak ? "Nice work! Click the timer to start a " + Cfg.BreakMinutes + "-minute break." : "Click the timer to start the next focus block.", null);
            }
            Redraw();
        }

        public void Toggle() { running = !running; if (running) timer.Start(); else timer.Stop(); Redraw(); }
        public void Reset() { running = false; onBreak = false; left = Total; if (timer != null) timer.Stop(); Redraw(); }
        public void SettingsChanged() { if (!running) { left = Total; Redraw(); } }

        protected override void PaintContent(Graphics g)
        {
            Rectangle r = Inner;
            Title(g, onBreak ? "Break" : "Focus", new Rectangle(r.X, r.Y, r.Width, T.S(20)));
            Txt(g, sessions + (sessions == 1 ? " session today" : " sessions today"), Gfx.F(T.FontName, T.Sf(11.5f), FontStyle.Regular), T.SubText, new RectangleF(r.X, r.Y, r.Width, T.Sf(20)), Gfx.Right);
            // progress ring
            float d = T.Sf(84); var ring = new RectangleF(r.X, r.Y + T.Sf(26), d, d);
            using (var pen = new Pen(T.Track, T.Sf(7))) g.DrawEllipse(pen, ring);
            float frac = 1f - (float)left / Total;
            Color c = onBreak ? Color.FromArgb(255, 46, 200, 140) : T.Accent;
            if (frac > 0.002f) using (var pen = new Pen(c, T.Sf(7)) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(pen, ring, -90, 360 * frac);
            // play / pause symbol in the middle
            float cx = ring.X + d / 2, cy = ring.Y + d / 2, s = T.Sf(11);
            using (var b = new SolidBrush(T.Text))
            {
                if (running) { g.FillRectangle(b, cx - s * .7f, cy - s, s * .5f, s * 2); g.FillRectangle(b, cx + s * .2f, cy - s, s * .5f, s * 2); }
                else g.FillPolygon(b, new[] { new PointF(cx - s * .6f, cy - s), new PointF(cx + s, cy), new PointF(cx - s * .6f, cy + s) });
            }
            string time = (left / 60).ToString("00") + ":" + (left % 60).ToString("00");
            float x = ring.Right + T.Sf(18);
            Txt(g, time, Gfx.F(T.DisplayFont, T.Sf(42), FontStyle.Regular), T.Text, new RectangleF(x, r.Y + T.Sf(30), r.Right - x, T.Sf(50)), Gfx.Left);
            Txt(g, running ? (onBreak ? "Relax for a bit" : "Stay on one thing") : "Click to " + (left < Total ? "continue" : "start"), Gfx.F(T.FontName, T.Sf(12.5f), FontStyle.Regular), T.SubText, new RectangleF(x, r.Y + T.Sf(82), r.Right - x, T.Sf(20)), Gfx.Left);
        }

        protected override void OnClickAt(Point p, MouseButtons b)
        {
            if (b == MouseButtons.Left) { Toggle(); return; }
            base.OnClickAt(p, b);
        }
    }
}
