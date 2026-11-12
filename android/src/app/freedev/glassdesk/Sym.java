package app.freedev.glassdesk;

/** Small line icons in the spirit of SF Symbols, drawn into a square box. */
public final class Sym {
    public static final int SUN = 1, MOON = 2, CLOUD = 3, CLOUD_SUN = 4, CLOUD_MOON = 5, RAIN = 6,
            DRIZZLE = 7, SNOW = 8, STORM = 9, FOG = 10,
            BATTERY = 20, CHIP = 21, DISK = 22, BOLT = 23,
            CHECK = 30, CHEVRON = 31, PLUS = 32, MINUS = 33, CHEVRON_DOWN = 34,
            GRID = 40, APPEARANCE = 41, DOC = 42, FOLDER = 43, INFO = 44,
            CALENDAR = 50, CLOCK = 51, QUOTE = 52, HOURGLASS = 53, LIST = 54, NOTE = 55,
            LOCATION = 56, REFRESH = 57, CODE = 58, SPARKLE = 59, THERMO = 60, SLIDERS = 61;

    public static final int SUN_YELLOW = 0xFFFFD60A, DROP_BLUE = 0xFF64D2FF, CLOUD_WHITE = 0xFFFFFFFF;

    private Sym() {}

    /** One-color glyph. */
    public static void draw(G g, int kind, float x, float y, float s, int color) {
        float w = s * 0.085f; // SF "regular" stroke
        switch (kind) {
            case SUN: sun(g, x, y, s, color, 1f); break;
            case MOON: moon(g, x, y, s, color); break;
            case CLOUD: cloud(g, x, y, s, color); break;
            case CLOUD_SUN: sun(g, x - s * .12f, y - s * .14f, s * .8f, color, 1f); cloud(g, x + s * .08f, y + s * .1f, s * .9f, color); break;
            case CLOUD_MOON: moon(g, x - s * .1f, y - s * .14f, s * .75f, color); cloud(g, x + s * .08f, y + s * .1f, s * .9f, color); break;
            case RAIN: case DRIZZLE: case SNOW: case STORM: case FOG:
                weather(g, kind, x, y, s, color, color, color); break;
            case BATTERY: {
                g.save();
                strokeRect(g, x + s * .06f, y + s * .3f, s * .76f, s * .4f, s * .12f, w, color);
                g.fillSquircle(x + s * .86f, y + s * .42f, s * .07f, s * .16f, s * .03f, color);
                g.fillSquircle(x + s * .14f, y + s * .38f, s * .44f, s * .24f, s * .05f, color);
                g.restore();
                break;
            }
            case BOLT: g.fillPolygon(new float[]{x + s * .58f, y + s * .08f, x + s * .2f, y + s * .56f, x + s * .48f, y + s * .56f,
                    x + s * .4f, y + s * .92f, x + s * .8f, y + s * .42f, x + s * .52f, y + s * .42f}, color); break;
            case CHIP: {
                strokeRect(g, x + s * .24f, y + s * .24f, s * .52f, s * .52f, s * .1f, w, color);
                g.fillSquircle(x + s * .38f, y + s * .38f, s * .24f, s * .24f, s * .05f, color);
                for (int i = 0; i < 3; i++) {
                    float o = s * (.36f + i * .14f);
                    g.line(x + o, y + s * .08f, x + o, y + s * .2f, w * .8f, color);
                    g.line(x + o, y + s * .8f, x + o, y + s * .92f, w * .8f, color);
                    g.line(x + s * .08f, y + o, x + s * .2f, y + o, w * .8f, color);
                    g.line(x + s * .8f, y + o, x + s * .92f, y + o, w * .8f, color);
                }
                break;
            }
            case DISK: {
                strokeRect(g, x + s * .1f, y + s * .3f, s * .8f, s * .4f, s * .12f, w, color);
                g.fillCircle(x + s * .7f, y + s * .5f, s * .055f, color);
                g.line(x + s * .24f, y + s * .5f, x + s * .5f, y + s * .5f, w, color);
                break;
            }
            case CHECK: g.polyline(new float[]{x + s * .24f, y + s * .52f, x + s * .42f, y + s * .7f, x + s * .76f, y + s * .32f}, s * .11f, color); break;
            case CHEVRON: g.polyline(new float[]{x + s * .38f, y + s * .2f, x + s * .66f, y + s * .5f, x + s * .38f, y + s * .8f}, s * .1f, color); break;
            case CHEVRON_DOWN: g.polyline(new float[]{x + s * .22f, y + s * .38f, x + s * .5f, y + s * .66f, x + s * .78f, y + s * .38f}, s * .1f, color); break;
            case PLUS: g.line(x + s * .5f, y + s * .2f, x + s * .5f, y + s * .8f, s * .1f, color);
                g.line(x + s * .2f, y + s * .5f, x + s * .8f, y + s * .5f, s * .1f, color); break;
            case MINUS: g.line(x + s * .22f, y + s * .5f, x + s * .78f, y + s * .5f, s * .1f, color); break;
            case GRID: {
                float q = s * .34f, a = s * .12f, b = s * .54f;
                g.fillSquircle(x + a, y + a, q, q, s * .07f, color);
                g.fillSquircle(x + b, y + a, q, q, s * .07f, color);
                g.fillSquircle(x + a, y + b, q, q, s * .07f, color);
                g.fillSquircle(x + b, y + b, q, q, s * .07f, color);
                break;
            }
            case APPEARANCE: {
                float cx = x + s / 2, cy = y + s / 2, r = s * .36f;
                g.strokeCircle(cx, cy, r, w, color);
                float[] p = new float[2 * 26];
                for (int i = 0; i <= 24; i++) {
                    double t = Math.toRadians(90 + i * 7.5);
                    p[2 * i] = cx + (float) Math.cos(t) * r;
                    p[2 * i + 1] = cy + (float) Math.sin(t) * r;
                }
                p[50] = cx; p[51] = cy - r;
                g.fillPolygon(p, color);
                break;
            }
            case DOC: {
                strokeRect(g, x + s * .2f, y + s * .1f, s * .6f, s * .8f, s * .12f, w, color);
                for (int i = 0; i < 3; i++) {
                    float ly = y + s * (.36f + i * .16f);
                    g.line(x + s * .34f, ly, x + s * (i == 2 ? .54f : .66f), ly, w, color);
                }
                break;
            }
            case FOLDER: {
                g.fillPolygon(new float[]{x + s * .08f, y + s * .26f, x + s * .38f, y + s * .26f, x + s * .46f, y + s * .34f,
                        x + s * .92f, y + s * .34f, x + s * .92f, y + s * .4f, x + s * .08f, y + s * .4f}, color);
                g.fillSquircle(x + s * .08f, y + s * .36f, s * .84f, s * .46f, s * .08f, color);
                break;
            }
            case INFO: {
                g.strokeCircle(x + s / 2, y + s / 2, s * .38f, w, color);
                g.fillCircle(x + s / 2, y + s * .32f, s * .055f, color);
                g.line(x + s / 2, y + s * .46f, x + s / 2, y + s * .7f, w * 1.1f, color);
                break;
            }
            case CALENDAR: {
                strokeRect(g, x + s * .12f, y + s * .18f, s * .76f, s * .7f, s * .14f, w, color);
                g.line(x + s * .14f, y + s * .38f, x + s * .86f, y + s * .38f, w, color);
                for (int r = 0; r < 2; r++) for (int c = 0; c < 3; c++)
                    g.fillCircle(x + s * (.32f + c * .18f), y + s * (.54f + r * .17f), s * .045f, color);
                break;
            }
            case CLOCK: {
                g.strokeCircle(x + s / 2, y + s / 2, s * .38f, w, color);
                g.polyline(new float[]{x + s * .5f, y + s * .28f, x + s * .5f, y + s * .5f, x + s * .66f, y + s * .6f}, w, color);
                break;
            }
            case QUOTE: {
                for (int i = 0; i < 2; i++) {
                    float ox = x + s * (.3f + i * .36f);
                    g.fillCircle(ox, y + s * .44f, s * .13f, color);
                    g.fillPolygon(new float[]{ox - s * .02f, y + s * .5f, ox + s * .13f, y + s * .44f, ox + s * .02f, y + s * .74f, ox - s * .1f, y + s * .74f}, color);
                }
                break;
            }
            case HOURGLASS: {
                g.line(x + s * .26f, y + s * .14f, x + s * .74f, y + s * .14f, w, color);
                g.line(x + s * .26f, y + s * .86f, x + s * .74f, y + s * .86f, w, color);
                g.polyline(new float[]{x + s * .32f, y + s * .14f, x + s * .32f, y + s * .28f, x + s * .5f, y + s * .5f, x + s * .68f, y + s * .28f, x + s * .68f, y + s * .14f}, w, color);
                g.polyline(new float[]{x + s * .32f, y + s * .86f, x + s * .32f, y + s * .72f, x + s * .5f, y + s * .5f, x + s * .68f, y + s * .72f, x + s * .68f, y + s * .86f}, w, color);
                g.fillPolygon(new float[]{x + s * .38f, y + s * .8f, x + s * .5f, y + s * .66f, x + s * .62f, y + s * .8f}, color);
                break;
            }
            case LIST: {
                for (int i = 0; i < 3; i++) {
                    float ly = y + s * (.28f + i * .22f);
                    g.fillCircle(x + s * .2f, ly, s * .065f, color);
                    g.line(x + s * .38f, ly, x + s * .84f, ly, w, color);
                }
                break;
            }
            case NOTE: {
                strokeRect(g, x + s * .14f, y + s * .14f, s * .72f, s * .72f, s * .16f, w, color);
                g.line(x + s * .3f, y + s * .4f, x + s * .7f, y + s * .4f, w, color);
                g.line(x + s * .3f, y + s * .6f, x + s * .56f, y + s * .6f, w, color);
                break;
            }
            case LOCATION: g.fillPolygon(new float[]{x + s * .8f, y + s * .2f, x + s * .16f, y + s * .48f, x + s * .46f, y + s * .54f, x + s * .52f, y + s * .84f}, color); break;
            case REFRESH: {
                g.arc(x + s / 2, y + s / 2, s * .32f, -60, 290, w, color);
                float ax = x + s / 2 + s * .32f * (float) Math.cos(Math.toRadians(-60));
                float ay = y + s / 2 + s * .32f * (float) Math.sin(Math.toRadians(-60));
                g.fillPolygon(new float[]{ax - s * .16f, ay - s * .06f, ax + s * .1f, ay - s * .12f, ax + s * .04f, ay + s * .14f}, color);
                break;
            }
            case CODE: {
                g.polyline(new float[]{x + s * .3f, y + s * .3f, x + s * .1f, y + s * .5f, x + s * .3f, y + s * .7f}, w, color);
                g.polyline(new float[]{x + s * .7f, y + s * .3f, x + s * .9f, y + s * .5f, x + s * .7f, y + s * .7f}, w, color);
                g.line(x + s * .58f, y + s * .22f, x + s * .42f, y + s * .78f, w, color);
                break;
            }
            case SPARKLE: {
                float cx = x + s / 2, cy = y + s / 2, R = s * .42f, r = s * .1f;
                g.fillPolygon(new float[]{cx, cy - R, cx + r, cy - r, cx + R, cy, cx + r, cy + r, cx, cy + R, cx - r, cy + r, cx - R, cy, cx - r, cy - r}, color);
                break;
            }
            case THERMO: {
                g.strokeCircle(x + s / 2, y + s * .7f, s * .16f, w, color);
                g.line(x + s / 2, y + s * .16f, x + s / 2, y + s * .54f, s * .2f, color);
                break;
            }
            case SLIDERS: {
                for (int i = 0; i < 3; i++) {
                    float ly = y + s * (.26f + i * .24f), kx = x + s * (i == 1 ? .64f : .36f);
                    g.line(x + s * .12f, ly, x + s * .88f, ly, w, color);
                    g.fillCircle(kx, ly, s * .09f, color);
                }
                break;
            }
            default: break;
        }
    }

