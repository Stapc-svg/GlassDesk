"""The Settings window."""
import os, datetime
from gi.repository import Gtk, Gdk, GLib

from . import core

CSS = b"""
.gd-settings { background: #12141c; color: #eef1f8; }
.gd-settings stacksidebar, .gd-settings stacksidebar list { background: #0d0f15; border-right: 1px solid rgba(255,255,255,0.06); }
.gd-settings stacksidebar row { padding: 10px 16px; color: rgba(238,241,248,0.75); }
.gd-settings stacksidebar row:selected { background: rgba(124,156,255,0.22); color: #fff; border-radius: 10px; }
.gd-settings .page-title { font-size: 22px; font-weight: 700; margin-bottom: 6px; }
.gd-settings .section { font-size: 11px; font-weight: 700; letter-spacing: 1px; color: rgba(238,241,248,0.5); margin-top: 14px; }
.gd-settings .card { background: rgba(255,255,255,0.045); border: 1px solid rgba(255,255,255,0.08); border-radius: 14px; }
.gd-settings .card row { padding: 10px 14px; background: transparent; }
.gd-settings .card row:hover { background: rgba(255,255,255,0.03); }
.gd-settings .sub { color: rgba(238,241,248,0.55); font-size: 12px; }
.gd-settings .title { font-size: 14px; }
.gd-settings label { color: #eef1f8; }
.gd-settings .sub, .gd-settings label.sub { color: rgba(238,241,248,0.55); }
.gd-settings .section, .gd-settings label.section { color: rgba(238,241,248,0.5); }
.gd-settings button, .gd-settings combobox button, .gd-settings spinbutton, .gd-settings spinbutton entry, .gd-settings spinbutton button {
  border-radius: 9px; padding: 6px 14px; background: #232735; background-image: none; color: #eef1f8;
  border: 1px solid rgba(255,255,255,0.10); box-shadow: none; text-shadow: none; }
.gd-settings button:hover { background: #2c3142; }
.gd-settings button label { color: inherit; }
.gd-settings scale trough { background: rgba(255,255,255,0.12); border: none; min-height: 4px; }
.gd-settings scale highlight { background: #7C9CFF; border: none; }
.gd-settings scale slider { background: #fff; border: none; min-width: 16px; min-height: 16px; }
.gd-settings switch { background: rgba(255,255,255,0.15); border: none; }
.gd-settings switch:checked { background: #7C9CFF; }
.gd-settings switch slider { background: #fff; border: none; }
.gd-settings menu, .gd-settings .menu { background: #232735; color: #eef1f8; }
.gd-settings button.suggested-action { background: #7C9CFF; color: #0b1020; border: none; }
.gd-settings button.suggested-action label { color: #0b1020; }
.gd-settings headerbar { background: #0d0f15; border-bottom: 1px solid rgba(255,255,255,0.06); color: #fff; }
"""


def themed_css(T):
    """The Settings window in the colors of the theme you picked."""
    hx = core.rgb_hex
    css = CSS.decode()
    light = not T.dark
    for old, new in (("#12141c", hx(T.ui_bg)), ("#0d0f15", hx(T.ui_side)), ("#7C9CFF", hx(T.accent)),
                     ("#232735", hx(T.ui_card if light else core.mix(T.ui_card, (1, 1, 1), 0.05))),
                     ("#2c3142", hx(core.mix(T.ui_card, T.accent, 0.15))), ("#eef1f8", hx(T.ui_text)), ("#0b1020", "#FFFFFF")):
        css = css.replace(old, new)
    if light:
        css = css.replace("rgba(238,241,248", "rgba(20,22,34").replace("rgba(255,255,255,0.0", "rgba(0,0,0,0.0") \
                 .replace("rgba(255,255,255,0.1", "rgba(0,0,0,0.1").replace("rgba(255,255,255,0.12)", "rgba(0,0,0,0.12)") \
                 .replace("rgba(255,255,255,0.15)", "rgba(0,0,0,0.15)").replace("color: #fff;", "color: %s;" % hx(T.ui_text))
    return css.encode()


