package app.freedev.glassdesk;

import java.text.SimpleDateFormat;
import java.util.Calendar;
import java.util.Date;
import java.util.Locale;

/** Draws every GlassDesk widget in an Apple (iOS / Liquid Glass) style. */
public final class Draw {
    public static final int CLOCK = 0, SYS = 1, CAL = 2, NOTES = 3, TODO = 4, WEATHER = 5, QUOTE = 6, COUNT = 7,
            FENCE_A = 8, FENCE_B = 9, FENCE_C = 10;

    /** Row weights shared with res/layout/w_rows.xml. */
    public static final float TODO_HEAD = 38, TODO_ROW = 20, TODO_FOOT = 12, TODO_TOTAL = 150;
    /** Weights shared with res/layout/w_grid.xml. */
    public static final float GRID_HEAD = 24, GRID_ROW = 38, GRID_TOTAL = 100;

    public static final String[][] QUOTES = {
            {"Simplicity is the ultimate sophistication.", "Leonardo da Vinci"},
            {"Quality is not an act, it is a habit.", "Aristotle"},
            {"The journey of a thousand miles begins with a single step.", "Lao Tzu"},
            {"It does not matter how slowly you go as long as you do not stop.", "Confucius"},
            {"Waste no more time arguing what a good man should be. Be one.", "Marcus Aurelius"},
            {"Luck is what happens when preparation meets opportunity.", "Seneca"},
            {"Well done is better than well said.", "Benjamin Franklin"},
            {"What we think, we become.", "Buddha"},
            {"The secret of getting ahead is getting started.", "Mark Twain"},
            {"Do what you can, with what you have, where you are.", "Theodore Roosevelt"},
            {"The only way to have a friend is to be one.", "Ralph Waldo Emerson"},
            {"Go confidently in the direction of your dreams.", "Henry David Thoreau"},
            {"He who has a why to live can bear almost any how.", "Friedrich Nietzsche"},
            {"Knowing yourself is the beginning of all wisdom.", "Aristotle"},
            {"Little by little, one travels far.", "Spanish proverb"},
            {"Fall seven times, stand up eight.", "Japanese proverb"},
            {"Design is not just what it looks like. Design is how it works.", "Steve Jobs"},
            {"Stay hungry. Stay foolish.", "Stewart Brand"},
    };

    final G g;
    final float w, h, d, pad, r;
    final Style s;
    final Data a;
    final Calendar cal = Calendar.getInstance();

    public Draw(G g, int w, int h, float density, Style s, Data a) {
        this.g = g; this.w = w; this.h = h; this.d = density; this.s = s; this.a = a;
        float m = Math.min(w, h);
        pad = Math.min(16 * d, m * 0.1f);
        r = Math.min(22 * d, m * 0.17f);
        cal.setTimeInMillis(a.now);
    }

    public void draw(int kind) {
        switch (kind) {
            case CLOCK: clock(); break;
            case SYS: sys(); break;
            case CAL: calendar(); break;
            case NOTES: notes(); break;
            case TODO: todo(); break;
            case WEATHER: weather(); break;
            case QUOTE: quote(); break;
            case COUNT: count(); break;
            default: fence(); break;
        }
    }

    // ---------------------------------------------------------------- panel

    void panel() {
        if (s.gd) {
            // GlassDesk: frosted glass tinted with the accent, a soft glow in the corner and a bright rim.
            g.fillSquircleV(0, 0, w, h, r, s.bg, s.bg2);
            g.save();
            g.clipSquircle(0, 0, w, h, r);
            g.glow(w * 0.92f, -h * 0.05f, Math.max(w, h) * 0.75f, s.glow);
            g.glow(-w * 0.05f, h * 1.05f, Math.max(w, h) * 0.5f, Style.withAlpha(s.accent, s.dark ? 0.12f : 0.10f));
            g.fillSquircleV(0, 0, w, h * 0.5f, r, s.dark ? 0x1AFFFFFF : 0x66FFFFFF, 0x00FFFFFF);
            g.restore();
            g.strokeSquircleV(d * .6f, d * .6f, w - d * 1.2f, h - d * 1.2f, r - d * .6f, d * 1.1f, s.rimTop, s.rimBottom);
            return;
        }
        if (s.glass) {
            g.fillSquircle(0, 0, w, h, r, s.bg);
            // Liquid Glass: a soft sheen across the top and a bright rim that fades downwards.
            g.fillSquircleV(0, 0, w, h, r, 0x2EFFFFFF, 0x05FFFFFF);
            g.strokeSquircleV(d * .6f, d * .6f, w - d * 1.2f, h - d * 1.2f, r - d * .6f, d * 1.2f, 0xA6FFFFFF, 0x1FFFFFFF);
            g.strokeSquircleV(d * 2f, d * 2f, w - d * 4f, h - d * 4f, r - d * 2f, d * .8f, 0x26FFFFFF, 0x00FFFFFF);
        } else {
            g.fillSquircle(0, 0, w, h, r, s.bg);
        }
    }

