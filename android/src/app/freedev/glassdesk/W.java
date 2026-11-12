package app.freedev.glassdesk;

import android.app.AlarmManager;
import android.app.PendingIntent;
import android.appwidget.AppWidgetManager;
import android.appwidget.AppWidgetProvider;
import android.content.BroadcastReceiver;
import android.content.ComponentName;
import android.content.Context;
import android.content.Intent;
import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.net.Uri;
import android.os.Bundle;
import android.provider.AlarmClock;
import android.widget.RemoteViews;

import java.util.Calendar;
import java.util.List;

/** All GlassDesk home-screen widgets. Each subclass is one widget in the picker. */
public abstract class W extends AppWidgetProvider {
    public static final String REFRESH = "app.freedev.glassdesk.REFRESH";
    public static final String TOGGLE = "app.freedev.glassdesk.TOGGLE";
    public static final String WEATHER_NOW = "app.freedev.glassdesk.WEATHER_NOW";
    public static final String NEXT_QUOTE = "app.freedev.glassdesk.NEXT_QUOTE";

    static final int FLAGS = PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE;

    public static final Class<?>[] CLS = {Clock.class, Sys.class, Cal.class, Notes.class, Todo.class, Weather.class,
            Quote.class, Count.class, FenceA.class, FenceB.class, FenceC.class};
    public static final String[] NAMES = {"Clock", "Batteries", "Calendar", "Notes", "To-do", "Weather", "Quote",
            "Countdown", "Fence 1", "Fence 2", "Fence 3"};
    /** Default size in dp when the launcher hasn't told us yet. */
    public static final int[][] SIZE = {{330, 155}, {155, 155}, {155, 155}, {155, 155}, {155, 155}, {330, 155},
            {155, 155}, {155, 155}, {330, 155}, {330, 155}, {330, 155}};

    abstract int kind();

    public static class Clock extends W { int kind() { return Draw.CLOCK; } }
    public static class Sys extends W { int kind() { return Draw.SYS; } }
    public static class Cal extends W { int kind() { return Draw.CAL; } }
    public static class Notes extends W { int kind() { return Draw.NOTES; } }
    public static class Todo extends W { int kind() { return Draw.TODO; } }
    public static class Weather extends W { int kind() { return Draw.WEATHER; } }
    public static class Quote extends W { int kind() { return Draw.QUOTE; } }
    public static class Count extends W { int kind() { return Draw.COUNT; } }
    public static class FenceA extends W { int kind() { return Draw.FENCE_A; } }
    public static class FenceB extends W { int kind() { return Draw.FENCE_B; } }
    public static class FenceC extends W { int kind() { return Draw.FENCE_C; } }

    /** Wakes every 15 minutes (only while the phone is awake) to keep widgets fresh. */
    public static class Tick extends BroadcastReceiver {
        @Override public void onReceive(Context c, Intent i) {
            updateAll(c);
            if (count(c, Draw.WEATHER) + count(c, Draw.CLOCK) > 0 && Wx.stale(c)) weatherAsync(c, goAsync());
        }
    }

    // ---------------------------------------------------------------- lifecycle

    @Override public void onUpdate(Context c, AppWidgetManager m, int[] ids) {
        for (int id : ids) update(c, m, id, kind());
        schedule(c);
        if ((kind() == Draw.WEATHER || kind() == Draw.CLOCK) && Wx.stale(c)) weatherAsync(c, goAsync());
    }

    @Override public void onAppWidgetOptionsChanged(Context c, AppWidgetManager m, int id, Bundle o) {
        update(c, m, id, kind());
    }

    @Override public void onReceive(Context c, Intent i) {
        String a = i.getAction();
        if (TOGGLE.equals(a)) {
            List<P.Task> t = P.todo(c);
            int idx = i.getIntExtra("i", -1);
            if (idx >= 0 && idx < t.size()) {
                t.get(idx).done = !t.get(idx).done;
                P.saveTodo(c, t);
            }
            updateKind(c, Draw.TODO);
            updateKind(c, Draw.CLOCK);
            updateKind(c, Draw.CAL);
            return;
        }
        if (NEXT_QUOTE.equals(a)) {
            P.sp(c).edit().putInt("q_off", P.sp(c).getInt("q_off", 0) + 1).apply();
            updateKind(c, Draw.QUOTE);
            return;
        }
        if (WEATHER_NOW.equals(a)) {
            weatherAsync(c, goAsync());
            return;
        }
        if (REFRESH.equals(a)) {
            updateAll(c);
            return;
        }
        super.onReceive(c, i);
    }