class LookCard(Gtk.DrawingArea):
    """A small preview of one theme for the gallery."""

    def __init__(self, win, look):
        super().__init__()
        self.win, self.look = win, look
        self.set_size_request(132, 112)
        self.add_events(Gdk.EventMask.BUTTON_RELEASE_MASK | Gdk.EventMask.ENTER_NOTIFY_MASK | Gdk.EventMask.LEAVE_NOTIFY_MASK)
        self.hot = False
        self.connect("draw", self.draw)
        self.connect("button-release-event", lambda *a: win.pick_look(look))
        self.connect("enter-notify-event", lambda *a: (setattr(self, "hot", True), self.queue_draw()))
        self.connect("leave-notify-event", lambda *a: (setattr(self, "hot", False), self.queue_draw()))
        self.set_tooltip_text("%s: %s" % (look["name"], look["desc"]))

    def draw(self, _w, cr):
        import cairo, math
        from .glass import rounded, text
        L = self.look
        T = self.win.app.theme
        w = self.get_allocated_width()
        sh = 76
        dark = T.dark if L["mode"] == "auto" else L["mode"] == "dark"
        top, bot = L["dark"] if dark else L["light"]
        acc = core.hex_rgb(L["accent"])
        # stage: a little wallpaper
        rounded(cr, 1, 1, w - 2, sh, 12)
        g = cairo.LinearGradient(0, 0, w, sh)
        g.add_color_stop_rgb(0, 0.32, 0.22, 0.62)
        g.add_color_stop_rgb(1, 0.78, 0.3, 0.55)
        cr.set_source(g)
        cr.fill()
        # mini panel in the theme's colors
        px, py, pw, ph = 16, 14, w - 32, sh - 26
        rr = max(4, L["radius"] * 0.5)
        rounded(cr, px, py, pw, ph, rr)
        g = cairo.LinearGradient(0, py, 0, py + ph)
        a = 1 if L["solid"] else 0.55 + L["tint"] / 250
        g.add_color_stop_rgba(0, *(top + (a,)))
        g.add_color_stop_rgba(1, *(bot + (a,)))
        cr.set_source(g)
        cr.fill_preserve()
        cr.set_source_rgba(*(acc + (0.9,)) if L["neon"] else (1, 1, 1, 0.35))
        cr.set_line_width(1.6 if L["neon"] else 1)
        cr.stroke()
        tc = (1, 1, 1) if dark else (0.1, 0.1, 0.14)
        cr.set_source_rgb(*acc)
        cr.arc(px + 12, py + 12, 3.5, 0, 2 * math.pi)
        cr.fill()
        for i, (ww, al) in enumerate(((0.45, 0.5), (0.7, 0.9))):
            rounded(cr, px + 22 if i == 0 else px + 10, py + 9 + i * 12, pw * ww, 6, 3)
            cr.set_source_rgba(*(tc + (al,)))
            cr.fill()
        rounded(cr, px + 10, py + ph - 11, pw - 20, 4, 2)
        cr.set_source_rgba(*(tc + (0.2,)))
        cr.fill()
        rounded(cr, px + 10, py + ph - 11, (pw - 20) * 0.6, 4, 2)
        cr.set_source_rgb(*acc)
        cr.fill()
        sel = self.win.S.get("look") == L["id"]
        if sel or self.hot:
            rounded(cr, 1, 1, w - 2, sh, 12)
            cr.set_source_rgba(*(T.accent + (1,)) if sel else (1, 1, 1, 0.5))
            cr.set_line_width(2.5 if sel else 1.2)
            cr.stroke()
        text(cr, L["name"], 2, sh + 6, w - 4, size=9.6, bold=True, color=T.ui_text + (1,))
        text(cr, L["desc"], 2, sh + 22, w - 4, size=8, color=T.ui_sub + (1,))
        return True


