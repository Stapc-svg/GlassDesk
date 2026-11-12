package app.freedev.glassdesk;

import java.awt.*;
import java.awt.font.TextAttribute;
import java.awt.geom.*;
import java.io.File;
import java.util.HashMap;
import java.util.Map;

/** Desktop (Java2D) version of G, used only to render previews. */
public final class J2DG implements G {
    public static final class SampleIcon {
        final int top, bottom, sym;
        public float scale = 1f;
        public SampleIcon(int top, int bottom, int sym) { this.top = top; this.bottom = bottom; this.sym = sym; }
    }

    final Graphics2D g;
    static Font[] base;
    final java.util.ArrayDeque<Shape> clips = new java.util.ArrayDeque<>();
    final java.util.ArrayDeque<java.awt.geom.AffineTransform> tf = new java.util.ArrayDeque<>();

    public J2DG(Graphics2D g, String fontDir) {
        this.g = g;
        g.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
        g.setRenderingHint(RenderingHints.KEY_TEXT_ANTIALIASING, RenderingHints.VALUE_TEXT_ANTIALIAS_ON);
        g.setRenderingHint(RenderingHints.KEY_FRACTIONALMETRICS, RenderingHints.VALUE_FRACTIONALMETRICS_ON);
        g.setRenderingHint(RenderingHints.KEY_STROKE_CONTROL, RenderingHints.VALUE_STROKE_PURE);
        g.setRenderingHint(RenderingHints.KEY_RENDERING, RenderingHints.VALUE_RENDER_QUALITY);
        if (base == null) {
            base = new Font[4];
            String[] f = {"inter_regular.otf", "inter_medium.otf", "inter_semibold.otf", "inter_bold.otf"};
            for (int i = 0; i < 4; i++) {
                try { base[i] = Font.createFont(Font.TRUETYPE_FONT, new File(fontDir, f[i])); }
                catch (Exception e) { base[i] = new Font("SansSerif", i >= 2 ? Font.BOLD : Font.PLAIN, 12); }
            }
        }
    }

    static Color c(int argb) { return new Color(argb, true); }

    Path2D.Float sq(float x, float y, float w, float h, float r) {
        final Path2D.Float p = new Path2D.Float();
        Sq.build(new Sq.Sink() {
            public void moveTo(float a, float b) { p.moveTo(a, b); }
            public void lineTo(float a, float b) { p.lineTo(a, b); }
            public void cubicTo(float a, float b, float c, float d, float e, float f) { p.curveTo(a, b, c, d, e, f); }
            public void close() { p.closePath(); }
        }, x, y, w, h, r);
        return p;
    }

    public void save() { clips.push(g.getClip() == null ? new Rectangle(-100000, -100000, 200000, 200000) : g.getClip()); }
    public void restore() { if (!clips.isEmpty()) g.setClip(clips.pop()); }
    public void clipSquircle(float x, float y, float w, float h, float r) { g.clip(sq(x, y, w, h, r)); }