    @Override public void onDisabled(Context c) { schedule(c); }

    // ---------------------------------------------------------------- helpers

    static void weatherAsync(final Context ctx, final PendingResult pr) {
        final Context app = ctx.getApplicationContext();
        new Thread(new Runnable() {
            public void run() {
                try {
                    Wx.fetch(app);
                    updateKind(app, Draw.WEATHER);
                    updateKind(app, Draw.CLOCK);
                } finally {
                    if (pr != null) pr.finish();
                }
            }
        }).start();
    }

    public static int count(Context c, int kind) {
        return AppWidgetManager.getInstance(c).getAppWidgetIds(new ComponentName(c, CLS[kind])).length;
    }

    public static void updateKind(Context c, int kind) {
        AppWidgetManager m = AppWidgetManager.getInstance(c);
        for (int id : m.getAppWidgetIds(new ComponentName(c, CLS[kind]))) update(c, m, id, kind);
    }

    public static void updateAll(Context c) {
        for (int k = 0; k < CLS.length; k++) updateKind(c, k);
        schedule(c);
    }

    static void schedule(Context c) {
        boolean any = false;
        for (int k = 0; k < CLS.length; k++) if (count(c, k) > 0) { any = true; break; }
        AlarmManager am = (AlarmManager) c.getSystemService(Context.ALARM_SERVICE);
        PendingIntent pi = PendingIntent.getBroadcast(c, 0, new Intent(c, Tick.class), FLAGS);
        am.cancel(pi);
        if (any) am.setInexactRepeating(AlarmManager.RTC, System.currentTimeMillis() + AlarmManager.INTERVAL_FIFTEEN_MINUTES,
                AlarmManager.INTERVAL_FIFTEEN_MINUTES, pi);
    }

    static void update(Context c, AppWidgetManager m, int id, int kind) {
        Bundle o = m.getAppWidgetOptions(id);
        boolean land = c.getResources().getConfiguration().orientation == android.content.res.Configuration.ORIENTATION_LANDSCAPE;
        int wdp = o.getInt(land ? AppWidgetManager.OPTION_APPWIDGET_MAX_WIDTH : AppWidgetManager.OPTION_APPWIDGET_MIN_WIDTH, 0);
        int hdp = o.getInt(land ? AppWidgetManager.OPTION_APPWIDGET_MIN_HEIGHT : AppWidgetManager.OPTION_APPWIDGET_MAX_HEIGHT, 0);
        if (wdp <= 0) wdp = SIZE[kind][0];
        if (hdp <= 0) hdp = SIZE[kind][1];
        try {
            m.updateAppWidget(id, build(c, kind, wdp, hdp, id));
        } catch (Throwable t) {
            RemoteViews rv = new RemoteViews(c.getPackageName(), R.layout.w_plain);
            m.updateAppWidget(id, rv);
        }
    }

    public static Bitmap render(Context c, int kind, int wdp, int hdp, Style st, Data a) {
        float d = c.getResources().getDisplayMetrics().density;
        float px = wdp * d * hdp * d;
        float max = 1_100_000f;
        if (px > max) d *= (float) Math.sqrt(max / px);
        int w = Math.max(1, Math.round(wdp * d)), h = Math.max(1, Math.round(hdp * d));
        Bitmap b = Bitmap.createBitmap(w, h, Bitmap.Config.ARGB_8888);
        new Draw(new AndroidG(c, new Canvas(b)), w, h, d, st, a).draw(kind);
        return b;
    }

