package app.freedev.glassdesk;

import android.app.ActivityManager;
import android.content.ComponentName;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.content.SharedPreferences;
import android.content.pm.ActivityInfo;
import android.content.pm.PackageManager;
import android.content.pm.ResolveInfo;
import android.content.res.Configuration;
import android.os.BatteryManager;
import android.os.Environment;
import android.os.StatFs;

import org.json.JSONArray;
import org.json.JSONObject;

import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Calendar;
import java.util.List;
import java.util.Locale;

/** Settings and saved content. */
public final class P {
    public static final String[] FENCES = {"fenceA", "fenceB", "fenceC"};
    public static final String[] FENCE_DEFAULT = {"Favorites", "Work", "Fun"};
    public static final int MAX_APPS = 8, MAX_TODO = 30;

    private P() {}

    public static SharedPreferences sp(Context c) { return c.getSharedPreferences("gd", Context.MODE_PRIVATE); }

    /** GlassDesk (default) or Clean. */
    public static int design(Context c) { return sp(c).getInt("design", Style.GLASSDESK); }
    public static boolean gd(Context c) { return design(c) == Style.GLASSDESK; }
    public static String themeKey(Context c) { return gd(c) ? "gd_theme" : "cl_theme"; }
    public static String accentKey(Context c) { return gd(c) ? "gd_accent" : "cl_accent"; }
    public static int theme(Context c) { return sp(c).getInt(themeKey(c), gd(c) ? Style.GLASS : Style.AUTO); }
    public static int accent(Context c) { return sp(c).getInt(accentKey(c), 0); }
    public static int tint(Context c) { return sp(c).getInt("tint", 45); }

    public static boolean systemDark(Context c) {
        return (c.getResources().getConfiguration().uiMode & Configuration.UI_MODE_NIGHT_MASK) == Configuration.UI_MODE_NIGHT_YES;
    }

    public static Style style(Context c) { return new Style(design(c), theme(c), systemDark(c), accent(c), tint(c)); }

    // ---------------------------------------------------------------- to-do

    public static final class Task {
        public String text; public boolean done;
        public Task(String t, boolean d) { text = t; done = d; }
    }

    public static List<Task> todo(Context c) {
        List<Task> out = new ArrayList<>();
        try {
            JSONArray a = new JSONArray(sp(c).getString("todo2", "[]"));
            for (int i = 0; i < a.length(); i++) {
                JSONObject o = a.getJSONObject(i);
                out.add(new Task(o.optString("t"), o.optBoolean("d")));
            }
        } catch (Exception ignored) {}
        return out;
    }

    public static void saveTodo(Context c, List<Task> list) {
        JSONArray a = new JSONArray();
        try {
            for (Task t : list) { JSONObject o = new JSONObject(); o.put("t", t.text); o.put("d", t.done); a.put(o); }
        } catch (Exception ignored) {}
        sp(c).edit().putString("todo2", a.toString()).apply();
    }

    // ---------------------------------------------------------------- fences

    public static String fenceName(Context c, int f) {
        String n = sp(c).getString(FENCES[f] + "_name", "");
        return n == null || n.trim().isEmpty() ? FENCE_DEFAULT[f] : n.trim();
    }

    public static List<String> fenceApps(Context c, int f) {
        List<String> out = new ArrayList<>();
        String s = sp(c).getString(FENCES[f] + "_apps2", "");
        if (s != null) for (String x : s.split("\n")) if (!x.trim().isEmpty()) out.add(x.trim());
        return out;
    }

    public static void saveFenceApps(Context c, int f, List<String> apps) {
        StringBuilder b = new StringBuilder();
        for (int i = 0; i < apps.size() && i < MAX_APPS; i++) { if (b.length() > 0) b.append('\n'); b.append(apps.get(i)); }
        sp(c).edit().putString(FENCES[f] + "_apps2", b.toString()).apply();
    }

    // ---------------------------------------------------------------- countdown

    public static long cdDate(Context c) {
        String s = sp(c).getString("cd_date", "");
        if (s == null || s.isEmpty()) return -1;
        try { return new SimpleDateFormat("yyyy-MM-dd", Locale.US).parse(s).getTime(); }
        catch (Exception e) { return -1; }
    }

