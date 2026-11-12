package app.freedev.glassdesk;

import javax.imageio.ImageIO;
import java.awt.*;
import java.awt.font.TextLayout;
import java.awt.geom.AffineTransform;
import java.awt.geom.Ellipse2D;
import java.awt.geom.Point2D;
import java.awt.geom.RoundRectangle2D;
import java.awt.image.BufferedImage;
import java.awt.image.DataBufferByte;
import java.io.BufferedOutputStream;
import java.io.File;
import java.io.OutputStream;
import java.util.HashMap;
import java.util.Map;
import java.util.Random;

/**
 * The GlassDesk 1.7.2 launch film, drawn frame by frame with the real widget code.
 *   java Film <fonts> <assets dir> scene <k>      -> raw BGR frames on stdout (segment k of the timeline)
 *   java Film <fonts> <assets dir> still <t> <out.png>
 */
public final class Film {
    static int W = 1920, H = 1080; static final int FPS = 30;
    /** Vertical 9:16 version (1080x1920). */
    static boolean V;
    static final float[] DUR = {5, 6, 8, 7, 7, 8, 7, 6, 7};
    /** Scenes overlap by TR seconds; the overlap is drawn as a transition. */
    static final float TR = 0.8f;
    static final int[] KIND = {1, 0, 2, 0, 1, 2, 0, 1}; // 0 zoom-through, 1 light sweep, 2 slide up

    static float start(int k) { float s = 0; for (int j = 0; j < k; j++) s += DUR[j]; return s; }
    static float total() { return start(DUR.length); }

    static BufferedImage icon, wallDark, wallLight, vignette;
    static final BufferedImage[] phones = new BufferedImage[3];
    static final Map<String, BufferedImage> cache = new HashMap<>();
    static final Map<String, BufferedImage> scratch = new HashMap<>();

    // floating light particles
    static final int NP = 30;
    static final float[] PX = new float[NP], PY = new float[NP], PR = new float[NP], PS = new float[NP];
    static final int[] PC = new int[NP];
    static {
        Random r = new Random(3);
        int[] cols = {0xFF7C9CFF, 0xFFC77DFF, 0xFFFFFFFF, 0xFF7DE3F0, 0xFFFF8AB3};
        for (int i = 0; i < NP; i++) {
            PX[i] = r.nextFloat(); PY[i] = r.nextFloat(); PR[i] = 6 + r.nextFloat() * 34; PS[i] = 0.4f + r.nextFloat();
            PC[i] = cols[r.nextInt(cols.length)];
        }
    }

    Graphics2D g;
    J2DG jg;

    Film(Graphics2D g) { this.g = g; this.jg = new J2DG(g, Preview.fonts); }

    // ---------------------------------------------------------------- easing

    static float clamp(float v) { return Math.max(0, Math.min(1, v)); }
    static float seg(float t, float a, float b) { return clamp((t - a) / (b - a)); }
    static float out(float x) { x = clamp(x); return 1 - (1 - x) * (1 - x) * (1 - x); }
    static float inOut(float x) { x = clamp(x); return x < .5f ? 4 * x * x * x : 1 - (float) Math.pow(-2 * x + 2, 3) / 2; }
    static float back(float x) { x = clamp(x); float c1 = 1.6f, c3 = c1 + 1; return 1 + c3 * (float) Math.pow(x - 1, 3) + c1 * (float) Math.pow(x - 1, 2); }
    static float lerp(float a, float b, float t) { return a + (b - a) * t; }
    static float life(float t, float a, float len, float end, float len2) { return Math.min(seg(t, a, a + len), 1 - seg(t, end - len2, end)); }

    // ---------------------------------------------------------------- drawing helpers

    void alpha(float a) { g.setComposite(AlphaComposite.getInstance(AlphaComposite.SRC_OVER, clamp(a))); }
    void solid() { g.setComposite(AlphaComposite.SrcOver); }
    void black() { solid(); g.setColor(Color.BLACK); g.fillRect(0, 0, W, H); }

    Shape textShape(String s, float size, int weight, float track) {
        Font f = jg.font(size, weight, track);
        TextLayout tl = new TextLayout(s, f, g.getFontRenderContext());
        float w = (float) tl.getAdvance();
        return tl.getOutline(AffineTransform.getTranslateInstance(-w / 2, 0));
    }

    /** Title that rises and sharpens in. c2 != 0 gives a gradient; shine in (0,1) sweeps a glint of light across it. */
    void title(String s, float cx, float y, float size, int weight, int c1, int c2, float a, float rise, float track, float shine) {
        if (a <= 0.001f) return;
        AffineTransform keep = g.getTransform();
        float sc = 1.05f - 0.05f * a;
        g.translate(cx, y + (1 - a) * rise);
        g.scale(sc, sc);
        Shape sh = textShape(s, size, weight, track);
        Rectangle r = sh.getBounds();
        alpha(a);
        if (c2 != 0) g.setPaint(new GradientPaint(r.x, 0, new Color(c1, true), r.x + r.width, 0, new Color(c2, true)));
        else g.setPaint(new Color(c1, true));
        g.fill(sh);
        if (shine > 0 && shine < 1) {
            Shape clip = g.getClip();
            g.clip(sh);
            float bx = r.x - 200 + (r.width + 400) * shine;
            g.setPaint(new LinearGradientPaint(new Point2D.Float(bx - 90, r.y), new Point2D.Float(bx + 90, r.y + r.height * 0.6f),
                    new float[]{0f, .5f, 1f}, new Color[]{new Color(255, 255, 255, 0), new Color(255, 255, 255, 235), new Color(255, 255, 255, 0)}));
            g.fill(r);
            g.setClip(clip);
        }
        solid();
        g.setTransform(keep);
    }

    void title(String s, float cx, float y, float size, int weight, int color, float a, float rise, float track) {
        title(s, cx, y, size, weight, color, 0, a, rise, track, 0);
    }

    void image(BufferedImage im, float cx, float cy, float scale, float a) {
        if (a <= 0.001f || im == null) return;
        AffineTransform keep = g.getTransform();
        g.translate(cx, cy);
        g.scale(scale, scale);
        alpha(a);
        g.drawImage(im, -im.getWidth() / 2, -im.getHeight() / 2, null);
        solid();
        g.setTransform(keep);
    }

