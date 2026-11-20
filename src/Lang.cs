// GlassDesk - translations. Text in the app is written in English; Lang.L("…") looks it up in the
// chosen language's table (assets/lang/<code>.txt, built into the exe). Missing lines stay English.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace GlassDesk
{
    internal static class Lang
    {
        /// <summary>Code, name shown in the menu (in its own language).</summary>
        public static readonly string[][] Languages =
        {
            new[] { "en", "English" }, new[] { "zh", "简体中文" }, new[] { "es", "Español" }, new[] { "ru", "Русский" },
        };

        static Dictionary<string, string> table;
        public static string Code = "en";

        public static string L(string en)
        {
            if (table == null || string.IsNullOrEmpty(en)) return en;
            string t;
            return table.TryGetValue(en, out t) ? t : en;
        }

        /// <summary>"auto" follows the Windows display language.</summary>
        public static void Init(string choice)
        {
            string code = string.IsNullOrEmpty(choice) || choice == "auto" ? FromWindows() : choice;
            table = null; Code = "en";
            if (code != "en")
            {
                var t = Load(code);
                if (t != null) { table = t; Code = code; }
            }
            // dates, month and day names follow the language you picked
            if (choice != null && choice != "auto")
                try
                {
                    var c = CultureInfo.GetCultureInfo(code == "zh" ? "zh-CN" : code);
                    Thread.CurrentThread.CurrentCulture = c; Thread.CurrentThread.CurrentUICulture = c;
                    CultureInfo.DefaultThreadCurrentCulture = c; CultureInfo.DefaultThreadCurrentUICulture = c;
                }
                catch { }
        }

        static string FromWindows()
        {
            var ui = CultureInfo.CurrentUICulture;
            if (ui.TwoLetterISOLanguageName == "zh")
                return ui.Name.EndsWith("TW") || ui.Name.EndsWith("HK") || ui.Name.EndsWith("MO") || ui.Name.Contains("Hant") ? "zh-TW" : "zh";
            foreach (var l in Languages) if (l[0] == ui.TwoLetterISOLanguageName) return l[0];
            return "en";
        }

        static Dictionary<string, string> Load(string code)
        {
            try
            {
                using (var s = typeof(Lang).Assembly.GetManifestResourceStream("GlassDesk.lang." + code + ".txt"))
                {
                    if (s == null) return null;
                    var d = new Dictionary<string, string>();
                    using (var r = new StreamReader(s, Encoding.UTF8))
                    {
                        string line;
                        while ((line = r.ReadLine()) != null)
                        {
                            int tab = line.IndexOf('\t');
                            if (tab <= 0 || line.StartsWith("#")) continue;
                            string en = line.Substring(0, tab).Replace("\\n", "\n"), tr = line.Substring(tab + 1).Replace("\\n", "\n");
                            if (tr.Length > 0) d[en] = tr;
                        }
                    }
                    return d;
                }
            }
            catch (Exception ex) { Program.Log(ex); return null; }
        }

        /// <summary>A font that has the letters of this language (Segoe UI has no Chinese or Japanese).</summary>
        public static string Font(string normal)
        {
            if (Code == "zh") return Theme.Pick("Microsoft YaHei UI", "Microsoft YaHei", normal);
            if (Code == "zh-TW") return Theme.Pick("Microsoft JhengHei UI", "Microsoft JhengHei", normal);
            if (Code == "ja") return Theme.Pick("Yu Gothic UI", "Meiryo UI", normal);
            return normal;
        }

        /// <summary>Translates a menu's items (and sub-menus) right before it opens.</summary>
        public static void Menu(ToolStrip m)
        {
            if (table == null) return;
            foreach (ToolStripItem it in m.Items)
            {
                if (!string.IsNullOrEmpty(it.Text)) it.Text = L(it.Text);
                var dd = it as ToolStripDropDownItem;
                if (dd != null && dd.HasDropDownItems) Menu(dd.DropDown);
            }
        }
    }
}
