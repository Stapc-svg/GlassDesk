// GlassDesk 1.6 - our own blur: a blurred copy of your wallpaper, painted behind each panel.
// Works on every Windows version, doesn't depend on Windows' blur features, and costs almost nothing
// because the blurred picture is made once and reused until the wallpaper changes.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace GlassDesk
{
    internal static class Backdrop
    {
        static readonly Dictionary<string, Bitmap> cache = new Dictionary<string, Bitmap>();
        static string sourceKey;                 // which wallpaper file + date the cache was made from
        public static string PreviewWallpaper;   // used only to make screenshots

        /// <summary>The blurred wallpaper for one monitor, sized to that monitor. strength: 1 = light, 2 = frosted.</summary>
        public static Bitmap For(Rectangle screen, int strength)
        {
            string src = SourceKey();
            if (src != sourceKey) Clear();
            sourceKey = src;
            string key = screen.ToString() + "|" + strength;
            Bitmap b;
            if (cache.TryGetValue(key, out b)) return b;
            b = Build(screen.Size, strength);
            cache[key] = b;
            return b;
        }

        public static void Clear()
        {
            foreach (var b in cache.Values) if (b != null) b.Dispose();
            cache.Clear();
        }

        /// <summary>True when the wallpaper changed since we last blurred it.</summary>
        public static bool Changed() { return SourceKey() != sourceKey; }

        static string WallpaperFile()
        {
            if (!string.IsNullOrEmpty(PreviewWallpaper)) return PreviewWallpaper;
            try
            {
                // Windows keeps the picture it is actually showing here (also for slideshows and Spotlight)
                string t = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes\TranscodedWallpaper");
                if (File.Exists(t)) return t;
            }
            catch { }
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
                {
                    string p = k == null ? null : k.GetValue("WallPaper") as string;
                    if (!string.IsNullOrEmpty(p) && File.Exists(p)) return p;
                }
            }
            catch { }
            return null;
        }

        static string SourceKey()
        {
            string f = WallpaperFile();
            try { return f == null ? "color" : f + "|" + File.GetLastWriteTimeUtc(f).Ticks + "|" + Style(); } catch { return "color"; }
        }

        static string Style()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
                    return (k == null ? "10" : (k.GetValue("WallpaperStyle") as string ?? "10")) + "/" + (k == null ? "0" : (k.GetValue("TileWallpaper") as string ?? "0"));
            }
            catch { return "10/0"; }
        }

        static Color DesktopColor()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Colors"))
                {
                    string[] p = ((k == null ? null : k.GetValue("Background") as string) ?? "0 0 0").Split(' ');
                    return Color.FromArgb(int.Parse(p[0]), int.Parse(p[1]), int.Parse(p[2]));
                }
            }
            catch { return Color.FromArgb(20, 24, 32); }
        }

        static Bitmap Build(Size size, int strength)
        {
            // work on a small copy: blurring 1/10 of the pixels is ~100x cheaper and looks the same once scaled up
            int div = strength >= 2 ? 10 : 6;
            int sw = Math.Max(8, size.Width / div), sh = Math.Max(8, size.Height / div);
            var small = new Bitmap(sw, sh, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(small))
            {
                g.Clear(DesktopColor());
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                string f = WallpaperFile();
                if (f != null)
                {
                    try
                    {
                        using (var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (var img = Image.FromStream(fs, false, false))
                        {
                            string st = Style().Split('/')[0];
                            float kx = (float)sw / img.Width, ky = (float)sh / img.Height;
                            float k = st == "6" ? Math.Min(kx, ky) : Math.Max(kx, ky);          // 6 = fit, else fill
                            if (st == "2") g.DrawImage(img, 0, 0, sw, sh);                      // 2 = stretch
                            else { float dw = img.Width * k, dh = img.Height * k; g.DrawImage(img, (sw - dw) / 2, (sh - dh) / 2, dw, dh); }
                        }
                    }
                    catch { }
                }
            }
            BoxBlur(small, strength >= 2 ? 4 : 2, 3);
            // scale back up smoothly to the monitor's size
            var big = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(big))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                using (var ia = new ImageAttributes()) { ia.SetWrapMode(WrapMode.TileFlipXY); g.DrawImage(small, new Rectangle(0, 0, size.Width, size.Height), 0, 0, sw, sh, GraphicsUnit.Pixel, ia); }
            }
            small.Dispose();
            return big;
        }

        /// <summary>Repeated box blur (3 passes looks like a gaussian blur).</summary>
        static void BoxBlur(Bitmap b, int radius, int passes)
        {
            var rect = new Rectangle(0, 0, b.Width, b.Height);
            var data = b.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            int w = b.Width, h = b.Height, stride = data.Stride;
            var px = new byte[stride * h];
            Marshal.Copy(data.Scan0, px, 0, px.Length);
            var tmp = new byte[px.Length];
            for (int p = 0; p < passes; p++)
            {
                Pass(px, tmp, w, h, stride, radius, true);
                Pass(tmp, px, w, h, stride, radius, false);
            }
            Marshal.Copy(px, 0, data.Scan0, px.Length);
            b.UnlockBits(data);
        }

        static void Pass(byte[] src, byte[] dst, int w, int h, int stride, int r, bool horizontal)
        {
            int n = horizontal ? h : w, len = horizontal ? w : h, win = r * 2 + 1;
            for (int line = 0; line < n; line++)
            {
                for (int c = 0; c < 4; c++)
                {
                    int sum = 0;
                    for (int i = -r; i <= r; i++) sum += src[Idx(line, Clamp(i, len), c, horizontal, stride)];
                    for (int i = 0; i < len; i++)
                    {
                        dst[Idx(line, i, c, horizontal, stride)] = (byte)(sum / win);
                        sum += src[Idx(line, Clamp(i + r + 1, len), c, horizontal, stride)] - src[Idx(line, Clamp(i - r, len), c, horizontal, stride)];
                    }
                }
            }
        }
        static int Clamp(int i, int len) { return i < 0 ? 0 : i >= len ? len - 1 : i; }
        static int Idx(int line, int i, int c, bool horizontal, int stride) { return horizontal ? line * stride + i * 4 + c : i * stride + line * 4 + c; }
    }
}