    void shadow(float cx, float cy, float w, float h, float a) {
        for (int k = 1; k <= 8; k++) {
            alpha(a * 0.035f);
            g.setColor(Color.BLACK);
            float e = k * 5;
            g.fill(new RoundRectangle2D.Float(cx - w / 2 - e, cy - h / 2 - e + 22, w + 2 * e, h + 2 * e, 90 + e, 90 + e));
        }
        solid();
    }

    /** A widget with a soft shadow, placed by its top-left corner. */
    void widget(BufferedImage im, float x, float y, float a, float rise, float scale) {
        if (a <= 0.001f) return;
        float cx = x + im.getWidth() / 2f, cy = y + im.getHeight() / 2f + (1 - a) * rise;
        shadow(cx, cy, im.getWidth() * scale, im.getHeight() * scale, a);
        image(im, cx, cy, scale, a);
    }

    /**
     * Draws an image turned around its vertical axis in real perspective (deg > 0: right side goes back).
     * Built from thin vertical strips, each scaled by its depth.
     */
    void tilt(BufferedImage im, float cx, float cy, float scale, float deg, float a, boolean withShadow) {
        if (a <= 0.001f) return;
        if (Math.abs(deg) < 0.05f) {
            if (withShadow) shadow(cx, cy, im.getWidth() * scale, im.getHeight() * scale, a);
            image(im, cx, cy, scale, a);
            return;
        }
        float w = im.getWidth(), h = im.getHeight();
        double th = Math.toRadians(deg);
        float cos = (float) Math.cos(th), sin = (float) Math.sin(th), f = w * 2.0f;
        if (withShadow) {
            float xl = (-w / 2) * cos * f / (f - w / 2 * sin), xr = (w / 2) * cos * f / (f + w / 2 * sin);
            shadow(cx + (xl + xr) / 2 * scale, cy, (xr - xl) * scale, h * scale, a);
        }
        int n = Math.max(32, (int) (w / 8));
        alpha(a);
        AffineTransform keep = g.getTransform();
        for (int i = 0; i < n; i++) {
            int sx0 = Math.round(w * i / n), sx1 = Math.round(w * (i + 1) / n);
            if (sx1 <= sx0) continue;
            float x0 = sx0 - w / 2, x1 = sx1 - w / 2;
            float p0 = f / (f + x0 * sin), p1 = f / (f + x1 * sin);
            float X0 = x0 * cos * p0, X1 = x1 * cos * p1;
            float pm = (p0 + p1) / 2, hh = h * pm;
            g.setTransform(keep);
            g.translate(cx + X0 * scale, cy - hh * scale / 2);
            g.scale((X1 - X0) * scale / (sx1 - sx0) + 0.004f, hh * scale / h);
            g.drawImage(im.getSubimage(sx0, 0, sx1 - sx0, (int) h), 0, 0, null);
        }
        g.setTransform(keep);
        solid();
    }

    /** Returns the image with a glossy reflection band passing over it (pos 0..1). */
    static BufferedImage sheen(BufferedImage im, float pos, float strength) {
        if (pos <= 0 || pos >= 1) return im;
        String key = im.getWidth() + "x" + im.getHeight() + "_" + System.identityHashCode(im) % 7;
        BufferedImage t = scratch.get(key);
        if (t == null) { t = new BufferedImage(im.getWidth(), im.getHeight(), BufferedImage.TYPE_INT_ARGB); scratch.put(key, t); }
        Graphics2D tg = t.createGraphics();
        tg.setComposite(AlphaComposite.Src);
        tg.drawImage(im, 0, 0, null);
        tg.setComposite(AlphaComposite.SrcAtop);
        float w = im.getWidth(), h = im.getHeight();
        float bx = -260 + (w + 520) * pos;
        int a = Math.round(255 * 0.42f * strength);
        tg.setPaint(new LinearGradientPaint(new Point2D.Float(bx - 130, 0), new Point2D.Float(bx + 130, h * 0.45f),
                new float[]{0f, .5f, 1f}, new Color[]{new Color(255, 255, 255, 0), new Color(255, 255, 255, a), new Color(255, 255, 255, 0)}));
        tg.fillRect(0, 0, (int) w, (int) h);
        tg.dispose();
        return t;
    }

    void bokeh(float t, float k) {
        if (k <= 0.01f) return;
        for (int i = 0; i < NP; i++) {
            float x = PX[i] * W + (float) Math.sin(t * 0.35f + i) * 40;
            float y = ((PY[i] * (H + 200) - t * PS[i] * 45) % (H + 200) + H + 200) % (H + 200) - 100;
            float tw = 0.6f + 0.4f * (float) Math.sin(t * 1.3f + i * 2.1f);
            jg.glow(x, y, PR[i] * 2.2f, Style.withAlpha(PC[i], 0.22f * k * tw));
            jg.fillCircle(x, y, PR[i] * 0.18f, Style.withAlpha(PC[i], 0.35f * k * tw));
        }
    }

    void ripple(float cx, float cy, float p) {
        if (p <= 0 || p >= 1) return;
        float r = 18 + 80 * out(p);
        jg.fillCircle(cx, cy, r, Style.withAlpha(0xFFFFFFFF, 0.4f * (1 - p)));
        jg.strokeCircle(cx, cy, r, 3, Style.withAlpha(0xFFFFFFFF, 0.6f * (1 - p)));
    }

    /** A burst of little glass confetti pieces. t = seconds since the burst. */
    void confetti(float cx, float cy, float t, int seed) {
        if (t <= 0 || t > 2.4f) return;
        Random r = new Random(seed);
        int[] cols = {0xFF7C9CFF, 0xFFC77DFF, 0xFFFF8AB3, 0xFF7DE3F0, 0xFFFFD866, 0xFFFFFFFF, 0xFF5BD68A};
        float fade = 1 - seg(t, 1.3f, 2.4f);
        AffineTransform keep = g.getTransform();
        for (int i = 0; i < 56; i++) {
            double ang = Math.toRadians(-90 + (r.nextFloat() - 0.5f) * 160);
            float spd = 520 + r.nextFloat() * 820;
            float vx = (float) Math.cos(ang) * spd, vy = (float) Math.sin(ang) * spd;
            float drag = (float) (1 - Math.exp(-2.2 * t)) / 2.2f;
            float x = cx + vx * drag, y = cy + vy * drag + 0.5f * 1100 * t * t * 0.55f;
            float rot = r.nextFloat() * 360 + t * (r.nextFloat() - 0.5f) * 900;
            float s = 9 + r.nextFloat() * 13;
            int c = cols[r.nextInt(cols.length)];
            g.setTransform(keep);
            g.translate(x, y);
            g.rotate(Math.toRadians(rot));
            alpha(fade * (0.75f + 0.25f * r.nextFloat()));
            g.setColor(new Color(c));
            g.fill(new RoundRectangle2D.Float(-s / 2, -s * 0.3f, s, s * 0.6f, s * 0.3f, s * 0.3f));
        }
        g.setTransform(keep);
        solid();
    }

