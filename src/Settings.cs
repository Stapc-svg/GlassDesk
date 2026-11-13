// GlassDesk - settings stored as a tiny text file in %AppData%\GlassDesk\settings.ini
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;

namespace GlassDesk
{
    internal class Settings
    {
        // General
        public bool StartWithWindows = true;
        public bool Hotkey = true;               // Ctrl + Alt + G shows / hides everything
        public bool LockPositions = false;
        public bool KeepBehindWindows = true;
        public bool SingleClickOpen = false;
        public bool Stacks = true;                 // 4+ files of the same type in a fence become one stack
        public bool PerDisplayLayouts = true;      // remember a layout for each monitor setup
        public string DisplayKey = "";             // the monitor setup the current layout belongs to
        public Dictionary<string, Dictionary<string, Rectangle>> Layouts = new Dictionary<string, Dictionary<string, Rectangle>>();
        public List<List<string>> Groups = new List<List<string>>();   // widget groups; the first id is the one showing

        // Appearance
        public string Look = "glassdesk";        // theme from the gallery (see Themes in Extras.cs)
        public string Theme = "Auto";            // Auto | Dark | Light
        public Color Accent = Color.FromArgb(124, 156, 255);
        public bool Blur = true;                 // kept for older settings files; GlassStyle decides
        public string GlassStyle = "frosted";    // frosted | blur | clear
        public bool GlassOn { get { return GlassStyle != "clear" && GlassStyle != "transparent"; } }
        public int Opacity = 30;                 // 5..95 (%) tint over the glass
        public int Radius = 16;                  // 0..28 px
        public int UiScale = 100;                // 80..150 (%)

        // Widgets
        public bool ShowClock = true, ShowSeconds = false, Clock24 = true;
        public bool ShowSystem = true, ShowCalendar = true, ShowNotes = true;
        public string NotesText = "Double-click to write a note.\n\n- Tidy desktop\n- Try the settings (tray icon)";
        public bool ShowWeather = true, WeatherF = false;
        public string WeatherCity = "", WeatherPlace = "";          // what you typed / what Open-Meteo found
        public double WeatherLat = double.NaN, WeatherLon = double.NaN;
        public bool ShowTodo = true;
        public string TodoText = "[ ] Try dragging an icon to another fence\n[ ] Make a rule in Settings > Organizer\n[x] Install GlassDesk";
        public bool ShowPhotos = false;
        public bool ShowCountdown = true, ShowQuote = true, ShowMusic = true;
        public string LastSong = "", LastArtist = "";   // the music widget shows these when nothing is playing
        public bool ShowGitHub = true, ShowNetwork = true, ShowFocus = true; public string GitHubUser = ""; public int FocusMinutes = 25, BreakMinutes = 5;
        public string CountdownTitle = "", CountdownDate = "";      // date as yyyy-MM-dd
        public bool WallpaperOn = false;
        public string WallpaperFolder = "";
        public int WallpaperMinutes = 30;
        public bool AutoBackup = true;
        public Dictionary<string, string> FenceColor = new Dictionary<string, string>();   // fence id -> RRGGBB
        public Dictionary<string, string> FenceIcons = new Dictionary<string, string>();   // fence id -> small | medium | large
        public HashSet<string> AutoHide = new HashSet<string>();                           // fences that roll up until hovered
        public string PhotoFolder = "";
        public int PhotoMinutes = 10;

        // Organizer
        public bool ShowFences = true, HideEmptyFences = true, HideDesktopIcons = true;
        public Dictionary<string, bool> Categories = new Dictionary<string, bool>();
        public List<string> FolderFences = new List<string>();
        public List<string> CustomFences = new List<string>();     // ids of fences you made yourself
        public Dictionary<string, string> Pins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // item path -> fence id
        public List<string[]> Rules = new List<string[]>();          // { name pattern, fence id }, first match wins
        public Dictionary<string, string> Sort = new Dictionary<string, string>();   // fence id -> name | type | date | size
        public bool SearchHotkey = true;                              // Ctrl + Alt + F

