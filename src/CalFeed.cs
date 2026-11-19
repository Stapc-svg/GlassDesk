// GlassDesk - reads online calendars (Google, Outlook, iCloud...) from their private iCal (.ics) link.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GlassDesk
{
    internal class CalEvent
    {
        public DateTime Start; public bool AllDay; public string Title = "";
    }

    /// <summary>
    /// A tiny reader for iCal feeds. Google Calendar ("Secret address in iCal format"), Outlook
    /// ("Publish a calendar" → ICS link) and iCloud (public calendar link) all give one. No sign-in:
    /// the link itself is the key, so it is only kept in GlassDesk's settings file on this PC.
    /// Repeating events (daily, weekly, monthly, yearly) are expanded for the next year.
    /// </summary>
    internal static class CalFeed
    {
        public static List<CalEvent> Load(IEnumerable<string> urls)
        {
            var all = new List<CalEvent>();
            foreach (string u0 in urls)
            {
                string u = u0.Trim();
                if (u.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)) u = "https://" + u.Substring(9);
                all.AddRange(Parse(Http.Get(u)));
            }
            all.Sort((a, b) => a.Start.CompareTo(b.Start));
            return all;
        }

        public static List<CalEvent> Parse(string ics)
        {
            var list = new List<CalEvent>();
            if (string.IsNullOrEmpty(ics) || ics.IndexOf("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase) < 0)
                throw new FormatException("not a calendar");
            // lines that start with a space continue the line before
            var lines = new List<string>();
            foreach (string raw in ics.Replace("\r\n", "\n").Split('\n'))
            {
                if ((raw.StartsWith(" ") || raw.StartsWith("\t")) && lines.Count > 0) lines[lines.Count - 1] += raw.Substring(1);
                else lines.Add(raw);
            }
            DateTime from = DateTime.Today.AddDays(-45), to = DateTime.Today.AddDays(400);
            CalEvent ev = null; string rule = null; bool cancelled = false;
            foreach (string line in lines)
            {
                if (line.StartsWith("BEGIN:VEVENT")) { ev = new CalEvent(); rule = null; cancelled = false; continue; }
                if (ev == null) continue;
                if (line.StartsWith("END:VEVENT"))
                {
                    if (!cancelled && ev.Start != DateTime.MinValue) Expand(ev, rule, from, to, list);
                    ev = null; continue;
                }
                int colon = line.IndexOf(':'); if (colon < 0) continue;
                string key = line.Substring(0, colon), val = line.Substring(colon + 1);
                string name = key.Split(';')[0].ToUpperInvariant();
                if (name == "SUMMARY") ev.Title = Unescape(val);
                else if (name == "DTSTART") { bool allDay; ev.Start = ParseDate(key, val, out allDay); ev.AllDay = allDay; }
                else if (name == "RRULE") rule = val;
                else if (name == "STATUS" && val.Trim().ToUpperInvariant() == "CANCELLED") cancelled = true;
            }
            return list;
        }

        static void Expand(CalEvent ev, string rule, DateTime from, DateTime to, List<CalEvent> list)
        {
            if (rule == null) { if (ev.Start >= from && ev.Start <= to) list.Add(ev); return; }
            string freq = null; int interval = 1, count = int.MaxValue; DateTime until = to;
            foreach (string part in rule.Split(';'))
            {
                var kv = part.Split('='); if (kv.Length != 2) continue;
                switch (kv[0].ToUpperInvariant())
                {
                    case "FREQ": freq = kv[1].ToUpperInvariant(); break;
                    case "INTERVAL": int.TryParse(kv[1], out interval); if (interval < 1) interval = 1; break;
                    case "COUNT": int.TryParse(kv[1], out count); break;
                    case "UNTIL": bool ad; var u = ParseDate("UNTIL", kv[1], out ad); if (u != DateTime.MinValue && u < until) until = u.Date.AddDays(1); break;
                }
            }
            DateTime d = ev.Start;
            for (int n = 0; n < count && n < 2000 && d <= until; n++)
            {
                if (d >= from) list.Add(new CalEvent { Start = d, AllDay = ev.AllDay, Title = ev.Title });
                switch (freq)
                {
                    case "DAILY": d = d.AddDays(interval); break;
                    case "WEEKLY": d = d.AddDays(7 * interval); break;
                    case "MONTHLY": d = ev.Start.AddMonths((n + 1) * interval); break;
                    case "YEARLY": d = ev.Start.AddYears((n + 1) * interval); break;
                    default: return;
                }
            }
        }

        static DateTime ParseDate(string key, string v, out bool allDay)
        {
            v = v.Trim(); allDay = false;
            DateTime d;
            if (v.Length == 8 && DateTime.TryParseExact(v, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) { allDay = true; return d; }
            bool utc = v.EndsWith("Z"); if (utc) v = v.Substring(0, v.Length - 1);
            if (DateTime.TryParseExact(v, "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
                return utc ? DateTime.SpecifyKind(d, DateTimeKind.Utc).ToLocalTime() : d;   // a named time zone is taken as yours
            return DateTime.MinValue;
        }

        static string Unescape(string s)
        {
            return s.Replace("\\n", " ").Replace("\\N", " ").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\").Trim();
        }
    }
}

namespace GlassDesk
{
    /// <summary>Where games and Windows put screenshots and clips, so they can be shown as fences.</summary>
    internal static class GameCaptures
    {
        public static List<string> Find()
        {
            var found = new List<string>();
            try
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string pics = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                string vids = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
                foreach (string p in new[] {
                    System.IO.Path.Combine(vids, "Captures"),                 // Xbox Game Bar (Win + Alt + PrtScn)
                    System.IO.Path.Combine(vids, "NVIDIA"),                   // NVIDIA app / ShadowPlay
                    System.IO.Path.Combine(vids, "Radeon ReLive"),            // AMD
                    System.IO.Path.Combine(pics, "Screenshots"),              // Win + PrtScn and Snipping Tool
                    System.IO.Path.Combine(home, "OneDrive", "Pictures", "Screenshots"),
                    System.IO.Path.Combine(pics, "Steam Screenshots") })
                    if (System.IO.Directory.Exists(p) && !found.Contains(p)) found.Add(p);
            }
            catch { }
            return found;
        }
    }
}