    static BufferedImage w(String key, int kind, int wdp, int hdp, float d, Style st, Data a) {
        BufferedImage im = cache.get(key);
        if (im == null) { im = Preview.widget(kind, wdp, hdp, d, st, a); cache.put(key, im); }
        return im;
    }

    static Data sample(int fence) { Data a = Preview.sample(fence); a.quote = 7; return a; }

    static Style gdGlass() { return new Style(Style.GLASSDESK, Style.GLASS, true, 0, 45); }

    // ---------------------------------------------------------------- scenes

    void draw(int scene, float t) {
        solid();
        switch (scene) {
            case 0: intro(t); break;
            case 1: iconScene(t); break;
            case 2: glass(t); break;
            case 3: fences(t); break;
            case 4: todo(t); break;
            case 5: styles(t); break;
            case 6: glance(t); break;
            case 7: phonesScene(t); break;
            default: end(t); break;
        }
    }

    /** 1. Particles of light. "Your phone. Your style." then the name, with a glint. */
    void intro(float t) {
        black();
        bokeh(t, out(seg(t, 0, 1.5f)));
        jg.glow(W / 2f, H / 2f, 900, Style.withAlpha(0xFF3B4BAA, 0.25f * out(seg(t, 0, 2))));
        float cy = H / 2f;
        title("Your phone.", W / 2f, cy - 40, 120, G.BOLD, 0xFFF5F5F7, life(t, 0.3f, 0.6f, 2.6f, 0.4f), 30, -0.03f);
        title("Your style.", W / 2f, cy + 100, 120, G.BOLD, 0xFF7C9CFF, 0xFFE08AFF, life(t, 1.0f, 0.6f, 2.6f, 0.4f), 30, -0.03f, 0);
        title("Introducing", W / 2f, cy - 110, 40, G.SEMI, 0xFF86868B, out(seg(t, 2.7f, 3.3f)), 20, 0.01f);
        float a = out(seg(t, 2.9f, 3.8f));
        if (a > 0) {
            float fs = V ? 118 : 150;
            Font f1 = jg.font(fs, G.BOLD, -0.03f);
            float w1 = (float) new TextLayout("GlassDesk ", f1, g.getFontRenderContext()).getAdvance();
            float w2 = (float) new TextLayout("1.7.2", f1, g.getFontRenderContext()).getAdvance();
            float left = W / 2f - (w1 + w2) / 2;
            float sh = seg(t, 3.7f, 4.7f);
            title("GlassDesk ", left + w1 / 2, cy + 70, fs, G.BOLD, 0xFFF5F5F7, 0, a, 36, -0.03f, sh);
            title("1.7.2", left + w1 + w2 / 2, cy + 70, fs, G.BOLD, 0xFF7C9CFF, 0xFFC77DFF, a, 36, -0.03f, seg(t, 3.9f, 4.9f));
        }
        title("for Android", W / 2f, cy + 160, 40, G.MEDIUM, 0xFF86868B, out(seg(t, 3.5f, 4.2f)), 16, 0);
    }

    /** 2. The icon blooms; widgets burst out of it and orbit in 3D. */
    void iconScene(float t) {
        black();
        bokeh(t + 5, 0.8f);
        float bloom = out(seg(t, 0.1f, 2.0f));
        float iy = V ? 760 : 430, orx = V ? 400 : 680, ory = V ? 290 : 210;
        jg.glow(W / 2f, iy, 620 * (0.6f + 0.4f * bloom), Style.withAlpha(0xFF7C9CFF, 0.55f * bloom));
        jg.glow(W / 2f + 140, iy + 50, 460 * bloom, Style.withAlpha(0xFFB06CFF, 0.35f * bloom));
        int[] kinds = {Draw.WEATHER, Draw.CAL, Draw.TODO, Draw.NOTES, Draw.COUNT, Draw.QUOTE};
        float burst = out(seg(t, 0.9f, 2.2f));
        Style st = gdGlass();
        float[] depth = new float[6];
        float[] xs = new float[6], ys = new float[6];
        for (int i = 0; i < 6; i++) {
            double th = Math.toRadians(i * 60 + t * 16 - 90);
            xs[i] = W / 2f + (float) Math.cos(th) * orx * burst;
            ys[i] = iy + (float) Math.sin(th) * ory * burst;
            depth[i] = (float) Math.sin(th);
        }
        // tiles behind the icon first
        for (int pass = 0; pass < 2; pass++) {
            for (int i = 0; i < 6; i++) {
                boolean front = depth[i] >= 0;
                if ((pass == 0) == front) continue;
                BufferedImage im = w("orb" + i, kinds[i], 155, 155, 1.5f, st, sample(0));
                float sc = (0.62f + 0.3f * (depth[i] + 1) / 2) * (0.4f + 0.6f * burst) * (V ? 1.3f : 1f);
                float yaw = -(float) Math.cos(Math.toRadians(i * 60 + t * 16 - 90)) * 35;
                tilt(im, xs[i], ys[i], sc, yaw, burst * (0.45f + 0.55f * (depth[i] + 1) / 2), false);
            }
            if (pass == 0) {
                float s = 0.62f + 0.38f * back(seg(t, 0.2f, 1.4f));
                image(icon, W / 2f, iy - (1 - out(seg(t, 0.2f, 1.4f))) * 30, s * 0.6f, out(seg(t, 0.2f, 1.0f)));
            }
        }
        title("Your Home Screen.", W / 2f, V ? 1330 : 840, 84, G.BOLD, 0xFFF5F5F7, out(seg(t, 2.2f, 3.1f)), 28, -0.025f);
        title("Reimagined.", W / 2f, V ? 1435 : 945, 84, G.BOLD, 0xFF7C9CFF, 0xFFE08AFF, out(seg(t, 3.0f, 3.9f)), 28, -0.025f, seg(t, 4.0f, 5.2f));
    }

