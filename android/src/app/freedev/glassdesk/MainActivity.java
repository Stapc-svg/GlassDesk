package app.freedev.glassdesk;

import android.app.Activity;
import android.app.DatePickerDialog;
import android.app.Dialog;
import android.appwidget.AppWidgetManager;
import android.content.ComponentName;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.ActivityInfo;
import android.content.pm.PackageManager;
import android.content.pm.ResolveInfo;
import android.content.res.ColorStateList;
import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.graphics.Paint;
import android.graphics.Typeface;
import android.graphics.drawable.ClipDrawable;
import android.graphics.drawable.ColorDrawable;
import android.graphics.drawable.Drawable;
import android.graphics.drawable.GradientDrawable;
import android.graphics.drawable.LayerDrawable;
import android.graphics.drawable.StateListDrawable;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.text.Editable;
import android.text.InputType;
import android.text.TextUtils;
import android.text.TextWatcher;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.KeyEvent;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewGroup;
import android.view.Window;
import android.view.WindowManager;
import android.view.inputmethod.EditorInfo;
import android.view.inputmethod.InputMethodManager;
import android.widget.BaseAdapter;
import android.widget.DatePicker;
import android.widget.EditText;
import android.widget.FrameLayout;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.ListView;
import android.widget.ScrollView;
import android.widget.SeekBar;
import android.widget.TextView;
import android.widget.Toast;

import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Calendar;
import java.util.HashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;

/** The GlassDesk app, laid out like iOS Settings. */
public class MainActivity extends Activity {
    static final String VERSION = "1.7.2";
    static final String[] TABS = {"widgets", "look", "content", "fences", "about"};
    static final String[] TAB_NAMES = {"Widgets", "Appearance", "Content", "Fences", "About"};
    static final int[] TAB_ICONS = {Sym.GRID, Sym.APPEARANCE, Sym.DOC, Sym.FOLDER, Sym.INFO};
    static final String[] DESC = {"Time, date and a greeting", "Battery, memory and storage", "This month at a glance",
            "A note to yourself", "Tasks you can tick off", "Now and the next three days", "A new thought each day",
            "Days until your big day", "Your favorite apps", "A second set of apps", "A third set of apps"};
    static final int[] WIDGET_ICON = {Sym.CLOCK, Sym.BATTERY, Sym.CALENDAR, Sym.NOTE, Sym.LIST, Sym.CLOUD_SUN, Sym.QUOTE,
            Sym.HOURGLASS, Sym.FOLDER, Sym.FOLDER, Sym.FOLDER};

    float d;
    boolean dark, gdUi;
    int BG, CELL, LABEL, SECOND, THIRD, SEP, FILL, PRESS, accent;
    SharedPreferences sp;
    FrameLayout root;
    ScrollView scroll;
    LinearLayout page, tabBar;
    int tab;
    final Map<String, View> sections = new HashMap<>();
    final List<Object[]> previews = new ArrayList<>();
    final Handler handler = new Handler(Looper.getMainLooper());
    int contentW;

    interface Pick { void pick(int i); }

    // ---------------------------------------------------------------- lifecycle

    @Override protected void onCreate(Bundle b) {
        super.onCreate(b);
        sp = P.sp(this);
        d = getResources().getDisplayMetrics().density;
        root = new FrameLayout(this);
        chrome();
        scroll = new ScrollView(this);
        scroll.setVerticalScrollBarEnabled(false);
        scroll.setClipToPadding(false);
        page = column();
        page.setPadding(dp(16), dp(8), dp(16), dp(120));
        scroll.addView(page, new ScrollView.LayoutParams(-1, -2));
        root.addView(scroll, new FrameLayout.LayoutParams(-1, -1));
        setContentView(root);
        contentW = getResources().getDisplayMetrics().widthPixels - dp(32);

        String t = b != null ? b.getString("tab") : null;
        handleIntent(getIntent(), t);
    }

    @Override protected void onNewIntent(Intent i) {
        super.onNewIntent(i);
        setIntent(i);
        handleIntent(i, null);
    }

    void handleIntent(Intent i, String savedTab) {
        String t = savedTab;
        String section = null;
        if (i != null && i.getStringExtra("tab") != null) { t = i.getStringExtra("tab"); section = i.getStringExtra("section"); }
        int idx = 0;
        for (int k = 0; k < TABS.length; k++) if (TABS[k].equals(t)) idx = k;
        show(idx, section);
    }

    @Override protected void onSaveInstanceState(Bundle b) {
        super.onSaveInstanceState(b);
        b.putString("tab", TABS[tab]);
    }

    @Override protected void onPause() {
        super.onPause();
        W.updateAll(this);
    }

    /** Colors, window bars and background for the current design. */
    void chrome() {
        colors();
        Window w = getWindow();
        int bar = gdUi ? 0xFF0A0E1F : BG;
        w.setStatusBarColor(bar);
        w.setNavigationBarColor(gdUi ? 0xFF0D1126 : BG);
        int flags = 0;
        if (!dark) {
            flags |= View.SYSTEM_UI_FLAG_LIGHT_STATUS_BAR;
            if (Build.VERSION.SDK_INT >= 27) flags |= View.SYSTEM_UI_FLAG_LIGHT_NAVIGATION_BAR;
        }
        w.getDecorView().setSystemUiVisibility(flags);
        root.setBackground(backdrop());
    }

    /** The app's backdrop: deep navy with soft accent glows (GlassDesk) or a calm grouped background with a faint aurora (Clean). */
    Drawable backdrop() {
        int H = getResources().getDisplayMetrics().heightPixels, Wd = getResources().getDisplayMetrics().widthPixels;
        GradientDrawable base = new GradientDrawable(GradientDrawable.Orientation.TOP_BOTTOM,
                gdUi ? new int[]{0xFF0A0E1F, 0xFF0F1430, 0xFF0D1126} : new int[]{BG, BG});
        GradientDrawable g1 = radial(Style.withAlpha(accent, gdUi ? 0.34f : (dark ? 0.16f : 0.13f)), Wd * 0.85f);
        GradientDrawable g2 = radial(Style.withAlpha(gdUi ? 0xFFB06CFF : Style.mix(accent, 0xFFFF6FA8, 0.5f), gdUi ? 0.20f : (dark ? 0.08f : 0.07f)), Wd * 0.7f);
        LayerDrawable l = new LayerDrawable(new Drawable[]{base, g1, g2});
        l.setLayerSize(1, Math.round(Wd * 1.7f), Math.round(Wd * 1.7f));
        l.setLayerInset(1, Math.round(Wd * 0.15f), -Math.round(Wd * 0.85f), 0, 0);
        l.setLayerGravity(1, Gravity.TOP | Gravity.RIGHT);
        l.setLayerSize(2, Math.round(Wd * 1.4f), Math.round(Wd * 1.4f));
        l.setLayerInset(2, -Math.round(Wd * 0.7f), 0, 0, 0);
        l.setLayerGravity(2, Gravity.TOP | Gravity.LEFT);
        if (gdUi) {
            GradientDrawable g3 = radial(Style.withAlpha(accent, 0.12f), Wd * 0.8f);
            l.addLayer(g3);
            l.setLayerSize(3, Math.round(Wd * 1.6f), Math.round(Wd * 1.6f));
            l.setLayerGravity(3, Gravity.BOTTOM | Gravity.RIGHT);
            l.setLayerInset(3, 0, 0, -Math.round(Wd * 0.6f), -Math.round(Wd * 0.4f));
        }
        return l;
    }

    GradientDrawable radial(int color, float radius) {
        GradientDrawable g = new GradientDrawable();
        g.setGradientType(GradientDrawable.RADIAL_GRADIENT);
        g.setColors(new int[]{color, color & 0x00FFFFFF});
        g.setGradientRadius(radius);
        g.setShape(GradientDrawable.OVAL);
        return g;
    }

