package app.freedev.glassdesk;

import javax.imageio.ImageIO;
import java.awt.*;
import java.awt.geom.Point2D;
import java.awt.image.BufferedImage;
import java.io.File;
import java.util.Calendar;

/** Renders widget-picker previews for the APK and showcase images. Desktop only. */
public final class Preview {
    static String fonts;

    static Data sample(int fence) {
        Data a = new Data();
        a.preview = true;
        Calendar c = Calendar.getInstance();
        c.set(2026, Calendar.NOVEMBER, 12, 9, 41, 0);
        a.now = c.getTimeInMillis();
        a.batt = 82; a.charging = false;
        a.memUsed = 41; a.memTotal = 80; a.diskUsed = 71; a.diskTotal = 128;
        a.note = "Weekend groceries\nMilk, eggs, avocados, sourdough bread and coffee beans. Ask Dana about the picnic on Saturday.";
        a.todo = new String[]{"Call the dentist", "Finish GlassDesk 1.3", "Water the plants", "Pick up the package", "Book train tickets"};
        a.done = new boolean[]{false, true, false, false, false};
        a.city = "New York"; a.wxOk = true; a.temp = 21; a.code = 1; a.hi = 24; a.lo = 16; a.isDay = true;
        a.dDay = new String[]{"Fri", "Sat", "Sun"}; a.dCode = new int[]{2, 61, 0}; a.dHi = new int[]{23, 19, 22}; a.dLo = new int[]{15, 13, 14};
        a.quote = 0;
        a.cdTitle = "Winter break";
        Calendar cd = Calendar.getInstance(); cd.set(2026, Calendar.DECEMBER, 25, 0, 0, 0);
        a.cdDate = cd.getTimeInMillis();
        String[][] names = {
                {"Messages", "Mail", "Photos", "Camera", "Maps", "Music", "Weather", "Notes"},
                {"Calendar", "Clock", "Files", "Settings", "Reminders", "Code", "Notes", "Weather"},
                {"Music", "Photos", "Camera", "Messages", "Mail", "Maps", "Files", "Clock"}};
        a.fenceName = new String[]{"Favorites", "Work", "Fun"}[fence];
        a.labels = names[fence];
        a.icons = new Object[8];
        for (int i = 0; i < 8; i++) a.icons[i] = iconFor(a.labels[i]);
        return a;
    }

    static Object iconFor(String n) {
        switch (n) {
            case "Messages": return new J2DG.SampleIcon(0xFF5BF675, 0xFF0CBD2A, Sym.QUOTE);
            case "Mail": return new J2DG.SampleIcon(0xFF1E9BFF, 0xFF1366F0, Sym.DOC);
            case "Photos": return new J2DG.SampleIcon(0xFFFFB340, 0xFFFF375F, Sym.SPARKLE);
            case "Camera": return new J2DG.SampleIcon(0xFFA6A6AC, 0xFF6C6C72, -1);
            case "Maps": return new J2DG.SampleIcon(0xFF62D96B, 0xFF27A0A8, Sym.LOCATION);
            case "Music": return new J2DG.SampleIcon(0xFFFF6680, 0xFFFA233B, Sym.SPARKLE);
            case "Weather": return new J2DG.SampleIcon(0xFF48A8F5, 0xFF1D61D6, Sym.CLOUD_SUN);
            case "Notes": return new J2DG.SampleIcon(0xFFFFE066, 0xFFFFC300, Sym.NOTE);
            case "Calendar": return new J2DG.SampleIcon(0xFFFF6B5E, 0xFFE0302A, Sym.CALENDAR);
            case "Clock": return new J2DG.SampleIcon(0xFF3A3A3C, 0xFF1C1C1E, Sym.CLOCK);
            case "Files": return new J2DG.SampleIcon(0xFF52B6FF, 0xFF1E7BF2, Sym.FOLDER);
            case "Settings": return new J2DG.SampleIcon(0xFFB4B4BA, 0xFF7C7C82, Sym.SLIDERS);
            case "Reminders": return new J2DG.SampleIcon(0xFFFFFFFF, 0xFFE9E9EE, Sym.LIST);
            default: return new J2DG.SampleIcon(0xFF5E5CE6, 0xFF3634A3, Sym.CODE);
        }
    }

    static BufferedImage widget(int kind, int wdp, int hdp, float d, Style st, Data a) {
        BufferedImage img = new BufferedImage(Math.round(wdp * d), Math.round(hdp * d), BufferedImage.TYPE_INT_ARGB);
        Graphics2D g2 = img.createGraphics();
        new Draw(new J2DG(g2, fonts), img.getWidth(), img.getHeight(), d, st, a).draw(kind);
        g2.dispose();
        return img;
    }

