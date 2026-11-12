package app.freedev.glassdesk;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.ColorFilter;
import android.graphics.PixelFormat;
import android.graphics.drawable.Drawable;

/** Drawables for the app's own screens, drawn with the same code as the widgets. */
final class Ui {
    private Ui() {}

    /** An SF Symbols-style glyph, optionally on a colored squircle or circle (like Settings icons). */
    static final class SymDrawable extends Drawable {
        final Context ctx;
        int kind, ink, bg;
        boolean circle;
        float glyph = 0.62f;
        final int size;

        SymDrawable(Context ctx, int kind, int ink, int bg, boolean circle, int sizePx) {
            this.ctx = ctx; this.kind = kind; this.ink = ink; this.bg = bg; this.circle = circle; this.size = sizePx;
        }

        @Override public void draw(Canvas c) {
            android.graphics.Rect b = getBounds();
            float s = Math.min(b.width(), b.height());
            float x = b.left + (b.width() - s) / 2f, y = b.top + (b.height() - s) / 2f;
            AndroidG g = new AndroidG(ctx, c);
            float gs = s;
            if (bg != 0) {
                if (circle) g.fillCircle(x + s / 2, y + s / 2, s / 2, bg);
                else g.fillSquircle(x, y, s, s, s * 0.23f, bg);
                gs = s * glyph;
            }
            Sym.draw(g, kind, x + (s - gs) / 2, y + (s - gs) / 2, gs, ink);
        }

        @Override public int getIntrinsicWidth() { return size; }
        @Override public int getIntrinsicHeight() { return size; }
        @Override public void setAlpha(int a) {}
        @Override public void setColorFilter(ColorFilter cf) {}
        @Override public int getOpacity() { return PixelFormat.TRANSLUCENT; }
    }

    /** An app icon masked to an iOS-style squircle. */
    static final class AppIcon extends Drawable {
        final Context ctx;
        final Drawable src;
        final int size;

        AppIcon(Context ctx, Drawable src, int size) { this.ctx = ctx; this.src = src; this.size = size; }

        @Override public void draw(Canvas c) {
            android.graphics.Rect b = getBounds();
            new AndroidG(ctx, c).icon(src, b.left, b.top, Math.min(b.width(), b.height()));
        }

        @Override public int getIntrinsicWidth() { return size; }
        @Override public int getIntrinsicHeight() { return size; }
        @Override public void setAlpha(int a) {}
        @Override public void setColorFilter(ColorFilter cf) {}
        @Override public int getOpacity() { return PixelFormat.TRANSLUCENT; }
    }
}
