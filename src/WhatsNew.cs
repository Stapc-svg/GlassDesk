// GlassDesk 2.2 - "What's new" after an update. Only people who already used GlassDesk see it
// (a first install gets the welcome screen instead). It lists what changed in every version
// newer than the one you had before, newest first.
using System;
using System.Collections.Generic;
using System.IO;

namespace GlassDesk
{
    internal static class WhatsNew
    {
        /// <summary>Every version's new things. Add the next version at the top.</summary>
        static List<KeyValuePair<Version, WelcomeForm.Tip[]>> All()
        {
            return new List<KeyValuePair<Version, WelcomeForm.Tip[]>>
            {
                new KeyValuePair<Version, WelcomeForm.Tip[]>(new Version(2, 2, 0), new[]
                {
                    new WelcomeForm.Tip("hide", Lang.L("Double-click to hide all"), Lang.L("Double-click an empty spot on the desktop for a clean wallpaper. Again to bring it back.")),
                    new WelcomeForm.Tip("peek", Lang.L("Peek at your desktop"), Lang.L("Ctrl + Alt + Space brings your fences and widgets over your apps. Esc puts them back.")),
                    new WelcomeForm.Tip("sliders", Lang.L("Quick settings widget"), Lang.L("Volume, brightness, Wi-Fi, Bluetooth, dark mode and mute, right on the desktop.")),
                    new WelcomeForm.Tip("game", Lang.L("Game stats widget"), Lang.L("GPU load, graphics memory, temperatures and CPU. Turn it on in Settings › Widgets.")),
                    new WelcomeForm.Tip("broom", Lang.L("Smart cleanup"), Lang.L("Tray icon › Tidy up old files. Nothing moves until you press Tidy, and Undo puts it back.")),
                }),
                new KeyValuePair<Version, WelcomeForm.Tip[]>(new Version(2, 1, 1), new[]
                {
                    new WelcomeForm.Tip("shield", Lang.L("No more false virus warnings"), Lang.L("The music widget no longer uses a hidden script, so antivirus apps stop flagging GlassDesk.")),
                }),
                new KeyValuePair<Version, WelcomeForm.Tip[]>(new Version(2, 1, 0), new[]
                {
                    new WelcomeForm.Tip("search", Lang.L("Quick search in liquid glass"), Lang.L("Every app sits on its own little glass tile (Ctrl + Alt + F).")),
                    new WelcomeForm.Tip("vinyl", Lang.L("The real album cover"), Lang.L("The music widget shows the cover of what's playing, with the record sliding out.")),
                    new WelcomeForm.Tip("photo", Lang.L("Your own clock photos"), Lang.L("Clock background › My photos. A new picture every 30 minutes.")),
                    new WelcomeForm.Tip("roll", Lang.L("Roll up widgets"), Lang.L("Right-click a widget › Roll up to shrink it to a small bar.")),
                }),
            };
        }

        /// <summary>The cards for an update from <paramref name="from"/> to this version (at most 6).</summary>
        public static List<WelcomeForm.Tip> Since(Version from, Version to)
        {
            var list = new List<WelcomeForm.Tip>();
            foreach (var kv in All())
                if (kv.Key > Norm(from) && kv.Key <= Norm(to)) list.AddRange(kv.Value);
            if (list.Count > 6)
            {
                var rest = new List<string>();
                for (int i = 5; i < list.Count; i++) rest.Add(list[i].Title);
                list = list.GetRange(0, 5);
                list.Add(new WelcomeForm.Tip("star", Lang.L("And also"), string.Join(" · ", rest.ToArray())));
            }
            return list;
        }
        static Version Norm(Version v) { return new Version(v.Major, v.Minor, Math.Max(0, v.Build)); }
        public static string Short(Version v) { return v.Build > 0 ? v.ToString(3) : v.ToString(2); }

        static string LastFile { get { return Path.Combine(Settings.Folder, "last-version"); } }

        /// <summary>Called at start-up. Returns the version you had before, when you just updated from it
        /// (null on a first install, or when nothing changed). Remembers this version for next time.</summary>
        public static Version JustUpdatedFrom(string welcomeMark, string settingsText)
        {
            Version cur = App.VersionNumber, prev = null;
            try
            {
                if (File.Exists(LastFile)) prev = Updater.ParseVersion(File.ReadAllText(LastFile).Trim());
                // GlassDesk before 2.2 didn't write last-version: the welcome screen's file says which version showed it
                else if (File.Exists(welcomeMark))
                {
                    prev = Updater.ParseVersion(File.ReadAllText(welcomeMark).Trim()) ?? new Version(2, 0, 0);
                    // a setting that only 2.1 and newer save: you already had 2.1, so its news isn't new to you
                    if ((settingsText ?? "").Contains("ClockPhotos=") && prev < new Version(2, 1, 0)) prev = new Version(2, 1, 0);
                }
            }
            catch { }
            try { Directory.CreateDirectory(Settings.Folder); File.WriteAllText(LastFile, App.Version); } catch { }
            if (prev == null || Norm(prev) >= Norm(cur)) return null;
            return prev;
        }
    }
}
