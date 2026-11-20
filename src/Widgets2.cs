// GlassDesk 1.3 - new widgets: weather, to-do list, photo frame.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Net;
using System.Threading;
using System.Windows.Forms;

namespace GlassDesk
{
    // ================================================================ Weather
    internal class WeatherWidget : Widget
    {
        class Day { public DateTime Date; public double Max, Min; public int Code; }
        class Hour { public DateTime Time; public double Temp; public int Code; }
        double temp = double.NaN, feels, wind, humidity, rainChance = double.NaN; int code = -1; bool isDay = true;
        DateTime sunrise, sunset;        // today, local time (MinValue = unknown)
        readonly List<Day> days = new List<Day>();
        readonly List<Hour> hours = new List<Hour>();
        string status = "";              // shown instead of the weather when something is missing
        bool busy;
        DateTime lastOk = DateTime.MinValue;
        System.Windows.Forms.Timer refresh;

        public WeatherWidget(App app) : base(app, "weather") { }
        protected override Size BaseSize { get { return new Size(330, 322); } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // check every minute: fetch when the data is 30+ minutes old, or retry 1 minute after an error
            // (e.g. the PC started before Wi-Fi connected). Hidden widgets (in a stack) still update.
            refresh = new System.Windows.Forms.Timer { Interval = 60 * 1000 };
            refresh.Tick += delegate
            {
                if (App.Paused) return;
                if (status.Length > 0 && !status.StartsWith("Couldn") && !string.IsNullOrEmpty(Cfg.WeatherCity)) Fetch();
                else if ((DateTime.Now - lastOk).TotalMinutes >= 30) Fetch();
            };
            refresh.Start();
            Fetch();
        }
        protected override void Dispose(bool disposing) { if (disposing && refresh != null) refresh.Dispose(); base.Dispose(disposing); }

        /// <summary>Preview data for screenshots.</summary>
        public void SetSample()
        {
            temp = 18.4; feels = 17; wind = 11; humidity = 64; code = 2; isDay = true; status = "";
            int pc; if (int.TryParse(Environment.GetEnvironmentVariable("GLASSDESK_PREVIEW_WCODE"), out pc)) { code = pc; isDay = Environment.GetEnvironmentVariable("GLASSDESK_PREVIEW_NIGHT") != "1"; }
            Cfg.WeatherPlace = "Tel Aviv, Israel";
            days.Clear();
            var d0 = App.Now.Date;
            int[] codes = { 2, 61, 0, 3 }; double[] mx = { 21, 19, 24, 22 }, mn = { 14, 13, 15, 15 };
            for (int i = 0; i < 4; i++) days.Add(new Day { Date = d0.AddDays(i), Max = mx[i], Min = mn[i], Code = codes[i] });
            rainChance = 12; sunrise = d0.AddHours(6).AddMinutes(38); sunset = d0.AddHours(17).AddMinutes(12);
            hours.Clear();
            double[] ht = { 18, 19, 20, 21, 21 }; int[] hc = { 2, 2, 0, 0, 1 };
            for (int i = 0; i < 5; i++) hours.Add(new Hour { Time = App.Now.Date.AddHours(App.Now.Hour + 1 + i), Temp = ht[i], Code = hc[i] });
        }