    /** Multicolor weather glyph like the iOS Weather app. */
    public static void weather(G g, int kind, float x, float y, float s, int cloud, int accent, int drop) {
        switch (kind) {
            case SUN: sun(g, x, y, s, SUN_YELLOW, 1f); return;
            case MOON: moon(g, x, y, s, cloud); return;
            case CLOUD_SUN:
                sun(g, x - s * .12f, y - s * .14f, s * .8f, SUN_YELLOW, 1f);
                cloud(g, x + s * .08f, y + s * .1f, s * .9f, cloud); return;
            case CLOUD_MOON:
                moon(g, x - s * .1f, y - s * .14f, s * .75f, cloud);
                cloud(g, x + s * .08f, y + s * .1f, s * .9f, cloud); return;
            case CLOUD: cloud(g, x, y, s, cloud); return;
            default: break;
        }
        cloud(g, x, y - s * .14f, s, cloud);
        float w = s * .075f;
        if (kind == RAIN || kind == DRIZZLE) {
            int n = kind == RAIN ? 3 : 2;
            for (int i = 0; i < n; i++) {
                float dx = x + s * (n == 3 ? .3f + i * .2f : .38f + i * .24f), dy = y + s * .7f;
                g.line(dx, dy, dx - s * .06f, dy + s * (kind == RAIN ? .2f : .12f), w, drop);
            }
        } else if (kind == SNOW) {
            for (int i = 0; i < 3; i++) g.fillCircle(x + s * (.3f + i * .2f), y + s * (.78f + (i % 2) * .08f), s * .055f, cloud);
        } else if (kind == STORM) {
            float bx = x + s * .1f, by = y + s * .5f, b = s * .5f;
            g.fillPolygon(new float[]{bx + b * .58f, by + b * .08f, bx + b * .2f, by + b * .56f, bx + b * .48f, by + b * .56f,
                    bx + b * .4f, by + b * .92f, bx + b * .8f, by + b * .42f, bx + b * .52f, by + b * .42f}, SUN_YELLOW);
        } else if (kind == FOG) {
            g.line(x + s * .2f, y + s * .74f, x + s * .8f, y + s * .74f, w, cloud);
            g.line(x + s * .3f, y + s * .88f, x + s * .7f, y + s * .88f, w, cloud);
        }
    }