    float sp(float v) { return v * d; }

    // ---------------------------------------------------------------- text helpers

    /** Text that shrinks to fit, then ellipsizes. */
    float fit(String t, float x, float y, float maxW, float size, int weight, int color, int align, float tr) {
        float sz = size;
        float min = size * 0.7f;
        while (sz > min && g.measure(t, sz, weight, tr) > maxW) sz -= size * 0.05f;
        String out = t;
        if (g.measure(out, sz, weight, tr) > maxW) {
            while (out.length() > 1 && g.measure(out + "…", sz, weight, tr) > maxW) out = out.substring(0, out.length() - 1);
            out = out.trim() + "…";
        }
        g.text(out, x, y, sz, weight, color, align, tr);
        return sz;
    }

    String ellipsize(String t, float maxW, float size, int weight) {
        if (g.measure(t, size, weight, 0) <= maxW) return t;
        String out = t;
        while (out.length() > 1 && g.measure(out + "…", size, weight, 0) > maxW) out = out.substring(0, out.length() - 1);
        return out.trim() + "…";
    }

    /** Word-wraps text. Returns the number of lines drawn (or that would be drawn if dry). */
    int wrap(String t, float x, float y, float maxW, float size, float lineH, int maxLines, int weight, int color, boolean dry) {
        String[] paras = t.split("\n");
        int lines = 0;
        for (int pi = 0; pi < paras.length && lines < maxLines; pi++) {
            String[] words = paras[pi].trim().split(" +");
            StringBuilder line = new StringBuilder();
            for (int i = 0; i < words.length; i++) {
                String test = line.length() == 0 ? words[i] : line + " " + words[i];
                if (g.measure(test, size, weight, 0) <= maxW || line.length() == 0) {
                    line.setLength(0); line.append(test);
                } else {
                    if (lines == maxLines - 1) {
                        if (!dry) g.text(ellipsize(line + " " + words[i] + "…", maxW, size, weight), x, y + lines * lineH, size, weight, color, G.LEFT, 0);
                        return lines + 1;
                    }
                    if (!dry) g.text(line.toString(), x, y + lines * lineH, size, weight, color, G.LEFT, 0);
                    lines++;
                    line.setLength(0); line.append(words[i]);
                }
            }
            if (line.length() > 0) {
                boolean last = lines == maxLines - 1 && pi < paras.length - 1;
                if (!dry) g.text(last ? ellipsize(line + "…", maxW, size, weight) : ellipsize(line.toString(), maxW, size, weight), x, y + lines * lineH, size, weight, color, G.LEFT, 0);
                lines++;
            }
        }
        return lines;
    }

    String fmt(String pattern, long t) { return new SimpleDateFormat(pattern, Locale.getDefault()).format(new Date(t)); }

    String caps(String t) { return t.toUpperCase(Locale.getDefault()); }

    /** Small accent glyph in a filled circle or squircle, like an iOS app badge. */
    void badge(int kind, float x, float y, float size, int color, boolean circle) {
        if (circle) g.fillCircle(x + size / 2, y + size / 2, size / 2, color);
        else g.fillSquircle(x, y, size, size, size * .26f, color);
        int ink = color == s.accent ? s.onAccent : 0xFFFFFFFF;
        Sym.draw(g, kind, x + size * .18f, y + size * .18f, size * .64f, ink);
    }

    /** Widget title row. Clean: colored badge + bold title. GlassDesk: accent glyph + small caps label. */
    void head(int sym, String label, int cleanColor, float maxW) {
        if (s.gd) {
            float gs = sp(13);
            Sym.draw(g, sym, pad, pad + sp(3), gs, s.accent);
            fit(caps(label), pad + gs + sp(7), pad + sp(14), maxW - gs - sp(7), sp(11), G.SEMI, s.sub, G.LEFT, 0.08f);
        } else {
            float bs = sp(20);
            badge(sym, pad, pad, bs, cleanColor, false);
            fit(label, pad + bs + sp(7), pad + bs / 2 + sp(5), maxW - bs - sp(7), sp(14), G.SEMI, cleanColor, G.LEFT, 0);
        }
    }

    public static String greeting(int hour) {
        if (hour < 5) return "Good night";
        if (hour < 12) return "Good morning";
        if (hour < 17) return "Good afternoon";
        if (hour < 22) return "Good evening";
        return "Good night";
    }

    // ---------------------------------------------------------------- clock

