package app.freedev.glassdesk;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.LinearGradient;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.RectF;
import android.graphics.Shader;
import android.graphics.Typeface;
import android.graphics.drawable.AdaptiveIconDrawable;
import android.graphics.drawable.Drawable;

/** Android Canvas version of G. */
public final class AndroidG implements G {
    private static final String[] FILES = {"inter_regular.otf", "inter_medium.otf", "inter_semibold.otf", "inter_bold.otf"};
    private static final Typeface[] CACHE = new Typeface[4];

    final Canvas c;
    final Context ctx;
    final Paint p = new Paint(Paint.ANTI_ALIAS_FLAG | Paint.FILTER_BITMAP_FLAG | Paint.SUBPIXEL_TEXT_FLAG);
    final Path path = new Path();
    final RectF rf = new RectF();

    public AndroidG(Context ctx, Canvas c) { this.ctx = ctx; this.c = c; }

    public static Typeface font(Context ctx, int weight) {
        if (weight == SERIF) return Typeface.SERIF;
        if (weight == SERIF_ITALIC) return Typeface.create(Typeface.SERIF, Typeface.ITALIC);
        int i = Math.max(0, Math.min(3, weight));
        synchronized (CACHE) {
            if (CACHE[i] == null) {
                try { CACHE[i] = Typeface.createFromAsset(ctx.getAssets(), FILES[i]); }
                catch (Throwable t) { CACHE[i] = Typeface.create("sans-serif" + (i == 1 ? "-medium" : ""), i >= 2 ? Typeface.BOLD : Typeface.NORMAL); }
            }
            return CACHE[i];
        }
    }

    Path sq(float x, float y, float w, float h, float r) {
        path.reset();
        Sq.build(new Sq.Sink() {
            public void moveTo(float a, float b) { path.moveTo(a, b); }
            public void lineTo(float a, float b) { path.lineTo(a, b); }
            public void cubicTo(float a, float b, float cc, float d, float e, float f) { path.cubicTo(a, b, cc, d, e, f); }
            public void close() { path.close(); }
        }, x, y, w, h, r);
        return path;
    }

    void fill(int color) { p.reset(); p.setAntiAlias(true); p.setFilterBitmap(true); p.setStyle(Paint.Style.FILL); p.setColor(color); }
    void stroke(int color, float width) {
        p.reset(); p.setAntiAlias(true); p.setStyle(Paint.Style.STROKE); p.setColor(color);
        p.setStrokeWidth(width); p.setStrokeCap(Paint.Cap.ROUND); p.setStrokeJoin(Paint.Join.ROUND);
    }

    public void save() { c.save(); }
    public void restore() { c.restore(); }
    public void clipSquircle(float x, float y, float w, float h, float r) { c.clipPath(sq(x, y, w, h, r)); }

    public void fillRect(float x, float y, float w, float h, int color) { fill(color); c.drawRect(x, y, x + w, y + h, p); }
    public void fillSquircle(float x, float y, float w, float h, float r, int color) { fill(color); c.drawPath(sq(x, y, w, h, r), p); }
    public void fillSquircleV(float x, float y, float w, float h, float r, int top, int bottom) {
        fill(0xFFFFFFFF);
        p.setShader(new LinearGradient(0, y, 0, y + h, top, bottom, Shader.TileMode.CLAMP));
        c.drawPath(sq(x, y, w, h, r), p);
        p.setShader(null);
    }
    public void strokeSquircleV(float x, float y, float w, float h, float r, float width, int top, int bottom) {
        stroke(0xFFFFFFFF, width);
        p.setShader(new LinearGradient(0, y, 0, y + h, top, bottom, Shader.TileMode.CLAMP));
        c.drawPath(sq(x, y, w, h, r), p);
        p.setShader(null);
    }
    public void glow(float cx, float cy, float r, int color) {
        fill(0xFFFFFFFF);
        p.setShader(new android.graphics.RadialGradient(cx, cy, Math.max(1f, r), new int[]{color, color & 0x00FFFFFF},
                null, Shader.TileMode.CLAMP));
        c.drawCircle(cx, cy, r, p);
        p.setShader(null);
    }
    public void fillCircle(float cx, float cy, float r, int color) { fill(color); c.drawCircle(cx, cy, r, p); }
    public void strokeCircle(float cx, float cy, float r, float width, int color) { stroke(color, width); c.drawCircle(cx, cy, r, p); }
    public void arc(float cx, float cy, float r, float start, float sweep, float width, int color) {
        stroke(color, width);
        rf.set(cx - r, cy - r, cx + r, cy + r);
        c.drawArc(rf, start, sweep, false, p);
    }
    public void line(float x1, float y1, float x2, float y2, float width, int color) { stroke(color, width); c.drawLine(x1, y1, x2, y2, p); }
    public void polyline(float[] pts, float width, int color) {
        stroke(color, width);
        path.reset();
        path.moveTo(pts[0], pts[1]);
        for (int i = 2; i < pts.length; i += 2) path.lineTo(pts[i], pts[i + 1]);
        c.drawPath(path, p);
    }
    public void fillPolygon(float[] pts, int color) {
        fill(color);
        path.reset();
        path.moveTo(pts[0], pts[1]);
        for (int i = 2; i < pts.length; i += 2) path.lineTo(pts[i], pts[i + 1]);
        path.close();
        c.drawPath(path, p);
    }

    void textPaint(float size, int weight, int color, float tr) {
        fill(color);
        p.setSubpixelText(true);
        p.setTypeface(font(ctx, weight));
        p.setTextSize(size);
        p.setLetterSpacing(tr);
    }

    public float text(String s, float x, float y, float size, int weight, int color, int align, float tr) {
        textPaint(size, weight, color, tr);
        float w = p.measureText(s);
        p.setTextAlign(Paint.Align.LEFT);
        float dx = align == CENTER ? -w / 2 : align == RIGHT ? -w : 0;
        c.drawText(s, x + dx, y, p);
        return w;
    }
    public float measure(String s, float size, int weight, float tr) { textPaint(size, weight, 0xFF000000, tr); return p.measureText(s); }
    public float capHeight(float size, int weight) { return size * 0.727f; }

    public void icon(Object img, float x, float y, float size) {
        if (!(img instanceof Drawable)) return;
        Drawable dr = (Drawable) img;
        if (dr instanceof AdaptiveIconDrawable) {
            AdaptiveIconDrawable ai = (AdaptiveIconDrawable) dr;
            c.save();
            c.clipPath(sq(x, y, size, size, size * 0.225f));
            float ext = size * 0.25f;
            int l = Math.round(x - ext), t = Math.round(y - ext), r = Math.round(x + size + ext), b = Math.round(y + size + ext);
            Drawable bg = ai.getBackground(), fg = ai.getForeground();
            if (bg != null) { bg.setBounds(l, t, r, b); bg.draw(c); } else { fill(0xFFFFFFFF); c.drawRect(x, y, x + size, y + size, p); }
            if (fg != null) { fg.setBounds(l, t, r, b); fg.draw(c); }
            c.restore();
        } else {
            dr.setBounds(Math.round(x), Math.round(y), Math.round(x + size), Math.round(y + size));
            dr.draw(c);
        }
    }
}
