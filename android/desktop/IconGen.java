package app.freedev.glassdesk;

import javax.imageio.ImageIO;
import java.awt.*;
import java.awt.geom.Point2D;
import java.awt.image.BufferedImage;
import java.io.File;

/** Makes the app icon: frosted glass panels on a soft Apple-style gradient. */
public final class IconGen {
    static void background(Graphics2D g, float x, float y, float s) {
        g.setPaint(new GradientPaint(x, y, new Color(0xFF3D7BFF), x + s, y + s, new Color(0xFFB04DF0)));
        g.fill(new java.awt.geom.Rectangle2D.Float(x, y, s, s));
        g.setPaint(new RadialGradientPaint(new Point2D.Float(x + s * .85f, y + s * .9f), s * .7f, new float[]{0, 1},
                new Color[]{new Color(0xCCFF6FA8, true), new Color(0x00FF6FA8, true)}));
        g.fill(new java.awt.geom.Rectangle2D.Float(x, y, s, s));
        g.setPaint(new RadialGradientPaint(new Point2D.Float(x + s * .15f, y + s * .1f), s * .6f, new float[]{0, 1},
                new Color[]{new Color(0xAA5AD2FF, true), new Color(0x005AD2FF, true)}));
        g.fill(new java.awt.geom.Rectangle2D.Float(x, y, s, s));
    }

    /** Glass panels drawn in a box of size s (the visible icon area). */
    static void foreground(J2DG jg, float x, float y, float s) {
        // back panel
        float a = s * .2f, b = s * .2f, w = s * .44f, h = s * .44f, r = s * .1f;
        jg.fillSquircle(x + a, y + b, w, h, r, 0x55FFFFFF);
        jg.strokeSquircleV(x + a, y + b, w, h, r, s * .012f, 0xCCFFFFFF, 0x33FFFFFF);
        // front panel
        float a2 = s * .36f, b2 = s * .36f;
        jg.fillSquircle(x + a2, y + b2, w, h, r, 0x66FFFFFF);
        jg.fillSquircleV(x + a2, y + b2, w, h, r, 0x55FFFFFF, 0x08FFFFFF);
        jg.strokeSquircleV(x + a2, y + b2, w, h, r, s * .014f, 0xF2FFFFFF, 0x40FFFFFF);
        // little "widgets" inside the front panel
        float p = s * .06f;
        jg.fillSquircle(x + a2 + p, y + b2 + p, w * .36f, w * .36f, s * .035f, 0xF2FFFFFF);
        jg.fillSquircle(x + a2 + p + w * .44f, y + b2 + p, w * .36f, w * .36f, s * .035f, 0xB3FFFFFF);
        jg.fillSquircle(x + a2 + p, y + b2 + p + w * .44f, w - 2 * p, w * .26f, s * .035f, 0xB3FFFFFF);
    }

    public static void main(String[] args) throws Exception {
        String fonts = args[0];
        File res = new File(args[1]);
        // Legacy icon (squircle, 192px) and a large one for the HTML preview.
        for (int[] spec : new int[][]{{192, 0}, {512, 1}}) {
            int S = spec[0];
            BufferedImage img = new BufferedImage(S, S, BufferedImage.TYPE_INT_ARGB);
            Graphics2D g = img.createGraphics();
            J2DG jg = new J2DG(g, fonts);
            jg.save();
            jg.clipSquircle(0, 0, S, S, S * 0.225f);
            background(g, 0, 0, S);
            foreground(jg, 0, 0, S);
            jg.restore();
            g.dispose();
            File out = spec[1] == 0 ? new File(res, "mipmap-xxxhdpi-v4/ic_launcher.png") : new File(args[2], "icon.png");
            out.getParentFile().mkdirs();
            ImageIO.write(img, "png", out);
        }
        // Adaptive icon layers: 108dp at xxxhdpi = 432px; visible area is the middle 72dp (288px).
        int L = 432;
        BufferedImage bg = new BufferedImage(L, L, BufferedImage.TYPE_INT_ARGB);
        Graphics2D g = bg.createGraphics();
        g.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
        background(g, 0, 0, L);
        g.dispose();
        BufferedImage fg = new BufferedImage(L, L, BufferedImage.TYPE_INT_ARGB);
        Graphics2D g2 = fg.createGraphics();
        foreground(new J2DG(g2, fonts), L / 6f, L / 6f, L * 2 / 3f);
        g2.dispose();
        File dir = new File(res, "mipmap-xxxhdpi-v4");
        ImageIO.write(bg, "png", new File(dir, "ic_launcher_bg.png"));
        ImageIO.write(fg, "png", new File(dir, "ic_launcher_fg.png"));
    }
}