    static RemoteViews build(Context c, int kind, int wdp, int hdp, int id) {
        Style st = P.style(c);
        Data a = P.data(c, kind);
        int layout = kind == Draw.CLOCK ? (st.gd ? R.layout.w_clock_gd : R.layout.w_clock) : kind == Draw.TODO ? R.layout.w_rows
                : kind >= Draw.FENCE_A ? R.layout.w_grid : R.layout.w_plain;
        RemoteViews rv = new RemoteViews(c.getPackageName(), layout);
        rv.setImageViewBitmap(R.id.img, render(c, kind, wdp, hdp, st, a));
        switch (kind) {
            case Draw.CLOCK: {
                rv.setTextColor(R.id.day, st.accent);
                rv.setTextColor(R.id.time, st.fg);
                rv.setTextColor(R.id.greet, st.sub);
                rv.setTextViewTextSize(R.id.time, android.util.TypedValue.COMPLEX_UNIT_SP, Math.max(28, Math.min(50, hdp * 0.34f)));
                rv.setTextViewText(R.id.greet, Draw.greeting(Calendar.getInstance().get(Calendar.HOUR_OF_DAY)));
                Intent clock = new Intent(AlarmClock.ACTION_SHOW_ALARMS);
                rv.setOnClickPendingIntent(R.id.root, clock.resolveActivity(c.getPackageManager()) != null
                        ? PendingIntent.getActivity(c, 100, clock, FLAGS) : open(c, "widgets", null));
                break;
            }
            case Draw.SYS: rv.setOnClickPendingIntent(R.id.root, self(c, kind, REFRESH, 0)); break;
            case Draw.CAL: {
                Intent i = new Intent(Intent.ACTION_VIEW, Uri.parse("content://com.android.calendar/time/" + System.currentTimeMillis()));
                rv.setOnClickPendingIntent(R.id.root, i.resolveActivity(c.getPackageManager()) != null
                        ? PendingIntent.getActivity(c, 101, i, FLAGS) : open(c, "content", "todo"));
                break;
            }
            case Draw.NOTES: rv.setOnClickPendingIntent(R.id.root, open(c, "content", "note")); break;
            case Draw.TODO: {
                rv.setOnClickPendingIntent(R.id.head, open(c, "content", "todo"));
                rv.setOnClickPendingIntent(R.id.foot, open(c, "content", "todo"));
                int[] rows = {R.id.r0, R.id.r1, R.id.r2, R.id.r3, R.id.r4};
                for (int i = 0; i < rows.length; i++)
                    rv.setOnClickPendingIntent(rows[i], i < a.todo.length ? self(c, kind, TOGGLE, i) : open(c, "content", "todo"));
                break;
            }
            case Draw.WEATHER:
                rv.setOnClickPendingIntent(R.id.root, a.city == null ? open(c, "content", "weather") : self(c, kind, WEATHER_NOW, 0));
                break;
            case Draw.QUOTE: rv.setOnClickPendingIntent(R.id.root, self(c, kind, NEXT_QUOTE, 0)); break;
            case Draw.COUNT: rv.setOnClickPendingIntent(R.id.root, open(c, "content", "countdown")); break;
            default: {
                int f = kind - Draw.FENCE_A;
                rv.setOnClickPendingIntent(R.id.head, open(c, "fences", "fence" + f));
                int[] cells = {R.id.c0, R.id.c1, R.id.c2, R.id.c3, R.id.c4, R.id.c5, R.id.c6, R.id.c7};
                List<String> apps = P.fenceApps(c, f);
                for (int i = 0; i < cells.length; i++) {
                    PendingIntent pi = null;
                    if (i < apps.size()) {
                        ComponentName cn = ComponentName.unflattenFromString(apps.get(i));
                        if (cn != null) {
                            Intent li = new Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_LAUNCHER).setComponent(cn)
                                    .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_RESET_TASK_IF_NEEDED);
                            pi = PendingIntent.getActivity(c, 1000 + f * 10 + i, li, FLAGS);
                        }
                    }
                    rv.setOnClickPendingIntent(cells[i], pi != null ? pi : open(c, "fences", "fence" + f));
                }
                break;
            }
        }
        return rv;
    }

    static PendingIntent self(Context c, int kind, String action, int extra) {
        Intent i = new Intent(c, CLS[kind]).setAction(action).putExtra("i", extra)
                .setData(Uri.parse("glassdesk://w/" + action + "/" + kind + "/" + extra));
        return PendingIntent.getBroadcast(c, kind * 100 + extra, i, FLAGS);
    }

    static PendingIntent open(Context c, String tab, String section) {
        Intent i = new Intent(c, MainActivity.class)
                .setData(Uri.parse("glassdesk://open/" + tab + "/" + (section == null ? "" : section)))
                .putExtra("tab", tab).putExtra("section", section)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TOP | Intent.FLAG_ACTIVITY_SINGLE_TOP);
        return PendingIntent.getActivity(c, (tab + section).hashCode(), i, FLAGS);
    }
}
