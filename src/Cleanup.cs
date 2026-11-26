// GlassDesk 2.2 - smart cleanup: finds old screenshots, installers you already downloaded long ago,
// desktop files you haven't touched in months and huge old downloads.
// NOTHING moves on its own. GlassDesk only shows the list; files move only after you press "Tidy".
// Files are moved into plain folders you can open any time ("Old screenshots", "Old installers",
// "Old desktop files"), and big files go to the Recycle Bin, so everything can be brought back.
// "Undo last tidy-up" puts moved files back where they were.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace GlassDesk
{
    internal class CleanItem
    {
        public string Path, Rule, Target;   // Target = folder it moves to, or null for the Recycle Bin
        public long Size; public DateTime When;
        public string Name { get { return System.IO.Path.GetFileName(Path); } }
    }

    internal sealed class CleanupWatch
    {
        readonly App app;
        readonly System.Windows.Forms.Timer timer;
        bool busy;

        public CleanupWatch(App app)
        {
            this.app = app;
            timer = new System.Windows.Forms.Timer { Interval = 90 * 1000 };     // first look a minute and a half after start
            timer.Tick += delegate { timer.Interval = 6 * 3600 * 1000; Remind(); };
            if (!app.Preview) timer.Start();
        }

        // ---------------- where to look ----------------
        [DllImport("shell32.dll")] static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);
        public static string Downloads
        {
            get
            {
                try
                {
                    IntPtr p;
                    if (Native.IsWindows && SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out p) == 0)
                    { string s = Marshal.PtrToStringUni(p); Marshal.FreeCoTaskMem(p); if (!string.IsNullOrEmpty(s)) return s; }
                }
                catch { }
                return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            }
        }
        static string Pictures { get { return Environment.GetFolderPath(Environment.SpecialFolder.MyPictures); } }
        public static string ShotsTarget { get { return System.IO.Path.Combine(Pictures, "Old screenshots"); } }
        public static string InstallersTarget { get { return System.IO.Path.Combine(Downloads, "Old installers"); } }
        public static string DesktopTarget { get { return System.IO.Path.Combine(Organizer.UserDesktop, "Old desktop files"); } }

        static readonly string[] installerExt = { ".exe", ".msi", ".msix", ".msixbundle", ".appx", ".appxbundle" };
        static bool IsShot(string name)
        {
            string n = name.ToLowerInvariant(), ext = System.IO.Path.GetExtension(n);
            if (ext != ".png" && ext != ".jpg" && ext != ".jpeg") return false;
            return n.StartsWith("screenshot") || n.StartsWith("screen shot") || n.StartsWith("capture") || n.StartsWith("snip");
        }

        /// <summary>Finds what could be tidied. Reads file names and dates only; changes nothing.</summary>
        public static List<CleanItem> Scan(Settings s)
        {
            var list = new List<CleanItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            DateTime now = DateTime.Now;
            Action<string, Func<FileInfo, string>> look = (folder, rule) =>
            {
                try
                {
                    if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
                    foreach (var f in new DirectoryInfo(folder).GetFiles())   // only the folder itself, never inside subfolders
                    {
                        if ((f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                        if (f.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) || seen.Contains(f.FullName)) continue;
                        string r = rule(f); if (r == null) continue;
                        seen.Add(f.FullName);
                        list.Add(new CleanItem { Path = f.FullName, Rule = r, Size = f.Length, When = f.LastWriteTime,
                            Target = r == "shots" ? ShotsTarget : r == "installers" ? InstallersTarget : r == "desktop" ? DesktopTarget : null });
                    }
                }
                catch { }
            };
            string desk = Organizer.UserDesktop, dl = Downloads;
            if (s.CleanShots)
            {
                Func<FileInfo, string> shot = f => IsShot(f.Name) && (now - f.LastWriteTime).TotalDays > 14 ? "shots" : null;
                look(System.IO.Path.Combine(Pictures, "Screenshots"), f => (now - f.LastWriteTime).TotalDays > 14 && IsImage(f.Name) ? "shots" : null);
                look(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"OneDrive\Pictures\Screenshots"), f => (now - f.LastWriteTime).TotalDays > 14 && IsImage(f.Name) ? "shots" : null);
                look(desk, shot); look(dl, shot);
            }
            if (s.CleanBig) look(dl, f => f.Length > 500L * 1048576 && (now - f.LastWriteTime).TotalDays > 30 ? "big" : null);
            if (s.CleanInstallers) look(dl, f => Array.IndexOf(installerExt, f.Extension.ToLowerInvariant()) >= 0 && (now - f.LastWriteTime).TotalDays > 14 ? "installers" : null);
            if (s.CleanDesktop)
                look(desk, f =>
                {
                    string ext = f.Extension.ToLowerInvariant();
                    if (ext == ".lnk" || ext == ".url" || ext == ".appref-ms") return null;   // app shortcuts stay
                    DateTime used = f.LastWriteTime > f.LastAccessTime ? f.LastWriteTime : f.LastAccessTime;
                    return (now - used).TotalDays > 60 ? "desktop" : null;
                });
            list.Sort((a, b) => a.When.CompareTo(b.When));
            return list;
        }
        static bool IsImage(string n) { string e = System.IO.Path.GetExtension(n).ToLowerInvariant(); return e == ".png" || e == ".jpg" || e == ".jpeg" || e == ".gif" || e == ".bmp" || e == ".webp"; }

        // ---------------- reminder ----------------
        void Remind()
        {
            var s = app.Settings;
            if (!s.SmartCleanup || busy) return;
            DateTime last;
            if (DateTime.TryParseExact(s.CleanLastAsk ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out last) && (DateTime.Today - last).TotalDays < 7) return;
            RunScan(list =>
            {
                if (list.Count < 5) return;
                s.CleanLastAsk = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); s.Save();
                app.Notify(string.Format(Lang.L("GlassDesk can tidy {0} old files"), list.Count), Lang.L("Nothing moves until you say yes. Click to see them."), delegate { Show(list); });
            });
        }

        void RunScan(Action<List<CleanItem>> done)
        {
            busy = true;
            var s = app.Settings;
            var ui = SynchronizationContext.Current;
            var t = new Thread(delegate ()
            {
                List<CleanItem> list; try { list = Scan(s); } catch { list = new List<CleanItem>(); }
                if (ui != null) ui.Post(delegate { busy = false; done(list); }, null); else { busy = false; }
            }) { IsBackground = true, Name = "GlassDesk cleanup scan" };
            t.Start();
        }

        /// <summary>Opens the review window (from the tray menu, Settings or the reminder).</summary>
        public void Review(bool fromReminder)
        {
            if (busy) return;
            RunScan(list =>
            {
                if (list.Count == 0) { app.Notify(Lang.L("Nothing to tidy"), Lang.L("Your desktop, Downloads and screenshots look tidy already."), null); return; }
                Show(list);
            });
        }
        void Show(List<CleanItem> list) { new CleanupForm(app, list).Show(); }

        // ---------------- doing it (only after you press Tidy) ----------------
        static string UndoFile { get { return System.IO.Path.Combine(Settings.Folder, "cleanup-undo.txt"); } }
        public static bool CanUndo { get { try { return File.Exists(UndoFile) && new FileInfo(UndoFile).Length > 0; } catch { return false; } } }

        /// <summary>Moves (or recycles) the files you ticked. Returns how many worked.</summary>
        public static int Apply(List<CleanItem> items, out long recycledBytes)
        {
            recycledBytes = 0; int done = 0;
            var log = new List<string>();
            foreach (var it in items)
            {
                try
                {
                    if (!File.Exists(it.Path)) continue;
                    if (it.Target == null)
                    {
                        Recycle(it.Path);
                        recycledBytes += it.Size; done++;
                        continue;
                    }
                    Directory.CreateDirectory(it.Target);
                    string to = Free(System.IO.Path.Combine(it.Target, it.Name));
                    File.Move(it.Path, to);
                    log.Add(it.Path + "\t" + to);
                    done++;
                }
                catch (Exception ex) { Program.Log(ex); }   // in use or no permission: it just stays where it is
            }
            try { Directory.CreateDirectory(Settings.Folder); File.WriteAllLines(UndoFile, log.ToArray()); } catch { }
            return done;
        }
        /// <summary>To the Recycle Bin (not deleted for good), the same as pressing Delete in File Explorer.</summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static void Recycle(string path)
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        }
        static string Free(string path)
        {
            if (!File.Exists(path)) return path;
            string dir = System.IO.Path.GetDirectoryName(path), name = System.IO.Path.GetFileNameWithoutExtension(path), ext = System.IO.Path.GetExtension(path);
            for (int i = 2; ; i++) { string p = System.IO.Path.Combine(dir, name + " (" + i + ")" + ext); if (!File.Exists(p)) return p; }
        }
        /// <summary>Puts the files from the last tidy-up back where they were.</summary>
        public static int Undo()
        {
            int n = 0;
            try
            {
                foreach (string line in File.ReadAllLines(UndoFile))
                {
                    var p = line.Split('\t'); if (p.Length != 2) continue;
                    try { if (File.Exists(p[1]) && !File.Exists(p[0])) { File.Move(p[1], p[0]); n++; } } catch { }
                    // the "Old …" folder goes away again if Undo emptied it
                    try { string dir = System.IO.Path.GetDirectoryName(p[1]); if (Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0) Directory.Delete(dir); } catch { }
                }
                File.WriteAllText(UndoFile, "");
            }
            catch { }
            return n;
        }

        public static string Ago(DateTime when)
        {
            double d = (DateTime.Now - when).TotalDays;
            if (d >= 60) return string.Format(Lang.L("{0} months old"), (int)(d / 30));
            if (d >= 14) return string.Format(Lang.L("{0} weeks old"), (int)(d / 7));
            return string.Format(Lang.L("{0} days old"), (int)d);
        }
        public static string Size(long b)
        {
            if (b >= 1073741824) return (b / 1073741824.0).ToString("0.0", CultureInfo.InvariantCulture) + " GB";
            if (b >= 1048576) return (b / 1048576.0).ToString("0", CultureInfo.InvariantCulture) + " MB";
            return Math.Max(1, b / 1024) + " KB";
        }
    }

    /// <summary>The review window: every file, ticked or not, grouped by why it was found.</summary>
    internal class CleanupForm : GlassForm
    {
        class Group { public string Rule, Title, Where; public List<CleanItem> Items = new List<CleanItem>(); }
        readonly List<Group> groups = new List<Group>();
        readonly HashSet<CleanItem> on = new HashSet<CleanItem>();
        readonly HashSet<string> open = new HashSet<string>();
        readonly List<KeyValuePair<RectangleF, Action>> hits = new List<KeyValuePair<RectangleF, Action>>();
        int scroll, contentH;
        Point mouse = new Point(-1, -1);
        Point dragFrom; bool dragging;
        string doneText;                     // after tidying: what happened

        public CleanupForm(App app, List<CleanItem> items) : base(app)
        {
            ShowInTaskbar = true; TopMost = true;
            if (app.AppIcon != null) Icon = app.AppIcon;
            Text = "GlassDesk — " + Lang.L("Tidy up old files");
            Add("shots", Lang.L("Old screenshots"), Lang.L("Older than 2 weeks · go to Pictures › Old screenshots"), items);
            Add("installers", Lang.L("Old installers"), Lang.L("Downloaded over 2 weeks ago · go to Downloads › Old installers"), items);
            Add("desktop", Lang.L("Desktop files you haven't opened in 2 months"), Lang.L("Go to a folder on your desktop: Old desktop files"), items);
            Add("big", Lang.L("Big old downloads"), Lang.L("Over 500 MB and a month old · go to the Recycle Bin (not ticked: tick the ones you want gone)"), items);
            foreach (var g in groups) if (g.Rule != "big") foreach (var it in g.Items) on.Add(it);
            int w = (int)(760 * k), h = (int)(640 * k);
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            w = Math.Min(w, wa.Width - 40); h = Math.Min(h, wa.Height - 40);
            Bounds = new Rectangle(wa.X + (wa.Width - w) / 2, wa.Y + (wa.Height - h) / 2, w, h);
        }
        void Add(string rule, string title, string where, List<CleanItem> items)
        {
            var g = new Group { Rule = rule, Title = title, Where = where };
            foreach (var it in items) if (it.Rule == rule) g.Items.Add(it);
            if (g.Items.Count > 0) groups.Add(g);
            if (g.Items.Count <= 6) open.Add(rule);
        }

        int Pad { get { return (int)(26 * k); } }
        int HeadH { get { return (int)(104 * k); } }
        int FootTop { get { return Height - (int)(70 * k); } }
        Rectangle Body { get { return new Rectangle(Pad, HeadH, Width - 2 * Pad, FootTop - HeadH - (int)(8 * k)); } }
        void Hit(RectangleF r, Action a) { hits.Add(new KeyValuePair<RectangleF, Action>(r, a)); }

        protected override void PaintAll(Graphics g)
        {
            hits.Clear();
            PaintGlass(g);
            using (var b = new SolidBrush(P.Text)) g.DrawString(Lang.L("Tidy up old files"), Gfx.F(T.DisplayFont, 24 * k, FontStyle.Bold), b, new RectangleF(Pad, 22 * k, Width - 2 * Pad, 34 * k), Gfx.Left);
            using (var b = new SolidBrush(P.Sub))
            {
                var f = Gfx.F(T.FontName, 13 * k, FontStyle.Regular);
                g.DrawString(Lang.L("Nothing has moved. Untick anything you want to keep, then press Tidy."), f, b, new RectangleF(Pad, 56 * k, Width - 2 * Pad - 60 * k, 20 * k), Gfx.Left);
                g.DrawString(Lang.L("Files go to folders you can open any time, and Undo puts them back."), f, b, new RectangleF(Pad, 76 * k, Width - 2 * Pad - 60 * k, 20 * k), Gfx.Left);
            }
            var cr = new RectangleF(Width - 50 * k, 16 * k, 36 * k, 36 * k);
            Close(g, cr, cr.Contains(mouse)); Hit(cr, delegate { Close(); });

            if (doneText != null) { PaintDone(g); return; }

            var body = Body;
            var st = g.Save(); g.SetClip(body);
            float y = body.Y - scroll, rowH = 40 * k, gap = 14 * k;
            var fName = Gfx.F(T.FontName, 12.5f * k, FontStyle.Regular);
            var fSub = Gfx.F(T.FontName, 11.5f * k, FontStyle.Regular);
            foreach (var grp in groups)
            {
                bool isOpen = open.Contains(grp.Rule);
                float ch = 66 * k + (isOpen ? grp.Items.Count * rowH + 8 * k : 0);
                var card = new RectangleF(body.X, y, body.Width - 10 * k, ch);
                y += ch + gap;
                if (card.Bottom < body.Y || card.Y > body.Bottom) continue;
                Chip(g, card, 14 * k, false);
                int n = 0; long bytes = 0; foreach (var it in grp.Items) if (on.Contains(it)) { n++; bytes += it.Size; }
                var box = new RectangleF(card.X + 16 * k, card.Y + 16 * k, 20 * k, 20 * k);
                DrawCheck(g, box, n == grp.Items.Count ? 2 : n > 0 ? 1 : 0, true);
                var gg = grp;
                Hit(new RectangleF(card.X, card.Y, 48 * k, 52 * k), delegate { bool all = true; foreach (var it in gg.Items) if (!on.Contains(it)) all = false; foreach (var it in gg.Items) { if (all) on.Remove(it); else on.Add(it); } Invalidate(); });
                using (var b = new SolidBrush(P.Text)) g.DrawString(grp.Title, Gfx.F(T.FontName, 14 * k, FontStyle.Bold), b, new RectangleF(box.Right + 12 * k, card.Y + 8 * k, card.Width - 240 * k, 26 * k), Gfx.Left);
                using (var b = new SolidBrush(P.Sub)) g.DrawString(grp.Where, fSub, b, new RectangleF(box.Right + 12 * k, card.Y + 32 * k, card.Width - 80 * k, 22 * k), Gfx.Left);
                string sel = n + " / " + grp.Items.Count + " · " + CleanupWatch.Size(bytes);
                var pill = new RectangleF(card.Right - 196 * k, card.Y + 12 * k, 140 * k, 24 * k);
                Gfx.FillRound(g, Gfx.WithAlpha(P.Accent, T.Dark ? 60 : 40), pill, 12 * k);
                using (var b = new SolidBrush(P.Text)) g.DrawString(sel, Gfx.F(T.FontName, 11 * k, FontStyle.Regular), b, pill, Gfx.Center);
                // show / hide the list
                var tog = new RectangleF(card.Right - 48 * k, card.Y + 8 * k, 34 * k, 32 * k);
                if (tog.Contains(mouse)) Chip(g, tog, 10 * k, true);
                Chevron(g, tog, isOpen);
                Hit(new RectangleF(box.Right, card.Y, card.Width - 48 * k, 56 * k), delegate { if (!open.Remove(gg.Rule)) open.Add(gg.Rule); Invalidate(); });
                if (!isOpen) continue;
                for (int i = 0; i < grp.Items.Count; i++)
                {
                    var it = grp.Items[i];
                    var row = new RectangleF(card.X + 8 * k, card.Y + 60 * k + i * rowH, card.Width - 16 * k, rowH);
                    if (row.Bottom < body.Y || row.Y > body.Bottom) continue;
                    bool isOn = on.Contains(it);
                    if (row.Contains(mouse)) Gfx.FillRound(g, T.Dark ? Color.FromArgb(22, 255, 255, 255) : Color.FromArgb(14, 0, 0, 0), row, 8 * k);
                    DrawCheck(g, new RectangleF(row.X + 10 * k, row.Y + (rowH - 18 * k) / 2, 18 * k, 18 * k), isOn ? 2 : 0, false);
                    using (var b = new SolidBrush(isOn ? P.Text : P.Sub)) g.DrawString(it.Name, fName, b, new RectangleF(row.X + 40 * k, row.Y, row.Width - 250 * k, rowH), Gfx.Left);
                    using (var b = new SolidBrush(P.Sub)) g.DrawString(CleanupWatch.Ago(it.When) + "  ·  " + CleanupWatch.Size(it.Size), fSub, b, new RectangleF(row.Right - 210 * k, row.Y, 200 * k, rowH), Gfx.Right);
                    var item = it;
                    Hit(row, delegate { if (!on.Remove(item)) on.Add(item); Invalidate(); });
                }
            }
            contentH = (int)(y + scroll - body.Y);
            g.Restore(st);
            int max = Math.Max(0, contentH - body.Height);
            if (max > 0)
            {
                float th = Math.Max(30 * k, body.Height * (float)body.Height / contentH);
                float ty = body.Y + (body.Height - th) * scroll / max;
                Gfx.FillRound(g, Gfx.WithAlpha(P.Text, 60), new RectangleF(Width - Pad / 2f - 3 * k, ty, 4 * k, th), 2 * k);
            }

            // footer
            using (var pen = new Pen(T.Dark ? Color.FromArgb(26, 255, 255, 255) : Color.FromArgb(18, 0, 0, 0))) g.DrawLine(pen, Pad, FootTop, Width - Pad, FootTop);
            long total = 0; foreach (var it in on) total += it.Size;
            using (var b = new SolidBrush(P.Sub))
                g.DrawString(on.Count + " " + Lang.L("files ticked") + "  ·  " + CleanupWatch.Size(total), Gfx.F(T.FontName, 12.5f * k, FontStyle.Regular), b, new RectangleF(Pad, FootTop, Width / 2f, Height - FootTop), Gfx.Left);
            var fB = Gfx.F(T.FontName, 13 * k, FontStyle.Bold);
            float bh = 40 * k, by = FootTop + (Height - FootTop - bh) / 2;
            var go = new RectangleF(Width - Pad - 180 * k, by, 180 * k, bh);
            var cancel = new RectangleF(go.X - 12 * k - 110 * k, by, 110 * k, bh);
            Chip(g, cancel, bh / 2, cancel.Contains(mouse));
            using (var b = new SolidBrush(P.Text)) g.DrawString(Lang.L("Not now"), fB, b, cancel, Gfx.Center);
            Hit(cancel, delegate { Close(); });
            if (on.Count > 0)
            {
                AccentButton(g, go, Lang.L("Tidy") + " " + on.Count + " " + Lang.L("files"), fB, go.Contains(mouse));
                Hit(go, Tidy);
            }
            else { Chip(g, go, bh / 2, false); using (var b = new SolidBrush(P.Sub)) g.DrawString(Lang.L("Nothing ticked"), fB, b, go, Gfx.Center); }
        }

        void Tidy()
        {
            var list = new List<CleanItem>();
            foreach (var grp in groups) foreach (var it in grp.Items) if (on.Contains(it)) list.Add(it);
            long rec; int n = CleanupWatch.Apply(list, out rec);
            doneText = string.Format(Lang.L("Tidied {0} files."), n) + (rec > 0 ? " " + string.Format(Lang.L("{0} went to the Recycle Bin."), CleanupWatch.Size(rec)) : "");
            app.Rebuild();
            Invalidate();
        }

        void PaintDone(Graphics g)
        {
            var body = Body;
            var ic = new RectangleF(Width / 2f - 36 * k, body.Y + 40 * k, 72 * k, 72 * k);
            using (var b = new LinearGradientBrush(ic, P.Accent, P.Accent2, 45f)) g.FillEllipse(b, ic);
            using (var pen = new Pen(Color.White, 5 * k) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(pen, new[] { new PointF(ic.X + ic.Width * 0.28f, ic.Y + ic.Height * 0.52f), new PointF(ic.X + ic.Width * 0.44f, ic.Y + ic.Height * 0.68f), new PointF(ic.X + ic.Width * 0.74f, ic.Y + ic.Height * 0.36f) });
            using (var b = new SolidBrush(P.Text)) g.DrawString(doneText, Gfx.F(T.FontName, 16 * k, FontStyle.Bold), b, new RectangleF(body.X, ic.Bottom + 20 * k, body.Width, 30 * k), Gfx.Center);
            using (var b = new SolidBrush(P.Sub)) g.DrawString(Lang.L("Changed your mind? Undo puts every moved file back."), Gfx.F(T.FontName, 13 * k, FontStyle.Regular), b, new RectangleF(body.X, ic.Bottom + 52 * k, body.Width, 24 * k), Gfx.Center);
            var fB = Gfx.F(T.FontName, 13 * k, FontStyle.Bold);
            float bh = 40 * k, by = FootTop + (Height - FootTop - bh) / 2;
            var close = new RectangleF(Width - Pad - 140 * k, by, 140 * k, bh);
            var undo = new RectangleF(close.X - 12 * k - 120 * k, by, 120 * k, bh);
            Chip(g, undo, bh / 2, undo.Contains(mouse));
            using (var b = new SolidBrush(P.Text)) g.DrawString(Lang.L("Undo"), fB, b, undo, Gfx.Center);
            Hit(undo, delegate { int n = CleanupWatch.Undo(); doneText = null; app.Rebuild(); Close(); app.Notify(Lang.L("Undone"), string.Format(Lang.L("{0} files are back where they were."), n), null); });
            AccentButton(g, close, Lang.L("Done"), fB, close.Contains(mouse));
            Hit(close, delegate { Close(); });
        }

        void Chevron(Graphics g, RectangleF r, bool up)
        {
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, s = 5 * k;
            using (var pen = new Pen(P.Sub, 1.8f * k) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(pen, up ? new[] { new PointF(cx - s, cy + s / 2), new PointF(cx, cy - s / 2), new PointF(cx + s, cy + s / 2) }
                                    : new[] { new PointF(cx - s, cy - s / 2), new PointF(cx, cy + s / 2), new PointF(cx + s, cy - s / 2) });
        }

        void DrawCheck(Graphics g, RectangleF r, int state, bool square)
        {
            float rad = square ? 5 * k : r.Width / 2;
            using (var p = Gfx.Round(r, rad))
            {
                if (state > 0) using (var b = new LinearGradientBrush(r, P.Accent, P.Accent2, 45f)) g.FillPath(b, p);
                else
                {
                    using (var b = new SolidBrush(T.Dark ? Color.FromArgb(40, 0, 0, 0) : Color.FromArgb(160, 255, 255, 255))) g.FillPath(b, p);
                    using (var pen = new Pen(Gfx.WithAlpha(P.Text, 120), 1.4f * k)) g.DrawPath(pen, p);
                }
            }
            using (var pen = new Pen(Color.White, 1.8f * k) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                if (state == 2) g.DrawLines(pen, new[] { new PointF(r.X + r.Width * 0.27f, r.Y + r.Height * 0.52f), new PointF(r.X + r.Width * 0.44f, r.Y + r.Height * 0.68f), new PointF(r.X + r.Width * 0.74f, r.Y + r.Height * 0.34f) });
                else if (state == 1) g.DrawLine(pen, r.X + r.Width * 0.3f, r.Y + r.Height / 2, r.Right - r.Width * 0.3f, r.Y + r.Height / 2);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging) { Location = new Point(Left + e.X - dragFrom.X, Top + e.Y - dragFrom.Y); return; }
            mouse = e.Location; Invalidate();
            bool any = false; foreach (var h in hits) if (h.Key.Contains(e.Location)) any = true;
            Cursor = any ? Cursors.Hand : Cursors.Default;
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && e.Y < HeadH && e.X < Width - 60 * k) { dragging = true; dragFrom = e.Location; }
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (dragging) { dragging = false; Invalidate(); return; }
            if (e.Button != MouseButtons.Left) return;
            for (int i = hits.Count - 1; i >= 0; i--)
                if (hits[i].Key.Contains(e.Location) && (doneText != null || Body.Contains(e.Location) == hits[i].Key.IntersectsWith(Body))) { hits[i].Value(); return; }
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            scroll = Math.Max(0, Math.Min(Math.Max(0, contentH - Body.Height), scroll - e.Delta * (int)(80 * k) / 120));
            Invalidate();
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); mouse = new Point(-1, -1); Invalidate(); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); base.OnKeyDown(e); }
        protected override void OnLocationChanged(EventArgs e) { base.OnLocationChanged(e); Invalidate(); }
    }
}
