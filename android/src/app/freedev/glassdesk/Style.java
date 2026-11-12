package app.freedev.glassdesk;

/**
 * Widget look. Two designs:
 *  - GLASSDESK (default): GlassDesk's own frosted navy glass with an accent glow, like the desktop app.
 *  - CLEAN: a clean, bright, rounded system-widget look with flat colors.
 * Each design has its own themes and accent palette.
 */
public final class Style {
    public static final int GLASSDESK = 0, CLEAN = 1;
    public static final int GLASS = 0, LIGHT = 1, DARK = 2, AUTO = 3;

    public static final String[] DESIGN_NAMES = {"GlassDesk", "Clean"};

    public static final String[] GD_ACCENT_NAMES = {"Default", "Ocean", "Sunset", "Forest", "Midnight", "Frost", "Rose"};
    static final int[] GD_ACCENTS = {0xFF7C9CFF, 0xFF4FC3F7, 0xFFFF8A65, 0xFF5BD68A, 0xFFA78BFA, 0xFF7DE3F0, 0xFFF48FB1};

    public static final String[] CL_ACCENT_NAMES = {"Blue", "Purple", "Pink", "Red", "Orange", "Yellow", "Green", "Graphite"};
    static final int[] CL_LIGHT = {0xFF007AFF, 0xFFAF52DE, 0xFFFF2D55, 0xFFFF3B30, 0xFFFF9500, 0xFFFFCC00, 0xFF34C759, 0xFF8E8E93};
    static final int[] CL_DARK = {0xFF0A84FF, 0xFFBF5AF2, 0xFFFF375F, 0xFFFF453A, 0xFFFF9F0A, 0xFFFFD60A, 0xFF30D158, 0xFF98989D};

    public final int design;
    public final boolean glass, dark, gd;
    public final int accent, tint;
    public final int bg, bg2, fg, sub, track, sep, onAccent, glow, rimTop, rimBottom;
    public final int green, red, orange, yellow;

    /** @param theme for GLASSDESK: GLASS/DARK/LIGHT; for CLEAN: AUTO/LIGHT/DARK.  @param tint 0..100 glass opacity */
    public Style(int design, int theme, boolean systemDark, int accentIndex, int tint) {
        this.design = design;
        this.tint = tint;
        gd = design == GLASSDESK;
        if (gd) {
            glass = theme == GLASS;
            dark = theme != LIGHT;
            int ac = GD_ACCENTS[clamp(accentIndex, GD_ACCENTS.length)];
            accent = ac;
            onAccent = 0xFF0B1020;
            if (theme == LIGHT) {
                int a = Math.min(255, alphaFor(tint) + 120);
                bg = (a << 24) | (mix(0xFFFFFFFF, ac, 0.05f) & 0xFFFFFF);
                bg2 = (a << 24) | (mix(0xFFF1F3FA, ac, 0.10f) & 0xFFFFFF);
                fg = 0xFF121626; sub = 0xFF5E667D; track = 0x1F121626; sep = 0x14121626;
                glow = withAlpha(ac, 0.22f); rimTop = 0xFFFFFFFF; rimBottom = 0x33FFFFFF;
                this.accentText = darker(ac);
            } else {
                int a = glass ? alphaFor(tint) + 40 : 255;
                a = Math.min(255, a);
                int top = mix(0xFF1A2140, ac, 0.16f), bottom = mix(0xFF0C1022, ac, 0.06f);
                bg = (a << 24) | (top & 0xFFFFFF);
                bg2 = (a << 24) | (bottom & 0xFFFFFF);
                fg = 0xFFF4F6FC; sub = 0xB3DCE2F0; track = 0x24FFFFFF; sep = 0x1AFFFFFF;
                glow = withAlpha(ac, glass ? 0.38f : 0.30f); rimTop = 0x8CFFFFFF; rimBottom = 0x14FFFFFF;
                this.accentText = ac;
            }
            green = dark ? 0xFF5BD68A : 0xFF1F9D55;
            red = dark ? 0xFFFF7A7A : 0xFFE0443A;
            orange = dark ? 0xFFFFB25B : 0xFFE07A10;
            yellow = dark ? 0xFFFFD866 : 0xFFC98A00;
        } else {
            glass = false;
            dark = theme == DARK || (theme == AUTO && systemDark);
            int ai = clamp(accentIndex, CL_LIGHT.length);
            accent = (dark ? CL_DARK : CL_LIGHT)[ai];
            this.accentText = accent;
            if (dark) {
                bg = bg2 = 0xFF1C1C1E; fg = 0xFFFFFFFF; sub = 0xFF98989E; track = 0xFF3A3A3C; sep = 0xFF38383A;
            } else {
                bg = bg2 = 0xFFFFFFFF; fg = 0xFF000000; sub = 0xFF8A8A8E; track = 0xFFE9E9EB; sep = 0xFFE3E3E6;
            }
            glow = 0; rimTop = rimBottom = 0;
            onAccent = ai == 5 ? 0xFF000000 : 0xFFFFFFFF;
            green = dark ? 0xFF30D158 : 0xFF34C759;
            red = dark ? 0xFFFF453A : 0xFFFF3B30;
            orange = dark ? 0xFFFF9F0A : 0xFFFF9500;
            yellow = dark ? 0xFFFFD60A : 0xFFE5A50A;
        }
    }

    /** Accent for text (darker on light glass so it stays readable). */
    public int accentText;

    public static String[] accentNames(int design) { return design == GLASSDESK ? GD_ACCENT_NAMES : CL_ACCENT_NAMES; }

    public static int accentColor(int design, int index, boolean dark) {
        if (design == GLASSDESK) return GD_ACCENTS[clamp(index, GD_ACCENTS.length)];
        return (dark ? CL_DARK : CL_LIGHT)[clamp(index, CL_LIGHT.length)];
    }

    static int clamp(int i, int n) { return Math.max(0, Math.min(n - 1, i)); }

    static int alphaFor(int tint) { return (int) (255 * (0.10f + Math.max(0, Math.min(100, tint)) / 100f * 0.62f)); }

    public static int argb(int a, int r, int g, int b) { return (a << 24) | (r << 16) | (g << 8) | b; }

    public static int withAlpha(int c, float a) { return ((int) (255 * a) << 24) | (c & 0xFFFFFF); }

    static int darker(int c) { return mix(c, 0xFF000000, 0.35f); }

    public static int mix(int a, int b, float t) {
        int r = 0;
        for (int s = 0; s < 32; s += 8) {
            int x = (a >>> s) & 255, y = (b >>> s) & 255;
            r |= ((int) (x + (y - x) * t) & 255) << s;
        }
        return r;
    }
}
