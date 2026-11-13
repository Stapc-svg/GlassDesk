"""GlassDesk for Linux - the application: tray icon, layout, fences, dialogs."""
import datetime, json, os, random, shutil, sys, time, uuid
import cairo
from gi.repository import Gtk, Gdk, Gio, GLib, GdkPixbuf

from . import core
from .core import Settings, Theme
from .glass import Backdrop, popup
from .fences import Organizer, Fence
from . import widgets as W

try:
    import gi
    gi.require_version("AyatanaAppIndicator3", "0.1")
    from gi.repository import AyatanaAppIndicator3 as AppIndicator
except Exception:
    try:
        gi.require_version("AppIndicator3", "0.1")
        from gi.repository import AppIndicator3 as AppIndicator
    except Exception:
        AppIndicator = None

try:
    gi.require_version("Keybinder", "3.0")
    from gi.repository import Keybinder
except Exception:
    Keybinder = None


class GlassDeskApp(Gtk.Application):
    def __init__(self):
        super().__init__(application_id=core.APP_ID, flags=Gio.ApplicationFlags.HANDLES_COMMAND_LINE)
        self.settings = None
        self.windows = {}
        self.fences = {}
        self.visible_all = True
        self.paused = False
        self.started = False
        self.settings_win = None
        self.search_win = None
        self.indicator = None
        self._hold = False
        self._fixed_now = None
        self._screen_id = 0
        self.live_groups = {}
        self._drop_target = None
        self._css = None

    # ------------------------------------------------------------ command line
    def do_command_line(self, cmd):
        try:
            return self._command_line(cmd.get_arguments()[1:])
        except Exception:
            import traceback
            core.log(traceback.format_exc())
            traceback.print_exc()
            if not self.started or core.PREVIEW:
                self.quit()
            return 1

    def _command_line(self, args):
        if "--quit" in args or "--exit" in args:
            if self.started:
                self.quit_app()
            return 0
        self._launched = self.started
        if not self.started:
            self.start("--startup" in args, args)
            if "--preview" in args:
                return 0
            if "--startup" in args:
                return 0
        if "--search" in args:
            self.show_search()
        if "--toggle" in args:
            self.toggle_all()
        if "--settings" in args or (self._launched and not args):
            self.show_settings()
        return 0

    # ------------------------------------------------------------ startup
    def now(self):
        return self._fixed_now or datetime.datetime.now()

    def start(self, at_login, args):
        self.started = True
        self.hold()
        self.settings = Settings()
        if os.environ.get("GLASSDESK_LIGHT"):
            self.settings["theme"] = "light"
        if core.PREVIEW:
            look = core.LOOK.get(os.environ.get("GLASSDESK_PREVIEW_LOOK", ""))
            if look:
                core.apply_look(self.settings, look)
                if os.environ.get("GLASSDESK_LIGHT"):
                    self.settings["theme"] = "light"
            else:
                self.settings["look"] = "glassdesk"
            grp = os.environ.get("GLASSDESK_PREVIEW_GROUP")
            if grp:
                self.settings["groups"] = [grp.split(",")]
            self.settings["show_photos"] = bool(os.environ.get("GLASSDESK_SAMPLE_PHOTO"))
        self.switch_display_layout()
        self.pick_wallpaper_accent()
        self.theme = Theme(self.settings)
        self.apply_css()
        self.organizer = Organizer(self)
        self.install_icon()
        self.make_widgets()
        self.rebuild()
        self.organizer.watch()
        if "--preview" in args:
            i = args.index("--preview")
            out = args[i + 1] if i + 1 < len(args) else "preview"
            GLib.timeout_add(900, lambda: self.write_preview(out))
            return
        self.make_tray()
        self.bind_keys()
        scr = Gdk.Screen.get_default()
        scr.connect("size-changed", self._screen_changed)
        scr.connect("monitors-changed", self._screen_changed)
        if not self.settings.get("_first_run_done"):
            self.settings["_first_run_done"] = True
            core.set_autostart(self.settings["start_at_login"])
            self.settings.save()
        # the welcome screen shows once ever, and stays gone after updates
        if not self.settings.get("_welcome_shown"):
            self.settings["_welcome_shown"] = core.VERSION
            self.settings.save()
            GLib.timeout_add(1200, lambda: (self.show_welcome(), False)[1])
        if self.settings["hide_desktop_icons"]:
            core.set_desktop_icons_hidden(True)
        GLib.timeout_add_seconds(15, self._check_wallpaper)
        GLib.timeout_add_seconds(60, self._minute_jobs)
        GLib.timeout_add_seconds(20, lambda: (self.check_updates(False), False)[1])
        self.daily_backup()

    def install_icon(self):
        """Makes sure the 'glassdesk' icon name resolves even when run from a folder."""
        here = os.path.dirname(os.path.abspath(__file__))
        theme = Gtk.IconTheme.get_default()
        for d in (os.path.join(here, "icons"),):
            if os.path.isdir(d):
                theme.append_search_path(d)
        icon = os.path.join(here, "icons", "glassdesk.png")
        if os.path.exists(icon):
            Gtk.Window.set_default_icon_from_file(icon)

    def make_widgets(self):
        self.widgets = {cls.SETTING: cls(self) for cls in W.ALL}
        if core.PREVIEW:
            self._fixed_now = datetime.datetime(2026, 10, 22, 9, 41)
            for wd in self.widgets.values():
                for fn in ("sample", "set_sample"):
                    if hasattr(wd, fn):
                        getattr(wd, fn)()

    # ------------------------------------------------------------ layout
    def _screen_changed(self, *a):
        """Resolution changed or a monitor was plugged in/out: put every panel back on screen."""
        if self._screen_id:
            GLib.source_remove(self._screen_id)
        def go():
            self._screen_id = 0
            self.switch_display_layout()
            self.place_all()
            for w in self.all_windows():
                w.queue_draw()
            return False
        self._screen_id = GLib.timeout_add(600, go)

    def _on_screen(self, x, y, pw, ph):
        """Moves a saved position back onto a monitor if it is no longer on any screen,
        and pulls a panel that hangs past the edge of its monitor fully back on."""
        disp = Gdk.Display.get_default()
        for i in range(disp.get_n_monitors()):
            g = disp.get_monitor(i).get_workarea()
            # keep it if at least a good part of the panel's top is on this monitor
            if x + min(pw, 80) > g.x and x + 40 < g.x + g.width and y + 20 > g.y - 10 and y + 30 < g.y + g.height:
                if core.PREVIEW:
                    return x, y
                w, h = min(pw, g.width), min(ph, g.height)
                return max(g.x, min(x, g.x + g.width - w)), max(g.y, min(y, g.y + g.height - h))
        wa = self.workarea()
        gap = int(self.theme.s(14))
        x = max(wa.x + gap, min(x, wa.x + wa.width - pw - gap))
        y = max(wa.y + gap, min(y, wa.y + wa.height - min(ph, wa.height // 2) - gap))
        return x, y

    def workarea(self):
        disp = Gdk.Display.get_default()
        mon = disp.get_primary_monitor() or disp.get_monitor(0)
        return mon.get_workarea()

    def rebuild(self):
        S = self.settings
        self.theme = Theme(S)
        ids = self.organizer.fence_ids()
        for fid in list(self.fences):
            if fid not in ids:
                self.fences.pop(fid).destroy()
        for fid in ids:
            if fid not in self.fences:
                self.fences[fid] = Fence(self, fid)
        self.refresh_fences()
        for key, wd in self.widgets.items():
            wd.wanted = bool(S.get(key))
        for f in self.fences.values():
            f.wanted = True
        self.apply_groups()
        self.place_all()
        self.show_hide()

    def show_hide(self):
        for win in self.all_windows():
            win.apply_size()
            if win.wanted and self.visible_all:
                win.show_all()
            else:
                win.hide()

    def all_windows(self):
        return list(self.fences.values()) + list(self.widgets.values())

    def refresh_fences(self):
        groups = self.organizer.assign()
        for fid, f in self.fences.items():
            f.set_items(groups.get(fid, []))

    def place_all(self):
        wa = self.workarea()
        gap = int(self.theme.s(14))
        # fences: columns from the left
        x, y, colw = wa.x + gap, wa.y + gap, 0
        for fid, f in self.fences.items():
            pos = self.settings["positions"].get(f.key)
            pw, ph = f.panel_size()
            if pos:
                f.place(*self._on_screen(pos[0], pos[1], pw, ph))
                continue
            if y + ph > wa.y + wa.height - gap and y > wa.y + gap:
                x += colw + gap
                y, colw = wa.y + gap, 0
            f.place(x, y)
            y += ph + gap
            colw = max(colw, pw)
        # widgets: columns from the right
        order = ["show_clock", "show_system", "show_calendar", "show_weather", "show_music", "show_day", "show_github", "show_focus",
                 "show_network", "show_todo", "show_notes", "show_quote", "show_countdown", "show_photos"]
        x, y, colw = wa.x + wa.width - gap, wa.y + gap, 0
        for key in order:
            wd = self.widgets[key]
            if not wd.wanted:
                continue
            pos = self.settings["positions"].get(wd.key)
            pw, ph = wd.panel_size()
            ph = wd.content_height(ph)
            if pos:
                wd.place(*self._on_screen(pos[0], pos[1], pw, ph))
                continue
            if y + ph > wa.y + wa.height - gap and y > wa.y + gap:
                x -= colw + gap
                y, colw = wa.y + gap, 0
            wd.place(x - pw, y)
            y += ph + gap
            colw = max(colw, pw)

    def reset_positions(self, fences=True, widgets=True):
        for k in list(self.settings["positions"]):
            if (fences and k.startswith("fence:")) or (widgets and k.startswith("w:")):
                del self.settings["positions"][k]
        self.settings.save()
        self.place_all()

    def settings_changed(self):
        self.pick_wallpaper_accent()
        self.theme = Theme(self.settings)
        self.apply_css()
        self.rebuild()
        for w in self.all_windows():
            w.queue_draw()
        f = self.widgets.get("show_focus")
        if f:
            f.settings_changed()
        self.settings.save()

    def toggle_all(self):
        self.visible_all = not self.visible_all
        for w in self.all_windows():
            if self.visible_all and w.wanted:
                w.show_all()
            else:
                w.hide()

    def hide_widget(self, setting):
        self.settings[setting] = False
        self.settings_changed()

    # ------------------------------------------------------------ periodic jobs
    def _check_wallpaper(self):
        if Backdrop.check_changed():
            if self.settings.get("wall_accent"):
                self.settings_changed()
            for w in self.all_windows():
                w.queue_draw()
        return True

    def _minute_jobs(self):
        S = self.settings
        mins = int(S.get("wallpaper_minutes") or 0)
        if mins and S.get("wallpaper_folder") and os.path.isdir(S["wallpaper_folder"]):
            last = S.get("_wall_last", 0)
            if time.time() - last >= mins * 60:
                self.next_wallpaper()
        if datetime.date.today().isoformat() != S.get("_backup_day"):
            self.daily_backup()
        if time.time() - S.get("last_update_check", 0) > 86400:
            self.check_updates(False)
        return True

    def next_wallpaper(self):
        d = self.settings.get("wallpaper_folder")
        if not d or not os.path.isdir(d):
            return
        files = [os.path.join(d, n) for n in sorted(os.listdir(d)) if n.lower().endswith((".jpg", ".jpeg", ".png", ".webp"))]
        if not files:
            return
        cur = self.settings.get("_wall_cur", "")
        nxt = files[(files.index(cur) + 1) % len(files)] if cur in files else random.choice(files)
        if core.set_wallpaper(nxt):
            self.settings["_wall_cur"], self.settings["_wall_last"] = nxt, time.time()
            self.settings.save()
            GLib.timeout_add(1500, self._check_wallpaper)

    def daily_backup(self):
        try:
            os.makedirs(core.BACKUP_DIR, exist_ok=True)
            day = datetime.date.today().isoformat()
            self.export_backup(os.path.join(core.BACKUP_DIR, "glassdesk-%s.glassdesk" % day))
            files = sorted(f for f in os.listdir(core.BACKUP_DIR) if f.endswith(".glassdesk"))
            for f in files[:-7]:
                os.remove(os.path.join(core.BACKUP_DIR, f))
            self.settings["_backup_day"] = day
            self.settings.save()
        except Exception as e:
            core.log("backup: %s" % e)

    def export_backup(self, path):
        data = {k: v for k, v in self.settings.items() if not k.startswith("_")}
        with open(path, "w") as f:
            json.dump({"glassdesk": core.VERSION, "settings": data}, f, indent=1)

    def import_backup(self, path):
        with open(path) as f:
            data = json.load(f)
        s = data.get("settings", data)
        for k, v in s.items():
            if k in core.DEFAULTS:
                self.settings[k] = v
        self.settings.save_now()
        self.settings_changed()

    def check_updates(self, manual):
        if core.PREVIEW:
            return
        def done(j, err):
            self.settings["last_update_check"] = time.time()
            self.settings.save()
            if err or not j:
                if manual:
                    self.message("Couldn't check for updates", "GitHub couldn't be reached. Try again later.")
                return
            tag = (j.get("tag_name") or "").lstrip("vV")
            def ver(s):
                try:
                    return tuple(int(p) for p in s.split("."))
                except Exception:
                    return (0,)
            if ver(tag) > ver(core.VERSION):
                core.notify("GlassDesk %s is available" % tag, "Open Settings → About to download it.")
                self.update_available = (tag, j.get("html_url") or "https://github.com/%s/releases" % core.REPO)
                if manual:
                    if self.confirm("GlassDesk %s is available" % tag, "Open the download page?"):
                        core.open_path(self.update_available[1])
            elif manual:
                self.message("You're up to date", "GlassDesk %s is the newest version." % core.VERSION)
        core.in_background(lambda: core.http_json("https://api.github.com/repos/%s/releases/latest" % core.REPO), done)

    # ------------------------------------------------------------ tray + keys
    def make_tray(self):
        menu = Gtk.Menu()
        for label, cb in (("Settings", self.show_settings), ("Find on desktop", self.show_search),
                          ("Organize desktop…", self.show_organize),
                          ("Show / hide panels", self.toggle_all), (None, None), ("Quit GlassDesk", self.quit_app)):
            mi = Gtk.SeparatorMenuItem() if label is None else Gtk.MenuItem(label=label)
            if cb:
                mi.connect("activate", lambda _m, f=cb: f())
            menu.append(mi)
        menu.show_all()
        self._tray_menu = menu
        icon_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "icons")
        if AppIndicator is not None:
            ind = AppIndicator.Indicator.new("glassdesk", "glassdesk", AppIndicator.IndicatorCategory.APPLICATION_STATUS)
            if os.path.isdir(icon_dir):
                ind.set_icon_theme_path(icon_dir)
            ind.set_status(AppIndicator.IndicatorStatus.ACTIVE)
            ind.set_title("GlassDesk")
            ind.set_menu(menu)
            self.indicator = ind
        else:
            try:
                si = Gtk.StatusIcon()
                si.set_from_icon_name("glassdesk")
                si.set_tooltip_text("GlassDesk")
                si.connect("activate", lambda *a: self.show_settings())
                si.connect("popup-menu", lambda s, b, t: menu.popup(None, None, Gtk.StatusIcon.position_menu, s, b, t))
                self.indicator = si
            except Exception:
                self.indicator = None

    def bind_keys(self):
        if Keybinder is None:
            return
        try:
            Keybinder.init()
            Keybinder.bind("<Ctrl><Alt>G", lambda *a: self.toggle_all())
            Keybinder.bind("<Ctrl><Alt>F", lambda *a: self.show_search())
        except Exception as e:
            core.log("keys: %s" % e)

    def quit_app(self):
        if self.settings and self.settings["hide_desktop_icons"]:
            core.set_desktop_icons_hidden(False)
        if self.settings:
            self.settings.save_now()
        for w in self.all_windows():
            w.destroy()
        self.release()
        self.quit()

    # ------------------------------------------------------------ files
    def pin(self, name, fid):
        cat = core.category_of(os.path.join(core.desktop_dir(), name))
        if fid == cat or fid is None:
            self.settings["pins"].pop(name, None)
        else:
            self.settings["pins"][name] = fid
        self.settings.save()

    def move_files(self, paths, folder, copy=False):
        errors = []
        for p in paths:
            src = Gio.File.new_for_path(p)
            dst_path = os.path.join(folder, os.path.basename(p))
            if os.path.abspath(dst_path) == os.path.abspath(p):
                continue
            base, ext = os.path.splitext(dst_path)
            n = 2
            while os.path.exists(dst_path):
                dst_path = "%s (%d)%s" % (base, n, ext)
                n += 1
            dst = Gio.File.new_for_path(dst_path)
            try:
                if copy:
                    if os.path.isdir(p):
                        shutil.copytree(p, dst_path)
                    else:
                        src.copy(dst, Gio.FileCopyFlags.NONE, None, None, None)
                else:
                    src.move(dst, Gio.FileCopyFlags.NONE, None, None, None)
            except Exception as e:
                try:
                    (shutil.copytree if os.path.isdir(p) else shutil.copy2)(p, dst_path) if copy else shutil.move(p, dst_path)
                except Exception as e2:
                    errors.append("%s: %s" % (os.path.basename(p), e2))
        if errors:
            self.message("Some files couldn't be moved", "\n".join(errors[:6]))
        self.organizer.refresh_soon()

    def rename_file(self, path):
        old = os.path.basename(path)
        new = self.ask_text("Rename", "New name", old)
        if not new or new == old or "/" in new:
            return
        dst = os.path.join(os.path.dirname(path), new)
        if os.path.exists(dst):
            self.message("Can't rename", "Something called \"%s\" is already there." % new)
            return
        try:
            os.rename(path, dst)
            if old in self.settings["pins"]:
                self.settings["pins"][new] = self.settings["pins"].pop(old)
                self.settings.save()
        except Exception as e:
            self.message("Can't rename", str(e))
        self.organizer.refresh_soon()

    def trash_file(self, path):
        if not self.confirm("Move to Trash?", "\"%s\" will be moved to the Trash." % os.path.basename(path)):
            return
        try:
            Gio.File.new_for_path(path).trash(None)
        except Exception as e:
            self.message("Couldn't move it to the Trash", str(e))
        self.organizer.refresh_soon()

    # ------------------------------------------------------------ fences
    def new_fence_dialog(self):
        name = self.ask_text("New fence", "Name (for example School or Games)", "")
        if not name:
            return
        fid = "c-" + uuid.uuid4().hex[:8]
        self.settings["custom_fences"].append({"id": fid, "name": name.strip()})
        self.settings.save()
        self.rebuild()

    def new_folder_fence_dialog(self):
        d = self.choose_folder("Show a folder as a fence", os.path.expanduser("~/Downloads"))
        if not d:
            return
        fid = "f-" + uuid.uuid4().hex[:8]
        self.settings["folder_fences"].append({"id": fid, "path": d, "name": os.path.basename(d.rstrip("/"))})
        self.settings.save()
        self.organizer.watch()
        self.rebuild()

    def hide_fence(self, fid):
        if fid in core.CAT_NAME:
            self.settings["categories"][fid] = False
        else:
            self.settings["hidden_fences"].append(fid)
        self.settings.save()
        self.rebuild()

    def delete_fence(self, fid):
        name = self.organizer.fence_name(fid)
        if not self.confirm("Delete \"%s\"?" % name, "The fence is removed. Your files stay on your desktop and go back to their normal fences."):
            return
        self.settings["custom_fences"] = [c for c in self.settings["custom_fences"] if c["id"] != fid]
        self.settings["folder_fences"] = [c for c in self.settings["folder_fences"] if c["id"] != fid]
        self.settings["pins"] = {k: v for k, v in self.settings["pins"].items() if v != fid}
        self.settings["rules"] = [r for r in self.settings["rules"] if r.get("fence") != fid]
        self.settings["positions"].pop("fence:" + fid, None)
        self.settings["fence_opts"].pop(fid, None)
        self.settings.save()
        self.rebuild()

    def add_rule_dialog(self, fid):
        pat = self.ask_text("Add a rule", "Send files whose name matches (for example *.pdf or Screenshot*) to \"%s\""
                            % self.organizer.fence_name(fid), "")
        if pat:
            self.settings["rules"].append({"pattern": pat.strip(), "fence": fid})
            self.settings.save()
            self.refresh_fences()

    # ------------------------------------------------------------ dialogs
    def _dialog(self, title):
        d = Gtk.Dialog(title=title, modal=True)
        d.set_default_size(420, -1)
        d.set_keep_above(True)
        d.set_position(Gtk.WindowPosition.CENTER)
        d.set_icon_name("glassdesk")
        return d

    def ask_text(self, title, label, default=""):
        d = self._dialog(title)
        d.add_buttons("Cancel", Gtk.ResponseType.CANCEL, "OK", Gtk.ResponseType.OK)
        d.set_default_response(Gtk.ResponseType.OK)
        box = d.get_content_area()
        box.set_spacing(8)
        box.set_border_width(14)
        lb = Gtk.Label(label=label, xalign=0)
        lb.set_line_wrap(True)
        e = Gtk.Entry(text=default or "")
        e.set_activates_default(True)
        box.add(lb)
        box.add(e)
        d.show_all()
        d.present()
        r = d.run()
        val = e.get_text()
        d.destroy()
        return val if r == Gtk.ResponseType.OK else None

    def ask_multiline(self, title, default=""):
        d = self._dialog(title)
        d.set_default_size(460, 320)
        d.add_buttons("Cancel", Gtk.ResponseType.CANCEL, "Save", Gtk.ResponseType.OK)
        sw = Gtk.ScrolledWindow()
        sw.set_vexpand(True)
        tv = Gtk.TextView()
        tv.set_wrap_mode(Gtk.WrapMode.WORD_CHAR)
        tv.set_left_margin(8); tv.set_right_margin(8); tv.set_top_margin(8)
        tv.get_buffer().set_text(default or "")
        sw.add(tv)
        box = d.get_content_area()
        box.set_border_width(10)
        box.pack_start(sw, True, True, 0)
        d.show_all()
        d.present()
        r = d.run()
        b = tv.get_buffer()
        val = b.get_text(b.get_start_iter(), b.get_end_iter(), False)
        d.destroy()
        return val if r == Gtk.ResponseType.OK else None

    def choose_folder(self, title, start=""):
        d = Gtk.FileChooserDialog(title=title, action=Gtk.FileChooserAction.SELECT_FOLDER)
        d.add_buttons("Cancel", Gtk.ResponseType.CANCEL, "Choose", Gtk.ResponseType.OK)
        d.set_keep_above(True)
        if start and os.path.isdir(start):
            d.set_current_folder(start)
        r = d.run()
        path = d.get_filename()
        d.destroy()
        return path if r == Gtk.ResponseType.OK else None

    def choose_file(self, title, save=False, name=""):
        d = Gtk.FileChooserDialog(title=title, action=Gtk.FileChooserAction.SAVE if save else Gtk.FileChooserAction.OPEN)
        d.add_buttons("Cancel", Gtk.ResponseType.CANCEL, "Save" if save else "Open", Gtk.ResponseType.OK)
        d.set_keep_above(True)
        flt = Gtk.FileFilter()
        flt.set_name("GlassDesk layout")
        flt.add_pattern("*.glassdesk")
        d.add_filter(flt)
        if save:
            d.set_do_overwrite_confirmation(True)
            d.set_current_name(name)
        r = d.run()
        path = d.get_filename()
        d.destroy()
        return path if r == Gtk.ResponseType.OK else None

    def message(self, title, body):
        d = Gtk.MessageDialog(message_type=Gtk.MessageType.INFO, buttons=Gtk.ButtonsType.OK, text=title)
        d.format_secondary_text(body)
        d.set_keep_above(True)
        d.run()
        d.destroy()

    def confirm(self, title, body):
        d = Gtk.MessageDialog(message_type=Gtk.MessageType.QUESTION, buttons=Gtk.ButtonsType.YES_NO, text=title)
        d.format_secondary_text(body)
        d.set_keep_above(True)
        r = d.run()
        d.destroy()
        return r == Gtk.ResponseType.YES

    # ------------------------------------------------------------ windows
    def show_settings(self):
        from .settings_ui import SettingsWindow
        if self.settings_win is None:
            self.settings_win = SettingsWindow(self)
            self.settings_win.connect("destroy", lambda *a: setattr(self, "settings_win", None))
        self.settings_win.show_all()
        self.settings_win.present()

    def show_welcome(self):
        from .forms import WelcomeWindow
        WelcomeWindow(self).show_all()

    def show_organize(self):
        from .forms import OrganizeWindow
        OrganizeWindow(self).show_all()

    # ------------------------------------------------------------ themed look for menus and windows
    def apply_css(self):
        T = self.theme
        hx = core.rgb_hex
        acc = T.accent
        css = """
menu, .menu, .context-menu {{ background-color: {card}; color: {text}; border: 1px solid {line}; border-radius: 10px; padding: 4px; }}
menu menuitem {{ padding: 6px 12px; border-radius: 6px; color: {text}; }}
menu menuitem:hover {{ background-image: linear-gradient(to right, rgba({ar},{ag},{ab},0.55), rgba({br},{bg},{bb},0.35)); color: {text}; }}
menu menuitem:disabled {{ color: {sub}; }}
menu separator {{ background-color: {line}; margin: 4px 6px; }}
""".format(card=hx(T.ui_card), text=hx(T.ui_text), line=hx(T.ui_line), sub=hx(T.ui_sub),
           ar=int(acc[0] * 255), ag=int(acc[1] * 255), ab=int(acc[2] * 255),
           br=int(T.accent2[0] * 255), bg=int(T.accent2[1] * 255), bb=int(T.accent2[2] * 255))
        scr = Gdk.Screen.get_default()
        if scr is None:
            return
        if self._css is None:
            self._css = Gtk.CssProvider()
            Gtk.StyleContext.add_provider_for_screen(scr, self._css, Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION + 1)
        try:
            self._css.load_from_data(css.encode())
        except Exception as e:
            core.log("css: %s" % e)

    # ------------------------------------------------------------ "Match my wallpaper"
    def pick_wallpaper_accent(self):
        if not self.settings.get("wall_accent"):
            return
        try:
            import colorsys
            p = core.wallpaper_path()
            if not p or not os.path.exists(p):
                return
            pb = GdkPixbuf.Pixbuf.new_from_file_at_scale(p, 80, 80, False)
            px, n, rs = pb.get_pixels(), pb.get_n_channels(), pb.get_rowstride()
            score, sums = [0.0] * 24, [[0.0, 0.0, 0.0] for _ in range(24)]
            for yy in range(pb.get_height()):
                for xx in range(pb.get_width()):
                    o = yy * rs + xx * n
                    r, g, b = px[o] / 255, px[o + 1] / 255, px[o + 2] / 255
                    h, l, sat = colorsys.rgb_to_hls(r, g, b)
                    if sat < 0.25 or l < 0.15 or l > 0.9:
                        continue
                    k = int(h * 24) % 24
                    w = sat * (1 - abs(l - 0.55))
                    score[k] += w
                    sums[k][0] += r * w; sums[k][1] += g * w; sums[k][2] += b * w
            best = max(range(24), key=lambda i: score[i])
            if score[best] <= 0:
                return
            avg = [v / score[best] for v in sums[best]]
            h, l, sat = colorsys.rgb_to_hls(*avg)
            c = colorsys.hls_to_rgb(h, max(0.55, min(0.68, l)), max(0.6, sat))
            self.settings["accent"] = core.rgb_hex(c)
        except Exception as e:
            core.log("wallpaper accent: %s" % e)

    # ------------------------------------------------------------ a layout for each monitor setup
    def display_key(self):
        disp = Gdk.Display.get_default()
        parts = []
        for i in range(disp.get_n_monitors()):
            g = disp.get_monitor(i).get_geometry()
            parts.append("%d_%d_%d_%d" % (g.x, g.y, g.width, g.height))
        import hashlib
        return "d" + hashlib.md5("|".join(sorted(parts)).encode()).hexdigest()[:8]

    def switch_display_layout(self):
        S = self.settings
        if core.PREVIEW or not S.get("per_display"):
            return
        key = self.display_key()
        if key == S.get("display_key"):
            return
        if S.get("display_key"):
            S["layouts"][S["display_key"]] = json.loads(json.dumps(S["positions"]))
        saved = S["layouts"].get(key)
        if saved:
            S["positions"] = json.loads(json.dumps(saved))
        S["display_key"] = key
        S.save()

    # ------------------------------------------------------------ stacks of fences or widgets
    def panel_name(self, win):
        if isinstance(win, Fence):
            return self.organizer.fence_name(win.fid)
        return getattr(win, "NAME", "") or W.NAMES.get(win.SETTING, win.key)

    def window_by_key(self, key):
        for w in self.all_windows():
            if w.key == key:
                return w
        return None

    def group_of(self, key):
        for g in self.settings["groups"]:
            if key in g:
                return g
        return None

    def apply_groups(self):
        """Only the first wanted panel of each stack shows; the others wait in the same spot."""
        self.live_groups = {}
        for g in self.settings["groups"]:
            live = [k for k in g if self.window_by_key(k) is not None and self.window_by_key(k).wanted]
            if len(live) < 2:
                continue
            for k in live:
                self.live_groups[k] = live
            for k in live[1:]:
                self.window_by_key(k).wanted = False

    def group_members(self, key):
        live = self.live_groups.get(key)
        if not live:
            return []
        return [(k, self.panel_name(self.window_by_key(k))) for k in live]

    def group_size(self, key):
        live = self.live_groups.get(key)
        if not live or not key.startswith("w:"):
            return None
        w = h = 0
        for k in live:
            win = self.window_by_key(k)
            nw, nh = win.natural_size()
            w, h = max(w, nw), max(h, nh)
        return w, h

    def switch_to(self, win, key):
        live = self.live_groups.get(win.key)
        g = self.group_of(win.key)
        if not live or not g or key not in live:
            return
        d = live.index(key) - live.index(win.key)
        pos = self.settings["positions"].get(win.key)
        if pos:
            old = self.settings["positions"].get(key)
            if isinstance(win, Fence):
                self.settings["positions"][key] = list(pos)                      # same spot and size
            else:
                self.settings["positions"][key] = list(pos[:2]) + (list(old[2:]) if old else [])
        g.remove(key)
        g.insert(0, key)
        self.settings.save()
        for k in live:
            self.window_by_key(k).wanted = True
        self.apply_groups()
        self.place_all()
        self.show_hide()
        nxt = self.window_by_key(key)
        if nxt:
            nxt.play_enter(d)

    def join_group(self, win, target):
        mine = self.group_of(win.key)
        if mine:
            mine.remove(win.key)
            if len(mine) < 2:
                self.settings["groups"].remove(mine)
        g = self.group_of(target.key)
        if g is None:
            g = [target.key]
            self.settings["groups"].append(g)
        g.insert(0, win.key)                  # the one you dropped shows, in the other one's spot
        tp = self.settings["positions"].get(target.key) or list(target.panel_origin())
        if isinstance(win, Fence):
            self.settings["positions"][win.key] = list(tp)
        else:
            self.settings["positions"][win.key] = list(tp[:2])
        self.settings.save()
        self.rebuild()

    def leave_group(self, win):
        g = self.group_of(win.key)
        if not g:
            return
        g.remove(win.key)
        if len(g) < 2:
            self.settings["groups"].remove(g)
        self.settings["positions"].pop(win.key, None)
        self.settings.save()
        self.rebuild()

    def stack_menu(self, win):
        same = [w for w in self.all_windows() if w is not win and w.get_visible() and isinstance(w, Fence) == isinstance(win, Fence)]
        items = []
        if same:
            items.append(("Stack with", [(self.panel_name(o), (lambda o=o: self.join_group(win, o))) for o in same]))
        if self.live_groups.get(win.key):
            items.append(("Take out of stack", lambda: self.leave_group(win)))
        return items

    def _target_at(self, win, x, y):
        for o in self.all_windows():
            if o is win or not o.get_visible() or isinstance(o, Fence) != isinstance(win, Fence):
                continue
            ox, oy = o.panel_origin()
            pw, ph = o.panel_rect()[2:]
            if ox <= x < ox + pw and oy <= y < oy + ph:
                return o
        return None

    def drag_over(self, win, x, y):
        t = self._target_at(win, x, y)
        if t is self._drop_target:
            return
        if self._drop_target is not None:
            self._drop_target.drop_hint = False
            self._drop_target.queue_draw()
        self._drop_target = t
        if t is not None:
            t.drop_hint = True
            t.queue_draw()

    def drag_ended(self, win, x, y):
        t = self._drop_target
        self._drop_target = None
        if t is None:
            return
        t.drop_hint = False
        t.queue_draw()
        g = self.group_of(win.key)
        if g and t.key in g:
            return
        popup(t, [("Stack “%s” with “%s”?" % (self.panel_name(win), self.panel_name(t)), None), None,
                  ("Stack them", lambda: self.join_group(win, t)),
                  ("No, just put it here", lambda: None)])

    def show_search(self):
        from .forms import Launcher
        if self.search_win is not None:
            self.search_win.present()
            return
        self.search_win = Launcher(self)
        self.search_win.connect("destroy", lambda *a: setattr(self, "search_win", None))
        self.search_win.open()

    # ------------------------------------------------------------ preview (screenshots for docs)
    def write_preview(self, out):
        os.makedirs(out, exist_ok=True)
        extra = os.environ.get("GLASSDESK_PREVIEW_FORMS")
        if extra:
            self._shot_forms(out)
        lines = []
        for w in self.all_windows():
            if not w.wanted or not w.get_visible():
                continue
            a = w.get_allocation()
            surf = cairo.ImageSurface(cairo.FORMAT_ARGB32, a.width, a.height)
            cr = cairo.Context(surf)
            w._on_draw(w, cr)
            name = w.key.replace(":", ".")
            surf.write_to_png(os.path.join(out, name + ".png"))
            x, y = w.get_position()
            lines.append("%s %d %d %d %d" % (name, x, y, a.width, a.height))
        with open(os.path.join(out, "layout.txt"), "w") as f:
            f.write("\n".join(lines) + "\n")
        if self.settings_win is None and os.environ.get("GLASSDESK_PREVIEW_SETTINGS"):
            self.show_settings()
            page = os.environ.get("GLASSDESK_PREVIEW_PAGE")
            if page:
                self.settings_win.stack.set_visible_child_name(page)
                self.settings_win.resize(980, 1000)
            GLib.timeout_add(900, lambda: self._shot_settings(out))
            return False
        self.quit_app()
        return False

    def _shot_forms(self, out):
        from .forms import Launcher, WelcomeWindow, OrganizeWindow
        l = Launcher(self)
        l.items = sorted(self.organizer.all_items(), key=lambda it: it.name.lower())
        l.set_query("")
        l.render_png(os.path.join(out, "launcher.png"), (610, 230))
        l.set_query("12*(3+4)")
        l.render_png(os.path.join(out, "launcher-calc.png"), (610, 230))
        l.set_query("ocea")
        l.render_png(os.path.join(out, "launcher-theme.png"), (610, 230))
        WelcomeWindow(self).render_png(os.path.join(out, "welcome.png"), (640, 290))
        OrganizeWindow(self).render_png(os.path.join(out, "organize.png"), (510, 220))

    def _shot_settings(self, out):
        w = self.settings_win
        win = w.get_window()
        a = w.get_allocation()
        pb = Gdk.pixbuf_get_from_window(win, 0, 0, a.width, a.height)
        if pb:
            pb.savev(os.path.join(out, "settings.png"), "png", [], [])
        self.quit_app()
        return False


def main():
    os.environ.setdefault("NO_AT_BRIDGE", "1")
    # GlassDesk places windows itself, which Wayland doesn't allow - run through XWayland.
    if os.environ.get("XDG_SESSION_TYPE") == "wayland" and not os.environ.get("GLASSDESK_NATIVE_WAYLAND"):
        os.environ["GDK_BACKEND"] = "x11"
    if "--preview" in sys.argv:
        os.environ["GLASSDESK_PREVIEW"] = "1"
        core.PREVIEW = True
        if "light" in sys.argv:
            os.environ["GLASSDESK_LIGHT"] = "1"
    if "--version" in sys.argv:
        print("GlassDesk", core.VERSION)
        return 0
    if "--help" in sys.argv or "-h" in sys.argv:
        print("GlassDesk %s - glass desktop organizer and widgets\n\n"
              "  glassdesk             start (or open Settings if already running)\n"
              "  glassdesk --search    find something on your desktop\n"
              "  glassdesk --toggle    show or hide all panels\n"
              "  glassdesk --settings  open Settings\n"
              "  glassdesk --quit      quit GlassDesk" % core.VERSION)
        return 0
    try:
        app = GlassDeskApp()
        return app.run(sys.argv)
    except Exception as e:
        core.log("crash: %r" % e)
        raise