    /** 3. Frosted glass widgets swing in with real perspective; light glides across the glass. */
    void glass(float t) {
        black();
        float zoom = 1.04f + 0.05f * inOut(t / 8f);
        image(wallDark, W / 2f, H / 2f, zoom, out(seg(t, 0f, 1.0f)));
        float d = V ? 2.6f : 2.3f;
        Style st = gdGlass();
        BufferedImage clock = w("g_clock", Draw.CLOCK, 330, 155, d, st, sample(0));
        BufferedImage wx = w("g_wx", Draw.WEATHER, 155, 155, d, st, sample(0));
        BufferedImage ca = w("g_cal", Draw.CAL, 155, 155, d, st, sample(0));
        BufferedImage fe = w("g_fence", Draw.FENCE_A, 330, 155, d, st, sample(0));
        float x0 = (W - clock.getWidth()) / 2f, gap = clock.getWidth() - 2 * wx.getWidth(), top = V ? 400 : 225;
        BufferedImage[] ims = V ? new BufferedImage[]{clock, wx, ca, fe} : new BufferedImage[]{clock, wx, ca};
        float r2 = top + clock.getHeight() + gap;
        float[][] pos = {{x0, top}, {x0, r2}, {x0 + wx.getWidth() + gap, r2}, {x0, r2 + wx.getHeight() + gap}};
        for (int i = 0; i < ims.length; i++) {
            float in = out(seg(t, 0.7f + i * 0.35f, 2.5f + i * 0.35f));
            float yaw = lerp(i == 2 ? 38 : -38, 0, in) * (V && i != 1 && i != 2 ? 0.7f : 1) + (float) Math.sin(t * 0.9f + i) * 3.5f * in;
            float bob = (float) Math.sin(t * 1.1f + i * 1.7f) * 6;
            float shine = seg(t, 3.0f + i * 0.18f, 4.6f + i * 0.18f);
            BufferedImage im = sheen(ims[i], shine, 1f);
            tilt(im, pos[i][0] + ims[i].getWidth() / 2f, pos[i][1] + ims[i].getHeight() / 2f + bob + (1 - in) * 60, 1f, yaw, in, true);
        }
        title(V ? "Frosted glass." : "Frosted glass. Real depth.", W / 2f, V ? 230 : 140, V ? 80 : 64, G.BOLD, 0xFFFFFFFF, out(seg(t, 2.6f, 3.6f)), 20, -0.02f);
        if (V) title("Real depth.", W / 2f, 320, 80, G.BOLD, 0xFF9DB4FF, 0xFFE08AFF, out(seg(t, 3.0f, 4.0f)), 20, -0.02f, seg(t, 4.2f, 5.4f));
    }

    /** 4. "Fences." Apps pop in; a tap opens Music like a real app launch. */
    void fences(float t) {
        black();
        bokeh(t + 11, 0.5f);
        jg.glow(W / 2f, V ? 960 : 640, 900, Style.withAlpha(0xFF7C9CFF, 0.30f * out(seg(t, 1f, 2.5f))));
        float mv = inOut(seg(t, 1.4f, 2.3f));
        float size = lerp(170, V ? 120 : 96, mv), y = lerp(V ? 1000 : 600, V ? 480 : 190, mv);
        title("Fences.", W / 2f, y, size, G.BOLD, 0xFFF5F5F7, 0xFFB9C6FF, out(seg(t, 0.2f, 1.1f)), 30, -0.03f, 0);
        float wa = out(seg(t, 2.0f, 2.8f));
        float d = 2.75f;
        float ww = 330 * d, wh = 155 * d, wx = (W - ww) / 2f, wy = V ? 720 : 330;
        if (wa > 0) {
            Data a = sample(0);
            int shown = 0;
            for (int i = 0; i < 8; i++) {
                float s = back(seg(t, 2.6f + i * 0.14f, 3.05f + i * 0.14f));
                ((J2DG.SampleIcon) a.icons[i]).scale = s;
                if (s > 0.6f) shown = i + 1;
            }
            String[] lab = new String[shown];
            System.arraycopy(a.labels, 0, lab, 0, shown);
            a.labels = lab;
            BufferedImage im = Preview.widget(Draw.FENCE_A, 330, 155, d, new Style(Style.GLASSDESK, Style.GLASS, true, 0, 60), a);
            jg.glow(W / 2f, wy + wh / 2, 700, Style.withAlpha(0xFFB06CFF, 0.22f * wa));
            widget(im, wx, wy, wa, 60, 1);
        }
        if (V) title("One tap away.", W / 2f, 1430, 44, G.MEDIUM, 0xFFA1A1A6, life(t, 3.6f, 0.8f, 5.2f, 0.4f), 14, 0);
        title(V ? "Up to eight apps." : "Up to eight apps. One tap away.", W / 2f, V ? 1370 : 1000, V ? 44 : 40, G.MEDIUM, 0xFFA1A1A6, life(t, 3.4f, 0.8f, 5.2f, 0.4f), 14, 0);
        // Music icon (index 5): same layout math as Draw.fence()
        float m = Math.min(ww, wh), pad = Math.min(16 * d, m * 0.1f);
        float headH = wh * Draw.GRID_HEAD / Draw.GRID_TOTAL, rowH = wh * Draw.GRID_ROW / Draw.GRID_TOTAL;
        float gx = pad * 0.6f, gw = ww - 2 * pad * 0.6f, cw = gw / 4f, label = 10 * d;
        boolean showLabels = rowH > 44 * d;
        float ic = Math.min(cw * 0.66f, showLabels ? rowH - label * 1.9f : rowH * 0.78f);
        ic = Math.min(ic, 60 * d);
        int idx = 5;
        float block = ic + (showLabels ? label * 1.6f : 0);
        float icx = wx + gx + cw * (idx % 4) + cw / 2, icy = wy + headH + rowH * (idx / 4) + (rowH - block) / 2 + ic / 2;
        ripple(icx, icy, seg(t, 4.5f, 5.1f));
        float e = inOut(seg(t, 4.9f, 6.3f));
        if (e > 0) {
            float x = lerp(icx - ic / 2, -40, e), yy = lerp(icy - ic / 2, -40, e);
            float rw = lerp(ic, W + 80, e), rh = lerp(ic, H + 80, e);
            jg.fillSquircleV(x, yy, rw, rh, lerp(ic * 0.225f, 60, e), 0xFFFF6680, 0xFFFA233B);
            float gs = lerp(ic * 0.6f, 260, e);
            Sym.draw(jg, Sym.SPARKLE, x + rw / 2 - gs / 2, yy + rh / 2 - gs / 2, gs, 0xFFFFFFFF);
            title("Music", W / 2f, H / 2f + 240, 56, G.BOLD, 0xFFFFFFFF, out(seg(t, 6.0f, 6.6f)), 16, -0.02f);
        }
    }