class SettingsWindow(Gtk.Window):
    def __init__(self, app):
        super().__init__(title="GlassDesk Settings")
        self.app = app
        self.S = app.settings
        self.set_default_size(860, 620)
        self.set_icon_name("glassdesk")
        self.set_position(Gtk.WindowPosition.CENTER)
        self.prov = Gtk.CssProvider()
        self.prov.load_from_data(themed_css(app.theme))
        Gtk.StyleContext.add_provider_for_screen(self.get_screen(), self.prov, Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION)
        st = Gtk.Settings.get_default()
        if st:
            st.set_property("gtk-application-prefer-dark-theme", app.theme.dark)
        hb = Gtk.HeaderBar(title="GlassDesk", subtitle="Version " + core.VERSION)
        hb.set_show_close_button(True)
        self.set_titlebar(hb)
        self.get_style_context().add_class("gd-settings")
        self.stack = Gtk.Stack()
        self.stack.set_transition_type(Gtk.StackTransitionType.CROSSFADE)
        side = Gtk.StackSidebar()
        side.set_stack(self.stack)
        side.set_size_request(190, -1)
        box = Gtk.Box()
        box.pack_start(side, False, False, 0)
        box.pack_start(self.stack, True, True, 0)
        self.add(box)
        self.build()

    # ---- helpers
    def page(self, name, title):
        sw = Gtk.ScrolledWindow()
        sw.set_policy(Gtk.PolicyType.NEVER, Gtk.PolicyType.AUTOMATIC)
        v = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=8)
        v.set_border_width(26)
        lb = Gtk.Label(label=title, xalign=0)
        lb.get_style_context().add_class("page-title")
        v.pack_start(lb, False, False, 0)
        sw.add(v)
        self.stack.add_titled(sw, name, title)
        self._page = v
        self._card = None
        return v

    def section(self, title):
        lb = Gtk.Label(label=title.upper(), xalign=0)
        lb.get_style_context().add_class("section")
        self._page.pack_start(lb, False, False, 0)
        self._card = Gtk.ListBox()
        self._card.set_selection_mode(Gtk.SelectionMode.NONE)
        self._card.get_style_context().add_class("card")
        self._page.pack_start(self._card, False, False, 0)

    def row(self, title, sub, control):
        if self._card is None:
            self.section("")
        hb = Gtk.Box(spacing=12)
        tv = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=2)
        t = Gtk.Label(label=title, xalign=0)
        t.get_style_context().add_class("title")
        tv.pack_start(t, False, False, 0)
        if sub:
            s = Gtk.Label(label=sub, xalign=0)
            s.set_line_wrap(True)
            s.set_max_width_chars(60)
            s.get_style_context().add_class("sub")
            tv.pack_start(s, False, False, 0)
        hb.pack_start(tv, True, True, 0)
        if control is not None:
            control.set_valign(Gtk.Align.CENTER)
            hb.pack_end(control, False, False, 0)
        r = Gtk.ListBoxRow()
        r.add(hb)
        self._card.add(r)
        return r

    def toggle(self, key, after=None):
        sw = Gtk.Switch(active=bool(self.S.get(key)))
        def on(s, _p):
            self.S[key] = s.get_active()
            if after:
                after(s.get_active())
            self.app.settings_changed()
        sw.connect("notify::active", on)
        return sw

    def button(self, label, cb, primary=False):
        b = Gtk.Button(label=label)
        if primary:
            b.get_style_context().add_class("suggested-action")
        b.connect("clicked", lambda *a: cb())
        return b

    def combo(self, key, options, after=None):
        c = Gtk.ComboBoxText()
        for val, lbl in options:
            c.append(val, lbl)
        c.set_active_id(str(self.S.get(key)))
        def on(cb):
            v = cb.get_active_id()
            self.S[key] = int(v) if isinstance(self.S.get(key), int) and v.isdigit() else v
            if after:
                after()
            self.app.settings_changed()
        c.connect("changed", on)
        return c

    def slider(self, key, lo, hi, step=1):
        sc = Gtk.Scale.new_with_range(Gtk.Orientation.HORIZONTAL, lo, hi, step)
        sc.set_value(self.S.get(key))
        sc.set_size_request(220, -1)
        sc.set_value_pos(Gtk.PositionType.RIGHT)
        sc.set_digits(0)
        self._sl = getattr(self, "_sl", {})
        def on(s):
            self.S[key] = int(s.get_value())
            if key in self._sl:
                GLib.source_remove(self._sl[key])
            self._sl[key] = GLib.timeout_add(120, lambda: (self._sl.pop(key, None), self.app.settings_changed(), False)[2])
        sc.connect("value-changed", on)
        return sc

    def spin(self, key, lo, hi):
        sp = Gtk.SpinButton.new_with_range(lo, hi, 1)
        sp.set_value(self.S.get(key))
        def on(s):
            self.S[key] = int(s.get_value())
            self.app.settings_changed()
        sp.connect("value-changed", on)
        return sp

    # ---- pages
    def build(self):
        S = self.S
        # General
        self.page("general", "General")
        self.section("Startup")
        self.row("Start GlassDesk when I sign in", "Adds GlassDesk to your desktop's startup apps.",
                 self.toggle("start_at_login", core.set_autostart))
        self.section("Behaviour")
        self.row("Lock panel positions", "Stops fences and widgets from being moved by accident.", self.toggle("lock_positions"))
        self.row("Open items with a single click", "Instead of a double-click.", self.toggle("single_click"))
        self.row("Hide the normal desktop icons", "So they don't sit under the fences. Works on GNOME, Cinnamon, MATE and XFCE.",
                 self.toggle("hide_desktop_icons", core.set_desktop_icons_hidden))
        self.section("Keyboard shortcuts")
        sub = "Ctrl + Alt + F finds anything on your desktop; Ctrl + Alt + G shows or hides all panels." \
            if __import__("glassdesk.app", fromlist=["Keybinder"]).Keybinder else \
            "Add these in your system's Keyboard settings → Custom shortcuts:  glassdesk --search  and  glassdesk --toggle"
        self.row("Search and show/hide", sub, self.button("Search now", self.app.show_search))

        # Appearance
        self.page("appearance", "Appearance")
        self.section("Theme")
        self.row("Color mode", "Auto follows your system's dark/light setting.",
                 self.combo("theme", [("auto", "Auto"), ("dark", "Dark"), ("light", "Light")]))
        cb = Gtk.ColorButton()
        rgba = Gdk.RGBA()
        rgba.parse(S["accent"])
        cb.set_rgba(rgba)
        def acc(b):
            c = b.get_rgba()
            S["accent"] = "#%02X%02X%02X" % (int(c.red * 255), int(c.green * 255), int(c.blue * 255))
            self.app.settings_changed()
        cb.connect("color-set", acc)
        self.row("Accent color", "Used for highlights, charts and today's date.", cb)
        self.row("Match my wallpaper", "Picks the accent color from your wallpaper, and changes with it.", self.toggle("wall_accent"))
        for title, group in (("Themes", ""), ("Windows themes", "windows")):
            self.section(title)
            fb = Gtk.FlowBox()
            fb.set_selection_mode(Gtk.SelectionMode.NONE)
            fb.set_max_children_per_line(5)
            fb.set_min_children_per_line(3)
            fb.set_column_spacing(10)
            fb.set_row_spacing(10)
            fb.set_homogeneous(True)
            for look in core.LOOKS:
                if look["group"] == group:
                    fb.add(LookCard(self, look))
            fb.set_margin_start(14); fb.set_margin_end(14); fb.set_margin_top(12); fb.set_margin_bottom(12)
            self._card.add(fb)
        self.section("Glass")
        self.row("Glass style", "Frosted is the softest blur; See-through shows the wallpaper clearly.",
                 self.combo("glass_style", [("frosted", "Frosted"), ("blur", "Light blur"), ("clear", "Clear"), ("transparent", "See-through")]))
        self.row("Glass tint", "How dark (or light) the glass is.", self.slider("opacity", 0, 100))
        self.row("Corner radius", None, self.slider("radius", 0, 28))
        self.row("Size", "Makes every panel bigger or smaller.", self.slider("ui_scale", 70, 160, 5))
        self.section("Wallpaper changer")
        lbl = Gtk.Label(label=S["wallpaper_folder"] or "No folder chosen")
        def pick():
            d = self.app.choose_folder("Wallpaper folder", S["wallpaper_folder"])
            if d:
                S["wallpaper_folder"] = d
                lbl.set_text(d)
                S.save()
        self.row("Folder", None, self.button("Choose…", pick))
        self.row("Change every", "0 = off",
                 self.combo("wallpaper_minutes", [("0", "Off"), ("15", "15 minutes"), ("30", "30 minutes"), ("60", "Hour"), ("1440", "Day")]))
        self.row("Next wallpaper now", None, self.button("Next", self.app.next_wallpaper))

        # Widgets
        self.page("widgets", "Widgets")
        self.section("Show on the desktop")
        for key, name, sub in (("show_clock", "Clock", "Time, date, week number and a greeting"),
                               ("show_system", "System monitor", "CPU, memory, disk and battery"),
                               ("show_calendar", "Calendar", "Month view"), ("show_notes", "Notes", "Double-click to write"),
                               ("show_todo", "To-do list", "Click the box to tick a task off"),
                               ("show_weather", "Weather", "Open-Meteo, free and without an account"),
                               ("show_photos", "Photo frame", "Pictures from a folder"), ("show_countdown", "Countdown", "Days until a date"),
                               ("show_quote", "Quote of the day", "Double-click for another"),
                               ("show_github", "GitHub stats", "Stars, followers, repos and your last 14 days"),
                               ("show_network", "Network meter", "Live download and upload speed"),
                               ("show_focus", "Focus timer", "Pomodoro: focus, then a short break"),
                               ("show_music", "Music", "The song that's playing, or the last one you listened to"),
                               ("show_day", "Today", "Rings for the day, week, month and year")):
            self.row(name, sub, self.toggle(key))
        self.section("Stacks and sizes")
        self.row("Stack widgets", "Drag a widget onto another one, then click the tabs to switch. Fences stack the same way.", None)
        self.row("Resize widgets", "Drag a widget's bottom-right corner; everything inside grows with it.",
                 self.button("Reset all sizes", lambda: (S["zoom"].clear(), [setattr(w, "zoom", 1.0) for w in self.app.widgets.values()], self.app.settings_changed())))
        self.row("Layout for each monitor setup", "Remembers where everything goes when you plug in or unplug a monitor.", self.toggle("per_display"))
        self.section("Widget options")
        self.row("GitHub username", "Public data only, no sign-in needed.",
                 self.button("Change…", lambda: self.app.widgets["show_github"].choose_user()))
        self.row("Weather city", None, self.button("Change…", lambda: self.app.widgets["show_weather"].on_double_click(0, 0)))
        self.row("Focus minutes", None, self.spin("focus_minutes", 1, 120))
        self.row("Break minutes", None, self.spin("break_minutes", 1, 60))
        self.row("Photo folder", None, self.button("Choose…", lambda: self.app.widgets["show_photos"].on_double_click(0, 0)))
        self.row("Reset widget positions", "Puts every widget back on the right side of the screen.",
                 self.button("Reset", lambda: self.app.reset_positions(fences=False)))

        # Organizer
        self.page("organizer", "Organizer")
        self.section("Desktop folder")
        self.row(core.desktop_dir(), "GlassDesk sorts what is in this folder.", self.button("Open", lambda: core.open_path(core.desktop_dir())))
        self.row("Stack similar files", "4 or more files of the same type become one stack. Click it to open.", self.toggle("stacks"))
        self.row("Organize desktop", "See where every file goes and pick which ones to sort.", self.button("Preview…", self.app.show_organize, True))
        self.section("Category fences")
        for key, name, _e in core.CATEGORIES:
            sw = Gtk.Switch(active=S["categories"].get(key, True))
            def on(s, _p, k=key):
                S["categories"][k] = s.get_active()
                self.app.settings_changed()
            sw.connect("notify::active", on)
            self.row(name, None, sw)
        self.section("Your fences")
        for c in S["custom_fences"]:
            self.row(c["name"], "Drag icons into it.", self.button("Delete", lambda fid=c["id"]: (self.app.delete_fence(fid), self.rebuild())))
        for f in S["folder_fences"]:
            self.row(f.get("name") or f["path"], f["path"], self.button("Delete", lambda fid=f["id"]: (self.app.delete_fence(fid), self.rebuild())))
        hb = Gtk.Box(spacing=8)
        hb.pack_start(self.button("New fence…", lambda: (self.app.new_fence_dialog(), self.rebuild()), True), False, False, 0)
        hb.pack_start(self.button("New folder fence…", lambda: (self.app.new_folder_fence_dialog(), self.rebuild())), False, False, 0)
        if S["hidden_fences"]:
            hb.pack_start(self.button("Show hidden fences", lambda: (S.__setitem__("hidden_fences", []), self.app.settings_changed(), self.rebuild())), False, False, 0)
        self.row("Add a fence", None, hb)
        self.section("Rules")
        for i, r in enumerate(S["rules"]):
            self.row("%s  →  %s" % (r["pattern"], self.app.organizer.fence_name(r["fence"])), None,
                     self.button("Remove", lambda i=i: (S["rules"].pop(i), self.app.settings_changed(), self.rebuild())))
        if not S["rules"]:
            self.row("No rules yet", "Right-click a fence title → Add a rule, for example *.pdf → School.", None)
        self.section("Layout")
        self.row("Reset fence layout", "Puts every fence back in columns on the left.",
                 self.button("Reset", lambda: self.app.reset_positions(widgets=False)))

        # Backup
        self.page("backup", "Backup & sync")
        self.section("Your layout")
        def exp():
            p = self.app.choose_file("Export layout", True, "my-desktop-%s.glassdesk" % datetime.date.today().isoformat())
            if p:
                self.app.export_backup(p if p.endswith(".glassdesk") else p + ".glassdesk")
        def imp():
            p = self.app.choose_file("Import layout")
            if p:
                try:
                    self.app.import_backup(p)
                    self.rebuild()
                except Exception as e:
                    self.app.message("Couldn't import that file", str(e))
        self.row("Export", "Saves fences, rules, widgets and looks to a .glassdesk file you can import on any Linux PC.",
                 self.button("Export…", exp, True))
        self.row("Import", "Loads a .glassdesk file.", self.button("Import…", imp))
        self.row("Automatic backups", "One a day, last 7 kept, in " + core.BACKUP_DIR, self.button("Open folder", lambda: core.open_path(core.BACKUP_DIR)))

        # About
        self.page("about", "About")
        self.section("GlassDesk for Linux")
        self.row("Version " + core.VERSION, "Free and open source. No subscription, no ads.",
                 self.button("Check for updates", lambda: self.app.check_updates(True), True))
        self.row("GitHub", "github.com/" + core.REPO, self.button("Open", lambda: core.open_path("https://github.com/" + core.REPO)))
        self.row("Settings file", core.SETTINGS_FILE, self.button("Open folder", lambda: core.open_path(core.CONFIG_DIR)))
        self.row("Quit GlassDesk", "Panels close; your files are not touched.", self.button("Quit", self.app.quit_app))

    def pick_look(self, look):
        core.apply_look(self.S, look)
        self.app.settings_changed()
        self.prov.load_from_data(themed_css(self.app.theme))
        st = Gtk.Settings.get_default()
        if st:
            st.set_property("gtk-application-prefer-dark-theme", self.app.theme.dark)
        self.queue_draw()

    def rebuild(self):
        name = self.stack.get_visible_child_name()
        for c in self.stack.get_children():
            self.stack.remove(c)
        self.build()
        self.show_all()
        if name:
            self.stack.set_visible_child_name(name)
