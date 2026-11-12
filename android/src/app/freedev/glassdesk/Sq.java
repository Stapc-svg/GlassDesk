package app.freedev.glassdesk;

/** Apple-style "continuous corner" rounded rectangle (a soft squircle). */
public final class Sq {
    public interface Sink {
        void moveTo(float x, float y);
        void lineTo(float x, float y);
        void cubicTo(float x1, float y1, float x2, float y2, float x3, float y3);
        void close();
    }

    private Sq() {}

    public static void build(Sink p, float x, float y, float w, float h, float r) {
        float k = Math.min(r * 1.5f, Math.min(w, h) / 2f);
        float c = k * 0.35f;
        float R = x + w, B = y + h;
        p.moveTo(x + k, y);
        p.lineTo(R - k, y);
        p.cubicTo(R - c, y, R, y + c, R, y + k);
        p.lineTo(R, B - k);
        p.cubicTo(R, B - c, R - c, B, R - k, B);
        p.lineTo(x + k, B);
        p.cubicTo(x + c, B, x, B - c, x, B - k);
        p.lineTo(x, y + k);
        p.cubicTo(x, y + c, x + c, y, x + k, y);
        p.close();
    }
}