    /** 5. A task gets ticked off on the widget, with a burst of confetti. */
    void todo(float t) {
        black();
        bokeh(t + 19, 0.6f);
        jg.glow(V ? W / 2f : 560, V ? 780 : 540, 700, Style.withAlpha(0xFF7C9CFF, 0.32f * out(seg(t, 0f, 1.2f))));
        float d = 3.4f;
        Style st = gdGlass();
        Data before = sample(0);
        before.done = new boolean[]{false, true, false, false, false};
        Data after = sample(0);
        after.done = new boolean[]{true, true, false, false, false};
        BufferedImage a = w("t_a", Draw.TODO, 155, 155, d, st, before);
        BufferedImage b = w("t_b", Draw.TODO, 155, 155, d, st, after);
        float x = V ? (W - a.getWidth()) / 2f : 300, y = V ? 470 : (H - a.getHeight()) / 2f;
        float in = out(seg(t, 0.2f, 1.4f));
        float yaw = lerp(-30, 0, in) + (float) Math.sin(t * 0.8f) * 2.5f * in;
        float tick = seg(t, 2.6f, 2.85f);
        float pop = tick > 0 ? 1 + 0.035f * (float) Math.sin(Math.PI * seg(t, 2.6f, 3.1f)) : 1;
        BufferedImage cur = tick >= 1 ? b : a;
        tilt(sheen(cur, seg(t, 3.4f, 4.8f), 1f), x + a.getWidth() / 2f, y + a.getHeight() / 2f + (1 - in) * 60, pop, yaw, in, true);
        if (tick > 0 && tick < 1) tilt(b, x + a.getWidth() / 2f, y + a.getHeight() / 2f, pop, yaw, tick, false);
        float m = 155 * d, pad = Math.min(16 * d, m * 0.1f), rowH = m * Draw.TODO_ROW / Draw.TODO_TOTAL;
        float cr = Math.min(8 * d, rowH * 0.34f);
        float cx = x + pad + cr, cy = y + m * Draw.TODO_HEAD / Draw.TODO_TOTAL + rowH / 2;
        ripple(cx, cy, seg(t, 2.45f, 3.2f));
        confetti(cx, cy, t - 2.7f, 11);
        float tx = V ? W / 2f : 1200;
        title("Tick it off.", tx, V ? 1300 : 520, 104, G.BOLD, 0xFFF5F5F7, out(seg(t, 1.2f, 2.0f)), 26, -0.025f);
        title("Right from your Home Screen.", tx, V ? 1385 : 605, 42, G.MEDIUM, 0xFFA1A1A6, out(seg(t, 1.7f, 2.5f)), 18, 0);
    }

    static final String[] STYLE_NAMES = {"GlassDesk · Glass", "GlassDesk · Dark", "GlassDesk · Light", "Clean · Light", "Clean · Dark"};
    static final boolean[] STYLE_DARK = {true, false, false, false, true};
    static final int[] DOT = {0xFF2A3A8A, 0xFF1A2140, 0xFFE9ECF7, 0xFFFFFFFF, 0xFF1C1C1E};

    static BufferedImage layer(int i) {
        BufferedImage im = cache.get("layer" + i);
        if (im != null) return im;
        Style[] st = {
                new Style(Style.GLASSDESK, Style.GLASS, true, 0, 45),
                new Style(Style.GLASSDESK, Style.DARK, false, 4, 45),
                new Style(Style.GLASSDESK, Style.LIGHT, false, 2, 45),
                new Style(Style.CLEAN, Style.LIGHT, false, 0, 45),
                new Style(Style.CLEAN, Style.DARK, true, 6, 45)};
        im = new BufferedImage(W, H, BufferedImage.TYPE_INT_RGB);
        Graphics2D lg = im.createGraphics();
        Film f = new Film(lg);
        f.image(STYLE_DARK[i] ? wallDark : wallLight, W / 2f, H / 2f, 1.06f, 1);
        float d = V ? 2.6f : 2.3f;
        BufferedImage clock = Preview.widget(Draw.CLOCK, 330, 155, d, st[i], sample(0));
        BufferedImage wx = Preview.widget(Draw.WEATHER, 155, 155, d, st[i], sample(0));
        BufferedImage ca = Preview.widget(Draw.CAL, 155, 155, d, st[i], sample(0));
        float x0 = (W - clock.getWidth()) / 2f, gap = clock.getWidth() - 2 * wx.getWidth(), top = V ? 370 : 190;
        f.widget(clock, x0, top, 1, 0, 1);
        f.widget(wx, x0, top + clock.getHeight() + gap, 1, 0, 1);
        f.widget(ca, x0 + wx.getWidth() + gap, top + clock.getHeight() + gap, 1, 0, 1);
        if (V) f.widget(Preview.widget(Draw.TODO, 155, 155, d, st[i], sample(0)), x0, top + clock.getHeight() + wx.getHeight() + 2 * gap, 1, 0, 1);
        if (V) f.widget(Preview.widget(Draw.NOTES, 155, 155, d, st[i], sample(0)), x0 + wx.getWidth() + gap, top + clock.getHeight() + wx.getHeight() + 2 * gap, 1, 0, 1);
        lg.dispose();
        cache.put("layer" + i, im);
        return im;
    }