    /** In a real widget the time is a live TextClock on top; previews draw it here. */
    void clock() {
        panel();
        boolean wide = w > h * 2.1f;
        if (a.preview) {
            float timeSz = Math.min(sp(50), h * 0.36f);
            float l1 = sp(13), l3 = sp(14);
            float block = l1 * 1.3f + timeSz * 1.02f + l3 * 1.35f;
            float y = (h - block) / 2;
            if (s.gd) {
                g.text(caps(greeting(cal.get(Calendar.HOUR_OF_DAY))), pad + sp(2), y + g.capHeight(l1, G.SEMI), sp(12), G.SEMI, s.accentText, G.LEFT, 0.08f);
                y += l1 * 1.3f;
                g.text(fmt(a.h24 ? "H:mm" : "h:mm", a.now), pad, y + g.capHeight(timeSz, G.REGULAR) + timeSz * 0.06f, timeSz, G.REGULAR, s.fg, G.LEFT, -0.03f);
                y += timeSz * 1.02f;
                g.text(fmt("EEEE, MMMM d", a.now), pad + sp(2), y + l3, l3, G.MEDIUM, s.sub, G.LEFT, 0);
            } else {
                g.text(caps(fmt("EEEE, MMMM d", a.now)), pad + sp(2), y + g.capHeight(l1, G.SEMI), l1, G.SEMI, s.accent, G.LEFT, 0.02f);
                y += l1 * 1.3f;
                g.text(fmt(a.h24 ? "H:mm" : "h:mm", a.now), pad, y + g.capHeight(timeSz, G.SEMI) + timeSz * 0.06f, timeSz, G.SEMI, s.fg, G.LEFT, -0.035f);
                y += timeSz * 1.02f;
                g.text(greeting(cal.get(Calendar.HOUR_OF_DAY)), pad + sp(2), y + l3, l3, G.MEDIUM, s.sub, G.LEFT, 0);
            }
        }
        if (!wide) return;
        // Right side: today's weather and to-dos, like a Smart Stack summary.
        float rx = w - pad, ty = pad + sp(6);
        if (a.city != null && a.wxOk) {
            float ic = sp(30);
            Sym.weather(g, Sym.forCode(a.code, a.isDay), rx - ic, ty, ic, s.dark ? 0xFFFFFFFF : 0xFFB8C4D2, s.accent, Sym.DROP_BLUE);
            g.text(a.temp + "°", rx - ic - sp(6), ty + sp(22), sp(24), G.SEMI, s.fg, G.RIGHT, -0.02f);
            g.text(ellipsize(a.city, w * 0.3f, sp(12), G.MEDIUM), rx, ty + sp(46), sp(12), G.MEDIUM, s.sub, G.RIGHT, 0);
        }
        int left = 0;
        for (int i = 0; i < a.done.length; i++) if (!a.done[i]) left++;
        if (a.todo.length > 0) {
            float by = h - pad - sp(4);
            String t = left == 0 ? "All done" : left + (left == 1 ? " to-do" : " to-dos");
            float tw = g.text(t, rx, by, sp(13), G.SEMI, s.fg, G.RIGHT, 0);
            float bs = sp(18);
            badge(Sym.LIST, rx - tw - sp(6) - bs, by - bs + sp(4), bs, s.accent, true);
        }
    }

    // ---------------------------------------------------------------- system

