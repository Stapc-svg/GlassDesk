// GlassDesk 1.8 - the music widget: what's playing right now, or the last song you heard.
// Windows tells every app which song is playing (the same info as the volume flyout). A small hidden
// PowerShell helper reads it every 1.5 seconds and prints one line; the widget only draws it.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace GlassDesk
{
    internal class MusicWidget : Widget
    {
        string title = "", artist = "", status = "";   // status: Playing, Paused, Stopped... "" = nothing open
        int pos, dur;                                  // seconds
        DateTime posAt = DateTime.Now;
        Bitmap cover;
        bool sample, started;
        Process helper;
        int restarts;
        System.Windows.Forms.Timer tick;
        RectangleF rPrev, rPlay, rNext, rVol;

        public MusicWidget(App app) : base(app, "music")
        {
            try { string f = CoverFile; if (File.Exists(f)) cover = Load(f); } catch { }
        }
        protected override Size BaseSize { get { return new Size(360, 132); } }
        bool Playing { get { return status == "Playing"; } }
        static string CoverFile { get { return Path.Combine(Settings.Folder, "music-cover.png"); } }

        public void SetSample()
        {
            sample = true; title = "Neon Avenue"; artist = "Glass Echoes"; status = "Playing"; pos = 72; dur = 214; posAt = DateTime.Now;
            var b = new Bitmap(200, 200);
            using (var g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var lg = new LinearGradientBrush(new Rectangle(0, 0, 200, 200), Color.FromArgb(255, 120, 60, 220), Color.FromArgb(255, 255, 90, 150), 45f)) g.FillRectangle(lg, 0, 0, 200, 200);
                for (int i = 0; i < 4; i++) using (var pen = new Pen(Color.FromArgb(70, 255, 255, 255), 6)) g.DrawEllipse(pen, 40 + i * 14, 40 + i * 14, 120 - i * 28, 120 - i * 28);
            }
            cover = b;
        }

        // ---------------- the helper ----------------
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            tick = new System.Windows.Forms.Timer { Interval = 1000 };
            tick.Tick += delegate { if (Playing && Visible) Redraw(); };
            tick.Start();
        }
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) StartHelper(); else StopHelper();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { StopHelper(); if (tick != null) tick.Dispose(); }
            base.Dispose(disposing);
        }

        void StartHelper()
        {
            if (sample || started || App.Preview || !Native.IsWindows) return;
            started = true;
            try
            {
                Directory.CreateDirectory(Settings.Folder);
                string script = Path.Combine(Settings.Folder, "music-helper.ps1");
                File.WriteAllText(script, Script, Encoding.UTF8);
                var psi = new ProcessStartInfo("powershell.exe",
                    "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + script + "\" " + Process.GetCurrentProcess().Id + " \"" + CoverFile + "\"")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, StandardOutputEncoding = Encoding.UTF8 };
                helper = new Process { StartInfo = psi, EnableRaisingEvents = true };
                helper.OutputDataReceived += (s, ev) => { string line = ev.Data; if (line == null) return; try { BeginInvoke(new Action(delegate { OnLine(line); })); } catch { } };
                helper.Exited += delegate
                {
                    try { BeginInvoke(new Action(delegate { started = false; helper = null; if (Visible && ++restarts <= 3) StartHelperLater(); })); } catch { }
                };
                helper.Start();
                helper.BeginOutputReadLine();
            }
            catch (Exception ex) { Program.Log(ex); started = false; helper = null; }
        }
        void StartHelperLater()
        {
            var t = new System.Windows.Forms.Timer { Interval = 20000 };
            t.Tick += delegate { t.Stop(); t.Dispose(); StartHelper(); };
            t.Start();
        }
        void StopHelper()
        {
            var h = helper; helper = null; started = false;
            if (h == null) return;
            try { if (!h.HasExited) h.Kill(); } catch { }
            try { h.Dispose(); } catch { }
        }

        // one line from the helper: N (nothing playing) or S <tab> status <tab> title <tab> artist <tab> pos <tab> dur <tab> cover changed (1/0)
        void OnLine(string line)
        {
            if (line == "N") { if (status != "") { status = ""; Redraw(); } return; }
            if (!line.StartsWith("S\t")) return;
            string[] p = line.Split('\t');
            if (p.Length < 7) return;
            string newTitle = p[2].Trim(), newArtist = p[3].Trim();
            int.TryParse(p[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out pos);
            int.TryParse(p[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out dur);
            posAt = DateTime.Now;
            status = p[1];
            if (p[6] == "1") { try { var c = Load(CoverFile); if (cover != null) cover.Dispose(); cover = c; } catch { } }
            else if (p[6] == "-") { if (cover != null) cover.Dispose(); cover = null; try { File.Delete(CoverFile); } catch { } }
            if (newTitle.Length > 0 && (newTitle != title || newArtist != artist))
            {
                Cfg.LastSong = newTitle; Cfg.LastArtist = newArtist; Cfg.Save();
            }
            title = newTitle; artist = newArtist;
            Redraw();
        }

        static Bitmap Load(string f)
        {
            using (var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var img = Image.FromStream(fs)) return new Bitmap(img);
        }

        // ---------------- buttons ----------------
        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        static void Key(byte vk) { if (!Native.IsWindows) return; try { keybd_event(vk, 0, 1, UIntPtr.Zero); keybd_event(vk, 0, 3, UIntPtr.Zero); } catch { } }
        public void PlayPause() { Key(0xB3); if (status == "Playing") status = "Paused"; else if (status != "") { status = "Playing"; posAt = DateTime.Now; } Redraw(); }
        public void Next() { Key(0xB0); }
        public void Previous() { Key(0xB1); }
        public void OpenMixer() { try { Process.Start("sndvol.exe"); } catch { } }

        protected override void OnClickAt(Point p, MouseButtons b)
        {
            if (b == MouseButtons.Left)
            {
                if (rPlay.Contains(p)) { PlayPause(); return; }
                if (rNext.Contains(p)) { Next(); return; }
                if (rPrev.Contains(p)) { Previous(); return; }
                if (rVol.Contains(p)) { OpenMixer(); return; }
            }
            base.OnClickAt(p, b);
        }

        // ---------------- drawing ----------------
        static string Time(int s) { s = Math.Max(0, s); return (s / 60).ToString("00") + ":" + (s % 60).ToString("00"); }

        protected override void PaintContent(Graphics g)
        {
            Rectangle r = Inner;
            bool live = status != "" && title.Length > 0;
            string showTitle = live ? title : Cfg.LastSong, showArtist = live ? artist : Cfg.LastArtist;
            bool any = !string.IsNullOrEmpty(showTitle);

            // cover
            float cs = Math.Min(r.Height, T.Sf(96));
            var cr = new RectangleF(r.X - T.Sf(2), r.Y + (r.Height - cs) / 2, cs, cs);
            using (var path = Gfx.Round(cr, T.Sf(14)))
            {
                if (cover != null && any)
                {
                    var st = g.Save(); g.SetClip(path, CombineMode.Intersect);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    float s = Math.Min(cover.Width, cover.Height);
                    g.DrawImage(cover, cr, new RectangleF((cover.Width - s) / 2, (cover.Height - s) / 2, s, s), GraphicsUnit.Pixel);
                    g.Restore(st);
                }
                else
                {
                    using (var lg = new LinearGradientBrush(cr, T.Accent, Gfx.WithAlpha(Color.FromArgb(176, 108, 255), 255), 45f)) g.FillPath(lg, path);
                    DrawNote(g, new PointF(cr.X + cr.Width / 2, cr.Y + cr.Height / 2), cr.Width * 0.22f);
                }
                using (var pen = new Pen(Color.FromArgb(50, 255, 255, 255), 1f)) g.DrawPath(pen, path);
            }

            float x = cr.Right + T.Sf(16), w = r.Right - x;
            // status line
            string label = live ? (Playing ? "NOW PLAYING" : "PAUSED") : any ? "LAST PLAYED" : "MUSIC";
            Color lc = live && Playing ? T.Accent : T.SubText;
            if (live && Playing) DrawBars(g, new RectangleF(x, r.Y + T.Sf(3), T.Sf(12), T.Sf(11)), lc);
            float lx = live && Playing ? x + T.Sf(17) : x;
            Txt(g, label, Gfx.F(T.FontName, T.Sf(10.5f), FontStyle.Bold), lc, new RectangleF(lx, r.Y, w, T.Sf(16)), Gfx.Left);
            // title and artist
            Txt(g, any ? showTitle : "Nothing played yet", Gfx.F(T.FontName, T.Sf(15.5f), FontStyle.Bold), T.Text, new RectangleF(x, r.Y + T.Sf(16), w, T.Sf(24)), Gfx.Left);
            Txt(g, any ? (string.IsNullOrEmpty(showArtist) ? "Unknown artist" : showArtist) : "Play a song in Spotify, YouTube or any player",
                Gfx.F(T.FontName, T.Sf(12.5f), FontStyle.Regular), T.SubText, new RectangleF(x, r.Y + T.Sf(39), w, T.Sf(18)), Gfx.Left);

            // progress
            float py = r.Y + T.Sf(66);
            int now = pos + (Playing ? (int)(DateTime.Now - posAt).TotalSeconds : 0);
            if (live && dur > 0)
            {
                now = Math.Min(now, dur);
                var fs = Gfx.F(T.FontName, T.Sf(10.5f), FontStyle.Regular);
                float tw = T.Sf(36);
                Txt(g, Time(now), fs, T.SubText, new RectangleF(x, py - T.Sf(8), tw, T.Sf(16)), Gfx.Left);
                Txt(g, Time(dur), fs, T.SubText, new RectangleF(r.Right - tw, py - T.Sf(8), tw, T.Sf(16)), Gfx.Right);
                var bar = new RectangleF(x + tw + T.Sf(4), py - T.Sf(2), w - 2 * tw - T.Sf(8), T.Sf(4));
                Gfx.FillRound(g, T.Track, bar, bar.Height / 2);
                float f = Math.Max(0, Math.Min(1, (float)now / dur));
                if (f > 0.005f)
                {
                    var fill = new RectangleF(bar.X, bar.Y, Math.Max(bar.Height, bar.Width * f), bar.Height);
                    Gfx.FillRound(g, T.Accent, fill, bar.Height / 2);
                    using (var b = new SolidBrush(T.Text)) g.FillEllipse(b, fill.Right - T.Sf(4.5f), py - T.Sf(4.5f), T.Sf(9), T.Sf(9));
                }
            }
            else
            {
                var bar = new RectangleF(x, py - T.Sf(2), w, T.Sf(4));
                Gfx.FillRound(g, T.Track, bar, bar.Height / 2);
            }

            // controls
            float cy = r.Bottom - T.Sf(14), mid = x + w / 2, d = T.Sf(32), gap = T.Sf(40);
            rPlay = new RectangleF(mid - d / 2, cy - d / 2, d, d);
            rPrev = new RectangleF(mid - gap - T.Sf(14), cy - T.Sf(14), T.Sf(28), T.Sf(28));
            rNext = new RectangleF(mid + gap - T.Sf(14), cy - T.Sf(14), T.Sf(28), T.Sf(28));
            rVol = new RectangleF(r.Right - T.Sf(26), cy - T.Sf(13), T.Sf(26), T.Sf(26));
            using (var path = Gfx.Round(rPlay, d / 2)) using (var lg = new LinearGradientBrush(rPlay, T.Accent, Gfx.WithAlpha(Color.FromArgb(176, 108, 255), 255), 45f)) g.FillPath(lg, path);
            float s2 = T.Sf(6.5f);
            using (var b = new SolidBrush(Color.White))
            {
                if (Playing) { g.FillRectangle(b, mid - s2 * 0.75f, cy - s2, s2 * 0.5f, s2 * 2); g.FillRectangle(b, mid + s2 * 0.25f, cy - s2, s2 * 0.5f, s2 * 2); }
                else g.FillPolygon(b, new[] { new PointF(mid - s2 * 0.55f, cy - s2), new PointF(mid + s2 * 0.95f, cy), new PointF(mid - s2 * 0.55f, cy + s2) });
            }
            using (var b = new SolidBrush(T.Text))
            {
                DrawSkip(g, b, rPrev, false); DrawSkip(g, b, rNext, true);
            }
            DrawSpeaker(g, rVol);
        }

        void DrawSkip(Graphics g, Brush b, RectangleF r, bool forward)
        {
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, s = T.Sf(5.5f), dir = forward ? 1 : -1;
            g.FillPolygon(b, new[] { new PointF(cx - dir * s, cy - s), new PointF(cx + dir * s * 0.8f, cy), new PointF(cx - dir * s, cy + s) });
            float bx = forward ? cx + s * 0.8f : cx - s * 0.8f - T.Sf(2);
            g.FillRectangle(b, bx, cy - s, T.Sf(2), s * 2);
        }

        void DrawSpeaker(Graphics g, RectangleF r)
        {
            float cx = r.X + r.Width / 2 - T.Sf(3), cy = r.Y + r.Height / 2, s = T.Sf(4.5f);
            using (var b = new SolidBrush(T.SubText))
                g.FillPolygon(b, new[] { new PointF(cx - s * 1.2f, cy - s * 0.5f), new PointF(cx - s * 0.4f, cy - s * 0.5f), new PointF(cx + s * 0.5f, cy - s * 1.2f), new PointF(cx + s * 0.5f, cy + s * 1.2f), new PointF(cx - s * 0.4f, cy + s * 0.5f), new PointF(cx - s * 1.2f, cy + s * 0.5f) });
            using (var pen = new Pen(T.SubText, T.Sf(1.4f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawArc(pen, cx - s * 0.2f, cy - s * 0.9f, s * 1.8f, s * 1.8f, -50, 100);
                g.DrawArc(pen, cx - s * 0.6f, cy - s * 1.6f, s * 3.2f, s * 3.2f, -50, 100);
            }
        }

        void DrawBars(Graphics g, RectangleF r, Color c)
        {
            float[] h = { 0.55f, 1f, 0.7f };
            float bw = r.Width / 4.2f;
            using (var b = new SolidBrush(c))
                for (int i = 0; i < 3; i++) { float hh = r.Height * h[i]; Gfx.FillRound(g, c, new RectangleF(r.X + i * bw * 1.6f, r.Bottom - hh, bw, hh), bw / 2); }
        }

        static void DrawNote(Graphics g, PointF c, float s)
        {
            using (var b = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
            using (var pen = new Pen(Color.FromArgb(235, 255, 255, 255), s * 0.22f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.FillEllipse(b, c.X - s * 1.05f, c.Y + s * 0.35f, s * 0.9f, s * 0.7f);
                g.FillEllipse(b, c.X + s * 0.45f, c.Y + s * 0.15f, s * 0.9f, s * 0.7f);
                g.DrawLine(pen, c.X - s * 0.2f, c.Y + s * 0.65f, c.X - s * 0.2f, c.Y - s * 0.9f);
                g.DrawLine(pen, c.X + s * 1.3f, c.Y + s * 0.45f, c.X + s * 1.3f, c.Y - s * 1.1f);
                g.DrawLine(pen, c.X - s * 0.2f, c.Y - s * 0.9f, c.X + s * 1.3f, c.Y - s * 1.1f);
            }
        }

        // ---------------- the PowerShell helper ----------------
        const string Script = @"param([int]$owner, [string]$cover)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$asTask = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]
function Await($op, [Type]$type) { $t = $asTask.MakeGenericMethod($type).Invoke($null, @($op)); $null = $t.Wait(5000); $t.Result }
$null = [Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager, Windows.Media.Control, ContentType = WindowsRuntime]
$null = [Windows.Storage.Streams.IRandomAccessStreamWithContentType, Windows.Storage.Streams, ContentType = WindowsRuntime]
$mgr = Await ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager]::RequestAsync()) ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager])
$last = '#'
function Clean($s) { if ($s -eq $null) { return '' } return ($s -replace ""[`t`r`n]"", ' ') }
while ($true) {
  if (-not (Get-Process -Id $owner -ErrorAction SilentlyContinue)) { exit }
  try {
    $s = $mgr.GetCurrentSession()
    if ($s -eq $null) { [Console]::Out.WriteLine('N') }
    else {
      $p = Await ($s.TryGetMediaPropertiesAsync()) ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionMediaProperties])
      $info = $s.GetPlaybackInfo(); $tl = $s.GetTimelineProperties()
      $status = [string]$info.PlaybackStatus
      $pos = $tl.Position.TotalSeconds
      if ($status -eq 'Playing' -and $tl.LastUpdatedTime.Year -gt 2000) { $pos += ([DateTimeOffset]::Now - $tl.LastUpdatedTime).TotalSeconds }
      $dur = ($tl.EndTime - $tl.StartTime).TotalSeconds
      if ($pos -gt $dur -and $dur -gt 0) { $pos = $dur }
      $key = $p.Title + '|' + $p.Artist
      $art = '0'
      if ($key -ne $last) {
        $last = $key; $art = '-'
        if ($p.Thumbnail -ne $null) {
          try {
            $st = Await ($p.Thumbnail.OpenReadAsync()) ([Windows.Storage.Streams.IRandomAccessStreamWithContentType])
            $in = [System.IO.WindowsRuntimeStreamExtensions]::AsStreamForRead($st)
            $tmp = $cover + '.tmp'
            $fs = [System.IO.File]::Create($tmp); $in.CopyTo($fs); $fs.Close(); $in.Close()
            Move-Item -Force $tmp $cover
            $art = '1'
          } catch { $art = '-' }
        }
      }
      [Console]::Out.WriteLine('S' + ""`t"" + $status + ""`t"" + (Clean $p.Title) + ""`t"" + (Clean $p.Artist) + ""`t"" + [int][Math]::Max(0, $pos) + ""`t"" + [int][Math]::Max(0, $dur) + ""`t"" + $art)
    }
  } catch { [Console]::Out.WriteLine('N') }
  [Console]::Out.Flush()
  Start-Sleep -Milliseconds 1500
}
";
    }
}
