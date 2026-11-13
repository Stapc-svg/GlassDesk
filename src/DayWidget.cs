// GlassDesk 1.8.1 - "Today": four rings for how much of the day, week, month and year has gone by.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace GlassDesk
{
    internal class DayWidget : Widget
    {
        System.Windows.Forms.Timer timer;
        public DayWidget(App app) : base(app, "day") { }
        protected override Size BaseSize { get { return new Size(340, 150); } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            timer = new System.Windows.Forms.Timer { Interval = 60000 };
            timer.Tick += delegate { if (Visible) Redraw(); };
            timer.Start();
        }
        protected override void Dispose(bool disposing) { if (disposing && timer != null) timer.Dispose(); base.Dispose(disposing); }

        protected override void PaintContent(Graphics g)
        {
            Rectangle r = Inner;
            DateTime now = App.Now;
            Title(g, "Today", new Rectangle(r.X, r.Y, r.Width, T.S(20)));
            Txt(g, now.ToString("dddd", CultureInfo.CurrentCulture), Gfx.F(T.FontName, T.Sf(11.5f), FontStyle.Regular), T.SubText, new RectangleF(r.X, r.Y, r.Width, T.Sf(20)), Gfx.Right);

            int dow = ((int)now.DayOfWeek + 6) % 7;   // Monday first
            double day = now.TimeOfDay.TotalHours / 24;
            double week = (dow + day) / 7;
            double month = (now.Day - 1 + day) / DateTime.DaysInMonth(now.Year, now.Month);
            double year = (now.DayOfYear - 1 + day) / (DateTime.IsLeapYear(now.Year) ? 366 : 365);
            double[] v = { day, week, month, year };
            string[] names = { "Day", "Week", "Month", "Year" };

            float cw = r.Width / 4f, d = Math.Min(T.Sf(58), cw - T.Sf(14));
            float top = r.Y + T.Sf(30);
            for (int i = 0; i < 4; i++)
            {
                float cx = r.X + cw * i + cw / 2;
                var ring = new RectangleF(cx - d / 2, top, d, d);
                float pw = T.Sf(6);
                var inner = RectangleF.Inflate(ring, -pw / 2, -pw / 2);
                using (var pen = new Pen(T.Track, pw)) g.DrawEllipse(pen, inner);
                float sweep = (float)(360 * Math.Max(0, Math.Min(1, v[i])));
                if (sweep > 1)
                {
                    // each ring a little further around the accent's color wheel
                    Color a = Shift(T.Accent, i * 22), b = Shift(T.Accent, i * 22 + 40);
                    using (var lg = new LinearGradientBrush(ring, a, b, 45f))
                    using (var pen = new Pen(lg, pw) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        g.DrawArc(pen, inner, -90, sweep);
                }
                Txt(g, ((int)Math.Floor(v[i] * 100)) + "%", Gfx.F(T.FontName, T.Sf(13), FontStyle.Bold), T.Text, ring, Gfx.Center);
                Txt(g, names[i], Gfx.F(T.FontName, T.Sf(11.5f), FontStyle.Regular), T.SubText, new RectangleF(cx - cw / 2, ring.Bottom + T.Sf(4), cw, T.Sf(18)), Gfx.Center);
            }
        }

        /// <summary>The same color with its hue turned by some degrees.</summary>
        static Color Shift(Color c, float deg)
        {
            float h = (c.GetHue() + deg) % 360, s = c.GetSaturation(), l = c.GetBrightness();
            float q = (1 - Math.Abs(2 * l - 1)) * s, x = q * (1 - Math.Abs(h / 60 % 2 - 1)), m = l - q / 2;
            float r = 0, g = 0, b = 0;
            if (h < 60) { r = q; g = x; } else if (h < 120) { r = x; g = q; } else if (h < 180) { g = q; b = x; }
            else if (h < 240) { g = x; b = q; } else if (h < 300) { r = x; b = q; } else { r = q; b = x; }
            return Color.FromArgb(255, (int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
        }
    }
}
