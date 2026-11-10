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
        double temp = double.NaN, feels, wind, humidity; int code = -1; bool isDay = true;
        readonly List<Day> days = new List<Day>();
        string status = "";              // shown instead of the weather when something is missing
        bool busy;
        System.Windows.Forms.Timer refresh;

        public WeatherWidget(App app) : base(app, "weather") { }
        protected override Size BaseSize { get { return new Size(320, 196); } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            refresh = new System.Windows.Forms.Timer { Interval = 30 * 60 * 1000 };   // every 30 minutes
            refresh.Tick += delegate { if (Visible && !App.Paused) Fetch(); };
            refresh.Start();
            Fetch();
        }
        protected override void Dispose(bool disposing) { if (disposing && refresh != null) refresh.Dispose(); base.Dispose(disposing); }

        /// <summary>Preview data for screenshots.</summary>
        public void SetSample()
        {
            temp = 18.4; feels = 17; wind = 11; humidity = 64; code = 2; isDay = true; status = "";
            Cfg.WeatherPlace = "Tel Aviv, Israel";
            days.Clear();
            var d0 = App.Now.Date;
            int[] codes = { 2, 61, 0, 3 }; double[] mx = { 21, 19, 24, 22 }, mn = { 14, 13, 15, 15 };
            for (int i = 0; i < 4; i++) days.Add(new Day { Date = d0.AddDays(i), Max = mx[i], Min = mn[i], Code = codes[i] });
        }

        public void Fetch()
        {
            if (busy || App.Preview) return;
            if (string.IsNullOrEmpty(Cfg.WeatherCity)) { status = "Double-click to choose your city"; Redraw(); return; }
            busy = true;
            string city = Cfg.WeatherCity; double lat = Cfg.WeatherLat, lon = Cfg.WeatherLon; bool f = Cfg.WeatherF;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string place = null, err = null; Dictionary<string, object> json = null;
                try
                {
                    if (double.IsNaN(lat))
                    {
                        var g = Http.GetJson("https://geocoding-api.open-meteo.com/v1/search?count=1&language=en&format=json&name=" + Uri.EscapeDataString(city));
                        var results = Json.Arr(g, "results");
                        if (results == null || results.Count == 0) throw new Exception("NOTFOUND");
                        var r0 = (Dictionary<string, object>)results[0];
                        lat = Json.Num(r0, "latitude"); lon = Json.Num(r0, "longitude");
                        place = Json.Str(r0, "name") + (Json.Str(r0, "country") != "" ? ", " + Json.Str(r0, "country") : "");
                    }
                    json = Http.GetJson(string.Format(CultureInfo.InvariantCulture,
                        "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,is_day,wind_speed_10m&daily=weather_code,temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=4{2}",
                        lat, lon, f ? "&temperature_unit=fahrenheit&wind_speed_unit=mph" : ""));
                }
                catch (Exception ex) { err = ex.Message == "NOTFOUND" ? "Couldn't find \"" + city + "\". Double-click to try another city." : "Can't reach the weather service right now"; }
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        busy = false;
                        if (err != null) { status = err; Redraw(); return; }
                        if (place != null) { Cfg.WeatherLat = lat; Cfg.WeatherLon = lon; Cfg.WeatherPlace = place; Cfg.Save(); }
                        Apply(json);
                    }));
                }
                catch { busy = false; }
            });
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
                status = "";
            }
            catch { status = "The weather service sent something unexpected"; }
            Redraw();
        }

        public static string Describe(int c)
        {
            if (c == 0) return "Clear";
            if (c <= 2) return "Partly cloudy";
            if (c == 3) return "Cloudy";
            if (c == 45 || c == 48) return "Fog";
            if (c >= 51 && c <= 57) return "Drizzle";
            if ((c >= 61 && c <= 67) || (c >= 80 && c <= 82)) return "Rain";
            if ((c >= 71 && c <= 77) || c == 85 || c == 86) return "Snow";
            if (c >= 95) return "Thunderstorm";
            return "—";
        }

        protected override void PaintContent(Graphics g)
        {
            Rectangle r = Inner;
            Title(g, "Weather", new Rectangle(r.X, r.Y, r.Width, T.S(20)));
            string place = string.IsNullOrEmpty(Cfg.WeatherPlace) ? Cfg.WeatherCity : Cfg.WeatherPlace;
            if (!string.IsNullOrEmpty(place))
                Txt(g, place, Gfx.F(T.FontName, T.Sf(11.5f), FontStyle.Regular), T.SubText, new RectangleF(r.X + T.Sf(70), r.Y, r.Width - T.Sf(70), T.Sf(20)), Gfx.Right);
            if (status.Length > 0 || double.IsNaN(temp))
            {
                string msg = status.Length > 0 ? status : busy ? "Loading…" : "Double-click to choose your city";
                WeatherIcon(g, new RectangleF(r.X, r.Y + T.Sf(34), T.Sf(54), T.Sf(54)), 2, true);
                Txt(g, msg, Gfx.F(T.FontName, T.Sf(13.5f), FontStyle.Regular), T.Text, new RectangleF(r.X + T.Sf(70), r.Y + T.Sf(36), r.Width - T.Sf(70), T.Sf(70)), Gfx.TopLeftWrap);
                return;
            }
            string unit = Cfg.WeatherF ? "°F" : "°C";
            WeatherIcon(g, new RectangleF(r.X - T.Sf(2), r.Y + T.Sf(28), T.Sf(62), T.Sf(62)), code, isDay);
            var big = Gfx.F(T.DisplayFont, T.Sf(46), FontStyle.Regular);
            string ts = Math.Round(temp).ToString(CultureInfo.InvariantCulture) + "°";
            Txt(g, ts, big, T.Text, new RectangleF(r.X + T.Sf(66), r.Y + T.Sf(24), T.Sf(130), T.Sf(56)), Gfx.Left);
            float x2 = r.X + T.Sf(170);
            Txt(g, Describe(code), Gfx.F(T.FontName, T.Sf(14), FontStyle.Bold), T.Text, new RectangleF(x2, r.Y + T.Sf(30), r.Right - x2, T.Sf(20)), Gfx.Left);
            Txt(g, "Feels " + Math.Round(feels) + unit, Gfx.F(T.FontName, T.Sf(12), FontStyle.Regular), T.SubText, new RectangleF(x2, r.Y + T.Sf(50), r.Right - x2, T.Sf(18)), Gfx.Left);
            Txt(g, Math.Round(humidity) + "% · " + Math.Round(wind) + (Cfg.WeatherF ? " mph" : " km/h"), Gfx.F(T.FontName, T.Sf(12), FontStyle.Regular), T.SubText, new RectangleF(x2, r.Y + T.Sf(67), r.Right - x2, T.Sf(18)), Gfx.Left);

            // next days
            float top = r.Y + T.Sf(100), cw = r.Width / 4f;
            using (var pen = new Pen(Gfx.WithAlpha(T.Text, 26))) g.DrawLine(pen, r.X, top - T.Sf(6), r.Right, top - T.Sf(6));
            for (int i = 0; i < days.Count && i < 4; i++)
            {
                var d = days[i]; float x = r.X + i * cw;
                string dn = i == 0 ? "Today" : d.Date.ToString("ddd", CultureInfo.CurrentCulture);
                Txt(g, dn, Gfx.F(T.FontName, T.Sf(11.5f), FontStyle.Bold), T.SubText, new RectangleF(x, top, cw, T.Sf(16)), Gfx.Center);
                WeatherIcon(g, new RectangleF(x + cw / 2 - T.Sf(12), top + T.Sf(18), T.Sf(24), T.Sf(24)), d.Code, true);
                Txt(g, Math.Round(d.Max) + "° " + Math.Round(d.Min) + "°", Gfx.F(T.FontName, T.Sf(12), FontStyle.Regular), T.Text, new RectangleF(x, top + T.Sf(44), cw, T.Sf(18)), Gfx.Center);
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
            Title(g, "To-do", new Rectangle(r.X, r.Y, r.Width, T.S(20)));
            Txt(g, list.Count == 0 ? "double-click to add" : left == 0 ? "all done" : left + " left", Gfx.F(T.FontName, T.Sf(11), FontStyle.Regular), T.SubText, new RectangleF(r.X, r.Y, r.Width, T.Sf(20)), Gfx.Right);
            if (list.Count == 0)
            {
                Txt(g, "Nothing to do. Double-click to add tasks.", Gfx.F(T.FontName, T.Sf(13.5f), FontStyle.Regular), T.SubText, new RectangleF(r.X, RowTop(0), r.Width, T.Sf(24)), Gfx.Left);
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
                else using (var pen = new Pen(Gfx.WithAlpha(T.Text, 150), T.Sf(1.5f))) g.DrawEllipse(pen, box);
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
            float rad = Math.Max(0, T.S(Cfg.Radius) - 1);
            if (shown == null)
            {
                Rectangle r = Inner;
                Title(g, "Photos", new Rectangle(r.X, r.Y, r.Width, T.S(20)));
                Txt(g, "Double-click to choose a folder of photos", Gfx.F(T.FontName, T.Sf(13.5f), FontStyle.Regular), T.SubText, new RectangleF(r.X, r.Y + T.Sf(36), r.Width, T.Sf(60)), Gfx.TopLeftWrap);
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
        public CountdownWidget(App app) : base(app, "countdown") { }
        protected override Size BaseSize { get { return new Size(320, 132); } }

        public bool TryTarget(out DateTime d)
        {
            return DateTime.TryParseExact(Cfg.CountdownDate ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d);
        }

        protected override void PaintContent(Graphics g)
        {
            Rectangle r = Inner;
            Title(g, "Countdown", new Rectangle(r.X, r.Y, r.Width, T.S(20)));
            DateTime target;
            if (!TryTarget(out target) || string.IsNullOrEmpty(Cfg.CountdownTitle))
            {
                Txt(g, "Double-click to count down to a birthday, a trip or the holidays", Gfx.F(T.FontName, T.Sf(13.5f), FontStyle.Regular), T.SubText, new RectangleF(r.X, r.Y + T.Sf(30), r.Width, T.Sf(50)), Gfx.TopLeftWrap);
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
            Title(g, "Quote of the day", new Rectangle(r.X, r.Y, r.Width, T.S(20)));
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