    void colors() {
        gdUi = P.gd(this);
        dark = gdUi || P.systemDark(this);
        accent = Style.accentColor(P.design(this), P.accent(this), dark);
        if (gdUi) {
            BG = 0xFF0B0F22; CELL = 0x14FFFFFF; LABEL = 0xFFF4F6FC; SECOND = 0xFFA3ACC6; THIRD = 0xFF5C6582;
            SEP = 0x1AFFFFFF; FILL = 0x1FFFFFFF; PRESS = 0x14FFFFFF;
            return;
        }
        if (dark) {
            BG = 0xFF000000; CELL = 0xFF1C1C1E; LABEL = 0xFFFFFFFF; SECOND = 0xFF98989F; THIRD = 0xFF5A5A5F;
            SEP = 0xFF38383A; FILL = 0xFF2C2C2E; PRESS = 0xFF2C2C2E;
        } else {
            BG = 0xFFF2F2F7; CELL = 0xFFFFFFFF; LABEL = 0xFF000000; SECOND = 0xFF8A8A8E; THIRD = 0xFFC4C4C7;
            SEP = 0xFFE0E0E4; FILL = 0xFFEEEEF0; PRESS = 0xFFE5E5EA;
        }
    }

    // ---------------------------------------------------------------- navigation

    void show(int idx, final String section) {
        tab = idx;
        hideKeyboard();
        page.removeAllViews();
        sections.clear();
        previews.clear();
        switch (idx) {
            case 0: widgetsPage(); break;
            case 1: lookPage(); break;
            case 2: contentPage(); break;
            case 3: fencesPage(); break;
            default: aboutPage(); break;
        }
        buildTabBar();
        scroll.scrollTo(0, 0);
        page.setAlpha(0f);
        page.setTranslationY(dp(14));
        page.animate().alpha(1f).translationY(0).setDuration(260).setInterpolator(new android.view.animation.DecelerateInterpolator(2f)).start();
        if (section != null) {
            scroll.postDelayed(new Runnable() {
                public void run() {
                    View v = sections.get(section);
                    if (v != null) scroll.smoothScrollTo(0, Math.max(0, v.getTop() - dp(8)));
                }
            }, 120);
        }
    }

    void buildTabBar() {
        if (tabBar != null) root.removeView(tabBar);
        tabBar = new LinearLayout(this);
        tabBar.setOrientation(LinearLayout.HORIZONTAL);
        tabBar.setPadding(dp(6), dp(6), dp(6), dp(6));
        GradientDrawable bg = new GradientDrawable(GradientDrawable.Orientation.TOP_BOTTOM,
                gdUi ? new int[]{0xF2232A4D, 0xF2141933} : dark ? new int[]{0xF22C2C2E, 0xF21C1C1E} : new int[]{0xFAFFFFFF, 0xF5F7F7FA});
        bg.setCornerRadius(dp(34));
        bg.setStroke(Math.max(1, dp(0.75f)), gdUi ? 0x33FFFFFF : dark ? 0x26FFFFFF : 0x12000000);
        tabBar.setBackground(bg);
        tabBar.setElevation(dp(18));
        if (Build.VERSION.SDK_INT >= 28) { tabBar.setOutlineAmbientShadowColor(gdUi ? accent : 0xFF000000); tabBar.setOutlineSpotShadowColor(gdUi ? accent : 0xFF000000); }
        for (int i = 0; i < TABS.length; i++) {
            final int k = i;
            boolean sel = i == tab;
            LinearLayout item = new LinearLayout(this);
            item.setOrientation(LinearLayout.VERTICAL);
            item.setGravity(Gravity.CENTER);
            if (sel) {
                GradientDrawable pillBg = new GradientDrawable(GradientDrawable.Orientation.TOP_BOTTOM,
                        new int[]{Style.withAlpha(accent, dark ? 0.30f : 0.16f), Style.withAlpha(accent, dark ? 0.16f : 0.08f)});
                pillBg.setCornerRadius(dp(28));
                if (gdUi) pillBg.setStroke(Math.max(1, dp(0.75f)), Style.withAlpha(accent, 0.45f));
                item.setBackground(pillBg);
            }
            int col = sel ? (gdUi ? Style.mix(accent, 0xFFFFFFFF, 0.25f) : accent) : (gdUi ? 0xFF8F98B5 : dark ? 0xFFB0B0B5 : 0xFF6E6E73);
            ImageView ic = new ImageView(this);
            ic.setImageDrawable(new Ui.SymDrawable(this, TAB_ICONS[i], col, 0, false, dp(24)));
            item.addView(ic, new LinearLayout.LayoutParams(dp(24), dp(24)));
            TextView l = tv(TAB_NAMES[i], 10, sel ? G.SEMI : G.MEDIUM, col);
            l.setPadding(0, dp(2), 0, 0);
            l.setSingleLine(true);
            item.addView(l);
            tappable(item, new Runnable() { public void run() { if (k != tab) show(k, null); } });
            tabBar.addView(item, new LinearLayout.LayoutParams(0, -1, 1));
        }
        FrameLayout.LayoutParams lp = new FrameLayout.LayoutParams(-1, dp(64), Gravity.BOTTOM);
        lp.setMargins(dp(14), 0, dp(14), dp(14));
        root.addView(tabBar, lp);
    }

    // ---------------------------------------------------------------- building blocks

    /** A fixed system color for icon badges (0 blue, 1 purple, 3 red, 4 orange, 6 green, 7 graphite). */
    int onAccent() { return gdUi ? 0xFF0B1020 : (P.accent(this) == 5 ? 0xFF000000 : 0xFFFFFFFF); }

    int tone(int i) { return Style.accentColor(Style.CLEAN, i, dark); }

    int dp(float v) { return Math.round(v * d); }

    LinearLayout column() { LinearLayout l = new LinearLayout(this); l.setOrientation(LinearLayout.VERTICAL); return l; }

    TextView tv(String text, float sizeSp, int weight, int color) {
        TextView t = new TextView(this);
        t.setText(text);
        t.setTextSize(TypedValue.COMPLEX_UNIT_SP, sizeSp);
        t.setTypeface(AndroidG.font(this, weight));
        t.setTextColor(color);
        t.setIncludeFontPadding(false);
        t.setLetterSpacing(sizeSp >= 28 ? -0.02f : sizeSp >= 17 ? -0.01f : 0f);
        t.setLineSpacing(0, 1.12f);
        return t;
    }

    GradientDrawable round(int color, float radius) {
        GradientDrawable g = new GradientDrawable();
        g.setColor(color);
        g.setCornerRadius(radius);
        return g;
    }

    void largeTitle(String title, String sub) {
        TextView eb = tv("GLASSDESK", 12, G.SEMI, gdUi ? Style.mix(accent, 0xFFFFFFFF, 0.2f) : accent);
        eb.setLetterSpacing(0.18f);
        eb.setPadding(dp(4), dp(22), 0, dp(6));
        page.addView(eb);
        TextView t = tv(title, 36, G.BOLD, LABEL);
        t.setLetterSpacing(-0.025f);
        t.setPadding(dp(4), 0, 0, dp(sub == null ? 16 : 8));
        if (gdUi) t.setShadowLayer(dp(18), 0, 0, Style.withAlpha(accent, 0.45f));
        page.addView(t);
        if (sub != null) {
            TextView s = tv(sub, 15, G.REGULAR, SECOND);
            s.setPadding(dp(4), 0, dp(4), dp(18));
            page.addView(s);
        }
    }

    TextView header(String text, String key) {
        TextView t = tv(text.toUpperCase(Locale.getDefault()), gdUi ? 12 : 13, gdUi ? G.SEMI : G.MEDIUM, SECOND);
        t.setLetterSpacing(gdUi ? 0.12f : 0.04f);
        t.setPadding(dp(16), dp(26), dp(16), dp(9));
        page.addView(t);
        if (key != null) sections.put(key, t);
        return t;
    }

    TextView footer(String text) {
        TextView t = tv(text, 13, G.REGULAR, SECOND);
        t.setPadding(dp(16), dp(7), dp(16), 0);
        page.addView(t);
        return t;
    }

    LinearLayout group() {
        LinearLayout g = column();
        g.setBackground(card(dp(22)));
        g.setClipToOutline(true);
        if (!gdUi && !dark) g.setElevation(dp(1.5f));
        page.addView(g, new LinearLayout.LayoutParams(-1, -2));
        return g;
    }

    /** Card surface: frosted glass with a bright top edge (GlassDesk) or a clean raised card (Clean). */
    Drawable card(float radius) {
        if (gdUi) {
            GradientDrawable g = new GradientDrawable(GradientDrawable.Orientation.TOP_BOTTOM, new int[]{0x1FFFFFFF, 0x0AFFFFFF});
            g.setCornerRadius(radius);
            g.setStroke(Math.max(1, dp(0.75f)), 0x24FFFFFF);
            return g;
        }
        return round(CELL, radius);
    }