    void sys() {
        panel();
        int battColor = a.batt >= 0 && a.batt <= 20 && !a.charging ? s.red : s.green;
        float battF = a.batt < 0 ? 0 : a.batt / 100f;
        float memF = a.memTotal > 0 ? (float) a.memUsed / a.memTotal : 0;
        float diskF = a.diskTotal > 0 ? (float) a.diskUsed / a.diskTotal : 0;
        int[] kinds = {a.charging ? Sym.BOLT : Sym.BATTERY, Sym.CHIP, Sym.DISK};
        float[] fr = {battF, memF, diskF};
        int[] col = {battColor, s.accent, s.orange};
        String[] val = {a.batt < 0 ? "–" : a.batt + "%", Math.round(memF * 100) + "%", Math.round(diskF * 100) + "%"};
        String[] lab = {a.charging ? "Charging" : "Battery", "Memory", "Storage"};

        if (w >= h * 1.45f) {
            float cw = (w - 2 * pad) / 3f;
            float textH = sp(15) * 1.25f + sp(11) * 1.3f;
            float D = Math.min(cw * 0.74f, h - 2 * pad - textH - sp(8));
            float top = (h - (D + sp(8) + textH)) / 2;
            for (int i = 0; i < 3; i++) {
                float cx = pad + cw * i + cw / 2;
                ring(cx, top + D / 2, D / 2, fr[i], col[i], kinds[i], i == 0 && a.charging);
                float ty = top + D + sp(8) + sp(15);
                g.text(val[i], cx, ty, sp(15), G.SEMI, s.fg, G.CENTER, 0);
                g.text(lab[i], cx, ty + sp(15), sp(11), G.MEDIUM, s.sub, G.CENTER, 0);
            }
        } else {
            // Square: battery ring on top, memory and storage as capsule bars underneath.
            float D = Math.min(w * 0.36f, h * 0.38f);
            ring(pad + D / 2, pad + D / 2, D / 2, fr[0], col[0], kinds[0], a.charging);
            float tx = pad + D + sp(12);
            g.text(val[0], tx, pad + D / 2 + sp(4), sp(24), G.SEMI, s.fg, G.LEFT, -0.02f);
            g.text(lab[0], tx, pad + D / 2 + sp(20), sp(12), G.MEDIUM, s.sub, G.LEFT, 0);
            float y = pad + D + (h - pad - (pad + D)) * 0.16f;
            float rowH = (h - pad - y) / 2f;
            for (int i = 1; i < 3; i++) {
                float by = y + rowH * (i - 1);
                Sym.draw(g, kinds[i], pad, by + sp(1), sp(14), s.sub);
                g.text(lab[i], pad + sp(18), by + sp(12), sp(12), G.MEDIUM, s.fg, G.LEFT, 0);
                g.text(val[i], w - pad, by + sp(12), sp(12), G.SEMI, s.sub, G.RIGHT, 0);
                float barY = by + sp(18), bw = w - 2 * pad, bh = sp(6);
                g.fillSquircle(pad, barY, bw, bh, bh / 2, s.track);
                if (fr[i] > 0.01f) g.fillSquircle(pad, barY, Math.max(bh, bw * fr[i]), bh, bh / 2, col[i]);
            }
        }
    }

    void ring(float cx, float cy, float rad, float f, int color, int kind, boolean charging) {
        float sw = Math.max(sp(4), rad * 0.2f);
        float rr = rad - sw / 2;
        g.strokeCircle(cx, cy, rr, sw, s.track);
        if (f > 0.005f) g.arc(cx, cy, rr, -90, Math.max(2f, 360 * Math.min(1f, f)), sw, color);
        float ic = rad * 0.9f;
        Sym.draw(g, kind, cx - ic / 2, cy - ic / 2, ic, charging ? s.green : s.fg);
    }

    // ---------------------------------------------------------------- calendar

    void calendar() {
        panel();
        boolean wide = w > h * 1.7f;
        float gx = pad, gw = w - 2 * pad;
        int today = cal.get(Calendar.DAY_OF_MONTH);
        int hot = s.gd ? s.accentText : s.red;
        if (wide) {
            float lw = w * 0.36f;
            if (wide) g.text(caps(fmt("EEEE", a.now)), pad + sp(1), pad + sp(12), sp(12), G.SEMI, hot, G.LEFT, s.gd ? 0.08f : 0.02f);
            float big = Math.min(sp(48), h * 0.34f);
            g.text(String.valueOf(today), pad - sp(1), pad + sp(18) + g.capHeight(big, G.REGULAR), big, G.REGULAR, s.fg, G.LEFT, -0.03f);
            int week = cal.get(Calendar.WEEK_OF_YEAR);
            int left = 0;
            for (int i = 0; i < a.done.length; i++) if (!a.done[i]) left++;
            float by = h - pad;
            g.text("Week " + week, pad + sp(1), by - sp(16), sp(12), G.MEDIUM, s.sub, G.LEFT, 0);
            g.text(left == 0 ? "No to-dos left" : left + (left == 1 ? " to-do left" : " to-dos left"), pad + sp(1), by - sp(1), sp(12), G.MEDIUM, s.sub, G.LEFT, 0);
            gx = lw; gw = w - lw - pad;
            g.line(lw - sp(10), pad, lw - sp(10), h - pad, Math.max(1f, d * .5f), s.sep);
        }
        float hdr = sp(12);
        g.text(caps(fmt(s.gd ? "MMMM yyyy" : "MMMM", a.now)), gx + sp(2), pad + hdr, s.gd ? sp(11) : hdr, G.SEMI,
                s.gd ? s.sub : (wide ? s.fg : s.red), G.LEFT, s.gd ? 0.08f : 0.02f);
        float top = pad + hdr + sp(6);
        Calendar c = (Calendar) cal.clone();
        c.set(Calendar.DAY_OF_MONTH, 1);
        int first = c.getFirstDayOfWeek();
        int offset = (c.get(Calendar.DAY_OF_WEEK) - first + 7) % 7;
        int days = c.getActualMaximum(Calendar.DAY_OF_MONTH);
        int rows = (offset + days + 6) / 7;
        float cw = gw / 7f, ch = (h - pad - top) / (rows + 1);
        float fs = Math.min(Math.min(ch * 0.56f, cw * 0.5f), sp(13));
        Calendar wd = (Calendar) c.clone();
        for (int i = 0; i < 7; i++) {
            wd.set(Calendar.DAY_OF_WEEK, ((first - 1 + i) % 7) + 1);
            String n = fmt("EEE", wd.getTimeInMillis());
            n = n.isEmpty() ? "" : n.substring(0, 1);
            g.text(caps(n),gx + cw * i + cw / 2, top + ch / 2 + fs * 0.36f, fs * 0.82f, G.SEMI, s.sub, G.CENTER, 0);
        }
        for (int day = 1; day <= days; day++) {
            int idx = offset + day - 1, row = idx / 7 + 1, col = idx % 7;
            float cx = gx + cw * col + cw / 2, cy = top + ch * row + ch / 2;
            boolean isToday = day == today;
            if (isToday) {
                if (s.gd) g.glow(cx, cy, Math.min(cw, ch) * 0.9f, Style.withAlpha(s.accent, 0.45f));
                g.fillCircle(cx, cy, Math.min(cw, ch) * 0.47f, s.gd ? s.accent : s.red);
            }
            int weekdayIdx = (first - 1 + col) % 7 + 1;
            boolean weekend = weekdayIdx == Calendar.SATURDAY || weekdayIdx == Calendar.SUNDAY;
            int color = isToday ? (s.gd ? s.onAccent : 0xFFFFFFFF) : (day < today ? s.sub : (weekend ? Style.mix(s.fg, s.sub, 0.45f) : s.fg));
            g.text(String.valueOf(day), cx, cy + fs * 0.36f, fs, isToday ? G.BOLD : G.MEDIUM, color, G.CENTER, 0);
        }
    }

