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

        /// <summary>Where the updater looks for new versions ("owner/repo" on GitHub). Can also be changed in Settings > About.</summary>
        public const string DefaultRepo = "YOUR-USERNAME/GlassDesk";
        public static Version VersionNumber { get { return typeof(App).Assembly.GetName().Version; } }
        public static string Version { get { return Updater.Short(VersionNumber); } }
        public bool Preview { get { return preview; } }
        public Control Invoker { get { return invoker; } }
        public Updater Updates;
        public WeatherWidget WeatherW { get { return weather; } }
        public PhotoWidget PhotosW { get { return photos; } }
        public TodoWidget TodoW { get { return todo; } }
        public CountdownWidget CountdownW { get { return countdown; } }

        readonly bool preview;
        public DateTime? FixedNow;
        public DateTime Now { get { return FixedNow ?? DateTime.Now; } }

        ClockWidget clock; SystemWidget system; CalendarWidget calendar; NotesWidget notes;
        WeatherWidget weather; TodoWidget todo; PhotoWidget photos;
        CountdownWidget countdown; QuoteWidget quote; GitHubWidget github; NetworkWidget network; FocusWidget focus; MusicWidget music; DayWidget day;
        internal GitHubWidget GitHubW { get { return github; } } internal FocusWidget FocusW { get { return focus; } }
        public WallpaperChanger Wallpaper;
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
            Safe("wallpaper accent", PickWallpaperAccent);
            Theme = Theme.From(Settings, DpiScale);
            AppIcon = MakeIcon(Theme.Accent);
            Safe("monitor layout", delegate { SwitchDisplayLayout(); });

            invoker = new Control(); invoker.CreateControl();

            tray = new NotifyIcon { Icon = AppIcon, Text = "GlassDesk", Visible = true };
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowSettings(); };
            tray.BalloonTipClicked += delegate { var a = balloonClick; balloonClick = null; if (a != null) a(); };
            tray.BalloonTipClosed += delegate { balloonClick = null; };
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
            Updates = new Updater(this);
            Updates.Start();
            Wallpaper = new WallpaperChanger(this);
            Safe("backup", delegate { Backup.Daily(this); });

            // the welcome screen shows once ever: the marker file stays when GlassDesk is updated
            string mark = Path.Combine(Settings.Folder, "welcome-screen-shown");
            if (!File.Exists(mark))
            {
                try { Directory.CreateDirectory(Settings.Folder); File.WriteAllText(mark, Version); } catch { }
                var wt = new Timer { Interval = 1200 };
                wt.Tick += delegate { wt.Stop(); wt.Dispose(); ShowWelcome(); };
                wt.Start();
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
            weather = new WeatherWidget(this); todo = new TodoWidget(this); photos = new PhotoWidget(this);
            Windows.Add(weather); Windows.Add(todo); Windows.Add(photos);
            countdown = new CountdownWidget(this); quote = new QuoteWidget(this);
            Windows.Add(countdown); Windows.Add(quote);
            github = new GitHubWidget(this); network = new NetworkWidget(this); focus = new FocusWidget(this);
            Windows.Add(github); Windows.Add(network); Windows.Add(focus);
            music = new MusicWidget(this); Windows.Add(music);
            day = new DayWidget(this); Windows.Add(day);
        }

        Action balloonClick;
        /// <summary>A Windows notification from the tray icon; clicking it runs onClick.</summary>
        public void Notify(string title, string text, Action onClick)
        {
            balloonClick = onClick;
            tray.ShowBalloonTip(8000, title, text, ToolTipIcon.Info);
        }

        // =============================================================== rules + search
        /// <summary>Fences that can hold desktop items, as (id, name) pairs for menus and dialogs.</summary>
        /// <summary>After importing a layout: read settings again and redraw everything.</summary>
        public void ReloadSettings()
        {
            Settings = Settings.Load();
            ApplySettings();
            PlaceAll();
            if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.RestyleSoon();
        }

        /// <summary>The wallpaper changed: make a new blurred copy and repaint every panel.</summary>
        public void RefreshBackdrop()
        {
            Backdrop.Clear();
            if (Settings.WallAccent) { ApplySettings(); return; }
            foreach (var w in Windows) if (w.Visible) w.Redraw();
        }

        public List<KeyValuePair<string, string>> FenceChoices()
        {
            var list = new List<KeyValuePair<string, string>>();
            foreach (string id in Settings.CustomFences) { string t; list.Add(new KeyValuePair<string, string>(id, Settings.Titles.TryGetValue(id, out t) ? t : "My fence")); }
            foreach (var c in Organizer.Categories) { string t; list.Add(new KeyValuePair<string, string>("cat." + c[0], Settings.Titles.TryGetValue("cat." + c[0], out t) ? t : c[1])); }
            return list;
        }
        public string FenceName(string id)
        {
            foreach (var kv in FenceChoices()) if (kv.Key == id) return kv.Value;
            return "a deleted fence";
        }

        public void AddRule(string preselect)
        {
            using (var d = new RuleDialog(this, FenceChoices(), preselect))
                if (d.ShowDialog() == DialogResult.OK)
                {
                    Settings.Rules.Add(new[] { d.Pattern, d.FenceId });
                    Settings.Save(); Rebuild();
                    if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.RefreshPage();
                }
        }

        LauncherForm search;
        public LauncherForm MakeLauncher()
        {
            var items = new List<KeyValuePair<DesktopItem, string>>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in fences.Values)
                foreach (var it in f.Items)
                    if (seen.Add(it.Path)) items.Add(new KeyValuePair<DesktopItem, string>(it, f.Caption));
            items.Sort((a, b) => string.Compare(a.Key.Name, b.Key.Name, StringComparison.CurrentCultureIgnoreCase));
            return new LauncherForm(this, items);
        }

        public const string UnsortedId = "cust.unsorted";

        public void ShowOrganize() { MakeOrganize().Show(); }

        public OrganizeForm MakeOrganize()
        {
            var groups = new List<OrganizeForm.Group>();
            var ticked = new HashSet<DesktopItem>();
            var byId = new Dictionary<string, OrganizeForm.Group>();
            foreach (var f in fences.Values)
            {
                if (!f.HoldsDesktopItems || f.Id == UnsortedId) continue;
                var g = new OrganizeForm.Group { Id = f.Id, Title = f.Caption };
                foreach (var it in f.Items) if (!it.IsRecycleBin) { g.Items.Add(it); ticked.Add(it); }
                groups.Add(g); byId[f.Id] = g;
            }
            FenceWindow un;
            if (fences.TryGetValue(UnsortedId, out un))
                foreach (var it in un.Items)
                {
                    string id = "cat." + Organizer.CategoryOf(it);
                    OrganizeForm.Group g;
                    if (!byId.TryGetValue(id, out g)) { g = new OrganizeForm.Group { Id = id, Title = Organizer.CategoryName(Organizer.CategoryOf(it)) }; groups.Add(g); byId[id] = g; }
                    g.Items.Add(it);
                }
            groups.Sort((a, b) => b.Items.Count.CompareTo(a.Items.Count));
            return new OrganizeForm(this, groups, ticked, ApplyOrganize);
        }

        void ApplyOrganize(HashSet<DesktopItem> sorted, List<DesktopItem> unsorted)
        {
            foreach (var it in sorted)
            {
                string pin;
                if (Settings.Pins.TryGetValue(it.Path, out pin) && pin == UnsortedId) Settings.Pins.Remove(it.Path);
            }
            foreach (var it in unsorted) if (!it.IsRecycleBin) Settings.Pins[it.Path] = UnsortedId;
            bool anyUnsorted = Settings.Pins.ContainsValue(UnsortedId);
            if (anyUnsorted && !Settings.CustomFences.Contains(UnsortedId)) { Settings.CustomFences.Add(UnsortedId); Settings.Titles[UnsortedId] = "Unsorted"; }
            if (!anyUnsorted && Settings.CustomFences.Contains(UnsortedId)) { Settings.CustomFences.Remove(UnsortedId); Settings.Positions.Remove(UnsortedId); }
            Settings.Save();
            Rebuild();
        }

        public void ShowWelcome()
        {
            var w = new WelcomeForm(this);
            w.Show(); w.Activate();
        }

        public void ShowSearch()
        {
            if (search != null && !search.IsDisposed) { search.Activate(); return; }
            search = MakeLauncher();
            search.FormClosed += delegate { search = null; };
            search.Show(); search.Activate();
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
                string ruled = null;
                if (!it.IsRecycleBin)
                    foreach (var rule in Settings.Rules)
                        if (holders.ContainsKey(rule[1]) && Organizer.Matches(rule[0], Path.GetFileName(it.Path))) { ruled = rule[1]; break; }
                if (Settings.Pins.TryGetValue(it.Path, out pin) && holders.ContainsKey(pin)) holders[pin].Add(it);
                else if (ruled != null) holders[ruled].Add(it);
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
            weather.Wanted = Settings.ShowWeather; todo.Wanted = Settings.ShowTodo; photos.Wanted = Settings.ShowPhotos;
            countdown.Wanted = Settings.ShowCountdown; quote.Wanted = Settings.ShowQuote; github.Wanted = Settings.ShowGitHub; network.Wanted = Settings.ShowNetwork; focus.Wanted = Settings.ShowFocus; music.Wanted = Settings.ShowMusic; day.Wanted = Settings.ShowDay;
            ApplyGroups();
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
                    if (!preview) r = KeepOnScreen(r);
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

        /// <summary>A panel that hangs past the edge of its screen (after a resolution or monitor change) is moved fully back on.</summary>
        static Rectangle KeepOnScreen(Rectangle r)
        {
            Rectangle wa = Screen.FromRectangle(r).WorkingArea;
            int w = Math.Min(r.Width, wa.Width), h = Math.Min(r.Height, wa.Height);
            int x = Math.Max(wa.Left, Math.Min(r.X, wa.Right - w));
            int y = Math.Max(wa.Top, Math.Min(r.Y, wa.Bottom - h));
            return new Rectangle(x, y, r.Width, r.Height);
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

        public void SaveZoom(string id, float zoom)
        {
            int pct = (int)Math.Round(zoom * 100);
            if (pct == 100) Settings.WidgetZoom.Remove(id); else Settings.WidgetZoom[id] = pct;
        }

        public void ResetZoom(Widget w)
        {
            Settings.WidgetZoom.Remove(w.Id);
            Settings.Save();
            PlaceWindow(w);
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
        /// <summary>"Match my wallpaper": take the accent from the wallpaper of the main screen.</summary>
        void PickWallpaperAccent()
        {
            if (!Settings.WallAccent) return;
            try
            {
                Rectangle scr = preview ? new Rectangle(0, 0, 1920, 1080) : Screen.PrimaryScreen.Bounds;
                Color c = Backdrop.Accent(scr);
                if (!c.IsEmpty) Settings.Accent = c;
            }
            catch (Exception ex) { Program.Log(ex); }
        }

        public void ApplySettings()
        {
            PickWallpaperAccent();
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
            photos.IntervalChanged();
            if (Wallpaper != null) Wallpaper.Apply();
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
            if (hotkey == null && Native.IsWindows) hotkey = new HotkeyWindow(this);
            if (hotkey != null) { hotkey.Set(1, Settings.Hotkey, Keys.G); hotkey.Set(2, Settings.SearchHotkey, Keys.F); }
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
            if (!Paused && Backdrop.Changed()) RefreshBackdrop();   // e.g. a slideshow changed the wallpaper
            // keep the Windows desktop icons hidden (Explorer can bring them back when it restarts)
            if (Settings.HideDesktopIcons && !hiddenByUser && (++iconCheck % (Settings.ShowSeconds ? 30 : 1)) == 0)
            {
                Native.SetDesktopIconsVisible(false); iconsHiddenByUs = true;
            }
            if (Paused || hiddenByUser) return;
            if (clock.Visible) clock.Redraw();
            if (Now.Date != lastDay) { lastDay = Now.Date; if (calendar.Visible) calendar.Redraw(); if (countdown.Visible) countdown.Redraw(); if (quote.Visible) quote.Redraw(); Backup.Daily(this); }
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
            try { invoker.BeginInvoke(new Action(delegate { Theme = Theme.From(Settings, DpiScale); Gfx.ClearFonts(); SwitchDisplayLayout(); PlaceAll(); })); } catch { }
        }

        /// <summary>A short name for the monitors plugged in right now (their sizes and places).</summary>
        static string CurrentDisplayKey()
        {
            var parts = new List<string>();
            foreach (var sc in Screen.AllScreens) parts.Add(sc.Bounds.X + "_" + sc.Bounds.Y + "_" + sc.Bounds.Width + "_" + sc.Bounds.Height);
            parts.Sort(StringComparer.Ordinal);
            uint h = 2166136261;
            foreach (char c in string.Join("|", parts.ToArray())) { h ^= c; h *= 16777619; }
            return "d" + h.ToString("x8");
        }
        public int DisplayCount { get { return Screen.AllScreens.Length; } }
        public int SavedLayouts { get { return Settings.Layouts.Count; } }

        /// <summary>Docked or undocked: keep the old layout for the old monitors, and bring back the one saved for these monitors.</summary>
        void SwitchDisplayLayout()
        {
            if (preview || !Settings.PerDisplayLayouts) return;
            string key = CurrentDisplayKey();
            if (key == Settings.DisplayKey) return;
            if (!string.IsNullOrEmpty(Settings.DisplayKey)) Settings.Layouts[Settings.DisplayKey] = new Dictionary<string, Rectangle>(Settings.Positions);
            Dictionary<string, Rectangle> saved;
            if (Settings.Layouts.TryGetValue(key, out saved) && saved.Count > 0) Settings.Positions = new Dictionary<string, Rectangle>(saved);
            Settings.DisplayKey = key;
            Settings.Save();
        }

        // =============================================================== widget groups
        static readonly Dictionary<string, string> widgetNames = new Dictionary<string, string>
        {
            { "clock", "Clock" }, { "system", "System" }, { "calendar", "Calendar" }, { "notes", "Notes" }, { "weather", "Weather" },
            { "todo", "To-do" }, { "photos", "Photos" }, { "countdown", "Countdown" }, { "quote", "Quote" }, { "github", "GitHub" },
            { "network", "Network" }, { "focus", "Focus timer" }, { "music", "Music" }, { "day", "Today" },
        };
        public static string WidgetName(string id) { string n; return widgetNames.TryGetValue(id, out n) ? n : id; }

        List<string> GroupOf(string id) { foreach (var g in Settings.Groups) if (g.Contains(id)) return g; return null; }
        readonly Dictionary<string, List<string>> liveGroups = new Dictionary<string, List<string>>();   // id -> wanted members of its group

        /// <summary>Only the first wanted widget of each group shows; the rest wait in the same spot.</summary>
        void ApplyGroups()
        {
            liveGroups.Clear();
            var byId = new Dictionary<string, GlassWindow>();
            foreach (var w in Windows) if (w is Widget) byId[w.Id] = w;
            foreach (var g in Settings.Groups)
            {
                var wanted = new List<string>();
                foreach (string id in g) { GlassWindow w; if (byId.TryGetValue(id, out w) && w.Wanted) wanted.Add(id); }
                if (wanted.Count < 2) continue;
                foreach (string id in wanted) liveGroups[id] = wanted;
                for (int i = 1; i < wanted.Count; i++) byId[wanted[i]].Wanted = false;
            }
        }

        /// <summary>Which member this is (0-based) and how many show in its group; false when not grouped.</summary>
        public bool GroupInfo(string id, out int index, out int count)
        {
            List<string> g; index = 0; count = 0;
            if (!liveGroups.TryGetValue(id, out g)) return false;
            index = g.IndexOf(id); count = g.Count; return true;
        }

        public void SwitchGroup(Widget w, int dir)
        {
            List<string> live; var g = GroupOf(w.Id);
            if (g == null || !liveGroups.TryGetValue(w.Id, out live)) return;
            string next = live[((live.IndexOf(w.Id) + dir) % live.Count + live.Count) % live.Count];
            if (next == w.Id) return;
            // the next one takes this one's spot and moves to the front
            Rectangle cur = new Rectangle(w.PanelBounds.Location, w.PanelBounds.Size), old;
            Settings.Positions[next] = Settings.Positions.TryGetValue(next, out old) ? new Rectangle(cur.Location, old.Size) : new Rectangle(cur.Location, Size.Empty);
            g.Remove(next); g.Insert(0, next);
            Settings.Save();
            ApplySettings();
        }

        public void JoinGroup(Widget w, Widget target)
        {
            var mine = GroupOf(w.Id); if (mine != null) { mine.Remove(w.Id); if (mine.Count < 2) Settings.Groups.Remove(mine); }
            var g = GroupOf(target.Id);
            if (g == null) { g = new List<string> { target.Id }; Settings.Groups.Add(g); }
            g.Add(w.Id);
            Settings.Save();
            ApplySettings();
        }

        public void LeaveGroup(Widget w)
        {
            var g = GroupOf(w.Id); if (g == null) return;
            g.Remove(w.Id); if (g.Count < 2) Settings.Groups.Remove(g);
            Settings.Positions.Remove(w.Id);   // find it a free spot of its own
            Settings.Save();
            ApplySettings();
        }

        void OnUserPrefChanged(object s, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.Desktop) { try { invoker.BeginInvoke(new Action(RefreshBackdrop)); } catch { } return; }
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
            m.Opened += delegate { Native.SetRoundCorners(m.Handle); };
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
            m.Opened += delegate { Native.SetRoundCorners(m.Handle); };
            m.Items.Add("Settings", null, delegate { ShowSettings(); });
            m.Items.Add((hiddenByUser ? "Show" : "Hide") + " panels    Ctrl+Alt+G", null, delegate { ToggleVisible(); });
            m.Items.Add("Find on desktop…    Ctrl+Alt+F", null, delegate { ShowSearch(); });
            m.Items.Add("New fence…", null, delegate { NewFence(); });
            m.Items.Add("Add a rule…", null, delegate { AddRule(null); });
            m.Items.Add("Edit note", null, delegate { if (!notes.Visible) { Settings.ShowNotes = true; ApplySettings(); } notes.Edit(); });
            m.Items.Add("Organize desktop…", null, delegate { ShowOrganize(); });
            m.Items.Add("Rescan desktop", null, delegate { Rebuild(); });
            m.Items.Add("Check for updates", null, delegate { Updates.Check(true); });
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
            if (w == countdown) m.Items.Add("Change event…", null, delegate { countdown.Edit(); });
            if (w == quote) m.Items.Add("Another quote", null, delegate { quote.Next(); });
            if (w == github) { m.Items.Add("Open profile", null, delegate { github.OpenProfile(); }); m.Items.Add("Change username…", null, delegate { github.ChooseUser(); }); m.Items.Add("Refresh now", null, delegate { github.Fetch(); }); }
            {
                int gi, gc;
                var grp = new ToolStripMenuItem("Group with");
                foreach (var o in Windows)
                    if (o is Widget && o != w && o.Visible) { var ow = (Widget)o; grp.DropDownItems.Add(WidgetName(ow.Id), null, delegate { JoinGroup(w, ow); }); }
                if (grp.DropDownItems.Count > 0) m.Items.Add(grp);
                if (GroupInfo(w.Id, out gi, out gc)) { m.Items.Add("Next in group  (scroll)", null, delegate { SwitchGroup(w, 1); }); m.Items.Add("Take out of group", null, delegate { LeaveGroup(w); }); }
            }
            if (w == music) { m.Items.Add("Play / pause", null, delegate { music.PlayPause(); }); m.Items.Add("Next song", null, delegate { music.Next(); }); m.Items.Add("Previous song", null, delegate { music.Previous(); }); m.Items.Add("Volume mixer", null, delegate { music.OpenMixer(); }); }
            if (w == focus) { m.Items.Add("Start / pause", null, delegate { focus.Toggle(); }); m.Items.Add("Reset", null, delegate { focus.Reset(); }); }
            if (w == todo) { m.Items.Add("Edit list", null, delegate { todo.Edit(); }); m.Items.Add("Clear finished tasks", null, delegate { todo.ClearDone(); }); }
            if (w == weather)
            {
                m.Items.Add("Refresh", null, delegate { weather.Fetch(); });
                m.Items.Add("Change city…", null, delegate { weather.ChooseCity(); });
                m.Items.Add(Settings.WeatherF ? "Use °C" : "Use °F", null, delegate { Settings.WeatherF = !Settings.WeatherF; Settings.Save(); weather.UnitsChanged(); });
            }
            if (w == photos) { m.Items.Add("Next photo", null, delegate { photos.Next(); }); m.Items.Add("Choose folder…", null, delegate { photos.ChooseFolder(); }); }
            if (w == clock)
            {
                m.Items.Add((Settings.ShowSeconds ? "Hide" : "Show") + " seconds", null, delegate { Settings.ShowSeconds = !Settings.ShowSeconds; Settings.Save(); ApplySettings(); });
                m.Items.Add(Settings.Clock24 ? "Use 12-hour time" : "Use 24-hour time", null, delegate { Settings.Clock24 = !Settings.Clock24; Settings.Save(); ApplySettings(); });
            }
            if (Settings.WidgetZoom.ContainsKey(w.Id)) m.Items.Add("Reset size", null, delegate { ResetZoom(w); });
            m.Items.Add("Hide this widget", null, delegate
            {
                if (w == clock) Settings.ShowClock = false; else if (w == system) Settings.ShowSystem = false;
                else if (w == calendar) Settings.ShowCalendar = false; else if (w == notes) Settings.ShowNotes = false;
                else if (w == countdown) Settings.ShowCountdown = false; else if (w == quote) Settings.ShowQuote = false; else if (w == github) Settings.ShowGitHub = false; else if (w == network) Settings.ShowNetwork = false; else if (w == focus) Settings.ShowFocus = false; else if (w == music) Settings.ShowMusic = false; else if (w == day) Settings.ShowDay = false;
                else if (w == weather) Settings.ShowWeather = false; else if (w == todo) Settings.ShowTodo = false; else if (w == photos) Settings.ShowPhotos = false;
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
                if (hotkey != null) { hotkey.Set(1, false, Keys.G); hotkey.Set(2, false, Keys.F); hotkey.Destroy(); }
                foreach (var w in watchers) w.Dispose();
                tray.Visible = false; tray.Dispose();
                if (settingsForm != null) settingsForm.Close();
                foreach (var w in Windows.ToArray()) w.Close();
            }
            catch { }
            ExitThread();
        }

        // =============================================================== logo / icon
        static Image logoImg; static bool logoTried;
        public static void DrawLogo(Graphics g, RectangleF r, Color accent)
        {
            if (!logoTried) { logoTried = true; try { var st = typeof(App).Assembly.GetManifestResourceStream("GlassDesk.logo.png"); if (st != null) logoImg = Image.FromStream(st); } catch { } }
            if (logoImg != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(logoImg, r); return;
            }
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
            readonly bool[] on = new bool[3];
            /// <summary>Turns Ctrl + Alt + key on or off for the given id.</summary>
            public void Set(int id, bool want, Keys key)
            {
                if (!IsHandleCreated || want == on[id]) return;
                if (want) on[id] = Native.RegisterHotKey(Handle, id, 0x1 | 0x2 | 0x4000, (uint)key);
                else { Native.UnregisterHotKey(Handle, id); on[id] = false; }
            }
            protected override void WndProc(ref Message m)
            {
                if (m.Msg == 0x312 && m.WParam.ToInt32() == 1) app.ToggleVisible();
                if (m.Msg == 0x312 && m.WParam.ToInt32() == 2) app.ShowSearch();
                base.WndProc(ref m);
            }
        }

        // =============================================================== preview (used to make screenshots)
        public void StartPreview(bool dark)
        {
            Settings = new Settings();
            Settings.Theme = dark ? "Dark" : "Light";
            Settings.GlassStyle = Environment.GetEnvironmentVariable("GLASSDESK_PREVIEW_CLEAR") == "1" ? "clear" : (Environment.GetEnvironmentVariable("GLASSDESK_PREVIEW_STYLE") ?? "frosted");
            Settings.Opacity = dark ? 30 : 34;
            Settings.FolderFences.Clear();
            DpiScale = 1f;
            Backdrop.PreviewWallpaper = Environment.GetEnvironmentVariable("GLASSDESK_PREVIEW_WALL");
            var look = Themes.Find(Environment.GetEnvironmentVariable("GLASSDESK_PREVIEW_LOOK") ?? "");
            string zs = Environment.GetEnvironmentVariable("GLASSDESK_PREVIEW_ZOOM");   // e.g. clock:150,todo:70
            if (!string.IsNullOrEmpty(zs)) foreach (string part in zs.Split(',')) { var kv = part.Split(':'); if (kv.Length == 2) Settings.WidgetZoom[kv[0]] = int.Parse(kv[1]); }
            if (look != null) { Settings.Look = look.Id; Settings.Accent = look.Accent; Settings.Radius = look.Radius; Settings.Opacity = look.Tint; }
            Theme = Theme.From(Settings, DpiScale);
            FixedNow = new DateTime(2026, 10, 22, 9, 41, 0);
            PreviewData.FakeStats(Stats);
            string demoDesk = Path.Combine(Path.GetTempPath(), "Desktop");
            Settings.CustomFences.Add("cust.demo");
            Settings.Titles["cust.demo"] = "School";
            foreach (string n in new[] { "Report final.docx", "Notes.txt", "School", "Budget 2026.xlsx" })
                Settings.Pins[Path.Combine(demoDesk, n)] = "cust.demo";
            Settings.Rules.Add(new[] { "*.pdf", "cust.demo" });
            Settings.Sort["cat.images"] = "date";
            Settings.FenceColor["cust.demo"] = "22BE82";
            Settings.FenceIcons["cat.folders"] = "large";
            Settings.CountdownTitle = "Summer trip"; Settings.CountdownDate = "2026-11-29";
            Settings.ShowWeather = false; Settings.ShowPhotos = false;
            Settings.ShowPhotos = true;
            Settings.TodoText = "[ ] Science project slides\n[ ] Return library books\n[x] Pay phone bill\n[ ] Back up photos\n[x] Install GlassDesk 1.3";
            string grp = Environment.GetEnvironmentVariable("GLASSDESK_PREVIEW_GROUP");   // e.g. music,quote
            if (!string.IsNullOrEmpty(grp)) Settings.Groups.Add(new List<string>(grp.Split(',')));
            CreateWidgets();
            weather.SetSample(); github.SetSample(); network.SetSample(); focus.SetSample(); music.SetSample();
            Rebuild();
            string sample = Environment.GetEnvironmentVariable("GLASSDESK_SAMPLE_PHOTO");
            if (!string.IsNullOrEmpty(sample) && File.Exists(sample)) photos.SetSampleFile(sample);
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
            add("images", new[] { "wallpaper.jpg", "logo.png", "sketch.png", "icon.png", "screenshot-1.png", "photo-0912.heic", "banner.webp" }, false);
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
