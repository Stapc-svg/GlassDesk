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
    internal static class Themes
    {
        public class Preset { public string Name, Glass, Mode; public Color Accent; public int Tint, Radius; }
        public static readonly Preset[] All =
        {
            new Preset { Name = "Ocean",    Accent = Color.FromArgb(20, 176, 196),  Glass = "frosted",     Tint = 26, Radius = 18, Mode = "Dark" },
            new Preset { Name = "Sunset",   Accent = Color.FromArgb(255, 122, 69),  Glass = "frosted",     Tint = 22, Radius = 20, Mode = "Dark" },
            new Preset { Name = "Forest",   Accent = Color.FromArgb(34, 190, 130),  Glass = "blur",        Tint = 34, Radius = 14, Mode = "Dark" },
            new Preset { Name = "Midnight", Accent = Color.FromArgb(138, 99, 255),  Glass = "frosted",     Tint = 55, Radius = 16, Mode = "Dark" },
            new Preset { Name = "Frost",    Accent = Color.FromArgb(0, 120, 212),   Glass = "frosted",     Tint = 40, Radius = 22, Mode = "Light" },
            new Preset { Name = "Rose",     Accent = Color.FromArgb(236, 72, 153),  Glass = "transparent", Tint = 30, Radius = 24, Mode = "Light" },
        };
        public static void Apply(Settings s, Preset p)
        {
            s.Accent = p.Accent; s.GlassStyle = p.Glass; s.Blur = s.GlassOn; s.Opacity = p.Tint; s.Radius = p.Radius; s.Theme = p.Mode;
        }
    }
}