    /** 6. Tap a color dot and the next look spreads out in a circle. */
    void styles(float t) {
        black();
        float[] rv = {0, 1.5f, 2.95f, 4.4f, 5.85f};
        float dur = 0.95f;
        int base = 0, next = -1;
        float p = 0;
        for (int k = 1; k < 5; k++) {
            if (t >= rv[k] + dur) base = k;
            else if (t >= rv[k]) { next = k; p = (t - rv[k]) / dur; }
        }
        float dotY = V ? H - 110 : 1032, dotGap = 74, dot0 = W / 2f - 2 * dotGap;
        alpha(out(seg(t, 0f, 0.7f)));
        g.drawImage(layer(base), 0, 0, null);
        solid();
        if (next >= 0) {
            Shape clip = g.getClip();
            float r = 2300 * inOut(p) * 0.92f + 20 * p;
            float cx = dot0 + next * dotGap;
            g.clip(new Ellipse2D.Float(cx - r, dotY - r, 2 * r, 2 * r));
            g.drawImage(layer(next), 0, 0, null);
            g.setClip(clip);
            // bright rim on the expanding circle
            jg.strokeCircle(cx, dotY, r, 4, Style.withAlpha(0xFFFFFFFF, 0.55f * (1 - p)));
        }
        int shownStyle = next >= 0 && p > 0.5f ? next : base;
        boolean dk = STYLE_DARK[shownStyle];
        if (V) {
            title("Two designs.", W / 2f, 190, 80, G.BOLD, dk ? 0xFFFFFFFF : 0xFF0B0F22, out(seg(t, 0.3f, 1.2f)), 20, -0.02f);
            title("Endless looks.", W / 2f, 285, 80, G.BOLD, dk ? 0xFFFFFFFF : 0xFF0B0F22, out(seg(t, 0.6f, 1.5f)), 20, -0.02f);
        } else
        title("Two designs. Endless looks.", W / 2f, 120, 64, G.BOLD, dk ? 0xFFFFFFFF : 0xFF0B0F22, out(seg(t, 0.3f, 1.2f)), 20, -0.02f);
        // dock of style dots
        float da = out(seg(t, 0.6f, 1.2f));
        float pw = 5 * dotGap + 24;
        alpha(da);
        jg.fillSquircle(W / 2f - pw / 2, dotY - 32, pw, 64, 32, dk ? 0x99101426 : 0x99FFFFFF);
        jg.strokeSquircleV(W / 2f - pw / 2, dotY - 32, pw, 64, 32, 1.5f, 0x80FFFFFF, 0x1AFFFFFF);
        solid();
        for (int k = 0; k < 5; k++) {
            float cx = dot0 + k * dotGap;
            boolean on = k == shownStyle;
            jg.fillCircle(cx, dotY, on ? 19 : 16, Style.withAlpha(DOT[k], da));
            jg.strokeCircle(cx, dotY, on ? 19 : 16, 2, Style.withAlpha(dk ? 0xFFFFFFFF : 0xFF0B0F22, da * (on ? 0.9f : 0.35f)));
            if (on) jg.strokeCircle(cx, dotY, 26, 3, Style.withAlpha(0xFF7C9CFF, da));
            if (k > 0) ripple(cx, dotY, seg(t, rv[k] - 0.3f, rv[k] + 0.35f));
        }
        String nm = STYLE_NAMES[shownStyle];
        jg.text(nm, W / 2f, dotY - 52, 26, G.SEMI, Style.withAlpha(dk ? 0xFFFFFFFF : 0xFF0B0F22, da), G.CENTER, 0.02f);
    }

    /** 7. Everything at a glance: rings fill, the countdown counts, light glides over the glass. */
    void glance(float t) {
        black();
        image(wallDark, W / 2f, H / 2f, 1.08f - 0.03f * inOut(t / 7f), out(seg(t, 0f, 1f)));
        float d = V ? 2.05f : 2.2f;
        Style st = gdGlass();
        float fill = out(seg(t, 1.2f, 3.2f));
        Data sys = sample(0);
        sys.batt = Math.round(82 * fill);
        sys.memUsed = Math.round(41 * fill);
        sys.diskUsed = Math.round(71 * fill);
        BufferedImage sysI = Preview.widget(Draw.SYS, 330, 155, d, st, sys);
        BufferedImage wx = w("gl_wx", Draw.WEATHER, 330, 155, d, st, sample(0));
        Data cd = sample(0);
        int days = Math.max(1, Math.round(43 * out(seg(t, 1.4f, 3.4f))));
        cd.cdDate = cd.now + days * 86400000L;
        cd.cdTitle = "Winter break";
        BufferedImage cdI = Preview.widget(Draw.COUNT, 155, 155, d, st, cd);
        float gap = 44, rowW = V ? sysI.getWidth() : sysI.getWidth() * 2 + gap, x0 = (W - rowW) / 2f, top = V ? 400 : 250;
        float sh = seg(t, 3.6f, 5.4f);
        widget(sheen(wx, sh, 0.9f), x0, top, out(seg(t, 0.5f, 1.4f)), 60, 1);
        if (V) widget(sheen(sysI, seg(t, 3.75f, 5.55f), 0.9f), x0, top + wx.getHeight() + gap, out(seg(t, 0.8f, 1.7f)), 60, 1);
        else widget(sheen(sysI, seg(t, 3.75f, 5.55f), 0.9f), x0 + wx.getWidth() + gap, top, out(seg(t, 0.8f, 1.7f)), 60, 1);
        int[] kinds = {Draw.COUNT, Draw.QUOTE, Draw.CAL, Draw.NOTES};
        float sw = 155 * d, sgap = V ? rowW - 2 * sw : (rowW - 4 * sw) / 3f;
        for (int i = 0; i < 4; i++) {
            BufferedImage im = i == 0 ? cdI : w("gl_" + i, kinds[i], 155, 155, d, st, sample(0));
            float xx = V ? x0 + (i % 2) * (sw + sgap) : x0 + i * (sw + sgap);
            float yy = V ? top + 2 * (wx.getHeight() + gap) + (i / 2) * (sw + gap) : top + wx.getHeight() + gap;
            widget(sheen(im, seg(t, 3.9f + i * 0.12f, 5.7f + i * 0.12f), 0.9f), xx, yy,
                    out(seg(t, 1.3f + i * 0.15f, 2.2f + i * 0.15f)), 60, 1);
        }
        if (V) title("at a glance.", W / 2f, 310, 80, G.BOLD, 0xFF9DB4FF, 0xFFE08AFF, out(seg(t, 2.7f, 3.6f)), 20, -0.02f, seg(t, 4.0f, 5.2f));
        title(V ? "Everything" : "Everything at a glance.", W / 2f, V ? 215 : 160, V ? 80 : 64, G.BOLD, 0xFFFFFFFF, out(seg(t, 2.4f, 3.3f)), 20, -0.02f);
    }