    StateListDrawable pressable() {
        StateListDrawable s = new StateListDrawable();
        s.addState(new int[]{android.R.attr.state_pressed}, new ColorDrawable(PRESS));
        s.addState(new int[]{}, new ColorDrawable(0));
        return s;
    }

    void sep(LinearLayout g, int insetDp) {
        View v = new View(this);
        v.setBackgroundColor(SEP);
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(-1, Math.max(1, dp(0.5f)));
        lp.leftMargin = dp(insetDp);
        g.addView(v, lp);
    }

    ImageView badge(int kind, int color) {
        ImageView iv = new ImageView(this);
        if (gdUi) iv.setImageDrawable(new Ui.SymDrawable(this, kind, Style.mix(color, 0xFFFFFFFF, 0.25f), Style.withAlpha(color, 0.22f), false, dp(30)));
        else {
            iv.setImageDrawable(new Ui.SymDrawable(this, kind, 0xFFFFFFFF, color, false, dp(30)));
        }
        return iv;
    }

    /** A Settings-style row: [icon] Title ........ value  > */
    LinearLayout row(LinearLayout g, int icon, int iconColor, String title, String value, boolean chevron, Runnable click) {
        if (g.getChildCount() > 0) sep(g, icon != 0 ? 60 : 16);
        LinearLayout r = new LinearLayout(this);
        r.setOrientation(LinearLayout.HORIZONTAL);
        r.setGravity(Gravity.CENTER_VERTICAL);
        r.setMinimumHeight(dp(52));
        r.setPadding(dp(16), dp(8), dp(14), dp(8));
        if (icon != 0) {
            LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(dp(30), dp(30));
            lp.rightMargin = dp(14);
            r.addView(badge(icon, iconColor), lp);
        }
        TextView t = tv(title, 17, G.REGULAR, click != null && icon == 0 && !chevron && value == null ? accent : LABEL);
        r.addView(t, new LinearLayout.LayoutParams(0, -2, 1));
        if (value != null) {
            TextView v = tv(value, 17, G.REGULAR, SECOND);
            v.setTag("value");
            r.addView(v);
        }
        if (chevron) {
            ImageView c = new ImageView(this);
            c.setImageDrawable(new Ui.SymDrawable(this, Sym.CHEVRON, THIRD, 0, false, dp(16)));
            LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(dp(16), dp(16));
            lp.leftMargin = dp(6);
            r.addView(c, lp);
        }
        if (click != null) { r.setBackground(pressable()); final Runnable ck = click; r.setOnClickListener(new View.OnClickListener() { public void onClick(View v) { ck.run(); } }); }
        g.addView(r, new LinearLayout.LayoutParams(-1, -2));
        return r;
    }