    /** Maps an Open-Meteo weather code to a glyph. */
    public static int forCode(int code, boolean day) {
        if (code < 0) return day ? CLOUD_SUN : CLOUD_MOON;
        if (code == 0) return day ? SUN : MOON;
        if (code <= 2) return day ? CLOUD_SUN : CLOUD_MOON;
        if (code == 3) return CLOUD;
        if (code == 45 || code == 48) return FOG;
        if (code >= 51 && code <= 57) return DRIZZLE;
        if ((code >= 61 && code <= 67) || (code >= 80 && code <= 82)) return RAIN;
        if ((code >= 71 && code <= 77) || code == 85 || code == 86) return SNOW;
        if (code >= 95) return STORM;
        return CLOUD;
    }

    static void strokeRect(G g, float x, float y, float w, float h, float r, float sw, int color) {
        g.strokeSquircleV(x, y, w, h, r, sw, color, color);
    }

    static void sun(G g, float x, float y, float s, int color, float k) {
        float cx = x + s / 2, cy = y + s / 2;
        g.fillCircle(cx, cy, s * .19f * k, color);
        for (int i = 0; i < 8; i++) {
            double a = Math.toRadians(i * 45);
            float c = (float) Math.cos(a), si = (float) Math.sin(a);
            g.line(cx + c * s * .29f, cy + si * s * .29f, cx + c * s * .4f, cy + si * s * .4f, s * .07f, color);
        }
    }