    // ---------------------------------------------------------------- everything a widget needs

    public static Data data(Context c, int kind) {
        Data a = new Data();
        a.h24 = android.text.format.DateFormat.is24HourFormat(c);
        SharedPreferences sp = sp(c);
        if (kind == Draw.SYS) {
            try {
                BatteryManager bm = (BatteryManager) c.getSystemService(Context.BATTERY_SERVICE);
                a.batt = bm.getIntProperty(BatteryManager.BATTERY_PROPERTY_CAPACITY);
                Intent bi = c.getApplicationContext().registerReceiver(null, new IntentFilter(Intent.ACTION_BATTERY_CHANGED));
                if (bi != null) {
                    int st = bi.getIntExtra(BatteryManager.EXTRA_STATUS, -1);
                    a.charging = st == BatteryManager.BATTERY_STATUS_CHARGING || st == BatteryManager.BATTERY_STATUS_FULL;
                    if (a.batt <= 0) {
                        int l = bi.getIntExtra(BatteryManager.EXTRA_LEVEL, -1), s = bi.getIntExtra(BatteryManager.EXTRA_SCALE, 100);
                        if (l >= 0) a.batt = Math.round(100f * l / s);
                    }
                }
            } catch (Exception ignored) {}
            try {
                ActivityManager am = (ActivityManager) c.getSystemService(Context.ACTIVITY_SERVICE);
                ActivityManager.MemoryInfo mi = new ActivityManager.MemoryInfo();
                am.getMemoryInfo(mi);
                a.memTotal = mi.totalMem; a.memUsed = mi.totalMem - mi.availMem;
            } catch (Exception ignored) {}
            try {
                StatFs fs = new StatFs(Environment.getDataDirectory().getPath());
                a.diskTotal = fs.getTotalBytes(); a.diskUsed = fs.getTotalBytes() - fs.getAvailableBytes();
            } catch (Exception ignored) {}
        }
        a.note = sp.getString("note", "");
        List<Task> t = todo(c);
        a.todo = new String[t.size()];
        a.done = new boolean[t.size()];
        for (int i = 0; i < t.size(); i++) { a.todo[i] = t.get(i).text; a.done[i] = t.get(i).done; }
        Wx.fill(c, a);
        Calendar cal = Calendar.getInstance();
        a.quote = cal.get(Calendar.DAY_OF_YEAR) + cal.get(Calendar.YEAR) * 7 + sp.getInt("q_off", 0);
        a.cdTitle = sp.getString("cd_title", "");
        a.cdDate = cdDate(c);
        if (kind >= Draw.FENCE_A) {
            int f = kind - Draw.FENCE_A;
            a.fenceName = fenceName(c, f);
            List<String> apps = fenceApps(c, f);
            PackageManager pm = c.getPackageManager();
            List<Object> icons = new ArrayList<>();
            List<String> labels = new ArrayList<>();
            for (String comp : apps) {
                try {
                    ComponentName cn = ComponentName.unflattenFromString(comp);
                    if (cn == null) continue;
                    ActivityInfo ai = pm.getActivityInfo(cn, 0);
                    icons.add(ai.loadIcon(pm));
                    labels.add(String.valueOf(ai.loadLabel(pm)));
                } catch (Exception ignored) {}
            }
            a.icons = icons.toArray();
            a.labels = labels.toArray(new String[0]);
        }
        return a;
    }

    /** Apps the user could put in a fence, sorted by name. */
    public static List<ResolveInfo> launchable(Context c) {
        Intent i = new Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_LAUNCHER);
        final PackageManager pm = c.getPackageManager();
        List<ResolveInfo> l = pm.queryIntentActivities(i, 0);
        List<ResolveInfo> out = new ArrayList<>();
        for (ResolveInfo r : l) if (!r.activityInfo.packageName.equals(c.getPackageName())) out.add(r);
        final java.util.Map<ResolveInfo, String> names = new java.util.HashMap<>();
        for (ResolveInfo r : out) names.put(r, String.valueOf(r.loadLabel(pm)));
        java.util.Collections.sort(out, new java.util.Comparator<ResolveInfo>() {
            public int compare(ResolveInfo x, ResolveInfo y) { return names.get(x).compareToIgnoreCase(names.get(y)); }
        });
        return out;
    }
}
