// GlassDesk 1.3 - web helpers and the auto-updater (GitHub Releases).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace GlassDesk
{
    /// <summary>Tiny HTTPS helper. Every request is short, has a timeout and runs off the UI thread.</summary>
    internal static class Http
    {
        static Http()
        {
            // .NET Framework 4.5 needs TLS 1.2 switched on for modern sites
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }
        }
        class Client : WebClient
        {
            protected override WebRequest GetWebRequest(Uri address)
            {
                var r = base.GetWebRequest(address);
                r.Timeout = 15000;
                var h = r as HttpWebRequest;
                if (h != null) { h.UserAgent = "GlassDesk/" + App.Version; h.Accept = "application/json"; h.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate; }
                return r;
            }
        }
        public static string Get(string url)
        {
            using (var c = new Client()) { c.Encoding = Encoding.UTF8; return c.DownloadString(url); }
        }
        public static Dictionary<string, object> GetJson(string url)
        {
            var o = Json.Parse(Get(url)) as Dictionary<string, object>;
            if (o == null) throw new Exception("Not a JSON object");
            return o;
        }
        public static WebClient NewDownloader() { return new Client(); }
    }

    /// <summary>Small JSON reader: objects become dictionaries, arrays lists, numbers doubles.</summary>
    internal static class Json
    {
        public static object Parse(string s) { int i = 0; object v = Value(s, ref i); return v; }
        static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }
        static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) throw new FormatException("Unexpected end");
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>(); i++; Ws(s, ref i);
                if (s[i] == '}') { i++; return d; }
                while (true)
                {
                    Ws(s, ref i); string k = Str(s, ref i); Ws(s, ref i);
                    if (s[i] != ':') throw new FormatException("Expected :"); i++;
                    d[k] = Value(s, ref i); Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw new FormatException("Expected , or }");
                }
            }
            if (c == '[')
            {
                var l = new List<object>(); i++; Ws(s, ref i);
                if (s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(Value(s, ref i)); Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return l; }
                    throw new FormatException("Expected , or ]");
                }
            }
            if (c == '"') return Str(s, ref i);
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (s.Length - i >= 5 && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (st == i) throw new FormatException("Bad value at " + i);
            return double.Parse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture);
        }
        static string Str(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("Expected string"); i++;
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break; case 't': sb.Append('\t'); break; case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break; case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
            throw new FormatException("Unterminated string");
        }
        public static Dictionary<string, object> Obj(Dictionary<string, object> o, string k) { object v; return o != null && o.TryGetValue(k, out v) ? v as Dictionary<string, object> : null; }
        public static List<object> Arr(Dictionary<string, object> o, string k) { object v; return o != null && o.TryGetValue(k, out v) ? v as List<object> : null; }
        public static string Str(Dictionary<string, object> o, string k) { object v; return o != null && o.TryGetValue(k, out v) && v is string ? (string)v : ""; }
        public static double Num(Dictionary<string, object> o, string k) { object v; return o != null && o.TryGetValue(k, out v) && v is double ? (double)v : double.NaN; }
    }

    /// <summary>Checks GitHub Releases for a newer GlassDesk and installs it when you say so.</summary>
    internal class Updater
    {
        readonly App app;
        System.Windows.Forms.Timer timer;
        bool checking;
        public class Release { public Version Version; public string Tag, Notes, Page, MsiUrl, MsiName; }
        Release pending;

        public Updater(App app) { this.app = app; }
        Settings S { get { return app.Settings; } }

        public static bool RepoConfigured(string repo)
        {
            return !string.IsNullOrEmpty(repo) && repo.IndexOf("YOUR-USERNAME", StringComparison.OrdinalIgnoreCase) < 0 && repo.Split('/').Length == 2;
        }

        public void Start()
        {
            timer = new System.Windows.Forms.Timer { Interval = 60 * 1000 };   // first look a minute after start-up
            timer.Tick += delegate
            {
                timer.Interval = 60 * 60 * 1000;                                // then hourly, but only checks once a day
                if (S.AutoUpdate && DateTime.UtcNow.Ticks - S.LastUpdateCheck > TimeSpan.TicksPerDay) Check(false);
            };
            timer.Start();
        }

        /// <summary>Looks for a newer release. manual = you clicked "Check now", so always report the result.</summary>
        public void Check(bool manual)
        {
            if (checking) return;
            if (!RepoConfigured(S.UpdateRepo))
            {
                if (manual) MessageBox.Show("GlassDesk doesn't know where to look for updates yet.\n\nIn Settings > About, set \"Update source\" to your GitHub repository, for example  yourname/GlassDesk.", "GlassDesk updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            checking = true;
            string repo = S.UpdateRepo;
            var ui = app.Invoker;
            ThreadPool.QueueUserWorkItem(delegate
            {
                Release rel = null; string err = null;
                try { rel = Fetch(repo); }
                catch (WebException ex)
                {
                    var hr = ex.Response as HttpWebResponse;
                    err = hr != null && hr.StatusCode == HttpStatusCode.NotFound ? "No releases were found for " + repo + "." : "Couldn't reach GitHub. Check your internet connection.";
                }
                catch (Exception ex) { err = "Couldn't read the release information (" + ex.Message + ")."; }
                try { ui.BeginInvoke(new Action(delegate { checking = false; Done(rel, err, manual); })); } catch { checking = false; }
            });
        }

        static Release Fetch(string repo)
        {
            var j = Http.GetJson("https://api.github.com/repos/" + repo + "/releases/latest");
            var r = new Release { Tag = Json.Str(j, "tag_name"), Notes = Json.Str(j, "body"), Page = Json.Str(j, "html_url") };
            r.Version = ParseVersion(r.Tag);
            var assets = Json.Arr(j, "assets");
            if (assets != null)
                foreach (var o in assets)
                {
                    var a = o as Dictionary<string, object>;
                    string name = Json.Str(a, "name"), url = Json.Str(a, "browser_download_url");
                    if (name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) && IsGitHubUrl(url)) { r.MsiUrl = url; r.MsiName = name; break; }
                }
            return r;
        }

        /// <summary>Only ever download installers from GitHub itself.</summary>
        public static bool IsGitHubUrl(string url)
        {
            Uri u;
            return Uri.TryCreate(url, UriKind.Absolute, out u) && u.Scheme == Uri.UriSchemeHttps && u.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase);
        }

        public static Version ParseVersion(string tag)
        {
            string t = (tag ?? "").Trim().TrimStart('v', 'V');
            int cut = t.IndexOfAny(new[] { '-', '+', ' ' }); if (cut > 0) t = t.Substring(0, cut);
            Version v;
            if (t.IndexOf('.') < 0) t += ".0";
            return Version.TryParse(t, out v) ? Normalize(v) : null;
        }
        static Version Normalize(Version v) { return new Version(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision)); }
        public static bool IsNewer(Version remote, Version current) { return remote != null && Normalize(remote) > Normalize(current); }

        void Done(Release rel, string err, bool manual)
        {
            S.LastUpdateCheck = DateTime.UtcNow.Ticks; S.Save();
            if (err != null) { if (manual) MessageBox.Show(err, "GlassDesk updates", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            Version cur = App.VersionNumber;
            if (!IsNewer(rel.Version, cur))
            {
                if (manual) MessageBox.Show("You have the latest version (" + App.Version + ").", "GlassDesk updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!manual && S.SkipVersion == rel.Version.ToString()) return;
            pending = rel;
            if (manual) ShowOffer();
            else app.Notify("GlassDesk " + Short(rel.Version) + " is available", "Click here to see what's new and update.", ShowOffer);
        }

        public static string Short(Version v) { return v.Build > 0 ? v.ToString(3) : v.ToString(2); }

        void ShowOffer()
        {
            if (pending == null) return;
            using (var f = new UpdateForm(app, pending))
            {
                var r = f.ShowDialog();
                if (r == DialogResult.Ignore) { S.SkipVersion = pending.Version.ToString(); S.Save(); }
            }
        }

        // ------------------------------------------------------------ the offer / download window
        class UpdateForm : Form
        {
            readonly App app; readonly Release rel;
            readonly ProgressBar bar; readonly Label state; readonly Button go, later, skip;
            WebClient dl; string file;

            public UpdateForm(App app, Release rel)
            {
                this.app = app; this.rel = rel;
                Theme t = app.Theme; float k = app.DpiScale;
                Text = "GlassDesk update"; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
                StartPosition = FormStartPosition.CenterScreen; TopMost = true; Icon = app.AppIcon; AutoScaleMode = AutoScaleMode.None;
                ClientSize = new Size((int)(480 * k), (int)(360 * k));
                BackColor = t.Dark ? Color.FromArgb(28, 30, 38) : Color.FromArgb(246, 247, 250);
                ForeColor = t.Dark ? Color.FromArgb(236, 238, 245) : Color.FromArgb(24, 26, 32);
                Font = Gfx.SafeFont(t.FontName, 13.5f * k, FontStyle.Regular);
                Color sub = t.Dark ? Color.FromArgb(150, 156, 172) : Color.FromArgb(104, 110, 124);
                int pad = (int)(22 * k);
                var head = new Label { Text = "GlassDesk " + Short(rel.Version) + " is ready", AutoSize = false, Location = new Point(pad, pad), Size = new Size(ClientSize.Width - 2 * pad, (int)(30 * k)), Font = Gfx.SafeFont(t.FontName, 20 * k, FontStyle.Bold) };
                var cur = new Label { Text = "You have " + App.Version + ". The update keeps your fences, widgets and settings.", AutoSize = false, Location = new Point(pad, head.Bottom + (int)(4 * k)), Size = new Size(ClientSize.Width - 2 * pad, (int)(22 * k)), ForeColor = sub };
                string notes = string.IsNullOrEmpty(rel.Notes) ? "No release notes." : rel.Notes.Replace("\r\n", "\n").Replace("\n", "\r\n");
                if (notes.Length > 1200) notes = notes.Substring(0, 1200) + "…";
                var box = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Text = notes, BorderStyle = BorderStyle.None, BackColor = t.Dark ? Color.FromArgb(36, 38, 48) : Color.White, ForeColor = ForeColor,
                    Location = new Point(pad, cur.Bottom + (int)(12 * k)), Size = new Size(ClientSize.Width - 2 * pad, (int)(150 * k)) };
                bar = new ProgressBar { Location = new Point(pad, box.Bottom + (int)(14 * k)), Size = new Size(ClientSize.Width - 2 * pad, (int)(8 * k)), Visible = false };
                state = new Label { AutoSize = false, Location = new Point(pad, bar.Bottom + (int)(4 * k)), Size = new Size(ClientSize.Width - 2 * pad, (int)(22 * k)), ForeColor = sub,
                    Text = rel.MsiUrl == null ? "This release has no installer attached, so it will open on GitHub." : "" };
                int bh = (int)(36 * k), by = ClientSize.Height - pad - bh;
                go = Btn(rel.MsiUrl == null ? "Open on GitHub" : "Update now", true, t, k); go.Location = new Point(ClientSize.Width - pad - go.Width, by);
                later = Btn("Later", false, t, k); later.Location = new Point(go.Left - (int)(10 * k) - later.Width, by);
                skip = Btn("Skip this version", false, t, k); skip.Location = new Point(pad, by);
                go.Click += delegate { Start(); };
                later.Click += delegate { DialogResult = DialogResult.Cancel; };
                skip.Click += delegate { DialogResult = DialogResult.Ignore; };
                Controls.AddRange(new Control[] { head, cur, box, bar, state, go, later, skip });
                Load += delegate { Native.SetDarkTitleBar(Handle, t.Dark); };
                FormClosing += (s, e) => { if (dl != null) { dl.CancelAsync(); } };
            }

            static Button Btn(string text, bool primary, Theme t, float k)
            {
                var b = new Button { Text = text, FlatStyle = FlatStyle.Flat, AutoSize = false, Height = (int)(36 * k), Cursor = Cursors.Hand };
                using (var g = b.CreateGraphics()) b.Width = (int)(g.MeasureString(text, Gfx.SafeFont(t.FontName, 13.5f * k, FontStyle.Regular)).Width + 36 * k);
                b.FlatAppearance.BorderSize = primary ? 0 : 1;
                b.BackColor = primary ? t.Accent : Color.Transparent;
                b.ForeColor = primary ? Color.White : (t.Dark ? Color.FromArgb(236, 238, 245) : Color.FromArgb(24, 26, 32));
                return b;
            }

            void Start()
            {
                if (rel.MsiUrl == null)
                {
                    if (IsGitHubUrl(rel.Page)) try { Process.Start(rel.Page); } catch { }
                    DialogResult = DialogResult.OK; return;
                }
                go.Enabled = later.Enabled = skip.Enabled = false;
                bar.Visible = true; state.Text = "Downloading " + rel.MsiName + "…";
                try
                {
                    string dir = Path.Combine(Path.GetTempPath(), "GlassDesk-Update");
                    Directory.CreateDirectory(dir);
                    file = Path.Combine(dir, "GlassDesk-Setup-" + Short(rel.Version) + ".msi");
                    if (File.Exists(file)) File.Delete(file);
                    dl = Http.NewDownloader();
                    dl.DownloadProgressChanged += (s, e) => { bar.Value = Math.Max(0, Math.Min(100, e.ProgressPercentage)); };
                    dl.DownloadFileCompleted += (s, e) =>
                    {
                        if (e.Cancelled) return;
                        if (e.Error != null) { Fail("The download didn't finish. Check your internet connection and try again."); return; }
                        Install();
                    };
                    dl.DownloadFileAsync(new Uri(rel.MsiUrl), file);
                }
                catch (Exception ex) { Fail("Couldn't start the download (" + ex.Message + ")."); }
            }

            void Install()
            {
                // a real .msi always starts with the same 8 bytes; anything else (an error page) is refused
                byte[] magic = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };
                try
                {
                    var head = new byte[8];
                    using (var fs = File.OpenRead(file)) fs.Read(head, 0, 8);
                    for (int i = 0; i < 8; i++) if (head[i] != magic[i]) { Fail("The downloaded file isn't a valid installer, so it wasn't run."); return; }
                }
                catch { Fail("Couldn't open the downloaded installer."); return; }
                state.Text = "Installing… GlassDesk will restart by itself.";
                try
                {
                    Process.Start(new ProcessStartInfo("msiexec.exe", "/i \"" + file + "\" /passive /norestart") { UseShellExecute = true });
                    DialogResult = DialogResult.OK;
                    app.Invoker.BeginInvoke(new Action(app.Exit));   // the installer replaces the app and starts the new one
                }
                catch (Exception ex) { Fail("Windows didn't start the installer (" + ex.Message + ")."); }
            }

            void Fail(string msg)
            {
                bar.Visible = false; state.Text = msg;
                go.Text = "Try again"; go.Enabled = later.Enabled = skip.Enabled = true;
            }
        }
    }
}
