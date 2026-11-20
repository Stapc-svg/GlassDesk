// GlassDesk - background pictures for widgets: drawn weather skies, and the "half picture, half glass" fade.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace GlassDesk
{
    internal static class SceneArt
    {
        static readonly Dictionary<string, Bitmap> cache = new Dictionary<string, Bitmap>();

        static Bitmap Cached(string key, Func<Bitmap> make)
        {
            Bitmap b;
            if (cache.TryGetValue(key, out b)) return b;
            if (cache.Count > 24) { foreach (var v in cache.Values) if (v != null) v.Dispose(); cache.Clear(); }
            try { b = make(); } catch (Exception ex) { Program.Log(ex); b = null; }
            cache[key] = b;
            return b;
        }

        /// <summary>The picture covers the top of the widget and fades smoothly into the glass:
        /// fully visible down to <paramref name="from"/> (0..1 of the height), gone by <paramref name="to"/>.</summary>
        public static void DrawFaded(Graphics g, RectangleF area, float radius, string key, Func<int, int, Bitmap> paint, float from, float to)
        {
            int w = Math.Max(1, (int)area.Width), h = Math.Max(1, (int)area.Height);
            var img = Cached(key + "|" + w + "x" + h + "|" + from + "|" + to, delegate
            {
                var b = paint(w, h);
                if (b == null) return null;
                ApplyFade(b, from, to);
                return b;
            });
            if (img == null) return;
            var st = g.Save();
            using (var clip = Gfx.Round(area, radius))
            {
                g.SetClip(clip, CombineMode.Intersect);
                g.DrawImage(img, area.X, area.Y, area.Width, area.Height);
            }
            g.Restore(st);
        }

        /// <summary>Cover-crops a photo to w x h (keeps the middle).</summary>
        public static Bitmap Cover(Bitmap src, int w, int h)
        {
            var b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            {
                float sc = Math.Max((float)w / src.Width, (float)h / src.Height);
                float sw = w / sc, sh = h / sc;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(src, new RectangleF(0, 0, w, h), new RectangleF((src.Width - sw) / 2, (src.Height - sh) / 2, sw, sh), GraphicsUnit.Pixel);
            }
            return b;
        }

        /// <summary>Multiplies each row's alpha: 1 above <paramref name="from"/>, eased down to 0 at <paramref name="to"/>.</summary>
        static void ApplyFade(Bitmap b, float from, float to)
        {
            var rect = new Rectangle(0, 0, b.Width, b.Height);
            var data = b.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride, bytes = stride * b.Height;
                var px = new byte[bytes];
                Marshal.Copy(data.Scan0, px, 0, bytes);
                for (int y = 0; y < b.Height; y++)
                {
                    float f = (float)y / Math.Max(1, b.Height - 1);
                    float a = f <= from ? 1f : f >= to ? 0f : 1f - (f - from) / (to - from);
                    a = a * a * (3 - 2 * a);   // smoothstep: soft at both ends
                    if (a >= 0.999f) continue;
                    int row = y * stride;
                    for (int x = 0; x < b.Width; x++)
                    {
                        int i = row + x * 4 + 3;
                        px[i] = (byte)(px[i] * a);
                    }
                }
                Marshal.Copy(px, 0, data.Scan0, bytes);
            }
            finally { b.UnlockBits(data); }
        }

        // ------------------------------------------------------------ drawn weather skies

        public static string Kind(int code, bool day)
        {
            if (code >= 95) return "storm";
            if ((code >= 71 && code <= 77) || code == 85 || code == 86) return "snow";
            if ((code >= 51 && code <= 67) || (code >= 80 && code <= 82)) return "rain";
            if (code == 45 || code == 48) return "fog";
            if (code == 3) return day ? "overcast" : "overcast-night";
            if (code == 1 || code == 2) return day ? "partly" : "partly-night";
            return day ? "clear" : "clear-night";
        }

        public static Bitmap Weather(string kind, int w, int h)
        {
            var b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                bool night = kind.EndsWith("night");
                Color top, bot;
                switch (kind)
                {
                    case "clear": top = Color.FromArgb(38, 120, 230); bot = Color.FromArgb(140, 200, 255); break;
                    case "partly": top = Color.FromArgb(52, 128, 220); bot = Color.FromArgb(160, 200, 240); break;
                    case "overcast": top = Color.FromArgb(96, 110, 132); bot = Color.FromArgb(160, 170, 186); break;
                    case "rain": top = Color.FromArgb(52, 62, 84); bot = Color.FromArgb(98, 110, 132); break;
                    case "storm": top = Color.FromArgb(30, 26, 52); bot = Color.FromArgb(70, 62, 100); break;
                    case "snow": top = Color.FromArgb(140, 160, 190); bot = Color.FromArgb(214, 224, 238); break;
                    case "fog": top = Color.FromArgb(130, 138, 150); bot = Color.FromArgb(190, 196, 204); break;
                    default: top = Color.FromArgb(10, 16, 46); bot = Color.FromArgb(36, 48, 96); break;   // nights
                }
                using (var lg = new LinearGradientBrush(new Point(0, -1), new Point(0, h + 1), top, bot)) g.FillRectangle(lg, 0, 0, w, h);
                var rnd = new Random(kind.GetHashCode() & 0xffff);
                float u = Math.Min(w, h * 2f) / 330f;   // scale with the widget

                if (night || kind == "clear-night")
                {
                    for (int i = 0; i < 70; i++)
                    {
                        float s = (0.8f + (float)rnd.NextDouble() * 1.8f) * u;
                        using (var br = new SolidBrush(Color.FromArgb(80 + rnd.Next(170), 255, 255, 255))) g.FillEllipse(br, (float)rnd.NextDouble() * w, (float)rnd.NextDouble() * h * 0.7f, s, s);
                    }
                    var mc = new PointF(w * 0.8f, h * 0.24f); float mr = 16 * u;
                    Gfx.Glow(g, mc, mr * 4, Color.FromArgb(70, 190, 205, 255));
                    // crescent: the moon disc minus a slightly shifted disc
                    using (var moon = new GraphicsPath())
                    using (var cut = new GraphicsPath())
                    using (var rg = new Region())
                    {
                        moon.AddEllipse(mc.X - mr, mc.Y - mr, mr * 2, mr * 2);
                        cut.AddEllipse(mc.X - mr + mr * 0.62f, mc.Y - mr - mr * 0.3f, mr * 2, mr * 2);
                        rg.MakeEmpty(); rg.Union(moon); rg.Exclude(cut);
                        using (var br = new SolidBrush(Color.FromArgb(255, 238, 242, 255))) g.FillRegion(br, rg);
                    }
                }
                if (kind == "clear" || kind == "partly")
                {
                    var sc = new PointF(w * 0.8f, h * 0.22f);
                    Gfx.Glow(g, sc, 90 * u, Color.FromArgb(150, 255, 230, 160));
                    Gfx.Glow(g, sc, 34 * u, Color.FromArgb(220, 255, 246, 210));
                    // a few light rays
                    for (int i = 0; i < 8; i++)
                    {
                        double a = i * Math.PI / 4 + 0.2;
                        using (var pen = new Pen(Color.FromArgb(28, 255, 250, 220), 10 * u) { EndCap = LineCap.Round })
                            g.DrawLine(pen, sc, new PointF(sc.X + (float)Math.Cos(a) * 140 * u, sc.Y + (float)Math.Sin(a) * 140 * u));
                    }
                }
                // clouds
                int clouds = kind == "clear" || kind == "clear-night" ? 2 : kind.StartsWith("partly") ? 4 : kind == "fog" ? 0 : 7;
                Color cc = kind == "storm" ? Color.FromArgb(70, 64, 96) : kind == "rain" ? Color.FromArgb(112, 122, 142) : kind.StartsWith("overcast") ? Color.FromArgb(190, 196, 208) : night ? Color.FromArgb(70, 82, 120) : Color.FromArgb(250, 252, 255);
                int ca = kind == "clear" || kind == "clear-night" ? 120 : 220;
                for (int i = 0; i < clouds; i++)
                {
                    float cx = (float)(rnd.NextDouble() * 1.1 - 0.05) * w, cy = (float)(0.08 + rnd.NextDouble() * 0.42) * h;
                    Cloud(g, cx, cy, (46 + (float)rnd.NextDouble() * 50) * u, Color.FromArgb(ca, cc), rnd);
                }
                if (kind == "rain" || kind == "storm")
                    for (int i = 0; i < 90; i++)
                    {
                        float x = (float)rnd.NextDouble() * (w + 40) - 20, y = (float)rnd.NextDouble() * h, l = (10 + (float)rnd.NextDouble() * 14) * u;
                        using (var pen = new Pen(Color.FromArgb(70 + rnd.Next(90), 200, 220, 255), 1.3f * u)) g.DrawLine(pen, x, y, x - l * 0.28f, y + l);
                    }
                if (kind == "storm")
                {
                    // a lightning bolt in the top-right corner, away from the text
                    float bx = w * 0.93f, by = h * 0.12f, bs = 0.12f * h;
                    var p = new[] { new PointF(bx, by), new PointF(bx - bs * 0.3f, by + bs * 0.9f), new PointF(bx - bs * 0.08f, by + bs * 0.9f), new PointF(bx - bs * 0.32f, by + bs * 1.8f), new PointF(bx + bs * 0.22f, by + bs * 0.7f), new PointF(bx, by + bs * 0.7f), new PointF(bx + bs * 0.2f, by) };
                    Gfx.Glow(g, new PointF(bx - bs * 0.05f, by + bs * 0.8f), 70 * u, Color.FromArgb(120, 200, 190, 255));
                    using (var br = new SolidBrush(Color.FromArgb(150, 255, 240, 170))) g.FillPolygon(br, p);
                }
                if (kind == "snow")
                    for (int i = 0; i < 80; i++)
                    {
                        float s = (1.5f + (float)rnd.NextDouble() * 3.2f) * u;
                        using (var br = new SolidBrush(Color.FromArgb(150 + rnd.Next(105), 255, 255, 255))) g.FillEllipse(br, (float)rnd.NextDouble() * w, (float)rnd.NextDouble() * h, s, s);
                    }
                if (kind == "fog")
                    for (int i = 0; i < 6; i++)
                    {
                        float y = h * (0.12f + i * 0.14f);
                        using (var lg = new LinearGradientBrush(new PointF(0, y), new PointF(0, y + 24 * u), Color.FromArgb(0, 255, 255, 255), Color.FromArgb(0, 255, 255, 255)))
                        {
                            lg.InterpolationColors = new ColorBlend { Colors = new[] { Color.FromArgb(0, 255, 255, 255), Color.FromArgb(110, 240, 242, 246), Color.FromArgb(0, 255, 255, 255) }, Positions = new[] { 0f, 0.5f, 1f } };
                            g.FillRectangle(lg, 0, y, w, 24 * u);
                        }
                    }
            }
            return b;
        }

        /// <summary>A soft, fluffy cloud made of overlapping round puffs with feathered edges.</summary>
        static void Cloud(Graphics g, float x, float y, float size, Color c, Random rnd)
        {
            int puffs = 6;
            for (int i = 0; i < puffs; i++)
            {
                float px = x + (float)(rnd.NextDouble() - 0.5) * size * 1.4f, py = y + (float)(rnd.NextDouble() - 0.5) * size * 0.35f;
                float r = size * (0.35f + (float)rnd.NextDouble() * 0.3f);
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(px - r, py - r * 0.8f, r * 2, r * 1.6f);
                    using (var pg = new PathGradientBrush(path))
                    {
                        pg.CenterColor = c;
                        pg.SurroundColors = new[] { Color.FromArgb(0, c) };
                        pg.FocusScales = new PointF(0.45f, 0.45f);
                        g.FillPath(pg, path);
                    }
                }
            }
        }
    }
}
