package app.freedev.glassdesk;

/**
 * Tiny drawing surface. The widgets and icons are drawn through this, so the
 * same code runs on Android (Canvas) and on the desktop (for previews).
 * All sizes are in pixels. Colors are ARGB ints.
 */
public interface G {
    int REGULAR = 0, MEDIUM = 1, SEMI = 2, BOLD = 3, SERIF = 10, SERIF_ITALIC = 11;
    int LEFT = 0, CENTER = 1, RIGHT = 2;

    void save();
    void restore();
    void clipSquircle(float x, float y, float w, float h, float r);

    void fillRect(float x, float y, float w, float h, int color);
    void fillSquircle(float x, float y, float w, float h, float r, int color);
    /** Vertical gradient from top color to bottom color. */
    void fillSquircleV(float x, float y, float w, float h, float r, int top, int bottom);
    /** Hairline rim whose color fades from top to bottom (glass edge). */
    void strokeSquircleV(float x, float y, float w, float h, float r, float width, int top, int bottom);

    /** Soft radial glow: color at the center fading to transparent at radius r. */
    void glow(float cx, float cy, float r, int color);

    void fillCircle(float cx, float cy, float r, int color);
    void strokeCircle(float cx, float cy, float r, float width, int color);
    /** Arc in degrees, 0 = 3 o'clock, clockwise. Round caps. */
    void arc(float cx, float cy, float r, float startDeg, float sweepDeg, float width, int color);
    void line(float x1, float y1, float x2, float y2, float width, int color);
    /** Open polyline with round joins and caps. pts = x0,y0,x1,y1... */
    void polyline(float[] pts, float width, int color);
    void fillPolygon(float[] pts, int color);

    /** Draws text with its baseline at y. Returns the advance width. */
    float text(String s, float x, float y, float size, int weight, int color, int align, float trackingEm);
    float measure(String s, float size, int weight, float trackingEm);
    /** Distance from baseline to the top of capital letters, roughly. */
    float capHeight(float size, int weight);

    /** Draws an app icon (platform object) into the box, clipped to an iOS-style squircle. */
    void icon(Object img, float x, float y, float size);
}
