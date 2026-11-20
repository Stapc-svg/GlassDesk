// GlassDesk - "liquid glass": a clear, thick piece of glass. The wallpaper behind it is bent around
// the rounded edges like through a lens, the colors split a little at the rim (like a prism), and light
// catches the edges. Made once per position and size and then reused, so it costs nothing per frame.
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace GlassDesk
{
    internal sealed class LiquidGlass : IDisposable
    {
        public Bitmap Glass;      // the bent wallpaper, cut to the rounded shape
        public Bitmap Light;      // white light on the rim (drawn on top of any tint)
        public float Brightness;  // how bright the wallpaper behind is (0..1), so text can stay readable
        string key;

        /// <summary>Makes (or reuses) the glass for a panel. src = where the panel sits on the wallpaper picture.</summary>
        public void Update(Bitmap wall, Rectangle src, int w, int h, float radius, float scale, bool dark)
        {
            string k = src + "|" + w + "x" + h + "|" + radius + "|" + dark + "|" + wall.GetHashCode() + "|" + wall.Width;
            if (k == key && Glass != null) return;
            Dispose();
            key = k;
            Build(wall, src, w, h, radius, scale, dark);
        }

        public void Dispose()
        {
            if (Glass != null) { Glass.Dispose(); Glass = null; }
            if (Light != null) { Light.Dispose(); Light = null; }
            key = null;
        }

        // the wallpaper pixels, copied out once per wallpaper
        static Bitmap wallFor; static int[] wallPx; static int wallW, wallH;

        static void LoadWall(Bitmap wall)
        {
            if (ReferenceEquals(wall, wallFor) && wallPx != null) return;
            wallW = wall.Width; wallH = wall.Height;
            var data = wall.LockBits(new Rectangle(0, 0, wallW, wallH), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            wallPx = new int[wallW * wallH];
            for (int y = 0; y < wallH; y++) Marshal.Copy(data.Scan0 + y * data.Stride, wallPx, y * wallW, wallW);
            wall.UnlockBits(data);
            wallFor = wall;
        }

        // bilinear sample of one color channel (shift 16 = red, 8 = green, 0 = blue)
        static float Sample(float x, float y, int shift)
        {
            if (x < 0) x = 0; else if (x > wallW - 1.001f) x = wallW - 1.001f;
            if (y < 0) y = 0; else if (y > wallH - 1.001f) y = wallH - 1.001f;
            int x0 = (int)x, y0 = (int)y; float fx = x - x0, fy = y - y0;
            int i = y0 * wallW + x0;
            float a = (wallPx[i] >> shift) & 255, b = (wallPx[i + 1] >> shift) & 255;
            float c = (wallPx[i + wallW] >> shift) & 255, d = (wallPx[i + wallW + 1] >> shift) & 255;
            return (a + (b - a) * fx) * (1 - fy) + (c + (d - c) * fx) * fy;
        }

        void Build(Bitmap wall, Rectangle src, int w, int h, float radius, float scale, bool dark)
        {
            LoadWall(wall);
            var glass = new int[w * h];
            var light = new int[w * h];
            float hw = w / 2f, hh = h / 2f, r = Math.Min(radius, Math.Min(hw, hh));
            float bevel = Math.Min(Math.Min(hw, hh) * 0.85f, Math.Max(12f, 36f * scale));   // how wide the curved rim is
            float bend = bevel * 1.25f;                                                     // how far the rim bends the picture
            float lx = -0.55f, ly = -0.835f;                                                // light comes from the top left
            float cx = src.X + hw, cy = src.Y + hh;
            double lum = 0; int lumN = 0;
            for (int y = 0; y < h; y++)
            {
                float py = y + 0.5f - hh;
                for (int x = 0; x < w; x++)
                {
                    float px = x + 0.5f - hw;
                    // distance to the rounded edge (negative inside) and the direction out of the glass
                    float qx = Math.Abs(px) - (hw - r), qy = Math.Abs(py) - (hh - r);
                    float ox = Math.Max(qx, 0), oy = Math.Max(qy, 0);
                    float outside = (float)Math.Sqrt(ox * ox + oy * oy) + Math.Min(Math.Max(qx, qy), 0) - r;
                    float depth = -outside;
                    if (depth <= -0.5f) continue;
                    float cover = Math.Min(1f, depth + 0.5f);
                    float nx, ny;
                    if (qx > 0 && qy > 0) { float l = (float)Math.Sqrt(qx * qx + qy * qy); nx = qx / l; ny = qy / l; }
                    else if (qx > qy) { nx = 1; ny = 0; } else { nx = 0; ny = 1; }
                    if (px < 0) nx = -nx; if (py < 0) ny = -ny;

                    // the rim is a rounded bevel: flat in the middle, curving down to the edge
                    float t = Math.Max(0f, Math.Min(1f, depth / bevel)), e = 1f - t;
                    float m = bend * e * e * e * (2.2f - 1.2f * e);
                    // a hint of magnification over the whole pane, like thick glass
                    float sx = cx + px * 0.985f, sy = cy + py * 0.985f;
                    // pull the picture in from the edge; red, green and blue bend by slightly different amounts
                    float R = Sample(sx - nx * m * 0.90f, sy - ny * m * 0.90f, 16);
                    float G = Sample(sx - nx * m, sy - ny * m, 8);
                    float B = Sample(sx - nx * m * 1.14f, sy - ny * m * 1.14f, 0);
                    // the rim glows a little, and the whole pane is lifted very slightly
                    float lift = (dark ? 6f : 14f) + 26f * e * e;
                    R += (255 - R) * lift / 255f; G += (255 - G) * lift / 255f; B += (255 - B) * lift / 255f;
                    int A = (int)(cover * 255);
                    glass[y * w + x] = (A << 24) | (Cl(R) << 16) | (Cl(G) << 8) | Cl(B);
                    if (t >= 1) { lum += (0.3f * R + 0.59f * G + 0.11f * B) / 255f; lumN++; }

                    // light catching the rim: brightest facing the light, a softer glint on the opposite corner
                    float facing = nx * lx + ny * ly;
                    float rim = (float)Math.Pow(e, 4.5) * (0.22f + 0.78f * Math.Max(0, facing) + 0.5f * Math.Max(0, -facing));
                    float edge = Math.Max(0f, 1f - depth / (1.6f * scale));                       // a crisp hairline right on the edge
                    float glow = Math.Min(1f, rim * 0.7f + edge * (0.4f + 0.55f * Math.Abs(facing)));
                    int la = (int)(glow * cover * 255);
                    if (la > 0) light[y * w + x] = (la << 24) | 0xFFFFFF;
                }
            }
            Brightness = lumN > 0 ? (float)(lum / lumN) : 0.5f;
            Glass = ToBitmap(glass, w, h);
            Light = ToBitmap(light, w, h);
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
