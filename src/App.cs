// GlassDesk - the application: owns settings, panels, tray icon, timers and file watchers.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace GlassDesk
{
    internal class App : ApplicationContext
    {
        public Settings Settings;
        public Theme Theme;
        public float DpiScale = 1f;
        public readonly SysStats Stats = new SysStats();
        public readonly List<GlassWindow> Windows = new List<GlassWindow>();
        public Icon AppIcon;
        public bool Paused;

        readonly bool preview;
        public DateTime? FixedNow;
        public DateTime Now { get { return FixedNow ?? DateTime.Now; } }

        ClockWidget clock; SystemWidget system; CalendarWidget calendar; NotesWidget notes;
        readonly Dictionary<string, FenceWindow> fences = new Dictionary<string, FenceWindow>();
        NotifyIcon tray;
        HotkeyWindow hotkey;
        Control invoker;
        Timer clockTimer, statsTimer, trimTimer, rescanTimer;
        readonly List<FileSystemWatcher> watchers = new List<FileSystemWatcher>();
        string watchKey = "";
        SettingsForm settingsForm;
        bool hiddenByUser, iconsHiddenByUs;
        readonly HashSet<GlassWindow> dirty = new HashSet<GlassWindow>();
        DateTime lastDay;

        public App(bool preview) { this.preview = preview; }

        // =============================================================== start
        public void Start()
        {
            Settings = Settings.Load();
            try { using (var g = Graphics.FromHwnd(IntPtr.Zero)) DpiScale = g.DpiX / 96f; } catch { DpiScale = 1f; }
            Theme = Theme.From(Settings, DpiScale);
            AppIcon = MakeIcon(Theme.Accent);

            invoker = new Control(); invoker.CreateControl();

            tray = new NotifyIcon { Icon = AppIcon, Text = "GlassDesk", Visible = true };
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowSettings(); };
            BuildTrayMenu();

            CreateWidgets();
            Safe("stats", delegate { Stats.Sample(); });
            Safe("layout", Rebuild);

            clockTimer = new Timer(); clockTimer.Tick += delegate { ClockTick(); }; ScheduleClock(); clockTimer.Start();
            statsTimer = new Timer(); statsTimer.Tick += delegate { StatsTick(); }; statsTimer.Interval = Settings.LowPower ? 5000 : 2000; statsTimer.Start();
            trimTimer = new Timer { Interval = 15000 }; trimTimer.Tick += delegate { trimTimer.Interval = 300000; if (Settings.TrimMemory && !hiddenByUser) Native.TrimMemory(); }; trimTimer.Start();
            rescanTimer = new Timer { Interval = 700 }; rescanTimer.Tick += delegate { rescanTimer.Stop(); Rebuild(); };
            lastDay = Now.Date;

            Safe("system settings", ApplySystemSettings);
            // at sign-in the desktop may not exist yet: try hiding its icons again shortly
            var again = new Timer { Interval = 4000 };
            again.Tick += delegate { again.Stop(); again.Dispose(); Safe("desktop icons", ApplySystemSettings); };
            again.Start();
            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
            SystemEvents.UserPreferenceChanged += OnUserPrefChanged;

            if (!File.Exists(Path.Combine(Settings.Folder, "welcomed")))
            {
                try { Directory.CreateDirectory(Settings.Folder); File.WriteAllText(Path.Combine(Settings.Folder, "welcomed"), "1"); } catch { }
                tray.ShowBalloonTip(4000, "GlassDesk is running", "Your desktop is now organized. Click the tray icon for settings, or press Ctrl + Alt + G to show / hide.", ToolTipIcon.Info);
            }
        }

        /// <summary>Runs one start-up step; a failure is logged instead of closing the app.</summary>
        public static void Safe(string what, Action a)
        {
            try { a(); } catch (Exception ex) { Program.Log(new Exception("Step failed: " + what, ex)); }
        }

        void CreateWidgets()
        {
            clock = new ClockWidget(this); system = new SystemWidget(this);
            calendar = new CalendarWidget(this); notes = new NotesWidget(this);
            Windows.Add(clock); Windows.Add(system); Windows.Add(calendar); Windows.Add(notes);
        }

        // =============================================================== organizer
        public void RescanSoon()
        {
            if (rescanTimer == null) return;
            rescanTimer.Stop(); rescanTimer.Start();
        }

        public void Rebuild()
        {
            // 1. everything on the desktop (+ the Recycle Bin, since the Windows icons are hidden)
            var all = new List<DesktopItem>();
            if (!preview)
            {
                foreach (string folder in Organizer.DesktopFolders()) all.AddRange(Organizer.List(folder));
                all.Add(DesktopItem.RecycleBin());
            }
            else PreviewData.Fill(all);

            // 2. which fences can hold desktop items
            var holders = new Dictionary<string, List<DesktopItem>>();
            if (Settings.ShowFences)
            {
                foreach (var c in Organizer.Categories) if (Settings.CategoryOn(c[0])) holders["cat." + c[0]] = new List<DesktopItem>();
                foreach (string id in Settings.CustomFences) holders[id] = new List<DesktopItem>();
            }

            // 3. put each item where you dragged it, otherwise in its category
            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var it in all)
            {
                present.Add(it.Path);
                string pin;
                if (Settings.Pins.TryGetValue(it.Path, out pin) && holders.ContainsKey(pin)) holders[pin].Add(it);
                else
                {
                    string id = "cat." + Organizer.CategoryOf(it);
                    if (holders.ContainsKey(id)) holders[id].Add(it);
                }
            }
            if (!preview)
            {
                bool pruned = false;
                foreach (string path in new List<string>(Settings.Pins.Keys))
                    if (!present.Contains(path)) { Settings.Pins.Remove(path); pruned = true; }
                if (pruned) Settings.Save();
            }

            // 4. create / update fence windows
            var keep = new HashSet<string>();
            foreach (var c in Organizer.Categories)
            {
                string id = "cat." + c[0];
                List<DesktopItem> items;
                if (!holders.TryGetValue(id, out items)) continue;
                if (Settings.HideEmptyFences && items.Count == 0) continue;
                keep.Add(id);
                GetFence(id, c[0], c[1], FenceKind.Category).SetItems(items);
            }
            foreach (string id in Settings.CustomFences)
            {
                List<DesktopItem> items;
                if (!holders.TryGetValue(id, out items)) continue;
                keep.Add(id);
                GetFence(id, id, "New fence", FenceKind.Custom).SetItems(items);
            }
            foreach (string folder in Settings.FolderFences)
            {
                string id = "fold." + Organizer.Hash(folder);
                keep.Add(id);
                string name = Path.GetFileName(folder.TrimEnd('\\', '/'));
                GetFence(id, folder, string.IsNullOrEmpty(name) ? folder : name, FenceKind.Folder)
                    .SetItems(preview ? new List<DesktopItem>() : Organizer.List(folder));
            }
            foreach (var id in new List<string>(fences.Keys))
                if (!keep.Contains(id))
                {
                    var f = fences[id]; fences.Remove(id); Windows.Remove(f);
                    foreach (var it in f.Items) it.Dispose();
                    f.Close(); f.Dispose();
                }
            clock.Wanted = Settings.ShowClock; system.Wanted = Settings.ShowSystem;
            calendar.Wanted = Settings.ShowCalendar; notes.Wanted = Settings.ShowNotes;
            if (!preview) UpdateWatchers();
            PlaceAll();
        }

        FenceWindow GetFence(string id, string key, string caption, FenceKind kind)
        {
            FenceWindow f;
            if (!fences.TryGetValue(id, out f))
            {
                f = new FenceWindow(this, id, key, caption, kind);
                fences[id] = f; Windows.Add(f);
            }
            string t;
            f.Caption = Settings.Titles.TryGetValue(id, out t) ? t : caption;
            return f;
        }

        /// <summary>Fences that hold desktop items (categories + your own), in screen order.</summary>
        public List<FenceWindow> DesktopFences()
        {
            var list = new List<FenceWindow>();
            foreach (var f in fences.Values) if (f.HoldsDesktopItems) list.Add(f);
            list.Sort((a, b) => a.Kind != b.Kind ? (a.Kind == FenceKind.Custom ? -1 : 1) : string.Compare(a.Caption, b.Caption, StringComparison.CurrentCultureIgnoreCase));
            return list;
        }

        public void Pin(string path, string fenceId)
        {
            // pinning an item back into its own category is the same as no pin
            if (fenceId.StartsWith("cat.") && fenceId == "cat." + Organizer.CategoryOf(new DesktopItem(path, Directory.Exists(path) || path == DesktopItem.RecycleBinPath)))
                Settings.Pins.Remove(path);
            else Settings.Pins[path] = fenceId;
        }

        public void Unpin(string path) { Settings.Pins.Remove(path); }

        /// <summary>Moves an item into a fence (from the right-click menu).</summary>
        public void MoveToFence(string path, FenceWindow target)
        {
            if (target.IsFolder) return;
            if (path == DesktopItem.RecycleBinPath || Organizer.IsOnDesktop(path))
            {
                Pin(path, target.Id); Settings.Save(); Rebuild();
                return;
            }
            string desk = Organizer.UserDesktop;
            if (Native.ShellTransfer(IntPtr.Zero, new[] { path }, desk, false))
            {
                Pin(Path.Combine(desk, Path.GetFileName(path.TrimEnd('\\'))), target.Id);
                Settings.Save();
            }
            RescanSoon();
        }

        /// <summary>Asks for a name and creates a new empty fence. Returns it, or null if cancelled.</summary>
        public FenceWindow NewFence()
        {
            string name = InputBox.Ask("New fence", "My fence", Theme);
            if (name == null) return null;
            name = name.Trim();
            if (name.Length == 0) name = "My fence";
            string id = "cust." + Guid.NewGuid().ToString("N").Substring(0, 8);
            Settings.CustomFences.Add(id);
            Settings.Titles[id] = name;
            Settings.ShowFences = true;
            Settings.Save();
            Rebuild();
            if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.RefreshPage();
            FenceWindow f;
            return fences.TryGetValue(id, out f) ? f : null;
        }

        public void DeleteCustomFence(string id)
        {
            string name;
            Settings.Titles.TryGetValue(id, out name);
            if (MessageBox.Show("Delete the fence \"" + (name ?? "fence") + "\"?\n\nIts items go back to their normal fences. No files are deleted.", "GlassDesk",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            Settings.CustomFences.Remove(id);
            foreach (var kv in new List<KeyValuePair<string, string>>(Settings.Pins)) if (kv.Value == id) Settings.Pins.Remove(kv.Key);
            Settings.Titles.Remove(id); Settings.Positions.Remove(id); Settings.Collapsed.Remove(id);
            Settings.Save();
            Rebuild();
            if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.RefreshPage();
        }

        void UpdateWatchers()
        {
            var folders = new List<string>(Organizer.DesktopFolders());
            foreach (string f in Settings.FolderFences) if (Directory.Exists(f)) folders.Add(f);
            string key = string.Join("|", folders.ToArray());
            if (key == watchKey) return;
            watchKey = key;
            foreach (var w in watchers) w.Dispose();
            watchers.Clear();
            foreach (string f in folders)
            {
                try
                {
                    var w = new FileSystemWatcher(f) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Attributes, InternalBufferSize = 8192 };
                    FileSystemEventHandler h = (s, e) => { try { invoker.BeginInvoke(new Action(RescanSoon)); } catch { } };
                    w.Created += h; w.Deleted += h; w.Changed += h;
                    w.Renamed += (s, e) => { try { invoker.BeginInvoke(new Action(RescanSoon)); } catch { } };
                    w.EnableRaisingEvents = true;
                    watchers.Add(w);
                }
                catch { }
            }
        }

        // =============================================================== layout
        public Rectangle WorkArea
        {
            get { return preview ? new Rectangle(0, 0, 1920, 1032) : Screen.PrimaryScreen.WorkingArea; }
        }

        static bool OnSomeScreen(Rectangle r)
        {
            foreach (var s in Screen.AllScreens)
            {
                var i = Rectangle.Intersect(s.WorkingArea, r);
                if (i.Width > 60 && i.Height > 30) return true;
            }
            return false;
        }

        public void PlaceAll()
        {
            Rectangle wa = WorkArea;
            int m = Theme.S(18);
            bool changed = false;
            var taken = new List<Rectangle>();
            var pending = new List<GlassWindow>();
            var placed = new List<KeyValuePair<GlassWindow, Rectangle>>();

            // windows you already placed keep their spot
            foreach (var w in Windows)
            {
                bool show = w.Wanted && !hiddenByUser;
                if (!show) { if (w.Visible) w.Hide(); continue; }
                Rectangle saved;
                if (Settings.Positions.TryGetValue(w.Id, out saved) && (preview || OnSomeScreen(saved)))
                {
                    var r = new Rectangle(saved.Location, w.PreferredPanelSize(saved.Size));
                    taken.Add(r); placed.Add(new KeyValuePair<GlassWindow, Rectangle>(w, r));
                }
                else pending.Add(w);
            }
            // new ones go into free space: widgets from the right, fences from the left
            foreach (var w in pending)
            {
                Size size = w.PreferredPanelSize(Size.Empty);
                var r = new Rectangle(FindFreeSpot(size, w is Widget, taken, wa, m), size);
                taken.Add(r); placed.Add(new KeyValuePair<GlassWindow, Rectangle>(w, r));
                Size store = size;
                if (w is FenceWindow && Settings.Collapsed.Contains(w.Id)) store.Height = Theme.S(220);
                Settings.Positions[w.Id] = new Rectangle(r.Location, store);
                changed = true;
            }
            foreach (var kv in placed)
            {
                var win = kv.Key; var rect = kv.Value;
                Safe("panel " + win.Id, delegate
                {
                    if (!preview && !win.Visible) win.Show();
                    win.SetPanelBounds(rect);
                });
            }
            if (changed && !preview) Settings.Save();
        }

        Point FindFreeSpot(Size s, bool fromRight, List<Rectangle> taken, Rectangle wa, int m)
        {
            int colStep = Math.Max(s.Width, Theme.S(fromRight ? 320 : 372)) + m;
            for (int col = 0; col < 20; col++)
            {
                int x = fromRight ? wa.Right - m - s.Width - col * colStep : wa.Left + m + col * colStep;
                if (x < wa.Left || x + s.Width > wa.Right) break;
                int y = wa.Top + m;
                while (y + s.Height <= wa.Bottom - m || y == wa.Top + m)
                {
                    var r = new Rectangle(x, y, s.Width, s.Height);
                    int blockBottom = -1;
                    foreach (var t in taken)
                        if (Rectangle.Inflate(t, m - 1, m - 1).IntersectsWith(r)) blockBottom = Math.Max(blockBottom, t.Bottom);
                    if (blockBottom < 0) return r.Location;
                    y = blockBottom + m;
                    if (y + s.Height > wa.Bottom - m) break;
                }
            }
            return new Point(wa.Left + m + Theme.S(40), wa.Top + m + Theme.S(40));   // screen is full: overlap a little
        }

        public void PlaceWindow(GlassWindow w)
        {
            Rectangle saved;
            if (!Settings.Positions.TryGetValue(w.Id, out saved)) { PlaceAll(); return; }
            w.SetPanelBounds(new Rectangle(saved.Location, w.PreferredPanelSize(saved.Size)));
        }

        public void SavePosition(string id, Rectangle r)
        {
            Rectangle old;
            if (Settings.Collapsed.Contains(id) && Settings.Positions.TryGetValue(id, out old)) r.Height = old.Height;
            Settings.Positions[id] = r;
            Settings.Save();
        }

        public void ResetLayout(bool fencesToo)
        {
            foreach (var w in Windows)
                if (fencesToo ? w is FenceWindow : w is Widget) Settings.Positions.Remove(w.Id);
            if (fencesToo) Settings.Collapsed.Clear();
            Settings.Save();
            PlaceAll();
        }

        public int VisibleCount { get { int n = 0; foreach (var w in Windows) if (w.Visible) n++; return n; } }

        // =============================================================== settings
        public void ApplySettings()
        {
            bool wasDark = Theme.Dark; Color oldAccent = Theme.Accent; float oldScale = Theme.Scale;
            Theme = Theme.From(Settings, DpiScale);
            if (oldScale != Theme.Scale) Gfx.ClearFonts();
            if (oldAccent.ToArgb() != Theme.Accent.ToArgb())
            {
                var old = AppIcon; AppIcon = MakeIcon(Theme.Accent); tray.Icon = AppIcon;
                if (settingsForm != null) settingsForm.Icon = AppIcon;
                if (old != null) old.Dispose();
            }
            Rebuild();
            ScheduleClock();
            statsTimer.Interval = Settings.LowPower ? 5000 : 2000;
            ApplySystemSettings();
            if (wasDark != Theme.Dark || oldAccent.ToArgb() != Theme.Accent.ToArgb())
            {
                BuildTrayMenu();
                if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.RestyleSoon();
            }
        }

        void ApplySystemSettings()
        {
            // start with Windows
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (k != null)
                    {
                        if (Settings.StartWithWindows) k.SetValue("GlassDesk", "\"" + Application.ExecutablePath + "\" --startup");
                        else if (k.GetValue("GlassDesk") != null) k.DeleteValue("GlassDesk", false);
                    }
                }
            }
            catch { }
            // hotkey
            if (Settings.Hotkey && hotkey == null) { hotkey = new HotkeyWindow(this); if (!hotkey.Register()) { hotkey.Destroy(); hotkey = null; } }
            else if (!Settings.Hotkey && hotkey != null) { hotkey.Unregister(); hotkey.Destroy(); hotkey = null; }
            // desktop icons
            if (Settings.HideDesktopIcons) { Native.SetDesktopIconsVisible(false); iconsHiddenByUs = true; }
            else if (iconsHiddenByUs) { Native.SetDesktopIconsVisible(true); iconsHiddenByUs = false; }
        }

        public void ShowSettings() { ShowSettings(-1); }
        public void ShowSettings(int page)
        {
            if (settingsForm == null || settingsForm.IsDisposed)
            {
                settingsForm = new SettingsForm(this, page < 0 ? 0 : page);
                settingsForm.FormClosed += delegate { settingsForm = null; };
                settingsForm.Show();
            }
            else
            {
                if (page >= 0) settingsForm.ShowPage(page);
                if (settingsForm.WindowState == FormWindowState.Minimized) settingsForm.WindowState = FormWindowState.Normal;
            }
            settingsForm.Activate();
        }

        // =============================================================== timers
        void ScheduleClock()
        {
            if (clockTimer == null) return;
            DateTime n = DateTime.Now;
            int ms = Settings.ShowSeconds && Settings.ShowClock ? 1000 - n.Millisecond : (60 - n.Second) * 1000 - n.Millisecond;
            clockTimer.Interval = Math.Max(50, ms + 15);
        }

        int iconCheck;
        void ClockTick()
        {
            ScheduleClock();
            CheckPause();
            // keep the Windows desktop icons hidden (Explorer can bring them back when it restarts)
            if (Settings.HideDesktopIcons && !hiddenByUser && (++iconCheck % (Settings.ShowSeconds ? 30 : 1)) == 0)
            {
                Native.SetDesktopIconsVisible(false); iconsHiddenByUs = true;
            }
            if (Paused || hiddenByUser) return;
            if (clock.Visible) clock.Redraw();
            if (Now.Date != lastDay) { lastDay = Now.Date; if (calendar.Visible) calendar.Redraw(); }
        }

        void StatsTick()
        {
            CheckPause();
            if (Paused || hiddenByUser || !system.Visible) return;
            Stats.Sample();
            system.Redraw();
        }

        void CheckPause()
        {
            bool p = Settings.PauseFullscreen && Native.FullScreenAppRunning();
            if (p == Paused) return;
            Paused = p;
            if (!Paused)
            {
                var list = new List<GlassWindow>(dirty); dirty.Clear();
                foreach (var w in list) w.Redraw();
                clock.Redraw();
            }
        }

        public void MarkDirty(GlassWindow w) { dirty.Add(w); }

        public void ToggleVisible()
        {
            hiddenByUser = !hiddenByUser;
            PlaceAll();
            if (Settings.HideDesktopIcons) Native.SetDesktopIconsVisible(hiddenByUser);
            if (hiddenByUser && Settings.TrimMemory) Native.TrimMemory();
            BuildTrayMenu();
        }

        void OnDisplayChanged(object s, EventArgs e)
        {
            try { invoker.BeginInvoke(new Action(delegate { Theme = Theme.From(Settings, DpiScale); Gfx.ClearFonts(); PlaceAll(); })); } catch { }
        }

        void OnUserPrefChanged(object s, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General || Settings.Theme != "Auto") return;
            try { invoker.BeginInvoke(new Action(delegate { if (Theme.Dark == Native.AppsUseLightTheme()) ApplySettings(); })); } catch { }
        }

        // =============================================================== menus
        public ContextMenuStrip NewMenu()
        {
            var m = new ContextMenuStrip
            {
                Renderer = new MenuRenderer(Palette.From(Theme)), ShowImageMargin = false,
                Font = Gfx.SafeFont(Theme.FontName, 13.5f * DpiScale, FontStyle.Regular),
                Padding = new Padding(2, 4, 2, 4)
            };
            m.Closed += delegate { try { invoker.BeginInvoke(new Action(delegate { var f = m.Font; m.Dispose(); f.Dispose(); })); } catch { } };
            return m;
        }

        void BuildTrayMenu()
        {
            if (tray == null) return;
            var m = new ContextMenuStrip
            {
                Renderer = new MenuRenderer(Palette.From(Theme)), ShowImageMargin = false,
                Font = Gfx.SafeFont(Theme.FontName, 13.5f * DpiScale, FontStyle.Regular),
                Padding = new Padding(2, 4, 2, 4)
            };
            m.Items.Add("Settings", null, delegate { ShowSettings(); });
            m.Items.Add((hiddenByUser ? "Show" : "Hide") + " panels    Ctrl+Alt+G", null, delegate { ToggleVisible(); });
            m.Items.Add("New fence…", null, delegate { NewFence(); });
            m.Items.Add("Edit note", null, delegate { if (!notes.Visible) { Settings.ShowNotes = true; ApplySettings(); } notes.Edit(); });
            m.Items.Add("Rescan desktop", null, delegate { Rebuild(); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Exit GlassDesk", null, delegate { Exit(); });
            var old = tray.ContextMenuStrip;
            tray.ContextMenuStrip = m;
            if (old != null) old.Dispose();
        }

        public void ShowWidgetMenu(Widget w)
        {
            var m = NewMenu();
            if (w == notes) m.Items.Add("Edit note", null, delegate { notes.Edit(); });
            if (w == clock)
            {
                m.Items.Add((Settings.ShowSeconds ? "Hide" : "Show") + " seconds", null, delegate { Settings.ShowSeconds = !Settings.ShowSeconds; Settings.Save(); ApplySettings(); });
                m.Items.Add(Settings.Clock24 ? "Use 12-hour time" : "Use 24-hour time", null, delegate { Settings.Clock24 = !Settings.Clock24; Settings.Save(); ApplySettings(); });
            }
            m.Items.Add("Hide this widget", null, delegate
            {
                if (w == clock) Settings.ShowClock = false; else if (w == system) Settings.ShowSystem = false;
                else if (w == calendar) Settings.ShowCalendar = false; else if (w == notes) Settings.ShowNotes = false;
                Settings.Save(); ApplySettings();
            });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Settings…", null, delegate { ShowSettings(2); });
            m.Show(Cursor.Position);
        }

        // =============================================================== exit
        public void Exit()
        {
            try
            {
                Settings.Save();
                SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
                SystemEvents.UserPreferenceChanged -= OnUserPrefChanged;
                if (iconsHiddenByUs) Native.SetDesktopIconsVisible(true);
                if (hotkey != null) { hotkey.Unregister(); hotkey.Destroy(); }
                foreach (var w in watchers) w.Dispose();
                tray.Visible = false; tray.Dispose();
                if (settingsForm != null) settingsForm.Close();
                foreach (var w in Windows.ToArray()) w.Close();
            }
            catch { }
            ExitThread();
        }

        // =============================================================== logo / icon
        public static void DrawLogo(Graphics g, RectangleF r, Color accent)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color light = Color.FromArgb(255, Math.Min(255, accent.R + 70), Math.Min(255, accent.G + 70), Math.Min(255, accent.B + 70));
            Color dark = Color.FromArgb(255, accent.R * 7 / 10, accent.G * 7 / 10, accent.B * 7 / 10);
            using (var path = Gfx.Round(r, r.Width * 0.26f))
            using (var lg = new LinearGradientBrush(new PointF(r.X, r.Y), new PointF(r.Right, r.Bottom), light, dark))
                g.FillPath(lg, path);
            float p = r.Width * 0.2f, gap = r.Width * 0.09f, cell = (r.Width - 2 * p - gap) / 2;
            for (int i = 0; i < 4; i++)
            {
                float x = r.X + p + (i % 2) * (cell + gap), y = r.Y + p + (i / 2) * (cell + gap);
                Gfx.FillRound(g, Color.FromArgb(i == 3 ? 120 : 235, 255, 255, 255), new RectangleF(x, y, cell, cell), cell * 0.3f);
            }
        }

        public static Bitmap LogoBitmap(int size, Color accent)
        {
            var b = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b)) { g.Clear(Color.Transparent); DrawLogo(g, new RectangleF(0.5f, 0.5f, size - 1, size - 1), accent); }
            return b;
        }

        static Icon MakeIcon(Color accent)
        {
            try
            {
                using (var b = LogoBitmap(32, accent)) return Icon.FromHandle(b.GetHicon());
            }
            catch { return SystemIcons.Application; }
        }

        // =============================================================== hotkey
        class HotkeyWindow : Control
        {
            readonly App app;
            public HotkeyWindow(App app) { this.app = app; CreateControl(); }
            public void Destroy() { Dispose(); }
            public bool Register() { return Native.IsWindows && Native.RegisterHotKey(Handle, 1, 0x1 | 0x2 | 0x4000, (uint)Keys.G); }
            public void Unregister() { if (Native.IsWindows && IsHandleCreated) Native.UnregisterHotKey(Handle, 1); }
            protected override void WndProc(ref Message m)
            {
                if (m.Msg == 0x312 && m.WParam.ToInt32() == 1) app.ToggleVisible();
                base.WndProc(ref m);
            }
        }

        // =============================================================== preview (used to make screenshots)
        public void StartPreview(bool dark)
        {
            Settings = new Settings();
            Settings.Theme = dark ? "Dark" : "Light";
            Settings.Blur = false;
            Settings.Opacity = dark ? 52 : 58;
            Settings.FolderFences.Clear();
            DpiScale = 1f;
            Theme = Theme.From(Settings, DpiScale);
            FixedNow = new DateTime(2026, 10, 22, 9, 41, 0);
            PreviewData.FakeStats(Stats);
            string demoDesk = Path.Combine(Path.GetTempPath(), "Desktop");
            Settings.CustomFences.Add("cust.demo");
            Settings.Titles["cust.demo"] = "School";
            foreach (string n in new[] { "Report final.docx", "Notes.txt", "School", "Budget 2026.xlsx" })
                Settings.Pins[Path.Combine(demoDesk, n)] = "cust.demo";
            CreateWidgets();
            Rebuild();
        }
    }

    internal static class PreviewData
    {
        public static void Fill(List<DesktopItem> all)
        {
            all.Add(DesktopItem.RecycleBin());
            Action<string, string[], bool> add = (cat, names, dir) => { foreach (var n in names) all.Add(new DesktopItem(Path.Combine(Path.Combine(Path.GetTempPath(), "Desktop"), n), dir)); };
            add("apps", new[] { "Chrome.lnk", "Spotify.lnk", "Visual Studio Code.lnk", "Steam.lnk", "Roblox Studio.lnk", "Discord.lnk", "Blender.lnk", "OBS Studio.lnk" }, false);
            add("folders", new[] { "Projects", "School", "Screenshots", "Game assets" }, true);
            add("docs", new[] { "Budget 2026.xlsx", "Notes.txt", "Report final.docx", "Invoice-1042.pdf", "Ideas.md" }, false);
            add("images", new[] { "wallpaper.jpg", "logo.png", "sketch.png", "photo-0912.heic", "banner.webp" }, false);
            add("media", new[] { "podcast.mp3", "clip.mp4" }, false);
            add("archives", new[] { "backup.zip", "assets.7z" }, false);
        }

        public static void FakeStats(SysStats s)
        {
            for (int i = 0; i < s.CpuHistory.Length; i++)
                s.CpuHistory[i] = (float)(0.16 + 0.09 * Math.Sin(i * 0.55) + 0.06 * Math.Sin(i * 1.7 + 1) + (i > 28 && i < 33 ? 0.18 : 0));
            s.Cpu = s.CpuHistory[s.CpuHistory.Length - 1];
            s.RamTotal = 16UL * 1073741824; s.RamUsed = (ulong)(9.4 * 1073741824);
            s.DiskTotal = 476L * 1073741824; s.DiskFree = 212L * 1073741824; s.DiskName = "C:";
            s.Battery = 82; s.Charging = true;
        }
    }
}