        // Updates
        public bool AutoUpdate = true;
        public string UpdateRepo = App.DefaultRepo;                   // "owner/repo" on GitHub
        public long LastUpdateCheck = 0;                              // UTC ticks
        public string SkipVersion = "";

        // Performance
        public bool LowPower = false, PauseFullscreen = true, TrimMemory = true;

        // Layout: id -> x,y,w,h  and collapsed fences
        public Dictionary<string, Rectangle> Positions = new Dictionary<string, Rectangle>();
        public Dictionary<string, int> WidgetZoom = new Dictionary<string, int>();   // widget id -> size in % (60..220)
        public HashSet<string> Collapsed = new HashSet<string>();
        public Dictionary<string, string> Titles = new Dictionary<string, string>();

        public static string Folder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GlassDesk"); }
        }
        static string FilePath { get { return Path.Combine(Folder, "settings.ini"); } }

        bool startupV2, iconsV2, glassV2, lookSeen, anyKey;

        public bool CategoryOn(string key) { bool v; return !Categories.TryGetValue(key, out v) || v; }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (string raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int eq = raw.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = raw.Substring(0, eq).Trim(), v = raw.Substring(eq + 1);
                    try { s.Apply(k, v); } catch { }
                }
            }
            catch { }
            if (!s.startupV2) s.StartWithWindows = true;   // 1.1: run on startup by default
            if (!s.iconsV2) s.HideDesktopIcons = true;     // 1.2: fences replace the Windows desktop icons
            if (!s.glassV2)                                  // 1.4: real frosted glass, with a lighter tint so it shows
            {
                s.GlassStyle = s.Blur ? "frosted" : "clear";
                if (s.Opacity == 55) s.Opacity = 30;
            }
            if (s.GlassStyle != "frosted" && s.GlassStyle != "blur" && s.GlassStyle != "clear" && s.GlassStyle != "transparent") s.GlassStyle = "frosted";
            s.Blur = s.GlassOn;
            if (s.anyKey && !s.lookSeen) s.Look = "classic";   // 1.8: people updating keep the look they had
            if (Themes.Find(s.Look) == null) s.Look = "glassdesk";
            return s;
        }

        static bool B(string v) { return v.Trim() == "1"; }
        static int I(string v) { return int.Parse(v.Trim(), CultureInfo.InvariantCulture); }
        static string Esc(string v) { return v.Replace("\\", "\\\\").Replace("\r", "").Replace("\n", "\\n"); }
        static string Unesc(string v)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < v.Length; i++)
            {
                if (v[i] == '\\' && i + 1 < v.Length) { i++; sb.Append(v[i] == 'n' ? '\n' : v[i]); }
                else sb.Append(v[i]);
            }
            return sb.ToString();
        }

        void Apply(string k, string v)
        {
            anyKey = true;
            switch (k)
            {
                case "Look": Look = v.Trim(); lookSeen = true; return;
                case "StartWithWindows": StartWithWindows = B(v); return;
                case "StartupV2": startupV2 = true; return;
                case "IconsV2": iconsV2 = true; return;
                case "Hotkey": Hotkey = B(v); return;
                case "LockPositions": LockPositions = B(v); return;
                case "KeepBehindWindows": KeepBehindWindows = B(v); return;
                case "SingleClickOpen": SingleClickOpen = B(v); return;
                case "Stacks": Stacks = B(v); return;
                case "PerDisplayLayouts": PerDisplayLayouts = B(v); return;
                case "DisplayKey": DisplayKey = v.Trim(); return;
                case "Theme": Theme = v.Trim(); return;
                case "Accent": Accent = Color.FromArgb(255, Color.FromArgb(int.Parse(v.Trim(), NumberStyles.HexNumber))); return;
                case "Blur": Blur = B(v); return;
                case "Opacity": Opacity = Math.Max(5, Math.Min(95, I(v))); return;
                case "GlassStyle": GlassStyle = v.Trim(); glassV2 = true; return;
                case "Radius": Radius = Math.Max(0, Math.Min(28, I(v))); return;
                case "UiScale": UiScale = Math.Max(80, Math.Min(150, I(v))); return;
                case "ShowClock": ShowClock = B(v); return;
                case "ShowSeconds": ShowSeconds = B(v); return;
                case "Clock24": Clock24 = B(v); return;
                case "ShowSystem": ShowSystem = B(v); return;
                case "ShowCalendar": ShowCalendar = B(v); return;
                case "ShowNotes": ShowNotes = B(v); return;
                case "NotesText": NotesText = Unesc(v); return;
                case "ShowFences": ShowFences = B(v); return;
                case "HideEmptyFences": HideEmptyFences = B(v); return;
                case "HideDesktopIcons": HideDesktopIcons = B(v); return;
                case "LowPower": LowPower = B(v); return;
                case "PauseFullscreen": PauseFullscreen = B(v); return;
                case "TrimMemory": TrimMemory = B(v); return;
                case "ShowWeather": ShowWeather = B(v); return;
                case "WeatherF": WeatherF = B(v); return;
                case "WeatherCity": WeatherCity = Unesc(v); return;
                case "WeatherPlace": WeatherPlace = Unesc(v); return;
                case "WeatherLat": WeatherLat = double.Parse(v, CultureInfo.InvariantCulture); return;
                case "WeatherLon": WeatherLon = double.Parse(v, CultureInfo.InvariantCulture); return;
                case "ShowTodo": ShowTodo = B(v); return;
                case "TodoText": TodoText = Unesc(v); return;
                case "ShowPhotos": ShowPhotos = B(v); return;
                case "PhotoFolder": PhotoFolder = Unesc(v); return;
                case "PhotoMinutes": PhotoMinutes = Math.Max(1, Math.Min(240, I(v))); return;
                case "SearchHotkey": SearchHotkey = B(v); return;
                case "AutoUpdate": AutoUpdate = B(v); return;
                case "UpdateRepo": UpdateRepo = v.Trim(); return;
                case "LastUpdateCheck": LastUpdateCheck = long.Parse(v.Trim(), CultureInfo.InvariantCulture); return;
                case "SkipVersion": SkipVersion = v.Trim(); return;
                case "ShowCountdown": ShowCountdown = B(v); return;
                case "ShowQuote": ShowQuote = B(v); return;
                case "ShowMusic": ShowMusic = B(v); return;
                case "LastSong": LastSong = Unesc(v); return;
                case "LastArtist": LastArtist = Unesc(v); return;
                case "ShowGitHub": ShowGitHub = B(v); return; case "ShowNetwork": ShowNetwork = B(v); return; case "ShowFocus": ShowFocus = B(v); return;
                case "GitHubUser": GitHubUser = v; return;
                case "FocusMinutes": { int n; if (int.TryParse(v, out n)) FocusMinutes = Math.Max(1, Math.Min(120, n)); } return;
                case "BreakMinutes": { int n; if (int.TryParse(v, out n)) BreakMinutes = Math.Max(1, Math.Min(60, n)); } return;
                case "CountdownTitle": CountdownTitle = Unesc(v); return;
                case "CountdownDate": CountdownDate = v.Trim(); return;
                case "WallpaperOn": WallpaperOn = B(v); return;
                case "WallpaperFolder": WallpaperFolder = Unesc(v); return;
                case "WallpaperMinutes": WallpaperMinutes = Math.Max(1, Math.Min(1440, I(v))); return;
                case "AutoBackup": AutoBackup = B(v); return;
            }
            if (k.StartsWith("cat.")) Categories[k.Substring(4)] = B(v);
            else if (k.StartsWith("folder.")) { string p = Unesc(v); if (p.Length > 0 && !FolderFences.Contains(p)) FolderFences.Add(p); }
            else if (k.StartsWith("custom.")) { string id = v.Trim(); if (id.Length > 0 && !CustomFences.Contains(id)) CustomFences.Add(id); }
            else if (k.StartsWith("pin."))
            {
                int bar = v.IndexOf('|');
                if (bar > 0) Pins[Unesc(v.Substring(bar + 1))] = v.Substring(0, bar);
            }
            else if (k.StartsWith("rule."))
            {
                int bar = v.IndexOf('|');
                if (bar > 0) Rules.Add(new[] { Unesc(v.Substring(bar + 1)), v.Substring(0, bar) });
            }
            else if (k.StartsWith("sort.")) Sort[k.Substring(5)] = v.Trim();
            else if (k.StartsWith("fcolor.")) FenceColor[k.Substring(7)] = v.Trim();
            else if (k.StartsWith("ficon.")) FenceIcons[k.Substring(6)] = v.Trim();
            else if (k.StartsWith("fauto.")) { if (B(v)) AutoHide.Add(k.Substring(6)); }
            else if (k.StartsWith("col.")) { if (B(v)) Collapsed.Add(k.Substring(4)); }
            else if (k.StartsWith("title.")) Titles[k.Substring(6)] = Unesc(v);
            else if (k.StartsWith("zoom.")) WidgetZoom[k.Substring(5)] = Math.Max(60, Math.Min(220, I(v)));
            else if (k.StartsWith("group.")) { var ids = new List<string>(); foreach (string id in v.Split(',')) if (id.Trim().Length > 0) ids.Add(id.Trim()); if (ids.Count >= 2) Groups.Add(ids); }
            else if (k.StartsWith("lay."))
            {
                // lay.<monitor setup>.<panel id> = x,y,w,h
                string rest = k.Substring(4); int dot = rest.IndexOf('.');
                string[] p = v.Split(',');
                if (dot > 0 && p.Length == 4)
                {
                    string key = rest.Substring(0, dot);
                    Dictionary<string, Rectangle> d;
                    if (!Layouts.TryGetValue(key, out d)) Layouts[key] = d = new Dictionary<string, Rectangle>();
                    d[rest.Substring(dot + 1)] = new Rectangle(I(p[0]), I(p[1]), I(p[2]), I(p[3]));
                }
            }
            else if (k.StartsWith("pos."))
            {
                string[] p = v.Split(',');
                if (p.Length == 4) Positions[k.Substring(4)] = new Rectangle(I(p[0]), I(p[1]), I(p[2]), I(p[3]));
            }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var sb = new StringBuilder();
                Action<string, string> w = (k, v) => sb.Append(k).Append('=').Append(v).Append("\r\n");
                Func<bool, string> b = x => x ? "1" : "0";
                w("StartWithWindows", b(StartWithWindows)); w("StartupV2", "1"); w("IconsV2", "1"); w("Hotkey", b(Hotkey)); w("LockPositions", b(LockPositions));
                w("KeepBehindWindows", b(KeepBehindWindows)); w("SingleClickOpen", b(SingleClickOpen));
                w("Look", Look); w("Theme", Theme); w("Accent", (Accent.ToArgb() & 0xFFFFFF).ToString("X6"));
                w("Blur", b(GlassOn)); w("GlassStyle", GlassStyle); w("Opacity", Opacity.ToString(CultureInfo.InvariantCulture));
                w("Radius", Radius.ToString(CultureInfo.InvariantCulture)); w("UiScale", UiScale.ToString(CultureInfo.InvariantCulture));
                w("ShowClock", b(ShowClock)); w("ShowSeconds", b(ShowSeconds)); w("Clock24", b(Clock24));
                w("ShowSystem", b(ShowSystem)); w("ShowCalendar", b(ShowCalendar)); w("ShowNotes", b(ShowNotes));
                w("NotesText", Esc(NotesText));
                w("ShowFences", b(ShowFences)); w("HideEmptyFences", b(HideEmptyFences)); w("HideDesktopIcons", b(HideDesktopIcons));
                w("LowPower", b(LowPower)); w("PauseFullscreen", b(PauseFullscreen)); w("TrimMemory", b(TrimMemory));
                w("ShowWeather", b(ShowWeather)); w("WeatherF", b(WeatherF)); w("WeatherCity", Esc(WeatherCity)); w("WeatherPlace", Esc(WeatherPlace));
                if (!double.IsNaN(WeatherLat)) { w("WeatherLat", WeatherLat.ToString("R", CultureInfo.InvariantCulture)); w("WeatherLon", WeatherLon.ToString("R", CultureInfo.InvariantCulture)); }
                w("ShowTodo", b(ShowTodo)); w("TodoText", Esc(TodoText));
                w("ShowPhotos", b(ShowPhotos)); w("PhotoFolder", Esc(PhotoFolder)); w("PhotoMinutes", PhotoMinutes.ToString(CultureInfo.InvariantCulture));
                w("SearchHotkey", b(SearchHotkey));
                w("AutoUpdate", b(AutoUpdate)); w("UpdateRepo", UpdateRepo); w("LastUpdateCheck", LastUpdateCheck.ToString(CultureInfo.InvariantCulture)); w("SkipVersion", SkipVersion);
                for (int i = 0; i < Rules.Count; i++) w("rule." + i, Rules[i][1] + "|" + Esc(Rules[i][0]));
                foreach (var kv in Sort) w("sort." + kv.Key, kv.Value);
                w("ShowGitHub", b(ShowGitHub)); w("ShowNetwork", b(ShowNetwork)); w("ShowFocus", b(ShowFocus)); w("GitHubUser", GitHubUser ?? ""); w("FocusMinutes", FocusMinutes.ToString()); w("BreakMinutes", BreakMinutes.ToString());
                w("ShowCountdown", b(ShowCountdown)); w("ShowQuote", b(ShowQuote)); w("ShowMusic", b(ShowMusic)); w("LastSong", Esc(LastSong ?? "")); w("LastArtist", Esc(LastArtist ?? "")); w("CountdownTitle", Esc(CountdownTitle)); w("CountdownDate", CountdownDate);
                w("WallpaperOn", b(WallpaperOn)); w("WallpaperFolder", Esc(WallpaperFolder)); w("WallpaperMinutes", WallpaperMinutes.ToString(CultureInfo.InvariantCulture));
                w("AutoBackup", b(AutoBackup));
                foreach (var kv in FenceColor) w("fcolor." + kv.Key, kv.Value);
                foreach (var kv in FenceIcons) w("ficon." + kv.Key, kv.Value);
                foreach (string a in AutoHide) w("fauto." + a, "1");
                foreach (var kv in Categories) w("cat." + kv.Key, b(kv.Value));
                for (int i = 0; i < FolderFences.Count; i++) w("folder." + i, Esc(FolderFences[i]));
                for (int i = 0; i < CustomFences.Count; i++) w("custom." + i, CustomFences[i]);
                int pn = 0;
                foreach (var kv in Pins) w("pin." + (pn++), kv.Value + "|" + Esc(kv.Key));
                foreach (string c in Collapsed) w("col." + c, "1");
                foreach (var kv in Titles) w("title." + kv.Key, Esc(kv.Value));
                foreach (var kv in WidgetZoom) if (kv.Value != 100) w("zoom." + kv.Key, kv.Value.ToString(CultureInfo.InvariantCulture));
                w("Stacks", b(Stacks)); w("PerDisplayLayouts", b(PerDisplayLayouts)); w("DisplayKey", DisplayKey ?? "");
                for (int i = 0; i < Groups.Count; i++) w("group." + i, string.Join(",", Groups[i].ToArray()));
                if (PerDisplayLayouts && !string.IsNullOrEmpty(DisplayKey)) Layouts[DisplayKey] = new Dictionary<string, Rectangle>(Positions);
                foreach (var lay in Layouts)
                    foreach (var kv in lay.Value)
                        w("lay." + lay.Key + "." + kv.Key, string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3}", kv.Value.X, kv.Value.Y, kv.Value.Width, kv.Value.Height));
                foreach (var kv in Positions)
                    w("pos." + kv.Key, string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3}", kv.Value.X, kv.Value.Y, kv.Value.Width, kv.Value.Height));
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), Encoding.UTF8);
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
            }
            catch { }
        }
    }

    /// <summary>Colors and fonts derived from the settings.</summary>
    internal class Theme
    {
        public bool Dark;
        public Color Accent, Text, SubText, Tint, Border, Hover, Track;
        /// <summary>Glass gradient (top to bottom), corner glows and special styles from the chosen theme.</summary>
        public Color TintTop, TintBottom, Glow = Color.Empty, Glow2 = Color.Empty;
        public bool Solid, Flat, Neon;
        public string LookId = "classic";
        public float Scale = 1f;
        public string FontName = "Segoe UI";
        public string DisplayFont = "Segoe UI";

        public int S(float v) { return (int)Math.Round(v * Scale); }
        /// <summary>The same theme drawn bigger or smaller (for a widget you resized).</summary>
        public Theme Zoomed(float z) { var t = (Theme)MemberwiseClone(); t.Scale = Scale * z; return t; }
        public float Sf(float v) { return v * Scale; }

        public static Theme From(Settings s, float dpiScale)
        {
            var t = new Theme();
            t.Dark = s.Theme == "Dark" || (s.Theme == "Auto" && !Native.AppsUseLightTheme());
            t.Scale = dpiScale * s.UiScale / 100f;
            t.Accent = s.Accent;
            int a = (int)(255 * s.Opacity / 100f);
            if (s.GlassStyle == "transparent") a = (int)(a * 0.3f);   // see-through: only a whisper of tint
            var look = Themes.Find(s.Look) ?? Themes.Find("classic");
            t.LookId = look.Id; t.Solid = look.Solid; t.Flat = look.Flat; t.Neon = look.Neon;
            if (t.Solid) a = 255;
            Color top = t.Dark ? look.DarkTop : look.LightTop, bot = t.Dark ? look.DarkBottom : look.LightBottom;
            // tinted looks carry a little of their color even when the tint slider is low
            int a2 = look.Id == "classic" || t.Solid ? a : Math.Min(255, a + 40);
            t.TintTop = Color.FromArgb(a2, top); t.TintBottom = Color.FromArgb(a2, bot);
            if (look.Glow) { t.Glow = Color.FromArgb(t.Dark ? 92 : 54, s.Accent); t.Glow2 = look.Glow2.IsEmpty ? Color.Empty : Color.FromArgb(t.Dark ? 52 : 36, look.Glow2); }
            if (t.Dark)
            {
                t.Tint = Color.FromArgb(a, 18, 20, 28);
                t.Text = Color.FromArgb(240, 244, 246, 252);
                t.SubText = Color.FromArgb(150, 220, 226, 240);
                t.Border = Color.FromArgb(55, 255, 255, 255);
                t.Hover = Color.FromArgb(34, 255, 255, 255);
                t.Track = Color.FromArgb(40, 255, 255, 255);
            }
            else
            {
                t.Tint = Color.FromArgb(a, 248, 249, 252);
                t.Text = Color.FromArgb(240, 18, 20, 28);
                t.SubText = Color.FromArgb(160, 40, 46, 60);
                t.Border = Color.FromArgb(70, 255, 255, 255);
                t.Hover = Color.FromArgb(28, 0, 0, 0);
                t.Track = Color.FromArgb(30, 0, 0, 0);
            }
            if (t.Solid)
            {
                t.Border = t.Dark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(22, 0, 0, 0);
                t.SubText = t.Dark ? Color.FromArgb(255, 152, 152, 158) : Color.FromArgb(255, 110, 110, 116);
            }
            if (look.Id == "paper") { t.Text = t.Dark ? Color.FromArgb(245, 238, 232, 220) : Color.FromArgb(245, 46, 40, 32); }
            t.Tint = t.TintTop;
            t.FontName = Pick("Segoe UI", "DejaVu Sans");
            t.DisplayFont = Pick("Segoe UI", "DejaVu Sans");
            return t;
        }

        static string[] installed;
        internal static string Pick(params string[] names)
        {
            if (installed == null)
            {
                try
                {
                    var list = new List<string>();
                    foreach (var f in FontFamily.Families) list.Add(f.Name);
                    installed = list.ToArray();
                }
                catch { installed = new string[0]; }
            }
            foreach (string n in names) if (Array.IndexOf(installed, n) >= 0) return n;
            return names[names.Length - 1];
        }
    }
}