    // ---------------------------------------------------------------- notes

    void notes() {
        panel();
        float bs = sp(20);
        head(Sym.NOTE, s.gd ? "Note" : "Notes", s.yellow, w - 2 * pad);
        String t = a.note == null ? "" : a.note.trim();
        float y = pad + bs + sp(14);
        float maxW = w - 2 * pad;
        if (t.isEmpty()) {
            g.text("No note yet", pad, y + sp(14), sp(15), G.SEMI, s.fg, G.LEFT, 0);
            g.text("Tap to write one", pad, y + sp(33), sp(13), G.REGULAR, s.sub, G.LEFT, 0);
            return;
        }
        int nl = t.indexOf('\n');
        String title = nl < 0 ? t : t.substring(0, nl).trim();
        String body = nl < 0 ? "" : t.substring(nl + 1).trim();
        float ts = sp(16);
        int tl = wrap(title, pad, y + ts, maxW, ts, ts * 1.22f, body.isEmpty() ? 4 : 2, G.SEMI, s.fg, false);
        y += ts + (tl - 1) * ts * 1.22f + sp(6);
        float bsz = sp(13), lh = bsz * 1.32f;
        int maxLines = (int) ((h - pad - sp(14) - y) / lh);
        if (!body.isEmpty() && maxLines > 0) wrap(body, pad, y + bsz, maxW, bsz, lh, maxLines, G.REGULAR, s.sub, false);
        g.text(fmt("MMM d", a.now), pad, h - pad + sp(2), sp(11), G.MEDIUM, s.sub, G.LEFT, 0);
    }

    // ---------------------------------------------------------------- to-do (Reminders)

