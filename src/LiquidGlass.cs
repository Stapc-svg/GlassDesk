// GlassDesk - "liquid glass": a thick, rounded piece of glass. Near its edges the glass bends light like a lens,
// so what's just outside the panel shows up squeezed and curved inside the rim. The glass is a little frosted
// (more at the rim, where it is thickest), colors split slightly at the edge like a prism, and light glints
// on the curved corners. Made once per position and size and then reused, so it costs nothing per frame.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace GlassDesk
{
    internal sealed class LiquidGlass : IDisposable
    {
        public Bitmap Glass;      // the bent wallpaper, cut to the rounded shape
        public Bitmap Light;      // light on the curved rim (drawn on top of any tint)
        public float Brightness;  // how bright the wallpaper behind is (0..1), so text can stay readable
        string key;

        /// <summary>Makes (or reuses) the glass for a panel.
        /// middle / rim = the wallpaper as seen through the flat middle and through the thick rim (the rim one is blurrier);
        /// src = where the panel sits on the wallpaper; frost = how much of the rim picture also shows in the middle (0..1).</summary>
        public void Update(Bitmap middle, Bitmap rim, Rectangle src, int w, int h, float radius, float scale, bool dark, float frost)
        {
            string k = src + "|" + w + "x" + h + "|" + radius + "|" + dark + "|" + frost + "|" + middle.GetHashCode() + "|" + rim.GetHashCode();
            if (k == key && Glass != null) return;
            Dispose();
            key = k;
            Build(Pixels.Of(middle), Pixels.Of(rim), src, w, h, radius, scale, dark, frost);
        }

        public void Dispose()
        {
            if (Glass != null) { Glass.Dispose(); Glass = null; }
            if (Light != null) { Light.Dispose(); Light = null; }
            key = null;
        }

        /// <summary>A wallpaper picture's pixels, copied out once and kept while that picture is in use.</summary>
        sealed class Pixels
        {
            public int[] Px; public int W, H; Bitmap from;
            static readonly List<Pixels> cache = new List<Pixels>();
            public static Pixels Of(Bitmap b)
            {
                foreach (var p in cache) if (ReferenceEquals(p.from, b)) return p;
                var n = new Pixels { from = b, W = b.Width, H = b.Height };
                var data = b.LockBits(new Rectangle(0, 0, n.W, n.H), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                n.Px = new int[n.W * n.H];
                for (int y = 0; y < n.H; y++) Marshal.Copy(data.Scan0 + y * data.Stride, n.Px, y * n.W, n.W);
                b.UnlockBits(data);
                cache.Add(n);
                if (cache.Count > 5) cache.RemoveAt(0);   // an old picture
                return n;
            }
            // bilinear sample of one color channel (shift 16 = red, 8 = green, 0 = blue)
            public float Sample(float x, float y, int shift)
            {
                if (x < 0) x = 0; else if (x > W - 1.001f) x = W - 1.001f;
                if (y < 0) y = 0; else if (y > H - 1.001f) y = H - 1.001f;
                int x0 = (int)x, y0 = (int)y; float fx = x - x0, fy = y - y0;
                int i = y0 * W + x0;
                float a = (Px[i] >> shift) & 255, b = (Px[i + 1] >> shift) & 255;
                float c = (Px[i + W] >> shift) & 255, d = (Px[i + W + 1] >> shift) & 255;
                return (a + (b - a) * fx) * (1 - fy) + (c + (d - c) * fx) * fy;
            }
        }

        /// <summary>Signed distance to a rounded rectangle centered at 0 (negative inside) and the outward direction.</summary>
        static float Sdf(float px, float py, float hw, float hh, float r, out float nx, out float ny)
        {
            float qx = Math.Abs(px) - (hw - r), qy = Math.Abs(py) - (hh - r);
            float ox = Math.Max(qx, 0), oy = Math.Max(qy, 0);
            float d = (float)Math.Sqrt(ox * ox + oy * oy) + Math.Min(Math.Max(qx, qy), 0) - r;
            if (qx > 0 && qy > 0) { float l = (float)Math.Sqrt(qx * qx + qy * qy); nx = qx / l; ny = qy / l; }
            else if (qx > qy) { nx = 1; ny = 0; } else { nx = 0; ny = 1; }
            if (px < 0) nx = -nx; if (py < 0) ny = -ny;
            return d;
        }

        void Build(Pixels mid, Pixels rimPic, Rectangle src, int w, int h, float radius, float scale, bool dark, float frost)
        {
            int n = w * h;
            var glass = new int[n];
            var light = new int[n];
            float hw = w / 2f, hh = h / 2f, r = Math.Min(radius, Math.Min(hw, hh));
            // the rim: a rounded edge about a fifth of the panel deep
            float bevel = Math.Max(14f * scale, Math.Min(48f * scale, Math.Min(w, h) * 0.22f));
            bevel = Math.Min(bevel, Math.Min(hw, hh) * 0.9f);
            // the rim is measured on a shape with rounder corners than the panel, so the bending flows around
            // the corners in a curve instead of meeting in a sharp diagonal seam
            float rb = Math.Min(Math.Min(hw, hh), Math.Max(r, bevel * 1.1f));
            float maxBend = bevel * 1.5f;

            // 1. how far (and which way) each pixel of the glass looks sideways
            var dx = new float[n]; var dy = new float[n]; var edge = new float[n];
            for (int y = 0; y < h; y++)
            {
                float py = y + 0.5f - hh;
                for (int x = 0; x < w; x++)
                {
                    float px = x + 0.5f - hw, nx, ny;
                    float depth = -Sdf(px, py, hw, hh, rb, out nx, out ny);
                    float t = Math.Max(0f, Math.Min(1f, depth / bevel)), e = 1f - t;
                    // quarter-circle rim: flat in the middle, steeper and steeper toward the edge
                    float hgt = (float)Math.Sqrt(Math.Max(0.0004f, 1f - e * e));
                    float m = Math.Min(maxBend, e / hgt * bevel * 0.42f);
                    // lean the bend toward the corners a little, so straight lines in the wallpaper curve
                    // through the glass the way they do through a real drop, instead of staying in straight rows
                    float rx = px / hw, ry = py / hh, rl = (float)Math.Sqrt(rx * rx + ry * ry);
                    if (rl > 0.0001f) { rx /= rl; ry /= rl; }
                    float vx = nx * 0.62f + rx * 0.38f, vy = ny * 0.62f + ry * 0.38f, vl = (float)Math.Sqrt(vx * vx + vy * vy);
                    if (vl > 0.0001f) { vx /= vl; vy /= vl; }
                    int i = y * w + x;
                    dx[i] = vx * m; dy[i] = vy * m; edge[i] = e;
                }
            }
            // 2. smooth the bending, like the soft surface of a liquid (no hard creases anywhere)
            int sr = Math.Max(1, (int)Math.Round(bevel * 0.2f));
            Smooth(dx, w, h, sr); Smooth(dy, w, h, sr); Smooth(edge, w, h, sr);

            // 3. look through the glass
            float cx = src.X + hw, cy = src.Y + hh;
            double lum = 0; int lumN = 0;
            for (int y = 0; y < h; y++)
            {
                float py = y + 0.5f - hh;
                for (int x = 0; x < w; x++)
                {
                    float px = x + 0.5f - hw, nx, ny;
                    float d = Sdf(px, py, hw, hh, r, out nx, out ny);
                    if (d >= 0.5f) continue;
                    float cover = Math.Min(1f, 0.5f - d);
                    int i = y * w + x;
                    float e = edge[i], mx = dx[i], my = dy[i];
                    // the middle magnifies a touch, like thick glass
                    float sx = cx + px * 0.97f, sy = cy + py * 0.97f;
                    // frosted: more where the glass is thick (the rim), less in the middle
                    float f = Math.Min(1f, frost + (1f - frost) * e * e * 1.2f);
                    float R = Mix(mid, rimPic, sx + mx * 0.93f, sy + my * 0.93f, 16, f);   // red bends least,
                    float G = Mix(mid, rimPic, sx + mx, sy + my, 8, f);
                    float B = Mix(mid, rimPic, sx + mx * 1.08f, sy + my * 1.08f, 0, f);    // blue the most: a thin rainbow at the rim
                    // richer colors through the glass, and a little light gathered in the thick rim
                    float avg = (R + G + B) / 3f;
                    R = avg + (R - avg) * 1.16f; G = avg + (G - avg) * 1.16f; B = avg + (B - avg) * 1.16f;
                    float lift = dark ? 2f : 8f;   // no extra brightening at the rim: it looked like a white area
                    R += (255 - R) * lift / 255f; G += (255 - G) * lift / 255f; B += (255 - B) * lift / 255f;
                    glass[i] = ((int)(cover * 255) << 24) | (Cl(R) << 16) | (Cl(G) << 8) | Cl(B);
                    if (e <= 0.01f) { lum += (0.3f * R + 0.59f * G + 0.11f * B) / 255f; lumN++; }

                    // a thin glint only where the curved rim faces the light (top-left and bottom-right corners)
                    float rnx, rny;
                    float rd = -Sdf(px, py, hw, hh, rb, out rnx, out rny);
                    float re = 1f - Math.Max(0f, Math.Min(1f, rd / bevel));
                    float diag = Math.Abs(rnx * -0.707f + rny * -0.707f);
                    float spec = 0f;   // the corner glint read as a white patch, so it is off
                    int la = (int)(Math.Min(1f, spec) * cover * 255);
                    if (la > 2) light[i] = (la << 24) | 0xFFFFFF;
                }
            }
            Brightness = lumN > 0 ? (float)(lum / lumN) : 0.5f;
            Glass = ToBitmap(glass, w, h);
            Light = ToBitmap(light, w, h);
        }

        static float Mix(Pixels a, Pixels b, float x, float y, int shift, float f)
        {
            float va = f >= 0.999f ? 0 : a.Sample(x, y, shift);
            float vb = f <= 0.001f ? 0 : b.Sample(x, y, shift);
            return va + (vb - va) * f;
        }

        /// <summary>Two-pass box blur of a float field (horizontal then vertical), done twice for a soft result.</summary>
        static void Smooth(float[] v, int w, int h, int r)
        {
            var tmp = new float[v.Length];
            for (int pass = 0; pass < 2; pass++)
            {
                for (int y = 0; y < h; y++)
                {
                    int row = y * w; float sum = 0; int cnt = 0;
                    for (int x = -r; x <= r; x++) if (x >= 0 && x < w) { sum += v[row + x]; cnt++; }
                    for (int x = 0; x < w; x++)
                    {
                        tmp[row + x] = sum / cnt;
                        int add = x + r + 1, rem = x - r;
                        if (add < w) { sum += v[row + add]; cnt++; }
                        if (rem >= 0) { sum -= v[row + rem]; cnt--; }
                    }
                }
                for (int x = 0; x < w; x++)
                {
                    float sum = 0; int cnt = 0;
                    for (int y = -r; y <= r; y++) if (y >= 0 && y < h) { sum += tmp[y * w + x]; cnt++; }
                    for (int y = 0; y < h; y++)
                    {
                        v[y * w + x] = sum / cnt;
                        int add = y + r + 1, rem = y - r;
                        if (add < h) { sum += tmp[add * w + x]; cnt++; }
                        if (rem >= 0) { sum -= tmp[rem * w + x]; cnt--; }
                    }
                }
            }
        }

        static int Cl(float v) { return v < 0 ? 0 : v > 255 ? 255 : (int)v; }

        static Bitmap ToBitmap(int[] px, int w, int h)
        {
            var b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            var data = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < h; y++) Marshal.Copy(px, y * w, data.Scan0 + y * data.Stride, w);
            b.UnlockBits(data);
            return b;
        }
    }
}