    /** 8. Three phones fan out in 3D, one per look. */
    void phonesScene(float t) {
        black();
        bokeh(t + 41, 0.7f);
        float rise = out(seg(t, 0.1f, 1.4f)), spread = inOut(seg(t, 0.8f, 2.2f));
        float sc = V ? 0.6f : 0.5f, baseY = V ? 1180 : 690, sideX = V ? 300 : 470;
        jg.glow(W / 2f, baseY, 900, Style.withAlpha(0xFF7C9CFF, 0.35f * rise));
        jg.glow(W / 2f - 400, baseY + 70, 600, Style.withAlpha(0xFFB06CFF, 0.22f * spread));
        jg.glow(W / 2f + 400, baseY + 70, 600, Style.withAlpha(0xFF7DE3F0, 0.18f * spread));
        // side phones (behind)
        for (int s = -1; s <= 1; s += 2) {
            BufferedImage ph = phones[s < 0 ? 1 : 2];
            float x = W / 2f + s * sideX * spread, y = baseY + 50 + (float) Math.sin(t * 1.2f + s) * 8 + (1 - rise) * 500;
            tilt(ph, x, y, sc * (V ? 0.82f : 0.9f), s * (V ? 32 : 26) * spread, rise * spread, true);
        }
        float y0 = baseY + (float) Math.sin(t * 1.1f) * 7 + (1 - rise) * 500;
        tilt(phones[0], W / 2f, y0, sc * 1.02f, (float) Math.sin(t * 0.7f) * 4, rise, true);
        float a = out(seg(t, 1.6f, 2.5f));
        if (a > 0) {
            if (V) {
                title("Organize", W / 2f, 250, 96, G.BOLD, 0xFFF5F5F7, a, 24, -0.03f);
                title("everything.", W / 2f, 355, 96, G.BOLD, 0xFF7C9CFF, 0xFFE08AFF, a, 24, -0.03f, seg(t, 3.0f, 4.2f));
            } else {
                Font f1 = jg.font(84, G.BOLD, -0.03f);
                float w1 = (float) new TextLayout("Organize ", f1, g.getFontRenderContext()).getAdvance();
                float w2 = (float) new TextLayout("everything.", f1, g.getFontRenderContext()).getAdvance();
                float left = W / 2f - (w1 + w2) / 2;
                title("Organize ", left + w1 / 2, 130, 84, G.BOLD, 0xFFF5F5F7, a, 24, -0.03f);
                title("everything.", left + w1 + w2 / 2, 130, 84, G.BOLD, 0xFF7C9CFF, 0xFFE08AFF, a, 24, -0.03f, seg(t, 3.0f, 4.2f));
            }
        }
        if (V) title("Clock, weather, calendar, to-do,", W / 2f, 430, 34, G.MEDIUM, 0xFFA1A1A6, out(seg(t, 2.2f, 3.0f)), 12, 0);
        title(V ? "notes, fences and more." : "Clock, weather, calendar, to-do, notes, fences and more.", W / 2f, V ? 476 : 195, V ? 34 : 32, G.MEDIUM, 0xFFA1A1A6, out(seg(t, 2.2f, 3.0f)), 12, 0);
    }

    /** 9. End card: shining logo and a glowing download button. */
    void end(float t) {
        black();
        float fade = 1 - seg(t, 6.0f, 7f);
        bokeh(t + 48, 0.8f * fade);
        float a = out(seg(t, 0.3f, 1.3f)) * fade;
        float pulse = 1 + 0.06f * (float) Math.sin(t * 2.4f);
        float o = V ? 420 : 0;
        jg.glow(W / 2f, 330 + o, 520 * pulse, Style.withAlpha(0xFF7C9CFF, 0.45f * a));
        image(icon, W / 2f, 330 + o, 0.4f + 0.04f * back(seg(t, 0.3f, 1.3f)), a);
        title("GlassDesk", W / 2f, 600 + o, 120, G.BOLD, 0xFFF5F5F7, 0, out(seg(t, 0.9f, 1.8f)) * fade, 26, -0.03f, seg(t, 1.9f, 3.0f));
        title("Version 1.7.2", W / 2f, 668 + o, 30, G.MEDIUM, 0xFF6E6E73, out(seg(t, 1.5f, 2.2f)) * fade, 12, 0.02f);
        float ba = out(seg(t, 2.2f, 3.0f)) * fade;
        if (ba > 0) {
            String lbl = "Download free for Android";
            float bw = jg.measure(lbl, 36, G.SEMI, 0) + 110, bh = 86, bx = W / 2f - bw / 2, by = 740 + o;
            float s = 0.92f + 0.08f * back(seg(t, 2.2f, 3.0f));
            AffineTransform keep = g.getTransform();
            g.translate(W / 2f, by + bh / 2); g.scale(s, s); g.translate(-W / 2f, -(by + bh / 2));
            jg.glow(W / 2f, by + bh / 2, bw * 0.7f * pulse, Style.withAlpha(0xFF7C9CFF, 0.45f * ba));
            alpha(ba);
            g.setPaint(new GradientPaint(bx, by, new Color(0xFF8FA9FF), bx + bw, by + bh, new Color(0xFFB57BFF)));
            g.fill(new RoundRectangle2D.Float(bx, by, bw, bh, bh, bh));
            g.setPaint(new GradientPaint(0, by, new Color(255, 255, 255, 110), 0, by + bh / 2, new Color(255, 255, 255, 0)));
            g.fill(new RoundRectangle2D.Float(bx + 2, by + 2, bw - 4, bh / 2, bh, bh));
            solid();
            jg.text(lbl, W / 2f, by + bh / 2 + 13, 36, G.SEMI, Style.withAlpha(0xFFFFFFFF, ba), G.CENTER, 0);
            g.setTransform(keep);
        }
        title("glassdesk.freedev.app", W / 2f, 920 + o, 36, G.SEMI, 0xFF7C9CFF, 0xFFC77DFF, out(seg(t, 2.8f, 3.6f)) * fade, 12, 0.01f, 0);
    }

    // ---------------------------------------------------------------- setup + output