    void todo() {
        panel();
        float headH = h * TODO_HEAD / TODO_TOTAL, rowH = h * TODO_ROW / TODO_TOTAL;
        int left = 0;
        for (int i = 0; i < a.done.length; i++) if (!a.done[i]) left++;
        float bs = Math.min(sp(24), headH * 0.62f);
        float by = pad * 0.9f;
        if (s.gd) {
            head(Sym.LIST, "To-do", s.accent, w * 0.6f);
            float cs = Math.min(sp(26), headH * 0.66f);
            g.text(String.valueOf(left), w - pad, pad + sp(16), cs, G.REGULAR, s.fg, G.RIGHT, -0.02f);
        } else {
            badge(Sym.LIST, pad, by, bs, s.accent, true);
            g.text("To-do", pad + bs + sp(7), by + bs / 2 + sp(5), sp(15), G.BOLD, s.accent, G.LEFT, 0);
            float cs = Math.min(sp(28), headH * 0.7f);
            g.text(String.valueOf(left), w - pad, by + bs / 2 + g.capHeight(cs, G.BOLD) / 2, cs, G.BOLD, s.fg, G.RIGHT, -0.02f);
        }
        if (a.todo.length == 0) {
            g.text("Nothing to do", pad, headH + rowH * 1.2f, sp(15), G.SEMI, s.fg, G.LEFT, 0);
            wrap("Add tasks in the GlassDesk app.", pad, headH + rowH * 2.1f, w - 2 * pad, sp(13), sp(17), 2, G.REGULAR, s.sub, false);
            return;
        }
        int n = Math.min(5, a.todo.length);
        float ts = Math.min(sp(14), rowH * 0.58f);
        float cr = Math.min(sp(8), rowH * 0.34f);
        for (int i = 0; i < n; i++) {
            float y = headH + rowH * i, cy = y + rowH / 2;
            float cx = pad + cr;
            if (a.done[i]) {
                g.fillCircle(cx, cy, cr, s.accent);
                Sym.draw(g, Sym.CHECK, cx - cr * .8f, cy - cr * .8f, cr * 1.6f, s.onAccent);
            } else {
                g.strokeCircle(cx, cy, cr - d * .8f, d * 1.5f, s.gd ? Style.withAlpha(s.accent, 0.8f) : Style.mix(s.sub, s.bg | 0xFF000000, 0.2f));
            }
            float tx = pad + cr * 2 + sp(10);
            String t = ellipsize(a.todo[i], w - pad - tx, ts, G.REGULAR);
            g.text(t, tx, cy + ts * 0.36f, ts, G.REGULAR, a.done[i] ? s.sub : s.fg, G.LEFT, 0);
            if (a.done[i]) g.line(tx, cy - ts * 0.02f, tx + g.measure(t, ts, G.REGULAR, 0), cy - ts * 0.02f, Math.max(1f, d * .9f), s.sub);
            if (i < n - 1) g.line(tx, y + rowH, w - pad, y + rowH, Math.max(1f, d * .5f), s.sep);
        }
        if (a.todo.length > 5) {
            g.text("+" + (a.todo.length - 5) + " more", pad + cr * 2 + sp(10), h - pad * 0.5f, sp(11), G.MEDIUM, s.sub, G.LEFT, 0);
        }
    }

    // ---------------------------------------------------------------- weather

    int[] sky() {
        int code = a.code;
        boolean night = !a.isDay;
        if (!a.wxOk) return new int[]{0xFF3A7BD5, 0xFF6FA8E8};
        if (code >= 95) return new int[]{0xFF2B3240, 0xFF4E596B};
        if (code >= 51 && code != 85 && code != 86 && !(code >= 71 && code <= 77)) return night ? new int[]{0xFF1B2433, 0xFF34425A} : new int[]{0xFF4D6178, 0xFF7D90A6};
        if ((code >= 71 && code <= 77) || code == 85 || code == 86) return night ? new int[]{0xFF2A3446, 0xFF4E5C74} : new int[]{0xFF7F97B2, 0xFFAFC0D3};
        if (code == 3 || code == 45 || code == 48) return night ? new int[]{0xFF1E2636, 0xFF3A4458} : new int[]{0xFF61758C, 0xFF94A6BA};
        return night ? new int[]{0xFF0B1631, 0xFF2A3C66} : new int[]{0xFF2F7BD6, 0xFF6CB3EE};
    }

    String condition(int code) {
        if (code < 0) return "–";
        if (code == 0) return a.isDay ? "Sunny" : "Clear";
        if (code == 1) return "Mostly Clear";
        if (code == 2) return "Partly Cloudy";
        if (code == 3) return "Cloudy";
        if (code == 45 || code == 48) return "Foggy";
        if (code >= 51 && code <= 57) return "Drizzle";
        if (code >= 61 && code <= 67) return "Rain";
        if (code >= 71 && code <= 77) return "Snow";
        if (code >= 80 && code <= 82) return "Showers";
        if (code == 85 || code == 86) return "Snow Showers";
        if (code >= 95) return "Thunderstorms";
        return "Cloudy";
    }

