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

        // Appearance
        public string Theme = "Auto";            // Auto | Dark | Light
        public Color Accent = Color.FromArgb(124, 156, 255);
        public bool Blur = true;
        public int Opacity = 55;                 // 20..95 (%)
        public int Radius = 16;                  // 0..28 px
        public int UiScale = 100;                // 80..150 (%)

        // Widgets
        public bool ShowClock = true, ShowSeconds = false, Clock24 = true;
        public bool ShowSystem = true, ShowCalendar = true, ShowNotes = true;
        public string NotesText = "Double-click to write a note.\n\n- Tidy desktop\n- Try the settings (tray icon)";

        // Organizer
        public bool ShowFences = true, HideEmptyFences = true, HideDesktopIcons = true;
        public Dictionary<string, bool> Categories = new Dictionary<string, bool>();
        public List<string> FolderFences = new List<string>();
        public List<string> CustomFences = new List<string>();     // ids of fences you made yourself
        public Dictionary<string, string> Pins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // item path -> fence id

        // Performance
        public bool LowPower = false, PauseFullscreen = true, TrimMemory = true;

        // Layout: id -> x,y,w,h  and collapsed fences
        public Dictionary<string, Rectangle> Positions = new Dictionary<string, Rectangle>();
        public HashSet<string> Collapsed = new HashSet<string>();
        public Dictionary<string, string> Titles = new Dictionary<string, string>();

        public static string Folder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GlassDesk"); }
        }
        static string FilePath { get { return Path.Combine(Folder, "settings.ini"); } }

        bool startupV2, iconsV2;

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
            switch (k)
            {
                case "StartWithWindows": StartWithWindows = B(v); return;
                case "StartupV2": startupV2 = true; return;
                case "IconsV2": iconsV2 = true; return;
                case "Hotkey": Hotkey = B(v); return;
                case "LockPositions": LockPositions = B(v); return;
                case "KeepBehindWindows": KeepBehindWindows = B(v); return;
                case "SingleClickOpen": SingleClickOpen = B(v); return;
                case "Theme": Theme = v.Trim(); return;
                case "Accent": Accent = Color.FromArgb(255, Color.FromArgb(int.Parse(v.Trim(), NumberStyles.HexNumber))); return;
                case "Blur": Blur = B(v); return;
                case "Opacity": Opacity = Math.Max(20, Math.Min(95, I(v))); return;
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
            }
            if (k.StartsWith("cat.")) Categories[k.Substring(4)] = B(v);
            else if (k.StartsWith("folder.")) { string p = Unesc(v); if (p.Length > 0 && !FolderFences.Contains(p)) FolderFences.Add(p); }
            else if (k.StartsWith("custom.")) { string id = v.Trim(); if (id.Length > 0 && !CustomFences.Contains(id)) CustomFences.Add(id); }
            else if (k.StartsWith("pin."))
            {
                int bar = v.IndexOf('|');
                if (bar > 0) Pins[Unesc(v.Substring(bar + 1))] = v.Substring(0, bar);
            }
            else if (k.StartsWith("col.")) { if (B(v)) Collapsed.Add(k.Substring(4)); }
            else if (k.StartsWith("title.")) Titles[k.Substring(6)] = Unesc(v);
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
                w("Theme", Theme); w("Accent", (Accent.ToArgb() & 0xFFFFFF).ToString("X6"));
                w("Blur", b(Blur)); w("Opacity", Opacity.ToString(CultureInfo.InvariantCulture));
                w("Radius", Radius.ToString(CultureInfo.InvariantCulture)); w("UiScale", UiScale.ToString(CultureInfo.InvariantCulture));
                w("ShowClock", b(ShowClock)); w("ShowSeconds", b(ShowSeconds)); w("Clock24", b(Clock24));
                w("ShowSystem", b(ShowSystem)); w("ShowCalendar", b(ShowCalendar)); w("ShowNotes", b(ShowNotes));
                w("NotesText", Esc(NotesText));
                w("ShowFences", b(ShowFences)); w("HideEmptyFences", b(HideEmptyFences)); w("HideDesktopIcons", b(HideDesktopIcons));
                w("LowPower", b(LowPower)); w("PauseFullscreen", b(PauseFullscreen)); w("TrimMemory", b(TrimMemory));
                foreach (var kv in Categories) w("cat." + kv.Key, b(kv.Value));
                for (int i = 0; i < FolderFences.Count; i++) w("folder." + i, Esc(FolderFences[i]));
                for (int i = 0; i < CustomFences.Count; i++) w("custom." + i, CustomFences[i]);
                int pn = 0;
                foreach (var kv in Pins) w("pin." + (pn++), kv.Value + "|" + Esc(kv.Key));
                foreach (string c in Collapsed) w("col." + c, "1");
                foreach (var kv in Titles) w("title." + kv.Key, Esc(kv.Value));
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
        public float Scale = 1f;
        public string FontName = "Segoe UI";
        public string DisplayFont = "Segoe UI";

        public int S(float v) { return (int)Math.Round(v * Scale); }
        public float Sf(float v) { return v * Scale; }

        public static Theme From(Settings s, float dpiScale)
        {
            var t = new Theme();
            t.Dark = s.Theme == "Dark" || (s.Theme == "Auto" && !Native.AppsUseLightTheme());
            t.Scale = dpiScale * s.UiScale / 100f;
            t.Accent = s.Accent;
            int a = (int)(255 * s.Opacity / 100f);
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