    static void load(String fonts, String dir) throws Exception {
        Preview.fonts = fonts;
        icon = ImageIO.read(new File(dir, "icon.png"));
        String[] ph = {"01_glassdesk.png", "05_light.png", "06_clean.png"};
        for (int i = 0; i < 3; i++) {
            BufferedImage shot = ImageIO.read(new File(dir, ph[i]));
            BufferedImage crop = new BufferedImage(840, 1630, BufferedImage.TYPE_INT_ARGB);
            Graphics2D cg = crop.createGraphics();
            cg.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
            cg.setClip(new RoundRectangle2D.Float(10, 10, 820, 1700, 120, 120));
            cg.drawImage(shot.getSubimage(120, 290, 840, 1630), 0, 0, null);
            cg.dispose();
            phones[i] = crop;
        }
        wallDark = new BufferedImage(W, H, BufferedImage.TYPE_INT_RGB);
        Graphics2D g1 = wallDark.createGraphics(); Preview.wallpaper(g1, W, H, true); g1.dispose();
        wallLight = new BufferedImage(W, H, BufferedImage.TYPE_INT_RGB);
        Graphics2D g2 = wallLight.createGraphics(); Preview.wallpaper(g2, W, H, false); g2.dispose();
        vignette = new BufferedImage(W, H, BufferedImage.TYPE_INT_ARGB);
        Graphics2D g3 = vignette.createGraphics();
        g3.setPaint(new RadialGradientPaint(new Point2D.Float(W / 2f, H / 2f), Math.max(W, H) * 0.65f, new float[]{0.5f, 1f},
                new Color[]{new Color(0, 0, 0, 0), new Color(0, 0, 0, 120)}));
        g3.fillRect(0, 0, W, H);
        g3.dispose();
    }

    static BufferedImage imgA, imgB;
    static Film fa, fb;

    static void frame(Graphics2D out, float T) {
        int k = 0;
        for (int j = 0; j < DUR.length; j++) if (T >= start(j)) k = j;
        float tk = T - start(k);
        if (k > 0 && tk < TR) {
            float e = inOut(tk / TR);
            fa.draw(k - 1, T - start(k - 1));
            fb.draw(k, tk);
            transition(out, KIND[k - 1], e);
        } else {
            fb.draw(k, tk);
            out.setComposite(AlphaComposite.SrcOver);
            out.drawImage(imgB, 0, 0, null);
        }
        out.setComposite(AlphaComposite.SrcOver);
        out.drawImage(vignette, 0, 0, null);
    }

    static void transition(Graphics2D g, int kind, float e) {
        g.setComposite(AlphaComposite.SrcOver);
        g.setRenderingHint(RenderingHints.KEY_INTERPOLATION, RenderingHints.VALUE_INTERPOLATION_BILINEAR);
        AffineTransform keep = g.getTransform();
        if (kind == 0) {
            float sb = 0.92f + 0.08f * e;
            g.translate(W / 2f, H / 2f); g.scale(sb, sb); g.translate(-W / 2f, -H / 2f);
            g.drawImage(imgB, 0, 0, null);
            g.setTransform(keep);
            float sa = 1f + 0.14f * e;
            g.translate(W / 2f, H / 2f); g.scale(sa, sa); g.translate(-W / 2f, -H / 2f);
            g.setComposite(AlphaComposite.getInstance(AlphaComposite.SRC_OVER, 1 - e));
            g.drawImage(imgA, 0, 0, null);
        } else if (kind == 1) {
            g.drawImage(imgA, 0, 0, null);
            float span = W + H * 0.6f + 400;
            float x = -400 - H * 0.6f + span * e;
            Polygon p = new Polygon();
            p.addPoint(-10, -10); p.addPoint(Math.round(x + H * 0.6f), -10); p.addPoint(Math.round(x), H + 10); p.addPoint(-10, H + 10);
            Shape oldClip = g.getClip();
            g.clip(p);
            g.drawImage(imgB, 0, 0, null);
            g.setClip(oldClip);
            float glowA = (float) Math.sin(Math.PI * e);
            for (int i = 0; i < 2; i++) {
                float wdt = i == 0 ? 260 : 60;
                int a = (int) ((i == 0 ? 70 : 150) * glowA);
                g.setPaint(new GradientPaint(x + H * 0.3f - wdt, 0, new Color(255, 255, 255, 0), x + H * 0.3f, 0, new Color(225, 232, 255, a), true));
                Polygon band = new Polygon();
                band.addPoint(Math.round(x + H * 0.6f - wdt), -10); band.addPoint(Math.round(x + H * 0.6f + wdt), -10);
                band.addPoint(Math.round(x + wdt), H + 10); band.addPoint(Math.round(x - wdt), H + 10);
                g.fill(band);
            }
        } else {
            g.translate(0, -H * 0.25f * e);
            g.drawImage(imgA, 0, 0, null);
            g.setTransform(keep);
            g.setColor(new Color(0, 0, 0, (int) (170 * e)));
            g.fillRect(0, 0, W, H);
            g.translate(0, H * (1 - e));
            g.setColor(new Color(0, 0, 0, (int) (90 * (1 - e))));
            g.fillRect(0, -40, W, 40);
            g.drawImage(imgB, 0, 0, null);
        }
        g.setTransform(keep);
        g.setComposite(AlphaComposite.SrcOver);
    }

    public static void main(String[] args) throws Exception {
        if (System.getProperty("vertical") != null) { V = true; W = 1080; H = 1920; }
        load(args[0], args[1]);
        BufferedImage img = new BufferedImage(W, H, BufferedImage.TYPE_3BYTE_BGR);
        Graphics2D g = img.createGraphics();
        imgA = new BufferedImage(W, H, BufferedImage.TYPE_INT_RGB);
        imgB = new BufferedImage(W, H, BufferedImage.TYPE_INT_RGB);
        Graphics2D ga = imgA.createGraphics(), gb = imgB.createGraphics();
        for (Graphics2D x : new Graphics2D[]{ga, gb}) x.setRenderingHint(RenderingHints.KEY_INTERPOLATION, RenderingHints.VALUE_INTERPOLATION_BILINEAR);
        fa = new Film(ga);
        fb = new Film(gb);
        if (args[2].equals("still")) {
            frame(g, Float.parseFloat(args[3]));
            ImageIO.write(img, "png", new File(args[4]));
            return;
        }
        int k = Integer.parseInt(args[3]);
        float a = start(k), b = k + 1 < DUR.length ? start(k + 1) : total();
        byte[] buf = ((DataBufferByte) img.getRaster().getDataBuffer()).getData();
        OutputStream os = new BufferedOutputStream(System.out, 1 << 22);
        int f0 = Math.round(a * FPS), f1 = Math.round(b * FPS);
        for (int i = f0; i < f1; i++) {
            frame(g, i / (float) FPS);
            os.write(buf);
        }
        os.flush();
    }
}