    EditText field(String hint, String value, boolean multi) {
        EditText e = new EditText(this);
        e.setBackground(null);
        e.setHint(hint);
        e.setText(value);
        e.setTextSize(TypedValue.COMPLEX_UNIT_SP, 17);
        e.setTypeface(AndroidG.font(this, G.REGULAR));
        e.setTextColor(LABEL);
        e.setHintTextColor(THIRD == 0xFF5A5A5F ? 0xFF6C6C70 : 0xFFB0B0B5);
        e.setLetterSpacing(-0.01f);
        e.setPadding(dp(16), dp(14), dp(16), dp(14));
        if (multi) {
            e.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_FLAG_MULTI_LINE | InputType.TYPE_TEXT_FLAG_CAP_SENTENCES);
            e.setMinLines(4);
            e.setGravity(Gravity.TOP | Gravity.START);
        } else {
            e.setSingleLine(true);
            e.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_FLAG_CAP_SENTENCES);
        }
        try { e.setTextCursorDrawable(null); } catch (Throwable ignored) {}
        e.setHighlightColor(Style.withAlpha(accent, 0.3f));
        return e;
    }

    TextView pill(String text, Runnable click) {
        TextView t = tv(text, 15, G.SEMI, gdUi ? 0xFFFFFFFF : accent);
        t.setGravity(Gravity.CENTER);
        t.setPadding(dp(18), dp(8), dp(18), dp(8));
        if (gdUi) {
            GradientDrawable g = new GradientDrawable(GradientDrawable.Orientation.TOP_BOTTOM, new int[]{0x2EFFFFFF, 0x14FFFFFF});
            g.setCornerRadius(dp(18));
            g.setStroke(Math.max(1, dp(0.75f)), 0x33FFFFFF);
            t.setBackground(g);
        } else t.setBackground(round(FILL, dp(18)));
        tappable(t, click);
        return t;
    }

    /** The main call to action: a glowing accent capsule. */
    TextView primary(String text, Runnable click) {
        int ink = Style.accentColor(Style.CLEAN, 5, dark) == accent || accent == 0xFF7DE3F0 ? 0xFF0B1020 : 0xFFFFFFFF;
        TextView t = tv(text, 15, G.SEMI, ink);
        t.setGravity(Gravity.CENTER);
        t.setPadding(dp(20), dp(9), dp(20), dp(9));
        GradientDrawable g = new GradientDrawable(GradientDrawable.Orientation.TL_BR,
                new int[]{Style.mix(accent, 0xFFFFFFFF, 0.18f), accent, Style.mix(accent, 0xFF000000, 0.12f)});
        g.setCornerRadius(dp(20));
        t.setBackground(g);
        t.setElevation(dp(6));
        if (Build.VERSION.SDK_INT >= 28) { t.setOutlineSpotShadowColor(accent); t.setOutlineAmbientShadowColor(accent); }
        tappable(t, click);
        return t;
    }

    /** iOS segmented control. */
    LinearLayout segmented(final String[] items, int sel, final Pick pick) {
        final LinearLayout s = new LinearLayout(this);
        s.setOrientation(LinearLayout.HORIZONTAL);
        s.setPadding(dp(3), dp(3), dp(3), dp(3));
        s.setBackground(round(gdUi ? 0x1AFFFFFF : dark ? 0xFF2C2C2E : 0xFFEEEEF0, dp(20)));
        final TextView[] tvs = new TextView[items.length];
        for (int i = 0; i < items.length; i++) {
            final int k = i;
            TextView t = tv(items[i], 14, G.MEDIUM, LABEL);
            t.setGravity(Gravity.CENTER);
            t.setSingleLine(true);
            tvs[i] = t;
            t.setOnClickListener(new View.OnClickListener() {
                public void onClick(View v) { segSelect(tvs, k); pick.pick(k); }
            });
            s.addView(t, new LinearLayout.LayoutParams(0, dp(32), 1));
        }
        segSelect(tvs, sel);
        return s;
    }

    void segSelect(TextView[] tvs, int sel) {
        for (int i = 0; i < tvs.length; i++) {
            boolean on = i == sel;
            Drawable onBg;
            if (gdUi) {
                GradientDrawable gg = new GradientDrawable(GradientDrawable.Orientation.TOP_BOTTOM,
                        new int[]{Style.withAlpha(accent, 0.55f), Style.withAlpha(accent, 0.32f)});
                gg.setCornerRadius(dp(17));
                gg.setStroke(Math.max(1, dp(0.75f)), Style.withAlpha(accent, 0.7f));
                onBg = gg;
            } else onBg = round(dark ? 0xFF636366 : 0xFFFFFFFF, dp(17));
            tvs[i].setBackground(on ? onBg : null);
            tvs[i].setElevation(on && !dark ? dp(2) : 0);
            tvs[i].setTextColor(on && gdUi ? 0xFFFFFFFF : LABEL);
            tvs[i].setTypeface(AndroidG.font(this, on ? G.SEMI : G.MEDIUM));
        }
    }

    void padded(LinearLayout g, View v) {
        LinearLayout box = new LinearLayout(this);
        box.setPadding(dp(14), dp(12), dp(14), dp(12));
        box.addView(v, new LinearLayout.LayoutParams(-1, -2));
        if (g.getChildCount() > 0) sep(g, 16);
        g.addView(box, new LinearLayout.LayoutParams(-1, -2));
    }

    /** Press feedback: a gentle shrink, like iOS buttons. */
    void tappable(final View v, final Runnable click) {
        v.setClickable(true);
        v.setOnTouchListener(new View.OnTouchListener() {
            public boolean onTouch(View view, MotionEvent e) {
                if (e.getAction() == MotionEvent.ACTION_DOWN) view.animate().scaleX(0.95f).scaleY(0.95f).alpha(0.7f).setDuration(90).start();
                else if (e.getAction() == MotionEvent.ACTION_UP || e.getAction() == MotionEvent.ACTION_CANCEL)
                    view.animate().scaleX(1f).scaleY(1f).alpha(1f).setDuration(220).start();
                return false;
            }
        });
        v.setOnClickListener(new View.OnClickListener() { public void onClick(View view) { click.run(); } });
    }

    void toast(String s) { Toast.makeText(this, s, Toast.LENGTH_SHORT).show(); }

    void hideKeyboard() {
        View f = getCurrentFocus();
        if (f != null) {
            ((InputMethodManager) getSystemService(Context.INPUT_METHOD_SERVICE)).hideSoftInputFromWindow(f.getWindowToken(), 0);
            f.clearFocus();
        }
    }

    Drawable wallpaper(float radius) {
        int[] cols = gdUi ? new int[]{0xFF1B2A6B, 0xFF3B1E6E, 0xFF6A2056}
                : dark ? new int[]{0xFF14204A, 0xFF3A1D5C, 0xFF5C1E46} : new int[]{0xFF8EC5FC, 0xFFC7B8FB, 0xFFF5B6D2};
        GradientDrawable g = new GradientDrawable(GradientDrawable.Orientation.TL_BR, cols);
        g.setCornerRadius(radius);
        if (gdUi) g.setStroke(Math.max(1, dp(0.75f)), 0x26FFFFFF);
        return g;
    }

    /** A live widget preview that redraws when the look changes. */
    ImageView preview(int kind, int wPx, int hPx) {
        ImageView iv = new ImageView(this);
        iv.setContentDescription(W.NAMES[kind] + " widget preview");
        previews.add(new Object[]{iv, kind, wPx, hPx});
        drawPreview(iv, kind, wPx, hPx);
        return iv;
    }

    void drawPreview(ImageView iv, int kind, int wPx, int hPx) {
        Data a = P.data(this, kind);
        a.preview = true; // draw the clock's time into the picture (on the Home Screen it's a live clock)
        iv.setImageBitmap(W.render(this, kind, Math.round(wPx / d), Math.round(hPx / d), P.style(this), a));
    }

    void redrawPreviews() {
        for (Object[] p : previews) drawPreview((ImageView) p[0], (Integer) p[1], (Integer) p[2], (Integer) p[3]);
    }

    void changed() { redrawPreviews(); W.updateAll(this); }

    // ---------------------------------------------------------------- Widgets tab

    void widgetsPage() {
        largeTitle("Widgets", "Tap Add to put a widget on your Home Screen. You can also touch and hold the Home Screen, then choose Widgets.");
        int[] order = {Draw.CLOCK, Draw.WEATHER, Draw.CAL, Draw.TODO, Draw.NOTES, Draw.SYS, Draw.QUOTE, Draw.COUNT, Draw.FENCE_A, Draw.FENCE_B, Draw.FENCE_C};
        int inner = contentW - dp(28);
        for (int k : order) {
            final int kind = k;
            boolean medium = W.SIZE[k][0] > 200;
            LinearLayout card = column();
            card.setBackground(card(dp(26)));
            if (!gdUi && !dark) card.setElevation(dp(2));
            card.setPadding(dp(14), dp(14), dp(14), dp(14));
            LinearLayout.LayoutParams clp = new LinearLayout.LayoutParams(-1, -2);
            clp.bottomMargin = dp(14);
            page.addView(card, clp);

            FrameLayout stage = new FrameLayout(this);
            stage.setBackground(wallpaper(dp(18)));
            int ph = medium ? Math.round(inner * 0.47f) : Math.round(Math.min(inner * 0.47f, dp(170)));
            int pw = medium ? inner - dp(28) : ph;
            ph = medium ? Math.round(pw * 155f / 330f) : ph;
            ImageView iv = preview(kind, pw, ph);
            FrameLayout.LayoutParams lp = new FrameLayout.LayoutParams(pw, ph, Gravity.CENTER);
            lp.setMargins(dp(14), dp(18), dp(14), dp(18));
            stage.addView(iv, lp);
            card.addView(stage, new LinearLayout.LayoutParams(-1, -2));

            LinearLayout info = new LinearLayout(this);
            info.setGravity(Gravity.CENTER_VERTICAL);
            info.setPadding(dp(2), dp(12), 0, 0);
            LinearLayout texts = column();
            texts.addView(tv(W.NAMES[k], 18, G.SEMI, LABEL));
            TextView ds = tv(DESC[k], 13, G.REGULAR, SECOND);
            ds.setPadding(0, dp(3), 0, 0);
            texts.addView(ds);
            info.addView(texts, new LinearLayout.LayoutParams(0, -2, 1));
            info.addView(primary("Add", new Runnable() { public void run() { pin(kind); } }));
            card.addView(info);
        }
    }

    void pin(int kind) {
        AppWidgetManager m = AppWidgetManager.getInstance(this);
        if (m.isRequestPinAppWidgetSupported()) {
            m.requestPinAppWidget(new ComponentName(this, W.CLS[kind]), null, null);
        } else {
            toast("Touch and hold your Home Screen, choose Widgets, then GlassDesk.");
        }
    }

    // ---------------------------------------------------------------- Appearance tab

    void lookPage() {
        largeTitle("Appearance", "Choose how your widgets and this app look.");

        // Design chooser: two big cards, like picking a wallpaper.
        LinearLayout pair = new LinearLayout(this);
        int cw = (contentW - dp(12)) / 2;
        String[] subs = {"Frosted glass with a soft glow", "Bright, simple and rounded"};
        for (int dsn = 0; dsn < 2; dsn++) {
            final int D = dsn;
            boolean sel = P.design(this) == D;
            FrameLayout holder = new FrameLayout(this);
            LinearLayout c = column();
            c.setPadding(dp(10), dp(10), dp(10), dp(14));
            Drawable bg = card(dp(24));
            if (sel && bg instanceof GradientDrawable) ((GradientDrawable) bg).setStroke(dp(2), accent);
            else if (sel) { GradientDrawable gg = round(CELL, dp(24)); gg.setStroke(dp(2), accent); bg = gg; }
            c.setBackground(bg);
            if (!gdUi && !dark) c.setElevation(dp(sel ? 4 : 1.5f));
            FrameLayout stage = new FrameLayout(this);
            GradientDrawable wall = new GradientDrawable(GradientDrawable.Orientation.TL_BR, D == Style.GLASSDESK
                    ? new int[]{0xFF1B2A6B, 0xFF3B1E6E, 0xFF6A2056} : new int[]{0xFF8EC5FC, 0xFFC7B8FB, 0xFFF5B6D2});
            wall.setCornerRadius(dp(16));
            stage.setBackground(wall);
            int iw = cw - dp(20) - dp(20);
            ImageView iv = new ImageView(this);
            Style st = new Style(D, D == Style.GLASSDESK ? sp.getInt("gd_theme", Style.GLASS) : sp.getInt("cl_theme", Style.AUTO),
                    P.systemDark(this), sp.getInt(D == Style.GLASSDESK ? "gd_accent" : "cl_accent", 0), P.tint(this));
            Data data = P.data(this, Draw.TODO);
            iv.setImageBitmap(W.render(this, Draw.TODO, Math.max(150, Math.round(iw / d)), Math.max(150, Math.round(iw / d)), st, data));
            FrameLayout.LayoutParams ilp = new FrameLayout.LayoutParams(iw, iw, Gravity.CENTER);
            ilp.setMargins(dp(10), dp(10), dp(10), dp(10));
            stage.addView(iv, ilp);
            c.addView(stage, new LinearLayout.LayoutParams(-1, -2));
            TextView n = tv(Style.DESIGN_NAMES[D], 17, G.SEMI, LABEL);
            n.setPadding(dp(4), dp(12), 0, dp(2));
            c.addView(n);
            TextView sb = tv(D == Style.GLASSDESK ? subs[0] + " · Default" : subs[1], 12, G.REGULAR, SECOND);
            sb.setPadding(dp(4), 0, dp(4), 0);
            c.addView(sb);
            holder.addView(c, new FrameLayout.LayoutParams(-1, -2));
            if (sel) {
                ImageView ck = new ImageView(this);
                ck.setImageDrawable(new Ui.SymDrawable(this, Sym.CHECK, onAccent(), accent, true, dp(26)));
                ck.setElevation(dp(8));
                FrameLayout.LayoutParams cl = new FrameLayout.LayoutParams(dp(26), dp(26), Gravity.TOP | Gravity.RIGHT);
                cl.setMargins(0, dp(16), dp(16), 0);
                holder.addView(ck, cl);
            }
            holder.setContentDescription(Style.DESIGN_NAMES[D] + " design" + (sel ? ", selected" : ""));
            tappable(holder, new Runnable() {
                public void run() {
                    if (P.design(MainActivity.this) == D) return;
                    sp.edit().putInt("design", D).apply();
                    chrome();
                    W.updateAll(MainActivity.this);
                    show(1, null);
                }
            });
            LinearLayout.LayoutParams hl = new LinearLayout.LayoutParams(0, -2, 1);
            if (D == 0) hl.rightMargin = dp(12);
            pair.addView(holder, hl);
        }
        page.addView(pair);

        // Live preview of the current design.
        header("Preview", null);
        LinearLayout stage = column();
        stage.setBackground(wallpaper(dp(26)));
        stage.setPadding(dp(14), dp(14), dp(14), dp(14));
        int inner = contentW - dp(28);
        int mh = Math.round(inner * 155f / 330f);
        stage.addView(preview(Draw.CLOCK, inner, mh), new LinearLayout.LayoutParams(inner, mh));
        LinearLayout two = new LinearLayout(this);
        int sw = (inner - dp(14)) / 2;
        LinearLayout.LayoutParams lp1 = new LinearLayout.LayoutParams(sw, sw);
        lp1.topMargin = dp(14);
        lp1.rightMargin = dp(14);
        two.addView(preview(Draw.CAL, sw, sw), lp1);
        LinearLayout.LayoutParams lp2 = new LinearLayout.LayoutParams(sw, sw);
        lp2.topMargin = dp(14);
        two.addView(preview(Draw.WEATHER, sw, sw), lp2);
        stage.addView(two);
        page.addView(stage);

        // Theme
        final boolean gdD = P.gd(this);
        header("Theme", null);
        LinearLayout g = group();
        final int[] map = gdD ? new int[]{Style.GLASS, Style.DARK, Style.LIGHT} : new int[]{Style.AUTO, Style.LIGHT, Style.DARK};
        String[] names = gdD ? new String[]{"Glass", "Dark", "Light"} : new String[]{"Automatic", "Light", "Dark"};
        int cur = 0;
        for (int i = 0; i < map.length; i++) if (map[i] == P.theme(this)) cur = i;
        padded(g, segmented(names, cur, new Pick() {
            public void pick(int i) { sp.edit().putInt(P.themeKey(MainActivity.this), map[i]).apply(); changed(); }
        }));
        footer(gdD ? "Glass lets your wallpaper show through. Dark and Light are solid panels with the same glow."
                : "Automatic follows your phone's light and dark mode the next time widgets refresh.");

        // Accent color
        header("Accent color", null);
        LinearLayout ag = group();
        final String[] an = Style.accentNames(P.design(this));
        final TextView name = tv(an[Math.min(an.length - 1, P.accent(this))], 13, G.MEDIUM, SECOND);
        LinearLayout dots = new LinearLayout(this);
        dots.setGravity(Gravity.CENTER);
        dots.setPadding(dp(8), dp(16), dp(8), dp(6));
        for (int i = 0; i < an.length; i++) {
            final int k = i;
            final int col = Style.accentColor(P.design(this), k, dark);
            final boolean sel = P.accent(this) == k;
            View dot = new View(this) {
                final Paint pp = new Paint(Paint.ANTI_ALIAS_FLAG);
                @Override protected void onDraw(Canvas c) {
                    float cx = getWidth() / 2f, cy = getHeight() / 2f, r = Math.min(cx, cy);
                    pp.setShader(null);
                    if (sel && gdUi) {
                        pp.setShader(new android.graphics.RadialGradient(cx, cy, r, new int[]{Style.withAlpha(col, 0.6f), col & 0xFFFFFF},
                                null, android.graphics.Shader.TileMode.CLAMP));
                        c.drawCircle(cx, cy, r, pp);
                        pp.setShader(null);
                    }
                    pp.setStyle(Paint.Style.FILL);
                    pp.setShader(new android.graphics.LinearGradient(0, cy - r, 0, cy + r, Style.mix(col, 0xFFFFFFFF, 0.2f), col, android.graphics.Shader.TileMode.CLAMP));
                    c.drawCircle(cx, cy, sel ? r * 0.6f : r * 0.78f, pp);
                    pp.setShader(null);
                    if (sel) {
                        pp.setStyle(Paint.Style.STROKE);
                        pp.setColor(col);
                        pp.setStrokeWidth(r * 0.12f);
                        c.drawCircle(cx, cy, r * 0.86f, pp);
                    }
                }
            };
            dot.setContentDescription(an[i]);
            tappable(dot, new Runnable() {
                public void run() {
                    sp.edit().putInt(P.accentKey(MainActivity.this), k).apply();
                    chrome();
                    W.updateAll(MainActivity.this);
                    show(1, null);
                }
            });
            dots.addView(dot, new LinearLayout.LayoutParams(0, dp(38), 1));
        }
        ag.addView(dots);
        name.setGravity(Gravity.CENTER);
        name.setPadding(0, dp(4), 0, dp(14));
        ag.addView(name);

        if (gdD) {
            header("Glass tint", null);
            LinearLayout tg = group();
            LinearLayout sl = new LinearLayout(this);
            sl.setGravity(Gravity.CENTER_VERTICAL);
            sl.setPadding(dp(16), dp(10), dp(16), dp(10));
            sl.addView(tv("Clear", 15, G.REGULAR, SECOND));
            SeekBar bar = new SeekBar(this);
            bar.setMax(100);
            bar.setProgress(P.tint(this));
            GradientDrawable track = round(0x26FFFFFF, dp(3));
            GradientDrawable fillD = new GradientDrawable(GradientDrawable.Orientation.LEFT_RIGHT, new int[]{Style.mix(accent, 0xFFFFFFFF, 0.3f), accent});
            fillD.setCornerRadius(dp(3));
            LayerDrawable ld = new LayerDrawable(new Drawable[]{track, new ClipDrawable(fillD, Gravity.START, ClipDrawable.HORIZONTAL)});
            ld.setId(0, android.R.id.background);
            ld.setId(1, android.R.id.progress);
            ld.setLayerHeight(0, dp(6)); ld.setLayerGravity(0, Gravity.CENTER_VERTICAL);
            ld.setLayerHeight(1, dp(6)); ld.setLayerGravity(1, Gravity.CENTER_VERTICAL);
            bar.setProgressDrawable(ld);
            GradientDrawable thumb = new GradientDrawable();
            thumb.setShape(GradientDrawable.OVAL);
            thumb.setColor(0xFFFFFFFF);
            thumb.setStroke(dp(3), accent);
            thumb.setSize(dp(26), dp(26));
            bar.setThumb(thumb);
            bar.setSplitTrack(false);
            bar.setOnSeekBarChangeListener(new SeekBar.OnSeekBarChangeListener() {
                public void onProgressChanged(SeekBar s, int p, boolean u) { if (u) { sp.edit().putInt("tint", p).apply(); redrawPreviews(); } }
                public void onStartTrackingTouch(SeekBar s) {}
                public void onStopTrackingTouch(SeekBar s) { W.updateAll(MainActivity.this); }
            });
            sl.addView(bar, new LinearLayout.LayoutParams(0, dp(40), 1));
            sl.addView(tv("Tinted", 15, G.REGULAR, SECOND));
            tg.addView(sl);
            footer("Slide left for more see-through glass, right for more solid. Android doesn't let widgets blur the wallpaper, so the glass is a clear tint.");
        }
    }

    // ---------------------------------------------------------------- Content tab

    void contentPage() {
        largeTitle("Content", "What your widgets show.");

        // Note
        header("Note", "note");
        LinearLayout ng = group();
        final EditText note = field("Write a note", sp.getString("note", ""), true);
        saveOnType(note, "note", Draw.NOTES);
        ng.addView(note);
        footer("Shown on the Notes widget. The first line becomes the title.");

        // To-do
        header("To-do", "todo");
        final LinearLayout tg = group();
        buildTodo(tg);
        footer("The widget shows the first 5. Tap a task on the widget to tick it off.");

        // Weather
        header("Weather", "weather");
        LinearLayout wg = group();
        LinearLayout r = new LinearLayout(this);
        r.setGravity(Gravity.CENTER_VERTICAL);
        r.setPadding(dp(16), dp(2), dp(14), dp(2));
        LinearLayout.LayoutParams blp = new LinearLayout.LayoutParams(dp(30), dp(30));
        r.addView(badge(Sym.LOCATION, tone(0)), blp);
        final EditText city = field("City, for example Haifa", sp.getString("wx_city", ""), false);
        city.setImeOptions(EditorInfo.IME_ACTION_DONE);
        city.setPadding(dp(14), dp(14), dp(8), dp(14));
        r.addView(city, new LinearLayout.LayoutParams(0, -2, 1));
        final TextView status = new TextView(this);
        final Runnable setCity = new Runnable() { public void run() { lookUpCity(city.getText().toString(), status); } };
        r.addView(pill("Set", setCity));
        city.setOnEditorActionListener(new TextView.OnEditorActionListener() {
            public boolean onEditorAction(TextView v, int a, KeyEvent e) { setCity.run(); return true; }
        });
        wg.addView(r);
        sep(wg, 16);
        LinearLayout units = new LinearLayout(this);
        units.setGravity(Gravity.CENTER_VERTICAL);
        units.setPadding(dp(16), dp(10), dp(14), dp(10));
        units.addView(tv("Units", 17, G.REGULAR, LABEL), new LinearLayout.LayoutParams(0, -2, 1));
        LinearLayout seg = segmented(new String[]{"°C", "°F"}, sp.getBoolean("wx_f", false) ? 1 : 0, new Pick() {
            public void pick(int i) {
                sp.edit().putBoolean("wx_f", i == 1).apply();
                new Thread(new Runnable() { public void run() { Wx.fetch(getApplicationContext()); W.updateKind(getApplicationContext(), Draw.WEATHER); W.updateKind(getApplicationContext(), Draw.CLOCK); } }).start();
            }
        });
        units.addView(seg, new LinearLayout.LayoutParams(dp(130), -2));
        wg.addView(units);
        TextView f = footer(Wx.hasCity(this) ? "Showing weather for " + sp.getString("wx_city", "") + ". Forecasts come from Open-Meteo."
                : "No city set yet. Forecasts come from Open-Meteo, and only the city you type is sent.");
        status.setTag(f);

        // Countdown
        header("Countdown", "countdown");
        LinearLayout cg = group();
        LinearLayout tr = new LinearLayout(this);
        tr.setGravity(Gravity.CENTER_VERTICAL);
        tr.setPadding(dp(16), dp(2), dp(4), dp(2));
        tr.addView(badge(Sym.HOURGLASS, tone(4)), new LinearLayout.LayoutParams(dp(30), dp(30)));
        final EditText title = field("What are you counting down to?", sp.getString("cd_title", ""), false);
        title.setPadding(dp(14), dp(14), dp(12), dp(14));
        saveOnType(title, "cd_title", Draw.COUNT);
        tr.addView(title, new LinearLayout.LayoutParams(0, -2, 1));
        cg.addView(tr);
        long cd = P.cdDate(this);
        final LinearLayout dateRow = row(cg, Sym.CALENDAR, tone(3), "Date",
                cd < 0 ? "Choose" : new SimpleDateFormat("d MMM yyyy", Locale.getDefault()).format(cd), true, null);
        dateRow.setBackground(pressable());
        dateRow.setOnClickListener(new View.OnClickListener() { public void onClick(View v) { pickDate(dateRow); } });
        footer("The Countdown widget counts the days to this date.");
    }

    void buildTodo(final LinearLayout tg) {
        tg.removeAllViews();
        final List<P.Task> list = P.todo(this);
        for (int i = 0; i < list.size(); i++) {
            final int k = i;
            final P.Task t = list.get(i);
            if (tg.getChildCount() > 0) sep(tg, 54);
            LinearLayout r = new LinearLayout(this);
            r.setGravity(Gravity.CENTER_VERTICAL);
            r.setMinimumHeight(dp(52));
            r.setPadding(dp(14), dp(8), dp(10), dp(8));
            ImageView circle = new ImageView(this);
            circle.setImageDrawable(t.done ? new Ui.SymDrawable(this, Sym.CHECK, onAccent(), accent, true, dp(26))
                    : ring(dp(26)));
            circle.setContentDescription(t.done ? "Done" : "Not done");
            circle.setPadding(dp(1), dp(1), dp(1), dp(1));
            tappable(circle, new Runnable() {
                public void run() { list.get(k).done = !list.get(k).done; P.saveTodo(MainActivity.this, list); buildTodo(tg); W.updateKind(MainActivity.this, Draw.TODO); }
            });
            r.addView(circle, new LinearLayout.LayoutParams(dp(28), dp(28)));
            TextView tx = tv(t.text, 17, G.REGULAR, t.done ? SECOND : LABEL);
            tx.setPadding(dp(14), 0, dp(8), 0);
            if (t.done) tx.setPaintFlags(tx.getPaintFlags() | Paint.STRIKE_THRU_TEXT_FLAG);
            r.addView(tx, new LinearLayout.LayoutParams(0, -2, 1));
            ImageView del = new ImageView(this);
            del.setImageDrawable(new Ui.SymDrawable(this, Sym.MINUS, 0xFFFFFFFF, dark ? 0xFFFF453A : 0xFFFF3B30, true, dp(22)));
            del.setContentDescription("Remove " + t.text);
            tappable(del, new Runnable() {
                public void run() { list.remove(k); P.saveTodo(MainActivity.this, list); buildTodo(tg); W.updateKind(MainActivity.this, Draw.TODO); }
            });
            LinearLayout.LayoutParams dl = new LinearLayout.LayoutParams(dp(30), dp(30));
            del.setPadding(dp(4), dp(4), dp(4), dp(4));
            r.addView(del, dl);
            tg.addView(r);
        }
        if (tg.getChildCount() > 0) sep(tg, 54);
        LinearLayout add = new LinearLayout(this);
        add.setGravity(Gravity.CENTER_VERTICAL);
        add.setPadding(dp(14), dp(2), dp(10), dp(2));
        ImageView plus = new ImageView(this);
        plus.setImageDrawable(new Ui.SymDrawable(this, Sym.PLUS, onAccent(), accent, true, dp(26)));
        plus.setPadding(dp(1), dp(1), dp(1), dp(1));
        add.addView(plus, new LinearLayout.LayoutParams(dp(28), dp(28)));
        final EditText e = field("New Task", "", false);
        e.setImeOptions(EditorInfo.IME_ACTION_DONE);
        e.setPadding(dp(14), dp(14), dp(8), dp(14));
        final Runnable addIt = new Runnable() {
            public void run() {
                String s = e.getText().toString().trim();
                if (s.isEmpty()) return;
                if (list.size() >= P.MAX_TODO) { toast("That's a lot of tasks! Remove a few first."); return; }
                list.add(new P.Task(s, false));
                P.saveTodo(MainActivity.this, list);
                buildTodo(tg);
                W.updateKind(MainActivity.this, Draw.TODO);
                View ne = tg.findViewWithTag("newtask");
                if (ne != null) ne.requestFocus();
            }
        };
        e.setTag("newtask");
        e.setOnEditorActionListener(new TextView.OnEditorActionListener() {
            public boolean onEditorAction(TextView v, int a, KeyEvent ev) { addIt.run(); return true; }
        });
        add.addView(e, new LinearLayout.LayoutParams(0, -2, 1));
        TextView addBtn = tv("Add", 17, G.SEMI, accent);
        addBtn.setPadding(dp(8), dp(8), dp(8), dp(8));
        tappable(addBtn, addIt);
        add.addView(addBtn);
        tg.addView(add);
    }

    Drawable ring(final int size) {
        GradientDrawable g = new GradientDrawable();
        g.setShape(GradientDrawable.OVAL);
        g.setStroke(Math.max(2, dp(1.6f)), dark ? 0xFF5A5A5F : 0xFFC4C4C7);
        g.setSize(size, size);
        return g;
    }

    void saveOnType(EditText e, final String key, final int kind) {
        e.addTextChangedListener(new TextWatcher() {
            final Runnable save = new Runnable() { public void run() { W.updateKind(MainActivity.this, kind); } };
            public void beforeTextChanged(CharSequence s, int a, int b, int c) {}
            public void onTextChanged(CharSequence s, int a, int b, int c) {}
            public void afterTextChanged(Editable s) {
                sp.edit().putString(key, s.toString()).apply();
                handler.removeCallbacks(save);
                handler.postDelayed(save, 700);
            }
        });
    }

    void lookUpCity(final String q, final TextView status) {
        hideKeyboard();
        if (q.trim().isEmpty()) return;
        final TextView f = (TextView) status.getTag();
        f.setText("Looking up " + q.trim() + "…");
        final Context app = getApplicationContext();
        new Thread(new Runnable() {
            public void run() {
                final String name = Wx.setCity(app, q);
                final boolean ok = name != null && Wx.fetch(app);
                W.updateKind(app, Draw.WEATHER);
                W.updateKind(app, Draw.CLOCK);
                handler.post(new Runnable() {
                    public void run() {
                        if (name == null) f.setText("Couldn't find that city. Check the spelling and your internet connection.");
                        else if (!ok) f.setText("Found " + name + ", but the forecast didn't load. It will try again later.");
                        else f.setText("Showing weather for " + name + ". Forecasts come from Open-Meteo.");
                    }
                });
            }
        }).start();
    }

    void pickDate(final LinearLayout dateRow) {
        Calendar c = Calendar.getInstance();
        long cur = P.cdDate(this);
        if (cur > 0) c.setTimeInMillis(cur); else c.add(Calendar.DAY_OF_MONTH, 30);
        DatePickerDialog dlg = new DatePickerDialog(this,
                dark ? android.R.style.Theme_DeviceDefault_Dialog_Alert : android.R.style.Theme_DeviceDefault_Light_Dialog_Alert,
                new DatePickerDialog.OnDateSetListener() {
                    public void onDateSet(DatePicker v, int y, int m, int day) {
                        sp.edit().putString("cd_date", String.format(Locale.US, "%04d-%02d-%02d", y, m + 1, day)).apply();
                        TextView val = (TextView) dateRow.findViewWithTag("value");
                        if (val != null) val.setText(new SimpleDateFormat("d MMM yyyy", Locale.getDefault()).format(P.cdDate(MainActivity.this)));
                        W.updateKind(MainActivity.this, Draw.COUNT);
                    }
                }, c.get(Calendar.YEAR), c.get(Calendar.MONTH), c.get(Calendar.DAY_OF_MONTH));
        dlg.show();
        dlg.getButton(DatePickerDialog.BUTTON_POSITIVE).setTextColor(accent);
        dlg.getButton(DatePickerDialog.BUTTON_NEGATIVE).setTextColor(accent);
    }

    // ---------------------------------------------------------------- Fences tab

    void fencesPage() {
        largeTitle("Fences", "A fence is a panel of up to 8 apps on your Home Screen, like an app folder you can always see.");
        int inner = contentW - dp(28);
        int mh = Math.round(inner * 155f / 330f);
        for (int f = 0; f < 3; f++) {
            final int fi = f;
            header("Fence " + (f + 1), "fence" + f);
            FrameLayout stage = new FrameLayout(this);
            stage.setBackground(wallpaper(dp(22)));
            stage.setPadding(dp(14), dp(14), dp(14), dp(14));
            stage.addView(preview(Draw.FENCE_A + f, inner, mh), new FrameLayout.LayoutParams(inner, mh));
            LinearLayout.LayoutParams slp = new LinearLayout.LayoutParams(-1, -2);
            slp.bottomMargin = dp(12);
            page.addView(stage, slp);
            LinearLayout g = group();
            LinearLayout nr = new LinearLayout(this);
            nr.setGravity(Gravity.CENTER_VERTICAL);
            nr.setPadding(dp(16), 0, dp(4), 0);
            nr.addView(tv("Name", 17, G.REGULAR, LABEL));
            final EditText name = field(P.FENCE_DEFAULT[f], sp.getString(P.FENCES[f] + "_name", ""), false);
            name.setGravity(Gravity.END | Gravity.CENTER_VERTICAL);
            name.setTextColor(SECOND);
            name.addTextChangedListener(new TextWatcher() {
                final Runnable save = new Runnable() { public void run() { W.updateKind(MainActivity.this, Draw.FENCE_A + fi); redrawPreviews(); } };
                public void beforeTextChanged(CharSequence s, int a, int b, int c) {}
                public void onTextChanged(CharSequence s, int a, int b, int c) {}
                public void afterTextChanged(Editable s) {
                    sp.edit().putString(P.FENCES[fi] + "_name", s.toString()).apply();
                    handler.removeCallbacks(save);
                    handler.postDelayed(save, 600);
                }
            });
            nr.addView(name, new LinearLayout.LayoutParams(0, -2, 1));
            g.addView(nr);
            int n = P.fenceApps(this, f).size();
            row(g, Sym.GRID, tone(1), n == 0 ? "Choose Apps" : "Change Apps", n + " of " + P.MAX_APPS, true,
                    new Runnable() { public void run() { chooseApps(fi); } });
            row(g, 0, 0, "Add to Home Screen", null, false, new Runnable() { public void run() { pin(Draw.FENCE_A + fi); } });
        }
        footer("Tap an app on the widget to open it. Tap the fence name on the widget to come back here.");
    }

    void chooseApps(final int f) {
        final Dialog dlg = new Dialog(this, dark ? android.R.style.Theme_DeviceDefault_NoActionBar : android.R.style.Theme_DeviceDefault_Light_NoActionBar);
        dlg.requestWindowFeature(Window.FEATURE_NO_TITLE);
        final List<String> chosen = new ArrayList<>(P.fenceApps(this, f));
        LinearLayout box = column();
        box.setBackgroundColor(BG);

        LinearLayout bar = new LinearLayout(this);
        bar.setGravity(Gravity.CENTER_VERTICAL);
        bar.setPadding(dp(8), dp(10), dp(8), dp(6));
        TextView cancel = tv("Cancel", 17, G.REGULAR, accent);
        cancel.setPadding(dp(10), dp(8), dp(10), dp(8));
        tappable(cancel, new Runnable() { public void run() { dlg.dismiss(); } });
        bar.addView(cancel);
        final TextView title = tv("", 17, G.SEMI, LABEL);
        title.setGravity(Gravity.CENTER);
        bar.addView(title, new LinearLayout.LayoutParams(0, -2, 1));
        TextView done = tv("Done", 17, G.SEMI, accent);
        done.setPadding(dp(10), dp(8), dp(10), dp(8));
        tappable(done, new Runnable() {
            public void run() {
                P.saveFenceApps(MainActivity.this, f, chosen);
                W.updateKind(MainActivity.this, Draw.FENCE_A + f);
                dlg.dismiss();
                show(3, "fence" + f);
            }
        });
        bar.addView(done);
        box.addView(bar);

        final EditText search = field("Search", "", false);
        search.setBackground(round(dark ? 0xFF1C1C1E : 0xFFE3E3E8, dp(12)));
        search.setPadding(dp(14), dp(9), dp(14), dp(9));
        search.setCompoundDrawablePadding(dp(8));
        LinearLayout.LayoutParams slp = new LinearLayout.LayoutParams(-1, -2);
        slp.setMargins(dp(16), dp(4), dp(16), dp(10));
        box.addView(search, slp);

        final ListView list = new ListView(this);
        list.setDivider(null);
        list.setSelector(new ColorDrawable(0));
        list.setBackground(round(CELL, dp(20)));
        list.setClipToOutline(true);
        list.setVerticalScrollBarEnabled(false);
        LinearLayout.LayoutParams llp = new LinearLayout.LayoutParams(-1, 0, 1);
        llp.setMargins(dp(16), 0, dp(16), dp(16));
        box.addView(list, llp);
        dlg.setContentView(box);
        Window w = dlg.getWindow();
        if (w != null) {
            w.setLayout(-1, -1);
            w.setStatusBarColor(BG);
            w.setNavigationBarColor(BG);
            w.setSoftInputMode(WindowManager.LayoutParams.SOFT_INPUT_ADJUST_RESIZE);
            if (!dark) w.getDecorView().setSystemUiVisibility(View.SYSTEM_UI_FLAG_LIGHT_STATUS_BAR | (Build.VERSION.SDK_INT >= 27 ? View.SYSTEM_UI_FLAG_LIGHT_NAVIGATION_BAR : 0));
        }
        final Runnable updTitle = new Runnable() { public void run() { title.setText(chosen.size() + " of " + P.MAX_APPS + " Apps"); } };
        updTitle.run();
        dlg.show();

        final Context app = getApplicationContext();
        new Thread(new Runnable() {
            public void run() {
                final List<ResolveInfo> all = P.launchable(app);
                final PackageManager pm = app.getPackageManager();
                final List<String> labels = new ArrayList<>();
                for (ResolveInfo r : all) labels.add(String.valueOf(r.loadLabel(pm)));
                handler.post(new Runnable() {
                    public void run() {
                        final AppAdapter ad = new AppAdapter(all, labels, chosen, updTitle);
                        list.setAdapter(ad);
                        search.addTextChangedListener(new TextWatcher() {
                            public void beforeTextChanged(CharSequence s, int a, int b, int c) {}
                            public void onTextChanged(CharSequence s, int a, int b, int c) {}
                            public void afterTextChanged(Editable s) { ad.filter(s.toString()); }
                        });
                    }
                });
            }
        }).start();
    }

    final class AppAdapter extends BaseAdapter {
        final List<ResolveInfo> all; final List<String> labels; final List<String> chosen; final Runnable onChange;
        final List<Integer> shown = new ArrayList<>();
        final Map<String, Drawable> icons = new HashMap<>();

        AppAdapter(List<ResolveInfo> all, List<String> labels, List<String> chosen, Runnable onChange) {
            this.all = all; this.labels = labels; this.chosen = chosen; this.onChange = onChange;
            filter("");
        }

        void filter(String q) {
            shown.clear();
            String ql = q.trim().toLowerCase(Locale.getDefault());
            for (int i = 0; i < all.size(); i++) if (ql.isEmpty() || labels.get(i).toLowerCase(Locale.getDefault()).contains(ql)) shown.add(i);
            notifyDataSetChanged();
        }

        String comp(int i) { ActivityInfo a = all.get(i).activityInfo; return new ComponentName(a.packageName, a.name).flattenToString(); }

        public int getCount() { return shown.size(); }
        public Object getItem(int p) { return shown.get(p); }
        public long getItemId(int p) { return shown.get(p); }

        public View getView(int pos, View cv, ViewGroup parent) {
            final int i = shown.get(pos);
            LinearLayout r = (LinearLayout) cv;
            if (r == null) {
                r = new LinearLayout(MainActivity.this);
                r.setGravity(Gravity.CENTER_VERTICAL);
                r.setMinimumHeight(dp(58));
                r.setPadding(dp(16), dp(8), dp(16), dp(8));
                ImageView ic = new ImageView(MainActivity.this);
                ic.setTag("icon");
                r.addView(ic, new LinearLayout.LayoutParams(dp(38), dp(38)));
                TextView t = tv("", 17, G.REGULAR, LABEL);
                t.setTag("label");
                t.setPadding(dp(14), 0, dp(8), 0);
                t.setSingleLine(true);
                t.setEllipsize(TextUtils.TruncateAt.END);
                r.addView(t, new LinearLayout.LayoutParams(0, -2, 1));
                ImageView ck = new ImageView(MainActivity.this);
                ck.setTag("check");
                r.addView(ck, new LinearLayout.LayoutParams(dp(22), dp(22)));
                r.setBackground(pressable());
            }
            final String c = comp(i);
            Drawable ic = icons.get(c);
            if (ic == null) {
                ic = new Ui.AppIcon(MainActivity.this, all.get(i).loadIcon(getPackageManager()), dp(38));
                icons.put(c, ic);
            }
            ((ImageView) r.findViewWithTag("icon")).setImageDrawable(ic);
            ((TextView) r.findViewWithTag("label")).setText(labels.get(i));
            boolean on = chosen.contains(c);
            ((ImageView) r.findViewWithTag("check")).setImageDrawable(on ? new Ui.SymDrawable(MainActivity.this, Sym.CHECK, accent, 0, false, dp(22)) : null);
            r.setOnClickListener(new View.OnClickListener() {
                public void onClick(View v) {
                    if (chosen.contains(c)) chosen.remove(c);
                    else if (chosen.size() >= P.MAX_APPS) { toast("A fence holds up to 8 apps."); return; }
                    else chosen.add(c);
                    onChange.run();
                    notifyDataSetChanged();
                }
            });
            return r;
        }
    }

    // ---------------------------------------------------------------- About tab

    void aboutPage() {
        LinearLayout top = column();
        top.setGravity(Gravity.CENTER_HORIZONTAL);
        top.setPadding(0, dp(12), 0, dp(8));
        ImageView ic = new ImageView(this);
        ic.setImageResource(R.mipmap.ic_launcher);
        ic.setElevation(dp(6));
        ic.setOutlineProvider(new android.view.ViewOutlineProvider() {
            public void getOutline(View v, android.graphics.Outline o) { o.setRoundRect(0, 0, v.getWidth(), v.getHeight(), v.getWidth() * 0.225f); }
        });
        ic.setClipToOutline(true);
        FrameLayout glowBox = new FrameLayout(this);
        View halo = new View(this);
        halo.setBackground(radial(Style.withAlpha(accent, gdUi ? 0.55f : 0.30f), dp(110)));
        glowBox.addView(halo, new FrameLayout.LayoutParams(dp(220), dp(220), Gravity.CENTER));
        glowBox.addView(ic, new FrameLayout.LayoutParams(dp(104), dp(104), Gravity.CENTER));
        top.addView(glowBox, new LinearLayout.LayoutParams(dp(220), dp(170)));
        TextView n = tv("GlassDesk", 32, G.BOLD, LABEL);
        n.setLetterSpacing(-0.025f);
        if (gdUi) n.setShadowLayer(dp(18), 0, 0, Style.withAlpha(accent, 0.5f));
        n.setPadding(0, dp(2), 0, dp(4));
        top.addView(n);
        top.addView(tv("Version " + VERSION + " for Android", 15, G.REGULAR, SECOND));
        page.addView(top);

        header("Good to know", null);
        LinearLayout g = group();
        String[][] tips = {
                {"" + Sym.SLIDERS, "Touch and hold a widget on your Home Screen to resize it. It redraws to fit."},
                {"" + Sym.CLOCK, "Widgets refresh about every 15 minutes to save battery. The clock is always live."},
                {"" + Sym.REFRESH, "Tap the System or Weather widget to refresh it right away. Tap the Quote widget for a new quote."},
                {"" + Sym.APPEARANCE, "Automatic style switches with your phone's dark mode the next time widgets refresh."}};
        int[] colors = {tone(7), tone(4), tone(6), tone(1)};
        for (int i = 0; i < tips.length; i++) {
            if (g.getChildCount() > 0) sep(g, 60);
            LinearLayout r = new LinearLayout(this);
            r.setPadding(dp(16), dp(13), dp(16), dp(13));
            LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(dp(30), dp(30));
            lp.rightMargin = dp(14);
            r.addView(badge(Integer.parseInt(tips[i][0]), colors[i]), lp);
            r.addView(tv(tips[i][1], 15, G.REGULAR, LABEL), new LinearLayout.LayoutParams(0, -2, 1));
            g.addView(r);
        }

        header("More", null);
        LinearLayout m = group();
        row(m, Sym.REFRESH, tone(6), "Refresh Widgets Now", null, false, new Runnable() {
            public void run() {
                W.updateAll(MainActivity.this);
                final Context app = getApplicationContext();
                new Thread(new Runnable() { public void run() { if (Wx.hasCity(app)) Wx.fetch(app); W.updateAll(app); } }).start();
                toast("Widgets refreshed.");
            }
        });
        row(m, Sym.CODE, tone(7), "GlassDesk on GitHub", null, true, new Runnable() {
            public void run() {
                try { startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse("https://github.com/Stapc-svg/GlassDesk"))); }
                catch (Exception e) { toast("No browser found."); }
            }
        });
        footer("Text is set in Inter, an open-source typeface by Rasmus Andersson. Weather by Open-Meteo.");
    }
}
