// GlassDesk 1.5 - wallpaper changer, backup & restore, and ready-made themes.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GlassDesk
{
    /// <summary>Changes the Windows desktop background from a folder, on a timer.</summary>
    internal class WallpaperChanger
    {
        readonly App app;
        readonly Timer timer = new Timer();
        List<string> files; string filesFor; int index = -1;
        readonly Random rng = new Random();
        static readonly string[] Exts = { ".jpg", ".jpeg", ".png", ".bmp" };

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool SystemParametersInfo(int action, int param, string value, int winIni);

        public WallpaperChanger(App app)
        {
            this.app = app;
            timer.Tick += delegate { if (!app.Paused) Next(); };
            Apply();
        }
        Settings S { get { return app.Settings; } }

        public string Folder
        {
            get
            {
                if (!string.IsNullOrEmpty(S.WallpaperFolder)) return S.WallpaperFolder;
                try { return Environment.GetFolderPath(Environment.SpecialFolder.MyPictures); } catch { return ""; }
            }
        }

        /// <summary>Call after settings change.</summary>
        public void Apply()
        {
            timer.Stop();
            if (!S.WallpaperOn) return;
            timer.Interval = Math.Max(1, S.WallpaperMinutes) * 60000;
            timer.Start();
        }

        public bool Next()
        {
            try
            {
                string folder = Folder;
                if (files == null || filesFor != folder)
                {
                    files = new List<string>(); filesFor = folder; index = -1;
                    if (Directory.Exists(folder))
                        foreach (string f in Directory.GetFiles(folder))
                            if (Array.IndexOf(Exts, Path.GetExtension(f).ToLowerInvariant()) >= 0) files.Add(f);
                    for (int i = files.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); string t = files[i]; files[i] = files[j]; files[j] = t; }
                }
                if (files.Count == 0) return false;
                index = (index + 1) % files.Count;
                if (!Native.IsWindows) return true;
                // 20 = SPI_SETDESKWALLPAPER, 1|2 = save it and tell other apps
                return SystemParametersInfo(20, 0, files[index], 1 | 2);
            }
            catch { return false; }
        }
        public void FolderChanged() { files = null; }
    }

    /// <summary>Your whole setup as one file: export, import, and quiet daily backups.</summary>
    internal static class Backup
    {
        const string Header = "; GlassDesk backup";
        static string SettingsFile { get { return Path.Combine(Settings.Folder, "settings.ini"); } }
        public static string BackupFolder { get { return Path.Combine(Settings.Folder, "backups"); } }

        public static void Export(App app)
        {
            app.Settings.Save();
            using (var d = new SaveFileDialog { Title = "Save your GlassDesk layout", Filter = "GlassDesk layout (*.glassdesk)|*.glassdesk", FileName = "My GlassDesk " + DateTime.Now.ToString("yyyy-MM-dd") + ".glassdesk" })
            {
                if (d.ShowDialog() != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(d.FileName, Header + " " + App.Version + " · " + DateTime.Now.ToString("s") + "\r\n" + File.ReadAllText(SettingsFile), System.Text.Encoding.UTF8);
                    MessageBox.Show("Saved. Open this file in GlassDesk on any PC (Settings > About > Import) to get the same fences, rules, widgets and look.", "GlassDesk", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) { MessageBox.Show("Couldn't save the file (" + ex.Message + ").", "GlassDesk"); }
            }
        }

        public static void Import(App app)
        {
            using (var d = new OpenFileDialog { Title = "Open a GlassDesk layout", Filter = "GlassDesk layout (*.glassdesk)|*.glassdesk|All files|*.*" })
            {
                if (d.ShowDialog() != DialogResult.OK) return;
                string text;
                try { text = File.ReadAllText(d.FileName); } catch (Exception ex) { MessageBox.Show("Couldn't open the file (" + ex.Message + ").", "GlassDesk"); return; }
                if (!text.StartsWith(Header) && text.IndexOf("StartWithWindows=", StringComparison.Ordinal) < 0)
                { MessageBox.Show("That doesn't look like a GlassDesk layout file.", "GlassDesk"); return; }
                if (MessageBox.Show("Replace your current fences, rules, widgets and look with this layout?\n\nYour current setup is backed up first, so you can go back.", "GlassDesk", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                try
                {
                    Snapshot("before-import");
                    var lines = new List<string>(text.Replace("\r\n", "\n").Split('\n'));
                    if (lines.Count > 0 && lines[0].StartsWith(Header)) lines.RemoveAt(0);
                    File.WriteAllText(SettingsFile, string.Join("\r\n", lines.ToArray()), System.Text.Encoding.UTF8);
                    app.ReloadSettings();
                }
                catch (Exception ex) { MessageBox.Show("Couldn't load the layout (" + ex.Message + ").", "GlassDesk"); }
            }
        }

        /// <summary>Copies settings.ini into the backups folder and keeps the newest 7.</summary>
        public static void Snapshot(string tag)
        {
            try
            {
                if (!File.Exists(SettingsFile)) return;
                Directory.CreateDirectory(BackupFolder);
                File.Copy(SettingsFile, Path.Combine(BackupFolder, "settings-" + DateTime.Now.ToString("yyyy-MM-dd-HHmm") + (tag != null ? "-" + tag : "") + ".ini"), true);
                var all = new List<string>(Directory.GetFiles(BackupFolder, "settings-*.ini"));
                all.Sort(StringComparer.Ordinal);
                while (all.Count > 7) { File.Delete(all[0]); all.RemoveAt(0); }
            }
            catch { }
        }

        /// <summary>Once a day, when automatic backups are on.</summary>
        public static void Daily(App app)
        {
            if (!app.Settings.AutoBackup) return;
            try
            {
                Directory.CreateDirectory(BackupFolder);
                string today = "settings-" + DateTime.Now.ToString("yyyy-MM-dd");
                foreach (string f in Directory.GetFiles(BackupFolder, today + "*.ini")) return;
                Snapshot(null);
            }
            catch { }
        }
    }

    /// <summary>Ready-made looks: accent, glass style, tint, corners and light / dark.</summary>
    /// <summary>The theme gallery. Each theme has its own glass colors for dark and light mode,
    /// and may add a soft glow, a neon rim, or turn the glass into solid, flat panels.</summary>
    internal static class Themes
    {
        public class Look
        {
            public string Id, Name, Desc, Mode = "Dark", Glass = "frosted";
            public Color Accent, DarkTop, DarkBottom, LightTop, LightBottom, Glow2 = Color.Empty;
            public bool Glow, Solid, Flat, Neon;
            public int Tint = 40, Radius = 16;
        }
        static Color C(int rgb) { return Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255); }
        static Look L(string id, string name, string desc, int accent, int dTop, int dBot, int lTop, int lBot, int glow2, string mode, int tint, int radius)
        {
            return new Look { Id = id, Name = name, Desc = desc, Accent = C(accent), DarkTop = C(dTop), DarkBottom = C(dBot), LightTop = C(lTop), LightBottom = C(lBot),
                Glow = glow2 >= 0, Glow2 = glow2 > 0 ? C(glow2) : Color.Empty, Mode = mode, Tint = tint, Radius = radius };
        }

        public static readonly Look[] All =
        {
            L("glassdesk", "GlassDesk", "Frosted navy glass with a soft glow", 0x7C9CFF, 0x1A2140, 0x0C1022, 0xFFFFFF, 0xECF0FA, 0xB06CFF, "Dark", 45, 18),
            new Look { Id = "clean", Name = "Clean", Desc = "Bright, flat and rounded", Accent = C(0x007AFF), DarkTop = C(0x1C1C1E), DarkBottom = C(0x1C1C1E),
                       LightTop = C(0xFFFFFF), LightBottom = C(0xFFFFFF), Solid = true, Flat = true, Mode = "Auto", Tint = 95, Radius = 22 },
            L("midnight", "Midnight", "Deep violet glass", 0xA78BFA, 0x241446, 0x0E0820, 0xF4EEFF, 0xE4DAFA, 0xFF6FA8, "Dark", 50, 16),
            L("ocean", "Ocean", "Teal water and cyan light", 0x4FC3F7, 0x0A3450, 0x061628, 0xE8F8FF, 0xD2ECF8, 0x2DD4BF, "Dark", 40, 18),
            L("sunset", "Sunset", "Warm coral evening glow", 0xFF8A65, 0x5C2632, 0x28101E, 0xFFF0E8, 0xFFE0D6, 0xFFC46B, "Dark", 38, 20),
            L("forest", "Forest", "Green glass, fresh and calm", 0x5BD68A, 0x123C2C, 0x081A14, 0xECFAF0, 0xD6F0DE, 0xC6F68D, "Dark", 42, 14),
            L("rose", "Rose", "Soft pink and lilac", 0xF48FB1, 0x501E3A, 0x220C1C, 0xFFECF4, 0xFADAE8, 0xC77DFF, "Dark", 38, 22),
            L("aurora", "Aurora", "Northern lights, teal to purple", 0x2DD4BF, 0x102C48, 0x2C1242, 0xE6FAFF, 0xF0E4FF, 0xC77DFF, "Dark", 40, 18),
            L("frost", "Frost", "Icy white glass", 0x3B82F6, 0x1E2838, 0x121824, 0xF6FAFF, 0xE2ECFA, 0x7DD3FC, "Light", 45, 22),
            L("ember", "Ember", "Glowing coals, red and amber", 0xFF6B4A, 0x3A1410, 0x160806, 0xFFF1EC, 0xFFE2D8, 0xFFB347, "Dark", 48, 16),
            L("lavender", "Lavender", "Light lilac glass", 0x8B5CF6, 0x2A2140, 0x16122A, 0xF7F2FF, 0xEDE4FF, 0xF0ABFC, "Light", 42, 20),
            L("mint", "Mint", "Cool, fresh and light", 0x10B981, 0x123A34, 0x081C18, 0xF0FFF9, 0xDDF7EE, 0x67E8F9, "Light", 42, 20),
            new Look { Id = "neon", Name = "Neon", Desc = "Black glass with a glowing edge", Accent = C(0xFF2BD6), DarkTop = C(0x0A0814), DarkBottom = C(0x04040A),
                       LightTop = C(0xFAF5FF), LightBottom = C(0xF0E8FA), Glow = true, Glow2 = C(0x00E5FF), Neon = true, Mode = "Dark", Tint = 70, Radius = 14 },
            new Look { Id = "graphite", Name = "Graphite", Desc = "Quiet gray, no color", Accent = C(0x98989D), DarkTop = C(0x2C2C30), DarkBottom = C(0x1C1C20),
                       LightTop = C(0xF0F0F2), LightBottom = C(0xE2E2E6), Mode = "Dark", Tint = 50, Radius = 12 },
            new Look { Id = "paper", Name = "Paper", Desc = "Warm, solid and easy to read", Accent = C(0xD97706), DarkTop = C(0x24211C), DarkBottom = C(0x24211C),
                       LightTop = C(0xFAF7F0), LightBottom = C(0xFAF7F0), Solid = true, Flat = true, Mode = "Light", Tint = 95, Radius = 10 },
            new Look { Id = "classic", Name = "Classic", Desc = "The original GlassDesk 1.7 look", Accent = C(0x7C9CFF), DarkTop = C(0x12141C), DarkBottom = C(0x12141C),
                       LightTop = C(0xF8F9FC), LightBottom = C(0xF8F9FC), Mode = "Auto", Tint = 30, Radius = 16 },
        };

        public static Look Find(string id)
        {
            foreach (var l in All) if (string.Equals(l.Id, id, StringComparison.OrdinalIgnoreCase)) return l;
            return null;
        }

        /// <summary>Picking a theme sets its look plus its suggested color, glass, tint, corners and mode. All can still be changed after.</summary>
        public static void Apply(Settings s, Look l)
        {
            s.Look = l.Id; s.Accent = l.Accent; s.GlassStyle = l.Glass; s.Blur = s.GlassOn; s.Opacity = l.Tint; s.Radius = l.Radius; s.Theme = l.Mode;
        }
    }
}