    static void cloud(G g, float x, float y, float s, int color) {
        g.fillSquircle(x + s * .12f, y + s * .5f, s * .76f, s * .28f, s * .13f, color);
        g.fillCircle(x + s * .36f, y + s * .54f, s * .17f, color);
        g.fillCircle(x + s * .57f, y + s * .45f, s * .21f, color);
        g.fillCircle(x + s * .74f, y + s * .6f, s * .14f, color);
    }

    static void moon(G g, float x, float y, float s, int color) {
        float cx = x + s * .52f, cy = y + s / 2, R = s * .32f;
        int n = 24;
        float[] p = new float[4 * (n + 1)];
        double tilt = Math.toRadians(-25);
        for (int i = 0; i <= n; i++) {
            double t = Math.toRadians(-90 + 180.0 * i / n);
            float ox = -(float) Math.cos(t) * R, oy = (float) Math.sin(t) * R;
            float ix = -(float) Math.cos(t) * R * 0.25f, iy = oy;
            p[2 * i] = cx + rotX(ox, oy, tilt); p[2 * i + 1] = cy + rotY(ox, oy, tilt);
            int j = 2 * (2 * n + 1 - i);
            p[j] = cx + rotX(ix, iy, tilt); p[j + 1] = cy + rotY(ix, iy, tilt);
        }
        g.fillPolygon(p, color);
    }

    private static float rotX(float x, float y, double a) { return (float) (x * Math.cos(a) - y * Math.sin(a)); }
    private static float rotY(float x, float y, double a) { return (float) (x * Math.sin(a) + y * Math.cos(a)); }
}