    static void wallpaper(Graphics2D g, int w, int h, boolean dark) {
        g.setPaint(new GradientPaint(0, 0, new Color(dark ? 0xFF0E1A3C : 0xFF7FB2FF, true), 0, h, new Color(dark ? 0xFF3B1240 : 0xFFF2A1C8, true)));
        g.fillRect(0, 0, w, h);
        int[][] blobs = dark
                ? new int[][]{{20, 18, 60, 0xFF2D5BFF}, {85, 40, 55, 0xFFB43CFF}, {30, 75, 60, 0xFFFF4D8D}, {80, 92, 50, 0xFF00C2FF}}
                : new int[][]{{15, 15, 60, 0xFF5AC8FA}, {90, 35, 55, 0xFFBF5AF2}, {25, 70, 65, 0xFFFF9F0A}, {85, 90, 55, 0xFFFF375F}};
        for (int[] b : blobs) {
            float cx = w * b[0] / 100f, cy = h * b[1] / 100f, r = w * b[2] / 100f;
            g.setPaint(new RadialGradientPaint(new Point2D.Float(cx, cy), r, new float[]{0f, 1f},
                    new Color[]{new Color((0xB0 << 24) | (b[3] & 0xFFFFFF), true), new Color(b[3] & 0xFFFFFF, true)}));
            g.fillRect(0, 0, w, h);
        }
    }

    static void showcase(File out, int design, int theme, boolean wallDark) throws Exception {
        float d = 2f;
        int S = 170, M = 360, gap = 20, margin = 24;
        int[][] rows = {
                {Draw.CLOCK, 1}, {Draw.WEATHER, 0, Draw.CAL, 0}, {Draw.FENCE_A, 1}, {Draw.TODO, 0, Draw.NOTES, 0},
                {Draw.SYS, 1}, {Draw.COUNT, 0, Draw.QUOTE, 0}, {Draw.WEATHER, 1}, {Draw.CAL, 1}, {Draw.SYS, 0, Draw.FENCE_B, 0}};
        int W = Math.round((M + 2 * margin) * d);
        int H = Math.round((rows.length * (S + gap) - gap + 2 * margin + 30) * d);
        BufferedImage img = new BufferedImage(W, H, BufferedImage.TYPE_INT_ARGB);
        Graphics2D g = img.createGraphics();
        g.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
        wallpaper(g, W, H, wallDark);
        Style st = new Style(design, theme, wallDark, 0, 45);
        float y = (margin + 30) * d;
        for (int[] row : rows) {
            float x = margin * d;
            for (int i = 0; i < row.length; i += 2) {
                int kind = row[i];
                boolean med = row[i + 1] == 1;
                Data a = sample(kind == Draw.FENCE_B ? 1 : kind == Draw.FENCE_C ? 2 : 0);
                BufferedImage wi = widget(kind, med ? M : S, S, d, st, a);
                // soft shadow like iOS widgets
                if (!(design == Style.GLASSDESK && theme == Style.GLASS)) {
                    for (int k = 6; k > 0; k--) {
                        g.setColor(new Color(0, 0, 0, 5));
                        g.fillRoundRect(Math.round(x - k * d), Math.round(y - k * d + 4 * d), wi.getWidth() + Math.round(2 * k * d), wi.getHeight() + Math.round(2 * k * d), Math.round(40 * d), Math.round(40 * d));
                    }
                }
                g.drawImage(wi, Math.round(x), Math.round(y), null);
                x += (med ? M : S) * d + gap * d;
            }
            y += (S + gap) * d;
        }
        g.dispose();
        ImageIO.write(img, "png", out);
    }

    public static void main(String[] args) throws Exception {
        fonts = args[0];
        File pv = new File(args[1]);
        pv.mkdirs();
        String[] names = {"pv_clock", "pv_sys", "pv_cal", "pv_notes", "pv_todo", "pv_weather", "pv_quote", "pv_count", "pv_fencea", "pv_fenceb", "pv_fencec"};
        boolean[] medium = {true, false, false, false, false, true, true, false, true, true, true};
        Style light = new Style(Style.GLASSDESK, Style.DARK, false, 0, 45);
        for (int k = 0; k < names.length; k++) {
            Data a = sample(k >= 8 ? k - 8 : 0);
            BufferedImage img = widget(k, medium[k] ? 360 : 170, 170, 2.2f, light, a);
            ImageIO.write(img, "png", new File(pv, names[k] + ".png"));
        }
        if (args.length > 2) {
            File sc = new File(args[2]);
            sc.mkdirs();
            showcase(new File(sc, "gd_glass.png"), Style.GLASSDESK, Style.GLASS, true);
            showcase(new File(sc, "gd_dark.png"), Style.GLASSDESK, Style.DARK, false);
            showcase(new File(sc, "gd_light.png"), Style.GLASSDESK, Style.LIGHT, false);
            showcase(new File(sc, "clean_light.png"), Style.CLEAN, Style.LIGHT, false);
            showcase(new File(sc, "clean_dark.png"), Style.CLEAN, Style.DARK, true);
        }
    }
}