    void weather() {
        int white, soft, dim, cloudC;
        if (s.gd) {
            panel();
            white = s.fg; soft = s.sub; dim = s.sub; cloudC = s.dark ? 0xFFFFFFFF : 0xFFAEB8CC;
        } else {
            int[] sk = sky();
            g.fillSquircleV(0, 0, w, h, r, sk[0], sk[1]);
            white = 0xFFFFFFFF; soft = 0xCCFFFFFF; dim = 0x99FFFFFF; cloudC = 0xFFFFFFFF;
        }
        boolean wide = w > h * 1.7f;
        float colW = wide ? w * 0.46f : w - 2 * pad;
        if (a.city == null || !a.wxOk) {
            g.text(a.city == null ? "Weather" : a.city, pad, pad + sp(14), sp(15), G.SEMI, white, G.LEFT, 0);
            float ic = Math.min(sp(46), h * 0.32f);
            Sym.weather(g, Sym.CLOUD_SUN, pad - sp(2), pad + sp(24), ic, cloudC, s.accent, Sym.DROP_BLUE);
            wrap(a.city == null ? "Set your city in the GlassDesk app." : "Tap to load the forecast.", pad, h - pad - sp(18), w - 2 * pad, sp(13), sp(16), 2, G.SEMI, soft, false);
            return;
        }
        float cityW = fit(a.city, pad, pad + sp(14), colW - sp(16), sp(15), G.SEMI, white, G.LEFT, 0);
        float cw = Math.min(g.measure(a.city, cityW, G.SEMI, 0), colW - sp(16));
        Sym.draw(g, Sym.LOCATION, pad + cw + sp(4), pad + sp(3), sp(11), s.gd ? s.accent : white);
        float big = Math.min(sp(46), h * 0.33f);
        g.text(a.temp + "°", pad - sp(1), pad + sp(20) + g.capHeight(big, G.REGULAR), big, G.REGULAR, white, G.LEFT, -0.03f);
        float by = h - pad;
        float ic = sp(17);
        Sym.weather(g, Sym.forCode(a.code, a.isDay), pad - sp(1), by - sp(17) - ic + sp(3), ic, cloudC, s.accent, Sym.DROP_BLUE);
        g.text(ellipsize(condition(a.code), colW - ic - sp(4), sp(13), G.SEMI), pad + ic + sp(3), by - sp(18), sp(13), G.SEMI, white, G.LEFT, 0);
        g.text("H:" + a.hi + "°  L:" + a.lo + "°", pad, by - sp(1), sp(13), G.SEMI, white, G.LEFT, 0);
        if (wide && a.dDay.length > 0) {
            int n = Math.min(3, a.dDay.length);
            float x0 = w * 0.5f, cwid = (w - pad - x0) / n;
            for (int i = 0; i < n; i++) {
                float cx = x0 + cwid * i + cwid / 2;
                g.text(a.dDay[i], cx, pad + sp(14), sp(12), G.SEMI, soft, G.CENTER, 0);
                float fi = Math.min(sp(28), cwid * 0.6f);
                Sym.weather(g, Sym.forCode(a.dCode[i], true), cx - fi / 2, h / 2 - fi / 2 - sp(2), fi, cloudC, s.accent, Sym.DROP_BLUE);
                g.text(a.dHi[i] + "°", cx, h - pad - sp(16), sp(14), G.SEMI, white, G.CENTER, 0);
                g.text(a.dLo[i] + "°", cx, h - pad - sp(1), sp(13), G.SEMI, dim, G.CENTER, 0);
            }
        }
    }

    // ---------------------------------------------------------------- quote

    void quote() {
        panel();
        String[] q = QUOTES[((a.quote % QUOTES.length) + QUOTES.length) % QUOTES.length];
        float bs = sp(20);
        head(Sym.QUOTE, "Quote of the Day", s.accent, w - 2 * pad);
        float top = pad + bs + sp(12);
        float bottom = h - pad - sp(22);
        float maxW = w - 2 * pad;
        float size = sp(21);
        String text = "“" + q[0] + "”";
        int lines;
        while (true) {
            float lh = size * 1.25f;
            int maxL = Math.max(1, (int) ((bottom - top) / lh));
            lines = wrap(text, 0, 0, maxW, size, lh, 99, G.SERIF, s.fg, true);
            if (lines <= maxL || size <= sp(11)) break;
            size -= sp(1);
        }
        float lh = size * 1.25f;
        int maxL = Math.max(1, (int) ((bottom - top) / lh));
        float used = Math.min(lines, maxL) * lh;
        float y = top + Math.max(0, (bottom - top - used) / 2) + size;
        wrap(text, pad, y, maxW, size, lh, maxL, G.SERIF, s.fg, false);
        g.text("— " + q[1], pad, h - pad, sp(12), G.MEDIUM, s.sub, G.LEFT, 0);
    }

    // ---------------------------------------------------------------- countdown

