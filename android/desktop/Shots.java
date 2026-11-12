package app.freedev.glassdesk;

import javax.imageio.ImageIO;
import java.awt.*;
import java.awt.geom.Point2D;
import java.awt.geom.RoundRectangle2D;
import java.awt.image.BufferedImage;
import java.io.File;

/** Store screenshots (1080x1920): headline + a phone showing real widget renders. Desktop only. */
public final class Shots {
    static final int W = 1080, H = 1920;

    /** rows: each row is {kind} for a medium widget or {kindA, kindB} for two small ones. */
    static void shot(File out, String title, String sub, int design, int theme, boolean darkWall, int accent, int[][] rows) throws Exception {
        BufferedImage img = new BufferedImage(W, H, BufferedImage.TYPE_INT_ARGB);
        Graphics2D g = img.createGraphics();
        J2DG jg = new J2DG(g, Preview.fonts);
        boolean darkPage = design == Style.GLASSDESK || darkWall;

        // page backdrop
        if (darkPage) {
            g.setPaint(new GradientPaint(0, 0, new Color(0xFF0A0E1F), 0, H, new Color(0xFF12173A)));
            g.fillRect(0, 0, W, H);
            int ac = Style.accentColor(design, accent, true);
            jg.glow(W * 0.9f, -100, 900, Style.withAlpha(ac, 0.45f));
            jg.glow(-100, 700, 800, Style.withAlpha(0xFFB06CFF, 0.25f));
        } else {
            g.setPaint(new GradientPaint(0, 0, new Color(0xFFF5F6FB), 0, H, new Color(0xFFE6E9F5)));
            g.fillRect(0, 0, W, H);
            jg.glow(W * 0.9f, -100, 900, Style.withAlpha(0xFF7C9CFF, 0.35f));
            jg.glow(-100, 700, 800, Style.withAlpha(0xFFF5A1C8, 0.30f));
        }
        int ink = darkPage ? 0xFFF4F6FC : 0xFF0B0F22, subInk = darkPage ? 0xFFA9B2CC : 0xFF5E667D;
        jg.text(title, W / 2f, 168, 66, G.BOLD, ink, G.CENTER, -0.025f);
        jg.text(sub, W / 2f, 236, 32, G.MEDIUM, subInk, G.CENTER, 0);

        // phone body
        int px = 130, py = 300, pw = W - 260, ph = 1760;
        g.setColor(new Color(0, 0, 0, 90));
        for (int k = 0; k < 12; k++) {
            g.setColor(new Color(0, 0, 0, 8));
            g.fill(new RoundRectangle2D.Float(px - k * 3, py - k * 2 + 20, pw + k * 6, ph + k * 6, 130, 130));
        }
        g.setColor(new Color(0xFF1C1F2A));
        g.fill(new RoundRectangle2D.Float(px, py, pw, ph, 120, 120));
        g.setColor(new Color(0xFF3A3F52));
        g.setStroke(new BasicStroke(3));
        g.draw(new RoundRectangle2D.Float(px + 1.5f, py + 1.5f, pw - 3, ph - 3, 118, 118));
        int sx = px + 16, sy = py + 16, sw = pw - 32, sh = ph - 32;
        Shape clip = new RoundRectangle2D.Float(sx, sy, sw, sh, 100, 100);
        Shape old = g.getClip();
        g.clip(clip);
        g.translate(sx, sy);
        Preview.wallpaper(g, sw, sh, darkWall);
        // status bar
        int sbInk = 0xFFFFFFFF;
        jg.text("9:41", 64, 64, 30, G.SEMI, sbInk, G.LEFT, 0);
        g.setColor(new Color(0xFF0A0A0A));
        g.fillOval(sw / 2 - 18, 34, 36, 36);
        // battery + signal glyphs
        Sym.draw(jg, Sym.BATTERY, sw - 110, 34, 48, sbInk);
        for (int i = 0; i < 4; i++) g.fill(new java.awt.geom.Rectangle2D.Float(sw - 168 + i * 11, 64 - 8 - i * 6, 7, 8 + i * 6));
        g.setColor(Color.WHITE);
        for (int i = 0; i < 4; i++) g.fill(new RoundRectangle2D.Float(sw - 168 + i * 11, 64 - 8 - i * 6, 7, 8 + i * 6, 3, 3));

        // widgets
        float margin = 40, inner = sw - 2 * margin;
        float d = inner / 330f;
        float small = 155 * d, gap = inner - 2 * small, rowH = 155 * d;
        Style st = new Style(design, theme, darkWall, accent, 45);
        float y = 120;
        for (int[] row : rows) {
            if (row.length == 1) {
                Data a = Preview.sample(row[0] >= Draw.FENCE_A ? row[0] - Draw.FENCE_A : 0);
                a.quote = 7;
                BufferedImage wi = Preview.widget(row[0], 330, 155, d, st, a);
                g.drawImage(wi, Math.round(margin), Math.round(y), null);
            } else {
                for (int i = 0; i < 2; i++) {
                    Data a = Preview.sample(row[i] >= Draw.FENCE_A ? row[i] - Draw.FENCE_A : 0);
                    a.quote = 7;
                    BufferedImage wi = Preview.widget(row[i], 155, 155, d, st, a);
                    g.drawImage(wi, Math.round(margin + i * (small + gap)), Math.round(y), null);
                }
            }
            y += rowH + gap;
        }

        // dock
        float dockH = 190, dockY = (H - sy) - dockH - 70, dockX = 30, dockW = sw - 60;
        jg.fillSquircle(dockX, dockY, dockW, dockH, 70, darkWall ? 0x4D1A1F33 : 0x40FFFFFF);
        jg.strokeSquircleV(dockX, dockY, dockW, dockH, 70, 2, 0x80FFFFFF, 0x1AFFFFFF);
        String[] dock = {"Messages", "Mail", "Photos", "Music"};
        float ic = 132, step = (dockW - 4 * ic) / 5;
        for (int i = 0; i < 4; i++) jg.icon(Preview.iconFor(dock[i]), dockX + step + i * (ic + step), dockY + (dockH - ic) / 2, ic);

        g.translate(-sx, -sy);
        g.setClip(old);
        g.dispose();
        ImageIO.write(img, "png", out);
    }