        public void Fetch()
        {
            if (busy || App.Preview) return;
            if (string.IsNullOrEmpty(Cfg.WeatherCity)) { status = "Double-click to choose your city"; Redraw(); return; }
            busy = true;
            string city = Cfg.WeatherCity; double lat = Cfg.WeatherLat, lon = Cfg.WeatherLon; bool f = Cfg.WeatherF;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string place = null, err = null; Dictionary<string, object> json = null, wttr = null;
                bool notFound = false;
                try
                {
                    if (double.IsNaN(lat))
                    {
                        var r0 = FindCity(city);
                        if (r0 == null) throw new Exception("NOTFOUND");
                        lat = Json.Num(r0, "latitude"); lon = Json.Num(r0, "longitude");
                        place = Json.Str(r0, "name") + (Json.Str(r0, "country") != "" ? ", " + Json.Str(r0, "country") : "");
                    }
                    json = Http.GetJson(string.Format(CultureInfo.InvariantCulture,
                        "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,is_day,wind_speed_10m&hourly=temperature_2m,weather_code&daily=weather_code,temperature_2m_max,temperature_2m_min,sunrise,sunset,precipitation_probability_max&timezone=auto&forecast_days=4{2}",
                        lat, lon, f ? "&temperature_unit=fahrenheit&wind_speed_unit=mph" : ""));
                }
                catch (Exception ex)
                {
                    notFound = ex.Message == "NOTFOUND";
                    Program.Log(new Exception("Weather (Open-Meteo) failed: " + ex.Message, ex));
                    json = null;
                }
                // second weather service: wttr.in understands city names directly
                if (json == null)
                    try { wttr = Http.GetJson("https://wttr.in/" + Uri.EscapeDataString(city) + "?format=j1"); place = null; }
                    catch (Exception ex2)
                    {
                        Program.Log(new Exception("Weather (wttr.in) failed: " + ex2.Message, ex2));
                        err = notFound ? "Couldn't find \"" + city + "\". Double-click to try another city." : "Can't reach the weather service – trying again in a minute";
                    }
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        busy = false;
                        if (err != null) { status = err; Redraw(); return; }
                        if (wttr != null) { ApplyWttr(wttr, f); return; }
                        if (place != null) { Cfg.WeatherLat = lat; Cfg.WeatherLon = lon; Cfg.WeatherPlace = place; Cfg.Save(); }
                        Apply(json);
                    }));
                }
                catch { busy = false; }
            });
        }

        /// <summary>wttr.in uses WorldWeatherOnline codes; turn them into the Open-Meteo (WMO) codes the icons use.</summary>
        static int WwoToWmo(int c)
        {
            switch (c)
            {
                case 113: return 0;
                case 116: return 2;
                case 119: case 122: return 3;
                case 143: case 248: case 260: return 45;
                case 176: case 263: case 266: case 293: case 296: case 353: return 61;
                case 299: case 302: case 305: case 308: case 356: case 359: return 63;
                case 185: case 281: case 284: case 311: case 314: case 182: case 317: case 320: case 362: case 365: case 374: case 377: case 350: return 66;
                case 179: case 227: case 230: case 323: case 326: case 329: case 332: case 335: case 338: case 368: case 371: return 73;
                case 200: case 386: case 389: case 392: case 395: return 95;
                default: return 3;
            }
        }
        static double NumS(Dictionary<string, object> o, string k)
        {
            double v; return double.TryParse(Json.Str(o, k), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : double.NaN;
        }
        static Dictionary<string, object> First(Dictionary<string, object> o, string k)
        {
            var a = Json.Arr(o, k); return a != null && a.Count > 0 ? a[0] as Dictionary<string, object> : null;
        }

        void ApplyWttr(Dictionary<string, object> j, bool f)
        {
            try
            {
                var cur = First(j, "current_condition");
                if (cur == null) throw new Exception("no data");
                temp = NumS(cur, f ? "temp_F" : "temp_C"); feels = NumS(cur, f ? "FeelsLikeF" : "FeelsLikeC");
                humidity = NumS(cur, "humidity"); wind = NumS(cur, f ? "windspeedMiles" : "windspeedKmph");
                code = WwoToWmo((int)NumS(cur, "weatherCode"));
                int h = DateTime.Now.Hour; isDay = h >= 6 && h < 18;
                days.Clear();
                var w = Json.Arr(j, "weather");
                for (int i = 0; w != null && i < w.Count && i < 4; i++)
                {
                    var d = w[i] as Dictionary<string, object>;
                    var hourly = Json.Arr(d, "hourly");
                    var noon = hourly != null && hourly.Count > 4 ? hourly[4] as Dictionary<string, object> : null;
                    days.Add(new Day
                    {
                        Date = DateTime.ParseExact(Json.Str(d, "date"), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                        Max = NumS(d, f ? "maxtempF" : "maxtempC"), Min = NumS(d, f ? "mintempF" : "mintempC"),
                        Code = noon != null ? WwoToWmo((int)NumS(noon, "weatherCode")) : code
                    });
                }
                sunrise = sunset = DateTime.MinValue; rainChance = double.NaN; hours.Clear();
                try
                {
                    var w0 = w != null && w.Count > 0 ? w[0] as Dictionary<string, object> : null;
                    var ast = First(w0, "astronomy");
                    DateTime t0;
                    if (ast != null && DateTime.TryParse(Json.Str(ast, "sunrise"), CultureInfo.InvariantCulture, DateTimeStyles.None, out t0)) sunrise = DateTime.Today.Add(t0.TimeOfDay);
                    if (ast != null && DateTime.TryParse(Json.Str(ast, "sunset"), CultureInfo.InvariantCulture, DateTimeStyles.None, out t0)) sunset = DateTime.Today.Add(t0.TimeOfDay);
                    // wttr.in gives 3-hourly steps; take the next five after now
                    for (int di = 0; w != null && di < w.Count && hours.Count < 5; di++)
                    {
                        var d = w[di] as Dictionary<string, object>;
                        var date = DateTime.ParseExact(Json.Str(d, "date"), "yyyy-MM-dd", CultureInfo.InvariantCulture);
                        var hl = Json.Arr(d, "hourly");
                        for (int hi = 0; hl != null && hi < hl.Count && hours.Count < 5; hi++)
                        {
                            var hh = hl[hi] as Dictionary<string, object>;
                            var tm = date.AddHours(NumS(hh, "time") / 100);
                            if (di == 0 && double.IsNaN(rainChance)) rainChance = NumS(hh, "chanceofrain");
                            if (tm > DateTime.Now) hours.Add(new Hour { Time = tm, Temp = NumS(hh, f ? "tempF" : "tempC"), Code = WwoToWmo((int)NumS(hh, "weatherCode")) });
                        }
                    }
                }
                catch (Exception ex) { Program.Log(ex); }
                var area = First(j, "nearest_area");
                if (area != null)
                {
                    var n = First(area, "areaName"); var c = First(area, "country");
                    string place = (n != null ? Json.Str(n, "value") : "") + (c != null && Json.Str(c, "value") != "" ? ", " + Json.Str(c, "value") : "");
                    if (place.Trim(',', ' ').Length > 0) { Cfg.WeatherPlace = place; Cfg.Save(); }
                }
                status = ""; lastOk = DateTime.Now;
            }
            catch (Exception ex) { Program.Log(ex); status = "The weather service sent something unexpected"; }
            Redraw();
        }

        /// <summary>Open-Meteo only understands a plain place name, so "Tel Aviv, Israel" or "Paris France"
        /// finds nothing. Try the full text first, then the part before the comma (or first words),
        /// preferring a result whose country/region matches the rest.</summary>
        static Dictionary<string, object> FindCity(string text)
        {
            var tries = new List<KeyValuePair<string, string>>();
            text = text.Trim();
            tries.Add(new KeyValuePair<string, string>(text, ""));
            int comma = text.IndexOf(',');
            if (comma > 0) tries.Add(new KeyValuePair<string, string>(text.Substring(0, comma).Trim(), text.Substring(comma + 1).Trim()));
            var words = text.Replace(",", " ").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int n = words.Length - 1; n >= 1; n--)
                tries.Add(new KeyValuePair<string, string>(string.Join(" ", words, 0, n), string.Join(" ", words, n, words.Length - n)));
            foreach (var t in tries)
            {
                if (t.Key.Length < 2) continue;
                var g = Http.GetJson("https://geocoding-api.open-meteo.com/v1/search?count=10&language=en&format=json&name=" + Uri.EscapeDataString(t.Key));
                var results = Json.Arr(g, "results");
                if (results == null || results.Count == 0) continue;
                if (t.Value.Length > 0)
                    foreach (Dictionary<string, object> r in results)
                    {
                        string where = (Json.Str(r, "country") + " " + Json.Str(r, "country_code") + " " + Json.Str(r, "admin1")).ToLowerInvariant();
                        if (where.Contains(t.Value.ToLowerInvariant())) return r;
                    }
                return (Dictionary<string, object>)results[0];
            }
            return null;
        }

        void Apply(Dictionary<string, object> j)
        {
            try
            {
                var cur = Json.Obj(j, "current");
                temp = Json.Num(cur, "temperature_2m"); feels = Json.Num(cur, "apparent_temperature");
                humidity = Json.Num(cur, "relative_humidity_2m"); wind = Json.Num(cur, "wind_speed_10m");
                code = (int)Json.Num(cur, "weather_code"); isDay = Json.Num(cur, "is_day") != 0;
                days.Clear();
                var d = Json.Obj(j, "daily");
                var t = Json.Arr(d, "time"); var mx = Json.Arr(d, "temperature_2m_max"); var mn = Json.Arr(d, "temperature_2m_min"); var c = Json.Arr(d, "weather_code");
                for (int i = 0; t != null && i < t.Count && i < 4; i++)
                    days.Add(new Day { Date = DateTime.ParseExact((string)t[i], "yyyy-MM-dd", CultureInfo.InvariantCulture), Max = Convert.ToDouble(mx[i]), Min = Convert.ToDouble(mn[i]), Code = Convert.ToInt32(c[i]) });
                sunrise = sunset = DateTime.MinValue; rainChance = double.NaN; hours.Clear();
                try
                {
                    var sr = Json.Arr(d, "sunrise"); var ss = Json.Arr(d, "sunset"); var pp = Json.Arr(d, "precipitation_probability_max");
                    if (sr != null && sr.Count > 0) sunrise = DateTime.Parse((string)sr[0], CultureInfo.InvariantCulture);
                    if (ss != null && ss.Count > 0) sunset = DateTime.Parse((string)ss[0], CultureInfo.InvariantCulture);
                    if (pp != null && pp.Count > 0 && pp[0] is double) rainChance = (double)pp[0];
                    // next hours, starting after the place's current time
                    var h = Json.Obj(j, "hourly");
                    var ht = Json.Arr(h, "time"); var htv = Json.Arr(h, "temperature_2m"); var hc = Json.Arr(h, "weather_code");
                    DateTime now = DateTime.Parse(Json.Str(cur, "time"), CultureInfo.InvariantCulture);
                    for (int i = 0; ht != null && i < ht.Count && hours.Count < 5; i++)
                    {
                        var tm = DateTime.Parse((string)ht[i], CultureInfo.InvariantCulture);
                        if (tm > now && htv[i] is double) hours.Add(new Hour { Time = tm, Temp = (double)htv[i], Code = Convert.ToInt32(hc[i]) });
                    }
                }
                catch (Exception ex) { Program.Log(ex); }
                status = ""; lastOk = DateTime.Now;
            }
            catch { status = "The weather service sent something unexpected"; }
            Redraw();
        }

        public static string Describe(int c)
        {
            if (c == 0) return Lang.L("Clear");
            if (c <= 2) return Lang.L("Partly cloudy");
            if (c == 3) return Lang.L("Cloudy");
            if (c == 45 || c == 48) return Lang.L("Fog");
            if (c >= 51 && c <= 57) return Lang.L("Drizzle");
            if ((c >= 61 && c <= 67) || (c >= 80 && c <= 82)) return Lang.L("Rain");
            if ((c >= 71 && c <= 77) || c == 85 || c == 86) return Lang.L("Snow");
            if (c >= 95) return Lang.L("Thunderstorm");
            return "—";
        }

        /// <summary>macOS style: the whole card is the sky, like the Weather widget on a Mac.</summary>
        protected override void PaintMacBack(Graphics g, GraphicsPath path, RectangleF pr)
        {
            Color top, bot;
            bool wet = code >= 51 && code != 71 && code != 73 && code != 75 && code != 77 && code != 85 && code != 86;
            if (!isDay) { top = Color.FromArgb(255, 14, 24, 56); bot = Color.FromArgb(255, 44, 58, 104); }
            else if (code >= 95) { top = Color.FromArgb(255, 52, 60, 80); bot = Color.FromArgb(255, 96, 104, 128); }
            else if (wet) { top = Color.FromArgb(255, 74, 96, 128); bot = Color.FromArgb(255, 122, 146, 176); }
            else if (code >= 3) { top = Color.FromArgb(255, 92, 120, 160); bot = Color.FromArgb(255, 150, 176, 206); }
            else { top = Color.FromArgb(255, 36, 116, 214); bot = Color.FromArgb(255, 100, 172, 240); }
            LiquidBack(g);
            using (var lg = new LinearGradientBrush(new PointF(0, pr.Top - 1), new PointF(0, pr.Bottom + 1), Color.FromArgb(110, top), Color.FromArgb(110, bot))) g.FillPath(lg, path);
            MacSheen(g, path, pr);
        }

        protected override void PaintContent(Graphics g)
        {
            Rectangle r = Inner;
            string place = string.IsNullOrEmpty(Cfg.WeatherPlace) ? Cfg.WeatherCity : Cfg.WeatherPlace;
            if (status.Length > 0 || double.IsNaN(temp))
            {
                Title(g, Lang.L("Weather"), new Rectangle(r.X, r.Y, r.Width, T.S(20)));
                string msg = status.Length > 0 ? Lang.L(status) : busy ? Lang.L("Loading…") : Lang.L("Double-click to choose your city");
                WeatherIcon(g, new RectangleF(r.X, r.Y + T.Sf(34), T.Sf(54), T.Sf(54)), 2, true);
                Txt(g, msg, Gfx.F(T.FontName, T.Sf(13.5f), FontStyle.Regular), T.Text, new RectangleF(r.X + T.Sf(70), r.Y + T.Sf(36), r.Width - T.Sf(70), T.Sf(70)), Gfx.TopLeftWrap);
                return;
            }
            string unit = Cfg.WeatherF ? "°F" : "°C";
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool art = Cfg.WeatherScene && !MacLook;
            Color ink = art || MacLook ? Color.White : T.Text, sub = art || MacLook ? Color.FromArgb(225, 255, 255, 255) : T.SubText;
            if (art)
            {
                int tb = TabH;
                var pr = new RectangleF(Panel.X + 1, Panel.Y + 1 + tb, Panel.Width - 2, Panel.Height - 2 - tb);
                string kind = SceneArt.Kind(code, isDay);
                SceneArt.DrawFaded(g, pr, Math.Max(2, T.S(PanelRadius) - 1.5f), "weather:" + kind, (w, h) =>
                {
                    var bmp = SceneArt.Weather(kind, w, h);
                    using (var cg = Graphics.FromImage(bmp))
                    using (var lg = new LinearGradientBrush(new Point(0, -1), new Point(0, h / 2 + 1), Color.FromArgb(70, 0, 0, 0), Color.FromArgb(0, 0, 0, 0)))
                        cg.FillRectangle(lg, 0, 0, w, h / 2);
                    return bmp;
                }, 0.30f, 0.62f);
            }

            // ── header: place, today's high / low
            string city = string.IsNullOrEmpty(place) ? Lang.L("Weather") : place.Split(',')[0];
            Txt(g, city, Gfx.F(T.FontName, T.Sf(13.5f), FontStyle.Bold), ink, new RectangleF(r.X, r.Y - T.Sf(1), r.Width * .62f, T.Sf(20)), Gfx.Left);
            if (days.Count > 0)
                Txt(g, string.Format(Lang.L("H {0}°  L {1}°"), Math.Round(days[0].Max), Math.Round(days[0].Min)), Gfx.F(T.FontName, T.Sf(11.5f), FontStyle.Regular), sub, new RectangleF(r.X, r.Y, r.Width, T.Sf(20)), Gfx.Right);

            // ── hero: glowing icon, big temperature, condition
            float hy = r.Y + T.Sf(26);
            Color glow = !isDay ? Color.FromArgb(70, 150, 170, 255) : code <= 2 ? Color.FromArgb(90, 255, 190, 80) : Color.FromArgb(60, 170, 200, 255);
            Gfx.Glow(g, new PointF(r.X + T.Sf(34), hy + T.Sf(34)), T.Sf(52), glow);
            WeatherIcon(g, new RectangleF(r.X, hy, T.Sf(68), T.Sf(68)), code, isDay);
            var big = Gfx.F(T.DisplayFont, T.Sf(50), FontStyle.Regular);
            string ts = Math.Round(temp).ToString(CultureInfo.InvariantCulture) + "°";
            var tsz = g.MeasureString(ts, big);
            Txt(g, ts, big, ink, new RectangleF(r.X + T.Sf(76), hy - T.Sf(4), tsz.Width + T.Sf(10), T.Sf(64)), Gfx.Left);
            float x2 = r.X + T.Sf(80) + tsz.Width;
            Txt(g, Describe(code), Gfx.F(T.FontName, T.Sf(14), FontStyle.Bold), ink, new RectangleF(x2, hy + T.Sf(12), r.Right - x2, T.Sf(20)), Gfx.Left);
            Txt(g, Lang.L("Feels") + " " + Math.Round(feels) + unit, Gfx.F(T.FontName, T.Sf(12), FontStyle.Regular), sub, new RectangleF(x2, hy + T.Sf(32), r.Right - x2, T.Sf(18)), Gfx.Left);

            // ── three glass chips: humidity, wind, rain
            float cy = hy + T.Sf(78), ch = T.Sf(46), gap = T.Sf(8), cw = (r.Width - 2 * gap) / 3f;
            string[][] chips = {
                new[] { Lang.L("Humidity"), Math.Round(humidity) + "%" },
                new[] { Lang.L("Wind"), Math.Round(wind) + (Cfg.WeatherF ? " mph" : " km/h") },
                new[] { Lang.L("Rain"), double.IsNaN(rainChance) ? "—" : Math.Round(rainChance) + "%" } };
            for (int i = 0; i < 3; i++)
            {
                var cr = new RectangleF(r.X + i * (cw + gap), cy, cw, ch);
                using (var path = Gfx.Round(cr, T.Sf(12)))
                {
                    using (var b2 = new SolidBrush(Gfx.WithAlpha(T.Text, T.Dark ? 16 : 22))) g.FillPath(b2, path);
                    using (var pen = new Pen(Gfx.WithAlpha(T.Text, 22))) g.DrawPath(pen, path);
                }
                Txt(g, chips[i][0], Gfx.F(T.FontName, T.Sf(10.5f), FontStyle.Regular), MacLook ? sub : T.SubText, new RectangleF(cr.X, cr.Y + T.Sf(5), cr.Width, T.Sf(16)), Gfx.Center);
                Txt(g, chips[i][1], Gfx.F(T.FontName, T.Sf(13.5f), FontStyle.Bold), T.Text, new RectangleF(cr.X, cr.Y + T.Sf(21), cr.Width, T.Sf(20)), Gfx.Center);
            }

            // ── sun path: sunrise → sunset arc with the sun where it is now
            float sy = cy + ch + T.Sf(12);
            if (sunrise != DateTime.MinValue && sunset > sunrise)
            {
                var sf = Gfx.F(T.FontName, T.Sf(10.5f), FontStyle.Regular);
                var sb = Gfx.F(T.FontName, T.Sf(12), FontStyle.Bold);
                Txt(g, Lang.L("Sunrise"), sf, MacLook ? sub : T.SubText, new RectangleF(r.X, sy, T.Sf(60), T.Sf(15)), Gfx.Left);
                Txt(g, sunrise.ToString("HH:mm"), sb, T.Text, new RectangleF(r.X, sy + T.Sf(14), T.Sf(60), T.Sf(18)), Gfx.Left);
                Txt(g, Lang.L("Sunset"), sf, MacLook ? sub : T.SubText, new RectangleF(r.Right - T.Sf(60), sy, T.Sf(60), T.Sf(15)), Gfx.Right);
                Txt(g, sunset.ToString("HH:mm"), sb, T.Text, new RectangleF(r.Right - T.Sf(60), sy + T.Sf(14), T.Sf(60), T.Sf(18)), Gfx.Right);
                var arc = new RectangleF(r.X + T.Sf(62), sy + T.Sf(4), r.Width - T.Sf(124), T.Sf(52));
                using (var pen = new Pen(Gfx.WithAlpha(T.Text, 40), T.Sf(1.5f)) { DashStyle = DashStyle.Dash }) g.DrawArc(pen, arc, 180, 180);
                double now = App.Now.TimeOfDay.TotalMinutes, a0 = sunrise.TimeOfDay.TotalMinutes, a1 = sunset.TimeOfDay.TotalMinutes;
                float f = (float)Math.Max(0, Math.Min(1, (now - a0) / (a1 - a0)));
                using (var pen = new Pen(Color.FromArgb(230, 255, 196, 90), T.Sf(2.2f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    if (f > 0.01f) g.DrawArc(pen, arc, 180, 180 * f);
                double ang = Math.PI * (1 - f);
                var sp = new PointF(arc.X + arc.Width / 2 + (float)Math.Cos(ang) * arc.Width / 2, arc.Y + arc.Height / 2 - (float)Math.Sin(ang) * arc.Height / 2);
                bool up = now > a0 && now < a1;
                Gfx.Glow(g, sp, T.Sf(12), Color.FromArgb(up ? 160 : 60, 255, 200, 90));
                using (var b2 = new SolidBrush(up ? Color.FromArgb(255, 255, 210, 110) : Gfx.WithAlpha(T.Text, 90))) g.FillEllipse(b2, sp.X - T.Sf(4.5f), sp.Y - T.Sf(4.5f), T.Sf(9), T.Sf(9));
                sy += T.Sf(40);
            }

            // ── next hours (or next days if hourly data is missing)
            float hy2 = sy + T.Sf(4);
            if (r.Bottom - hy2 < T.Sf(60)) return;
            using (var pen = new Pen(Gfx.WithAlpha(T.Text, 24))) g.DrawLine(pen, r.X, hy2, r.Right, hy2);
            hy2 += T.Sf(8);
            int n = hours.Count > 0 ? Math.Min(5, hours.Count) : Math.Min(4, days.Count);
            float colw = r.Width / Math.Max(1f, n);
            for (int i = 0; i < n; i++)
            {
                float x = r.X + i * colw;
                string top; int c; string tv;
                if (hours.Count > 0) { var h = hours[i]; top = h.Time.ToString("HH:mm"); c = h.Code; tv = Math.Round(h.Temp) + "°"; }
                else { var d = days[i]; top = i == 0 ? Lang.L("Today") : d.Date.ToString("ddd", CultureInfo.CurrentCulture); c = d.Code; tv = Math.Round(d.Max) + "° " + Math.Round(d.Min) + "°"; }
                if (i == 0)
                    using (var path = Gfx.Round(new RectangleF(x + T.Sf(3), hy2 - T.Sf(2), colw - T.Sf(6), T.Sf(66)), T.Sf(12)))
                    using (var b2 = new SolidBrush(Gfx.WithAlpha(T.Accent, 46))) g.FillPath(b2, path);
                Txt(g, top, Gfx.F(T.FontName, T.Sf(11), FontStyle.Regular), MacLook ? sub : T.SubText, new RectangleF(x, hy2 + T.Sf(2), colw, T.Sf(16)), Gfx.Center);
                bool dayAt = hours.Count == 0 || sunrise == DateTime.MinValue || (hours[i].Time.TimeOfDay > sunrise.TimeOfDay && hours[i].Time.TimeOfDay < sunset.TimeOfDay);
                WeatherIcon(g, new RectangleF(x + colw / 2 - T.Sf(12), hy2 + T.Sf(19), T.Sf(24), T.Sf(24)), c, dayAt);
                Txt(g, tv, Gfx.F(T.FontName, T.Sf(12.5f), FontStyle.Bold), T.Text, new RectangleF(x, hy2 + T.Sf(44), colw, T.Sf(18)), Gfx.Center);
            }
        }

        /// <summary>Simple drawn weather icons (no image files needed).</summary>
        void WeatherIcon(Graphics g, RectangleF b, int c, bool day)
        {
            float s = b.Width;
            bool cloudy = c != 0, rain = (c >= 51 && c <= 67) || (c >= 80 && c <= 82), snow = (c >= 71 && c <= 77) || c == 85 || c == 86, storm = c >= 95, fog = c == 45 || c == 48;
            if (c <= 2)
            {
                var sun = new RectangleF(b.X + s * (c == 0 ? .18f : .08f), b.Y + s * (c == 0 ? .18f : .08f), s * (c == 0 ? .64f : .5f), s * (c == 0 ? .64f : .5f));
                if (day)
                    using (var br = new LinearGradientBrush(sun, Color.FromArgb(255, 255, 214, 102), Color.FromArgb(255, 255, 150, 60), 45f)) g.FillEllipse(br, sun);
                else
                    using (var br = new SolidBrush(Color.FromArgb(255, 222, 228, 255))) g.FillEllipse(br, sun);
            }
            if (cloudy && !fog)
            {
                Color cc = storm || rain ? Color.FromArgb(255, 150, 162, 186) : Color.FromArgb(255, 236, 241, 250);
                using (var br = new SolidBrush(cc))
                {
                    g.FillEllipse(br, b.X + s * .14f, b.Y + s * .38f, s * .42f, s * .36f);
                    g.FillEllipse(br, b.X + s * .32f, b.Y + s * .24f, s * .44f, s * .44f);
                    g.FillEllipse(br, b.X + s * .56f, b.Y + s * .40f, s * .34f, s * .32f);
                    g.FillRectangle(br, b.X + s * .3f, b.Y + s * .52f, s * .44f, s * .2f);
                }
            }
            if (fog)
                using (var pen = new Pen(Color.FromArgb(220, 220, 228, 240), s * .07f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    for (int i = 0; i < 3; i++) g.DrawLine(pen, b.X + s * (.15f + i * .05f), b.Y + s * (.35f + i * .17f), b.X + s * (.85f - i * .05f), b.Y + s * (.35f + i * .17f));
            if (rain)
                using (var pen = new Pen(Color.FromArgb(255, 110, 170, 255), s * .06f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    for (int i = 0; i < 3; i++) g.DrawLine(pen, b.X + s * (.32f + i * .16f), b.Y + s * .8f, b.X + s * (.27f + i * .16f), b.Y + s * .95f);
            if (snow)
                using (var br = new SolidBrush(Color.White))
                    for (int i = 0; i < 3; i++) g.FillEllipse(br, b.X + s * (.28f + i * .16f), b.Y + s * .8f, s * .09f, s * .09f);
            if (storm)
                using (var br = new SolidBrush(Color.FromArgb(255, 255, 206, 72)))
                    g.FillPolygon(br, new[] { new PointF(b.X + s * .52f, b.Y + s * .62f), new PointF(b.X + s * .4f, b.Y + s * .86f), new PointF(b.X + s * .5f, b.Y + s * .84f), new PointF(b.X + s * .44f, b.Y + s * 1f), new PointF(b.X + s * .62f, b.Y + s * .76f), new PointF(b.X + s * .52f, b.Y + s * .78f) });
        }

        protected override void OnDoubleClickAt(Point p) { ChooseCity(); }

        public void ChooseCity()
        {
            string c = InputBox.Ask("Weather: your city", Cfg.WeatherCity, App.Theme);
            if (c == null || c.Trim().Length == 0) return;
            Cfg.WeatherCity = c.Trim(); Cfg.WeatherLat = double.NaN; Cfg.WeatherLon = double.NaN; Cfg.WeatherPlace = "";
            Cfg.Save(); status = ""; temp = double.NaN; Redraw();
            Fetch();
        }
        public void UnitsChanged() { Fetch(); }
    }

    // ================================================================ To-do
    internal class TodoWidget : Widget
    {
        protected override Color MacColor { get { return Color.FromArgb(255, 10, 132, 255); } }
        public TodoWidget(App app) : base(app, "todo") { }
        const int MaxRows = 8;

        class Task { public string Text; public bool Done; }
        List<Task> Read()
        {
            var list = new List<Task>();
            foreach (string raw in (Cfg.TodoText ?? "").Split('\n'))
            {
                string l = raw.Trim(); if (l.Length == 0) continue;
                bool done = l.StartsWith("[x]", StringComparison.OrdinalIgnoreCase);
                if (l.StartsWith("[x]", StringComparison.OrdinalIgnoreCase) || l.StartsWith("[ ]")) l = l.Substring(3).Trim();
                if (l.Length > 0) list.Add(new Task { Text = l, Done = done });
            }
            return list;
        }
        void Write(List<Task> list)
        {
            var parts = new List<string>();
            foreach (var t in list) parts.Add((t.Done ? "[x] " : "[ ] ") + t.Text);
            Cfg.TodoText = string.Join("\n", parts.ToArray());
            Cfg.Save();
        }
        int Rows { get { return Math.Max(1, Math.Min(MaxRows, Read().Count)); } }
        protected override Size BaseSize { get { return new Size(320, 58 + Rows * 28 + 14); } }
        float RowTop(int i) { return Inner.Y + T.Sf(32) + i * T.Sf(28); }

        protected override void PaintContent(Graphics g)
        {
            Rectangle r = Inner;
            var list = Read();
            int left = list.FindAll(t => !t.Done).Count;
            Title(g, Lang.L("To-do"), new Rectangle(r.X, r.Y, r.Width, T.S(20)));
            if (MacLook)
            {
                // like Reminders: a big count of what's left in the corner
                Txt(g, left.ToString(CultureInfo.InvariantCulture), Gfx.F(T.DisplayFont, T.Sf(24), FontStyle.Bold), T.Text, new RectangleF(r.X, r.Y - T.Sf(8), r.Width, T.Sf(32)), Gfx.Right);
            }
            else
            Txt(g, list.Count == 0 ? "double-click to add" : left == 0 ? "all done" : left + " left", Gfx.F(T.FontName, T.Sf(11), FontStyle.Regular), T.SubText, new RectangleF(r.X, r.Y, r.Width, T.Sf(20)), Gfx.Right);
            if (list.Count == 0)
            {
                Txt(g, Lang.L("Nothing to do. Double-click to add tasks."), Gfx.F(T.FontName, T.Sf(13.5f), FontStyle.Regular), T.SubText, new RectangleF(r.X, RowTop(0), r.Width, T.Sf(24)), Gfx.Left);
                return;
            }
            var f = Gfx.F(T.FontName, T.Sf(13.5f), FontStyle.Regular);
            var fd = Gfx.F(T.FontName, T.Sf(13.5f), FontStyle.Strikeout);
            for (int i = 0; i < list.Count && i < MaxRows; i++)
            {
                var t = list[i]; float y = RowTop(i), cb = T.Sf(17);
                var box = new RectangleF(r.X, y + (T.Sf(24) - cb) / 2, cb, cb);
                if (t.Done)
                {
                    using (var b = new SolidBrush(T.Accent)) g.FillEllipse(b, box);
                    using (var pen = new Pen(Color.White, T.Sf(1.8f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                        g.DrawLines(pen, new[] { new PointF(box.X + cb * .27f, box.Y + cb * .52f), new PointF(box.X + cb * .44f, box.Y + cb * .69f), new PointF(box.X + cb * .74f, box.Y + cb * .34f) });
                }
                else using (var pen = new Pen(MacLook ? T.Accent : Gfx.WithAlpha(T.Text, 150), T.Sf(1.5f))) g.DrawEllipse(pen, box);
                if (MacLook && i < list.Count - 1 && i < MaxRows - 1)
                    using (var pen = new Pen(T.Track, 1f)) g.DrawLine(pen, r.X + cb + T.Sf(10), y + T.Sf(26), r.Right, y + T.Sf(26));
                string text = i == MaxRows - 1 && list.Count > MaxRows ? "+" + (list.Count - MaxRows + 1) + " more…" : t.Text;
                Txt(g, text, t.Done ? fd : f, t.Done ? T.SubText : T.Text, new RectangleF(r.X + cb + T.Sf(10), y, r.Width - cb - T.Sf(10), T.Sf(24)), Gfx.Left);
            }
        }

        int HitRow(Point p)
        {
            Rectangle r = Inner;
            if (p.X < r.X - T.S(4) || p.X > r.Right) return -1;
            for (int i = 0; i < Rows; i++) if (p.Y >= RowTop(i) && p.Y < RowTop(i) + T.Sf(28)) return i;
            return -1;
        }
        protected override bool IsDragArea(Point p) { return HitRow(p) < 0 || p.X > Inner.X + T.S(26); }

        protected override void OnClickAt(Point p, MouseButtons b)
        {
            int i = HitRow(p);
            if (b == MouseButtons.Left && i >= 0 && p.X <= Inner.X + T.S(26))
            {
                var list = Read();
                if (i < list.Count && !(i == MaxRows - 1 && list.Count > MaxRows)) { list[i].Done = !list[i].Done; Write(list); Redraw(); }
                return;
            }
            base.OnClickAt(p, b);
        }
        protected override void OnDoubleClickAt(Point p) { if (HitRow(p) < 0 || p.X > Inner.X + T.S(26)) Edit(); }

        public void Edit()
        {
            var list = Read();
            var lines = new List<string>(); foreach (var t in list) lines.Add(t.Text);
            var done = new HashSet<string>(); foreach (var t in list) if (t.Done) done.Add(t.Text);
            var f = new NoteEditor(App, PanelBounds, string.Join("\n", lines.ToArray()), "One task per line. Esc or click outside to save.");
            f.FormClosed += delegate
            {
                var nl = new List<Task>();
                foreach (string raw in f.Result.Split('\n')) { string l = raw.Trim(); if (l.Length > 0) nl.Add(new Task { Text = l, Done = done.Contains(l) }); }
                Write(nl);
                App.PlaceWindow(this);
            };
            f.Show(); f.Activate();
        }
        public void ClearDone() { var l = Read(); l.RemoveAll(t => t.Done); Write(l); App.PlaceWindow(this); }
    }

    // ================================================================ Photo frame
    internal class PhotoWidget : Widget
    {
        static readonly string[] Exts = { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };
        Bitmap shown; string shownName = "";
        List<string> files; string filesFor;
        int index = -1;
        System.Windows.Forms.Timer next;
        readonly Random rng = new Random();

        protected override Color MacColor { get { return Color.FromArgb(255, 191, 90, 242); } }

        public PhotoWidget(App app) : base(app, "photos") { }
        protected override Size BaseSize { get { return new Size(320, 214); } }

        public string Folder
        {
            get
            {
                if (!string.IsNullOrEmpty(Cfg.PhotoFolder)) return Cfg.PhotoFolder;
                try { return Environment.GetFolderPath(Environment.SpecialFolder.MyPictures); } catch { return ""; }
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            next = new System.Windows.Forms.Timer { Interval = Math.Max(1, Cfg.PhotoMinutes) * 60000 };
            next.Tick += delegate { if (Visible && !App.Paused) Next(); };
            next.Start();
            Next();
        }
        public void IntervalChanged() { if (next != null) next.Interval = Math.Max(1, Cfg.PhotoMinutes) * 60000; }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { if (next != null) next.Dispose(); if (shown != null) shown.Dispose(); }
            base.Dispose(disposing);
        }

        public void Next()
        {
            if (App.Preview) return;
            try
            {
                string folder = Folder;
                if (files == null || filesFor != folder)
                {
                    files = new List<string>(); filesFor = folder; index = -1;
                    if (Directory.Exists(folder))
                        foreach (string f in Directory.GetFiles(folder))
                            if (Array.IndexOf(Exts, Path.GetExtension(f).ToLowerInvariant()) >= 0) files.Add(f);
                    // shuffle once, then walk through in order so nothing repeats too soon
                    for (int i = files.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); string t = files[i]; files[i] = files[j]; files[j] = t; }
                }
                if (files.Count == 0) { SetPhoto(null, ""); return; }
                for (int tries = 0; tries < Math.Min(5, files.Count); tries++)
                {
                    index = (index + 1) % files.Count;
                    Bitmap b = LoadScaled(files[index]);
                    if (b != null) { SetPhoto(b, Path.GetFileNameWithoutExtension(files[index])); return; }
                }
            }
            catch { }
        }
        public void Reload() { files = null; Next(); }

        void SetPhoto(Bitmap b, string name)
        {
            if (shown != null) shown.Dispose();
            shown = b; shownName = name;
            Redraw();
            if (Cfg.TrimMemory) GC.Collect();
        }

        /// <summary>Loads a photo already shrunk to the widget's size, so a 20 MB picture costs a few hundred KB of RAM.</summary>
        Bitmap LoadScaled(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var img = Image.FromStream(fs, false, false))
                {
                    RotateFlipType rot = RotateFlipType.RotateNoneFlipNone;
                    try
                    {
                        if (Array.IndexOf(img.PropertyIdList, 0x0112) >= 0)
                        {
                            int o = img.GetPropertyItem(0x0112).Value[0];
                            rot = o == 3 ? RotateFlipType.Rotate180FlipNone : o == 6 ? RotateFlipType.Rotate90FlipNone : o == 8 ? RotateFlipType.Rotate270FlipNone : rot;
                        }
                    }
                    catch { }
                    int w = Math.Max(1, Panel.Width > 0 ? Panel.Width : T.S(320)), h = Math.Max(1, Panel.Height > 0 ? Panel.Height : T.S(214));
                    bool swap = rot == RotateFlipType.Rotate90FlipNone || rot == RotateFlipType.Rotate270FlipNone;
                    float iw = swap ? img.Height : img.Width, ih = swap ? img.Width : img.Height;
                    float k = Math.Max(w / iw, h / ih);
                    var bmp = new Bitmap(w, h);
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.TranslateTransform(w / 2f, h / 2f);
                        if (rot == RotateFlipType.Rotate90FlipNone) g.RotateTransform(90);
                        else if (rot == RotateFlipType.Rotate180FlipNone) g.RotateTransform(180);
                        else if (rot == RotateFlipType.Rotate270FlipNone) g.RotateTransform(270);
                        float dw = img.Width * k, dh = img.Height * k;
                        g.DrawImage(img, -dw / 2, -dh / 2, dw, dh);
                    }
                    return bmp;
                }
            }
            catch { return null; }
        }

        public void SetSampleFile(string path) { SetPhoto(LoadScaled(path), Path.GetFileNameWithoutExtension(path)); }

        protected override void PaintContent(Graphics g)
        {
            var pr = new RectangleF(Panel.X + 1, Panel.Y + 1, Panel.Width - 2, Panel.Height - 2);
            float rad = Math.Max(0, T.S(PanelRadius) - 1);
            if (shown == null)
            {
                Rectangle r = Inner;
                Title(g, Lang.L("Photos"), new Rectangle(r.X, r.Y, r.Width, T.S(20)));
                Txt(g, Lang.L("Double-click to choose a folder of photos"), Gfx.F(T.FontName, T.Sf(13.5f), FontStyle.Regular), T.SubText, new RectangleF(r.X, r.Y + T.Sf(36), r.Width, T.Sf(60)), Gfx.TopLeftWrap);
                return;
            }
            using (var path = Gfx.Round(pr, rad))
            {
                var st = g.Save();
                g.SetClip(path);
                g.DrawImage(shown, pr);
                // soft shade at the bottom so the caption reads on any photo
                var cap = new RectangleF(pr.X, pr.Bottom - T.Sf(54), pr.Width, T.Sf(54));
                using (var lg = new LinearGradientBrush(new PointF(0, cap.Top - 1), new PointF(0, cap.Bottom), Color.FromArgb(0, 0, 0, 0), Color.FromArgb(150, 0, 0, 0)))
                    g.FillRectangle(lg, cap);
                g.Restore(st);
            }
            using (var b = new SolidBrush(Color.White))
                g.DrawString(shownName, Gfx.F(T.FontName, T.Sf(12.5f), FontStyle.Bold), b, new RectangleF(pr.X + T.Sf(14), pr.Bottom - T.Sf(30), pr.Width - T.Sf(60), T.Sf(20)), Gfx.Left);
            // "next" arrow
            float cx = pr.Right - T.Sf(24), cy = pr.Bottom - T.Sf(20);
            Gfx.FillRound(g, Color.FromArgb(90, 255, 255, 255), new RectangleF(cx - T.Sf(12), cy - T.Sf(12), T.Sf(24), T.Sf(24)), T.Sf(12));
            using (var pen = new Pen(Color.White, T.Sf(1.8f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(pen, new[] { new PointF(cx - T.Sf(2), cy - T.Sf(5)), new PointF(cx + T.Sf(3), cy), new PointF(cx - T.Sf(2), cy + T.Sf(5)) });
        }

        bool OnArrow(Point p) { return shown != null && p.X > Panel.Right - T.S(48) && p.Y > Panel.Bottom - T.S(44); }
        protected override bool IsDragArea(Point p) { return !OnArrow(p); }
        protected override void OnClickAt(Point p, MouseButtons b)
        {
            if (b == MouseButtons.Left && OnArrow(p)) { Next(); return; }
            base.OnClickAt(p, b);
        }
        protected override void OnDoubleClickAt(Point p) { if (shown == null) ChooseFolder(); else if (!OnArrow(p)) OpenCurrent(); }

        void OpenCurrent()
        {
            if (files == null || index < 0 || index >= files.Count) return;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(files[index]) { UseShellExecute = true }); } catch { }
        }

        public void ChooseFolder()
        {
            using (var fb = new FolderBrowserDialog { Description = "Choose a folder of photos for the photo frame", SelectedPath = Folder })
                if (fb.ShowDialog() == DialogResult.OK) { Cfg.PhotoFolder = fb.SelectedPath; Cfg.Save(); Reload(); }
        }
    }
}

namespace GlassDesk
{
    // ================================================================ Countdown
    internal class CountdownWidget : Widget
    {
        protected override Color MacColor { get { return Color.FromArgb(255, 255, 69, 58); } }
        public CountdownWidget(App app) : base(app, "countdown") { }
        protected override Size BaseSize { get { return new Size(320, 132); } }

        public bool TryTarget(out DateTime d)
        {
            return DateTime.TryParseExact(Cfg.CountdownDate ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d);
        }

        protected override void PaintContent(Graphics g)
        {
            Rectangle r = Inner;
            Title(g, Lang.L("Countdown"), new Rectangle(r.X, r.Y, r.Width, T.S(20)));
            DateTime target;
            if (!TryTarget(out target) || string.IsNullOrEmpty(Cfg.CountdownTitle))
            {
                Txt(g, Lang.L("Double-click to count down to a birthday, a trip or the holidays"), Gfx.F(T.FontName, T.Sf(13.5f), FontStyle.Regular), T.SubText, new RectangleF(r.X, r.Y + T.Sf(30), r.Width, T.Sf(50)), Gfx.TopLeftWrap);
                return;
            }
            int days = (int)(target.Date - App.Now.Date).TotalDays;
            Txt(g, target.ToString("d MMM yyyy", CultureInfo.CurrentCulture), Gfx.F(T.FontName, T.Sf(11.5f), FontStyle.Regular), T.SubText, new RectangleF(r.X, r.Y, r.Width, T.Sf(20)), Gfx.Right);
            string big = days > 0 ? days.ToString(CultureInfo.InvariantCulture) : days == 0 ? "Today!" : (-days).ToString(CultureInfo.InvariantCulture);
            var bf = Gfx.F(T.DisplayFont, T.Sf(days == 0 ? 40 : 50), FontStyle.Regular);
            SizeF bs = g.MeasureString(big, bf, PointF.Empty, StringFormat.GenericTypographic);
            using (var b = new SolidBrush(days == 0 ? T.Accent : T.Text)) g.DrawString(big, bf, b, new PointF(r.X - T.Sf(2), r.Y + T.Sf(22)), StringFormat.GenericTypographic);
            string unit = days == 0 ? "" : days > 0 ? (days == 1 ? "day to go" : "days to go") : (days == -1 ? "day ago" : "days ago");
            float x = r.X + bs.Width + T.Sf(10);
            Txt(g, unit, Gfx.F(T.FontName, T.Sf(13), FontStyle.Bold), T.Accent, new RectangleF(x, r.Y + T.Sf(30), r.Right - x, T.Sf(20)), Gfx.Left);
            Txt(g, Cfg.CountdownTitle, Gfx.F(T.FontName, T.Sf(14), FontStyle.Regular), T.Text, new RectangleF(x, r.Y + T.Sf(50), r.Right - x, T.Sf(22)), Gfx.Left);
        }

        protected override void OnDoubleClickAt(Point p) { Edit(); }
        public void Edit()
        {
            string title = InputBox.Ask("What are you counting down to?", string.IsNullOrEmpty(Cfg.CountdownTitle) ? "Summer holiday" : Cfg.CountdownTitle, App.Theme);
            if (title == null || title.Trim().Length == 0) return;
            DateTime cur; string start = TryTarget(out cur) ? cur.ToString("yyyy-MM-dd") : App.Now.AddDays(30).ToString("yyyy-MM-dd");
            string date = InputBox.Ask("Date (year-month-day, e.g. 2026-12-25)", start, App.Theme);
            if (date == null) return;
            DateTime d;
            if (!DateTime.TryParseExact(date.Trim(), new[] { "yyyy-MM-dd", "yyyy-M-d", "d/M/yyyy", "d.M.yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
            { MessageBox.Show("Please write the date like 2026-12-25.", "GlassDesk"); return; }
            Cfg.CountdownTitle = title.Trim(); Cfg.CountdownDate = d.ToString("yyyy-MM-dd"); Cfg.Save(); Redraw();
        }
    }

    // ================================================================ Quote of the day
    internal class QuoteWidget : Widget
    {
        protected override Color MacColor { get { return Color.FromArgb(255, 175, 82, 222); } }
        public QuoteWidget(App app) : base(app, "quote") { }
        protected override Size BaseSize { get { return new Size(320, 150); } }
        int extra;   // "next quote" clicks today

        static readonly string[][] Quotes =
        {
            new[] { "The secret of getting ahead is getting started.", "Mark Twain" },
            new[] { "Well begun is half done.", "Aristotle" },
            new[] { "It always seems impossible until it's done.", "Nelson Mandela" },
            new[] { "Simplicity is the ultimate sophistication.", "Leonardo da Vinci" },
            new[] { "Do what you can, with what you have, where you are.", "Theodore Roosevelt" },
            new[] { "Quality is not an act, it is a habit.", "Aristotle" },
            new[] { "The best way out is always through.", "Robert Frost" },
            new[] { "Little by little, one travels far.", "J. R. R. Tolkien" },
            new[] { "Happiness depends upon ourselves.", "Aristotle" },
            new[] { "Energy and persistence conquer all things.", "Benjamin Franklin" },
            new[] { "Everything you can imagine is real.", "Pablo Picasso" },
            new[] { "Life is really simple, but we insist on making it complicated.", "Confucius" },
            new[] { "Act as if what you do makes a difference. It does.", "William James" },
            new[] { "In the middle of difficulty lies opportunity.", "Albert Einstein" },
            new[] { "The journey of a thousand miles begins with one step.", "Lao Tzu" },
            new[] { "Be yourself; everyone else is already taken.", "Oscar Wilde" },
            new[] { "What we think, we become.", "Buddha" },
            new[] { "Tell me and I forget. Teach me and I remember. Involve me and I learn.", "Benjamin Franklin" },
            new[] { "You miss 100% of the shots you don't take.", "Wayne Gretzky" },
            new[] { "Creativity is intelligence having fun.", "Albert Einstein" },
            new[] { "Dream big and dare to fail.", "Norman Vaughan" },
            new[] { "Order is the shape upon which beauty depends.", "Pearl S. Buck" },
            new[] { "Done is better than perfect.", "Sheryl Sandberg" },
            new[] { "Small deeds done are better than great deeds planned.", "Peter Marshall" },
        };

        public string[] Current { get { int i = (App.Now.DayOfYear + App.Now.Year * 7 + extra) % Quotes.Length; return Quotes[i]; } }

        protected override void PaintContent(Graphics g)
        {
            Rectangle r = Inner;
            Title(g, Lang.L("Quote of the day"), new Rectangle(r.X, r.Y, r.Width, T.S(20)));
            var q = Current;
            using (var b = new SolidBrush(Gfx.WithAlpha(T.Accent, 200)))
                g.DrawString("“", Gfx.F(T.DisplayFont, T.Sf(40), FontStyle.Bold), b, new PointF(r.X - T.Sf(4), r.Y + T.Sf(14)));
            Txt(g, q[0], Gfx.F(T.FontName, T.Sf(14.5f), FontStyle.Regular), T.Text, new RectangleF(r.X + T.Sf(24), r.Y + T.Sf(26), r.Width - T.Sf(24), T.Sf(66)), Gfx.TopLeftWrap);
            Txt(g, "— " + q[1], Gfx.F(T.FontName, T.Sf(12), FontStyle.Regular), T.SubText, new RectangleF(r.X + T.Sf(24), r.Bottom - T.Sf(18), r.Width - T.Sf(24), T.Sf(18)), Gfx.Left);
        }
        protected override void OnDoubleClickAt(Point p) { Next(); }
        public void Next() { extra++; Redraw(); }
    }
}