    void count() {
        panel();
        String title = a.cdTitle == null || a.cdTitle.trim().isEmpty() ? "Countdown" : a.cdTitle.trim();
        if (s.gd) head(Sym.HOURGLASS, title, s.accent, w - 2 * pad);
        else {
            float bs = sp(20);
            badge(Sym.HOURGLASS, pad, pad, bs, s.accent, false);
            fit(title, pad + bs + sp(7), pad + bs / 2 + sp(5), w - 2 * pad - bs - sp(7), sp(14), G.SEMI, s.fg, G.LEFT, 0);
        }
        if (a.cdDate < 0) {
            g.text("No date yet", pad, h / 2 + sp(8), sp(17), G.SEMI, s.fg, G.LEFT, 0);
            wrap("Pick one in the GlassDesk app.", pad, h / 2 + sp(28), w - 2 * pad, sp(13), sp(17), 2, G.REGULAR, s.sub, false);
            return;
        }
        Calendar t0 = (Calendar) cal.clone();
        t0.set(Calendar.HOUR_OF_DAY, 12); t0.set(Calendar.MINUTE, 0); t0.set(Calendar.SECOND, 0); t0.set(Calendar.MILLISECOND, 0);
        Calendar t1 = Calendar.getInstance();
        t1.setTimeInMillis(a.cdDate);
        t1.set(Calendar.HOUR_OF_DAY, 12); t1.set(Calendar.MINUTE, 0); t1.set(Calendar.SECOND, 0); t1.set(Calendar.MILLISECOND, 0);
        long diff = Math.round((t1.getTimeInMillis() - t0.getTimeInMillis()) / 86400000.0);
        String num = diff == 0 ? "Today" : String.valueOf(Math.abs(diff));
        String unit = diff == 0 ? "is the day" : (Math.abs(diff) == 1 ? "day" : "days") + (diff > 0 ? " to go" : " ago");
        float big = Math.min(sp(diff == 0 ? 40 : 54), h * 0.36f);
        float by = h - pad - sp(22);
        fit(num, pad - sp(1), by - sp(20), w - 2 * pad, big, s.gd ? G.REGULAR : G.BOLD, s.gd ? s.fg : s.accent, G.LEFT, -0.03f);
        g.text(unit, pad, by, sp(14), G.SEMI, s.gd ? s.accentText : s.fg, G.LEFT, 0);
        g.text(fmt("EEE, d MMM yyyy", a.cdDate), pad, h - pad, sp(12), G.MEDIUM, s.sub, G.LEFT, 0);
    }

    // ---------------------------------------------------------------- fences (app folders)

    void fence() {
        panel();
        float headH = h * GRID_HEAD / GRID_TOTAL, rowH = h * GRID_ROW / GRID_TOTAL;
        float hs = Math.min(sp(13), headH * 0.42f);
        float gs = hs * 1.25f;
        Sym.draw(g, Sym.FOLDER, pad, headH / 2 - gs / 2 + sp(2), gs, s.accent);
        String name = a.fenceName == null || a.fenceName.isEmpty() ? "Apps" : a.fenceName;
        if (s.gd) g.text(ellipsize(caps(name), w * 0.6f, hs * 0.85f, G.SEMI), pad + gs + sp(6), headH / 2 + hs * 0.32f + sp(2), hs * 0.85f, G.SEMI, s.sub, G.LEFT, 0.08f);
        else g.text(ellipsize(name, w * 0.6f, hs, G.SEMI), pad + gs + sp(6), headH / 2 + hs * 0.36f + sp(2), hs, G.SEMI, s.fg, G.LEFT, 0);
        int n = a.icons.length;
        if (n > 0) g.text(n + (n == 1 ? " app" : " apps"), w - pad, headH / 2 + hs * 0.36f + sp(2), hs * 0.9f, G.MEDIUM, s.sub, G.RIGHT, 0);
        float gw = w - 2 * pad * 0.6f, gx = pad * 0.6f;
        float cw = gw / 4f;
        float label = sp(10);
        boolean showLabels = rowH > sp(44);
        float icon = Math.min(cw * 0.66f, showLabels ? rowH - label * 1.9f : rowH * 0.78f);
        icon = Math.min(icon, sp(60));
        for (int i = 0; i < 8; i++) {
            float cx = gx + cw * (i % 4) + cw / 2;
            float cellTop = headH + rowH * (i / 4);
            float block = icon + (showLabels ? label * 1.6f : 0);
            float iy = cellTop + (rowH - block) / 2;
            if (i < n) {
                g.icon(a.icons[i], cx - icon / 2, iy, icon);
                if (showLabels && i < a.labels.length)
                    g.text(ellipsize(a.labels[i], cw - sp(2), label, G.MEDIUM), cx, iy + icon + label * 1.3f, label, G.MEDIUM, s.fg, G.CENTER, 0);
            } else {
                g.fillSquircle(cx - icon / 2, iy, icon, icon, icon * 0.225f, s.track);
                if (n == 0 && i == 0) Sym.draw(g, Sym.PLUS, cx - icon * .3f, iy + icon * .2f, icon * .6f, s.sub);
            }
        }
        if (n == 0) {
            float ty = h - pad * 0.6f;
            g.text("Tap to choose apps", w / 2, ty, sp(11), G.MEDIUM, s.sub, G.CENTER, 0);
        }
    }
}