    public static void main(String[] args) throws Exception {
        Preview.fonts = args[0];
        File dir = new File(args[1]);
        dir.mkdirs();
        int GD = Style.GLASSDESK, CL = Style.CLEAN;
        shot(new File(dir, "01_glassdesk.png"), "Your Home Screen, organized", "Beautiful widgets made of frosted glass", GD, Style.GLASS, true, 0,
                new int[][]{{Draw.CLOCK}, {Draw.WEATHER, Draw.CAL}, {Draw.FENCE_A}});
        shot(new File(dir, "02_fences.png"), "Fences for your apps", "Keep up to 8 favorite apps in one panel", GD, Style.GLASS, true, 4,
                new int[][]{{Draw.FENCE_A}, {Draw.FENCE_B}, {Draw.FENCE_C}});
        shot(new File(dir, "03_productive.png"), "Stay on top of your day", "To-do, notes and a countdown at a glance", GD, Style.GLASS, true, 1,
                new int[][]{{Draw.TODO, Draw.NOTES}, {Draw.SYS}, {Draw.COUNT, Draw.QUOTE}});
        shot(new File(dir, "04_weather.png"), "Weather and calendar", "Today and the next three days", GD, Style.DARK, false, 6,
                new int[][]{{Draw.WEATHER}, {Draw.CAL}, {Draw.CLOCK}});
        shot(new File(dir, "05_light.png"), "Glass, Dark or Light", "Seven accent colors to make it yours", GD, Style.LIGHT, false, 2,
                new int[][]{{Draw.CLOCK}, {Draw.TODO, Draw.CAL}, {Draw.FENCE_A}});
        shot(new File(dir, "06_clean.png"), "Or go Clean", "A bright, simple second design", CL, Style.LIGHT, false, 0,
                new int[][]{{Draw.CLOCK}, {Draw.WEATHER, Draw.CAL}, {Draw.TODO, Draw.NOTES}});
        shot(new File(dir, "07_clean_dark.png"), "Clean in the dark", "Follows your phone's dark mode", CL, Style.DARK, true, 0,
                new int[][]{{Draw.WEATHER}, {Draw.SYS, Draw.COUNT}, {Draw.FENCE_B}});
    }
}