    public void fillRect(float x, float y, float w, float h, int color) { g.setColor(c(color)); g.fill(new Rectangle2D.Float(x, y, w, h)); }
    public void fillSquircle(float x, float y, float w, float h, float r, int color) { g.setColor(c(color)); g.fill(sq(x, y, w, h, r)); }
    public void fillSquircleV(float x, float y, float w, float h, float r, int top, int bottom) {
        g.setPaint(new GradientPaint(0, y, c(top), 0, y + h, c(bottom)));
        g.fill(sq(x, y, w, h, r));
    }
    public void strokeSquircleV(float x, float y, float w, float h, float r, float width, int top, int bottom) {
        g.setPaint(new GradientPaint(0, y, c(top), 0, y + h, c(bottom)));
        g.setStroke(new BasicStroke(width, BasicStroke.CAP_ROUND, BasicStroke.JOIN_ROUND));
        g.draw(sq(x, y, w, h, r));
    }
    public void glow(float cx, float cy, float r, int color) {
        g.setPaint(new RadialGradientPaint(new Point2D.Float(cx, cy), Math.max(1f, r), new float[]{0f, 1f},
                new Color[]{c(color), c(color & 0x00FFFFFF)}));
        g.fill(new Ellipse2D.Float(cx - r, cy - r, 2 * r, 2 * r));
    }
    public void fillCircle(float cx, float cy, float r, int color) { g.setColor(c(color)); g.fill(new Ellipse2D.Float(cx - r, cy - r, 2 * r, 2 * r)); }
    public void strokeCircle(float cx, float cy, float r, float width, int color) {
        g.setColor(c(color)); g.setStroke(new BasicStroke(width)); g.draw(new Ellipse2D.Float(cx - r, cy - r, 2 * r, 2 * r));
    }
    public void arc(float cx, float cy, float r, float start, float sweep, float width, int color) {
        g.setColor(c(color)); g.setStroke(new BasicStroke(width, BasicStroke.CAP_ROUND, BasicStroke.JOIN_ROUND));
        g.draw(new Arc2D.Float(cx - r, cy - r, 2 * r, 2 * r, -start, -sweep, Arc2D.OPEN));
    }
    public void line(float x1, float y1, float x2, float y2, float width, int color) {
        g.setColor(c(color)); g.setStroke(new BasicStroke(width, BasicStroke.CAP_ROUND, BasicStroke.JOIN_ROUND));
        g.draw(new Line2D.Float(x1, y1, x2, y2));
    }
    public void polyline(float[] p, float width, int color) {
        Path2D.Float path = new Path2D.Float();
        path.moveTo(p[0], p[1]);
        for (int i = 2; i < p.length; i += 2) path.lineTo(p[i], p[i + 1]);
        g.setColor(c(color)); g.setStroke(new BasicStroke(width, BasicStroke.CAP_ROUND, BasicStroke.JOIN_ROUND));
        g.draw(path);
    }
    public void fillPolygon(float[] p, int color) {
        Path2D.Float path = new Path2D.Float();
        path.moveTo(p[0], p[1]);
        for (int i = 2; i < p.length; i += 2) path.lineTo(p[i], p[i + 1]);
        path.closePath();
        g.setColor(c(color)); g.fill(path);
    }

    Font font(float size, int weight, float tr) {
        Font f;
        if (weight == SERIF) f = new Font("Serif", Font.PLAIN, 12);
        else if (weight == SERIF_ITALIC) f = new Font("Serif", Font.ITALIC, 12);
        else f = base[Math.max(0, Math.min(3, weight))];
        Map<TextAttribute, Object> m = new HashMap<>();
        m.put(TextAttribute.SIZE, size);
        if (tr != 0) m.put(TextAttribute.TRACKING, tr);
        m.put(TextAttribute.KERNING, TextAttribute.KERNING_ON);
        m.put(TextAttribute.LIGATURES, TextAttribute.LIGATURES_ON);
        return f.deriveFont(m);
    }

    public float text(String s, float x, float y, float size, int weight, int color, int align, float tr) {
        Font f = font(size, weight, tr);
        g.setFont(f);
        float wdt = (float) f.getStringBounds(s, g.getFontRenderContext()).getWidth();
        float dx = align == CENTER ? -wdt / 2 : align == RIGHT ? -wdt : 0;
        g.setColor(c(color));
        g.drawString(s, x + dx, y);
        return wdt;
    }
    public float measure(String s, float size, int weight, float tr) {
        Font f = font(size, weight, tr);
        return (float) f.getStringBounds(s, g.getFontRenderContext()).getWidth();
    }
    public float capHeight(float size, int weight) { return size * 0.727f; }

    public void icon(Object img, float x, float y, float size) {
        if (img instanceof SampleIcon) {
            SampleIcon si = (SampleIcon) img;
            if (si.scale <= 0.01f) return;
            if (si.scale != 1f) { float ns = size * si.scale; x += (size - ns) / 2; y += (size - ns) / 2; size = ns; }
            fillSquircleV(x, y, size, size, size * 0.225f, si.top, si.bottom);
            if (si.sym == -1) { // camera-ish ring
                strokeCircle(x + size / 2, y + size / 2, size * 0.22f, size * 0.07f, 0xFFFFFFFF);
            } else {
                Sym.draw(this, si.sym, x + size * .2f, y + size * .2f, size * .6f, 0xFFFFFFFF);
            }
        }
    }
}
