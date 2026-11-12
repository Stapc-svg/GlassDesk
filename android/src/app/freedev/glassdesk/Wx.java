package app.freedev.glassdesk;

import android.content.Context;
import android.content.SharedPreferences;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.net.URLEncoder;
import java.text.SimpleDateFormat;
import java.util.Locale;

/** Weather from Open-Meteo (no key needed). Only the city name the user types is sent. */
public final class Wx {
    private Wx() {}

    static String get(String url) throws Exception {
        HttpURLConnection c = (HttpURLConnection) new URL(url).openConnection();
        c.setConnectTimeout(10000);
        c.setReadTimeout(10000);
        c.setRequestProperty("User-Agent", "GlassDesk-Android");
        try (InputStream in = c.getInputStream()) {
            ByteArrayOutputStream o = new ByteArrayOutputStream();
            byte[] b = new byte[8192];
            int n;
            while ((n = in.read(b)) > 0) o.write(b, 0, n);
            return o.toString("UTF-8");
        } finally {
            c.disconnect();
        }
    }

    public static boolean hasCity(Context c) { return P.sp(c).contains("wx_lat"); }

    /** Looks up a city. Returns its display name, or null if not found. Call off the main thread. */
    public static String setCity(Context c, String query) {
        try {
            JSONObject o = new JSONObject(get("https://geocoding-api.open-meteo.com/v1/search?count=1&language=en&name=" + URLEncoder.encode(query.trim(), "UTF-8")));
            JSONArray r = o.optJSONArray("results");
            if (r == null || r.length() == 0) return null;
            JSONObject p = r.getJSONObject(0);
            String name = p.optString("name", query.trim());
            P.sp(c).edit().putString("wx_city", name)
                    .putFloat("wx_lat", (float) p.getDouble("latitude"))
                    .putFloat("wx_lon", (float) p.getDouble("longitude"))
                    .remove("wx_json").putLong("wx_time", 0).apply();
            return name;
        } catch (Exception e) {
            return null;
        }
    }

    /** Downloads the forecast. Call off the main thread. */
    public static boolean fetch(Context c) {
        SharedPreferences sp = P.sp(c);
        if (!hasCity(c)) return false;
        try {
            String url = "https://api.open-meteo.com/v1/forecast?latitude=" + sp.getFloat("wx_lat", 0) + "&longitude=" + sp.getFloat("wx_lon", 0)
                    + "&current=temperature_2m,weather_code,is_day&daily=weather_code,temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=4"
                    + (sp.getBoolean("wx_f", false) ? "&temperature_unit=fahrenheit" : "");
            String json = get(url);
            new JSONObject(json); // validate
            sp.edit().putString("wx_json", json).putLong("wx_time", System.currentTimeMillis()).apply();
            return true;
        } catch (Exception e) {
            return false;
        }
    }

    public static boolean stale(Context c) {
        return hasCity(c) && System.currentTimeMillis() - P.sp(c).getLong("wx_time", 0) > 45 * 60 * 1000L;
    }

    public static void fill(Context c, Data a) {
        SharedPreferences sp = P.sp(c);
        if (!hasCity(c)) { a.city = null; return; }
        a.city = sp.getString("wx_city", "");
        String json = sp.getString("wx_json", null);
        if (json == null) return;
        try {
            JSONObject o = new JSONObject(json);
            JSONObject cur = o.getJSONObject("current");
            a.temp = (int) Math.round(cur.getDouble("temperature_2m"));
            a.code = cur.optInt("weather_code", -1);
            a.isDay = cur.optInt("is_day", 1) == 1;
            JSONObject d = o.getJSONObject("daily");
            JSONArray time = d.getJSONArray("time"), wc = d.getJSONArray("weather_code"),
                    mx = d.getJSONArray("temperature_2m_max"), mn = d.getJSONArray("temperature_2m_min");
            a.hi = (int) Math.round(mx.getDouble(0));
            a.lo = (int) Math.round(mn.getDouble(0));
            int n = Math.max(0, Math.min(3, time.length() - 1));
            a.dDay = new String[n]; a.dCode = new int[n]; a.dHi = new int[n]; a.dLo = new int[n];
            SimpleDateFormat in = new SimpleDateFormat("yyyy-MM-dd", Locale.US), out = new SimpleDateFormat("EEE", Locale.getDefault());
            for (int i = 0; i < n; i++) {
                a.dDay[i] = out.format(in.parse(time.getString(i + 1)));
                a.dCode[i] = wc.getInt(i + 1);
                a.dHi[i] = (int) Math.round(mx.getDouble(i + 1));
                a.dLo[i] = (int) Math.round(mn.getDouble(i + 1));
            }
            a.wxOk = true;
        } catch (Exception e) {
            a.wxOk = false;
        }
    }
}
