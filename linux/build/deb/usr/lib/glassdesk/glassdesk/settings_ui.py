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


class SettingsWindow(Gtk.Window):
    def __init__(self, app):
        super().__init__(title="GlassDesk Settings")
        self.app = app
        self.S = app.settings
        self.set_default_size(860, 620)
        self.set_icon_name("glassdesk")
        self.set_position(Gtk.WindowPosition.CENTER)
        prov = Gtk.CssProvider()
        prov.load_from_data(CSS)
        Gtk.StyleContext.add_provider_for_screen(self.get_screen(), prov, Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION)
        st = Gtk.Settings.get_default()
        if st:
            st.set_property("gtk-application-prefer-dark-theme", True)
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
        tb = Gtk.Box(spacing=6)
        for name in core.THEMES:
            def apply(n=name):
                S.update(core.THEMES[n])
                self.app.settings_changed()
            tb.pack_start(self.button(name, apply), False, False, 0)
        self.row("Themes", "One click looks.", None)
        self._card.add(tb)
        tb.set_margin_start(14); tb.set_margin_bottom(10)
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
                               ("show_focus", "Focus timer", "Pomodoro: focus, then a short break")):
            self.row(name, sub, self.toggle(key))
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

    def rebuild(self):
        name = self.stack.get_visible_child_name()
        for c in self.stack.get_children():
            self.stack.remove(c)
        self.build()
        self.show_all()
        if name:
            self.stack.set_visible_child_name(name)
