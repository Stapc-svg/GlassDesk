"""GlassDesk for Linux - settings, theme, desktop helpers, small web helpers."""
import json, os, re, shutil, subprocess, threading, time, urllib.request, urllib.parse, fnmatch, configparser

import gi
gi.require_version("Gtk", "3.0")
gi.require_version("Gdk", "3.0")
gi.require_version("GdkPixbuf", "2.0")
gi.require_version("PangoCairo", "1.0")
from gi.repository import GLib, Gio, Gtk, Gdk, GdkPixbuf

VERSION = "1.7.2"
REPO = "Stapc-svg/GlassDesk"
APP_ID = "app.glassdesk.GlassDesk"

CONFIG_DIR = os.path.join(GLib.get_user_config_dir(), "glassdesk")
SETTINGS_FILE = os.path.join(CONFIG_DIR, "settings.json")
BACKUP_DIR = os.path.join(CONFIG_DIR, "backups")
CACHE_DIR = os.path.join(GLib.get_user_cache_dir(), "glassdesk")
LOG_FILE = os.path.join(CONFIG_DIR, "error.log")
PREVIEW = bool(os.environ.get("GLASSDESK_PREVIEW"))
PREVIEW_NO_BACKDROP = False


def desktop_dir():
    d = os.environ.get("GLASSDESK_DESKTOP") or GLib.get_user_special_dir(GLib.UserDirectory.DIRECTORY_DESKTOP)
    if not d or os.path.realpath(d) == os.path.realpath(os.path.expanduser("~")):
        d = os.path.expanduser("~/Desktop")
    os.makedirs(d, exist_ok=True)
    return d


def log(msg):
    try:
        os.makedirs(CONFIG_DIR, exist_ok=True)
        with open(LOG_FILE, "a") as f:
            f.write(time.strftime("%Y-%m-%d %H:%M:%S ") + str(msg) + "\n")
    except Exception:
        pass


# ------------------------------------------------------------------ settings
DEFAULTS = {
    "theme": "auto",                # dark / light / auto
    "accent": "#7C9CFF",
    "glass_style": "frosted",       # frosted / blur / clear / transparent
    "opacity": 55,                  # tint strength, %
    "radius": 16,
    "ui_scale": 100,
    "start_at_login": True,
    "lock_positions": False,
    "single_click": False,
    "hide_desktop_icons": False,
    "show_clock": True, "show_system": True, "show_calendar": True, "show_notes": True,
    "show_todo": True, "show_weather": True, "show_photos": False, "show_countdown": False,
    "show_quote": True, "show_github": True, "show_network": True, "show_focus": True,
    "note_text": "Double-click to write a note.",
    "todo": [["Try GlassDesk on Linux", False], ["Drag a file into a fence", False]],
    "weather_city": "", "weather_lat": None, "weather_lon": None, "weather_units": "c",
    "photo_folder": "", "photo_interval": 60,
    "countdown_title": "New Year", "countdown_date": "",
    "github_user": "",
    "focus_minutes": 25, "break_minutes": 5,
    "categories": {"apps": True, "folders": True, "docs": True, "images": True, "media": True, "archives": True, "other": True},
    "custom_fences": [],            # [{"id","name"}]
    "folder_fences": [],            # [{"id","path","name"}]
    "pins": {},                     # file name -> fence id
    "rules": [],                    # [{"pattern","fence"}]
    "fence_opts": {},               # fence id -> {"sort","color","icon","collapsed","peek","name"}
    "positions": {},                # window key -> [x,y,w,h]
    "hidden_fences": [],
    "wallpaper_folder": "", "wallpaper_minutes": 0,
    "last_update_check": 0,
}


class Settings(dict):
    def __init__(self):
        super().__init__(json.loads(json.dumps(DEFAULTS)))
        self._save_id = 0
        self.listeners = []
        self.load()

    def load(self):
        try:
            with open(SETTINGS_FILE) as f:
                data = json.load(f)
            for k, v in data.items():
                if k in DEFAULTS and isinstance(DEFAULTS[k], dict) and isinstance(v, dict):
                    self[k].update(v)
                else:
                    self[k] = v
        except FileNotFoundError:
            pass
        except Exception as e:
            log("settings: %s" % e)

    def save_now(self):
        self._save_id = 0
        try:
            os.makedirs(CONFIG_DIR, exist_ok=True)
            tmp = SETTINGS_FILE + ".tmp"
            with open(tmp, "w") as f:
                json.dump(self, f, indent=1)
            os.replace(tmp, SETTINGS_FILE)
        except Exception as e:
            log("save: %s" % e)
        return False

    def save(self):
        """Saves a moment later, so a drag does not write the file 60 times a second."""
        if not self._save_id:
            self._save_id = GLib.timeout_add(600, self.save_now)

    def changed(self):
        self.save()
        for f in list(self.listeners):
            try:
                f()
            except Exception as e:
                log("listener: %s" % e)


# ------------------------------------------------------------------ theme
def hex_rgb(h):
    h = h.lstrip("#")
    if len(h) == 3:
        h = "".join(c * 2 for c in h)
    try:
        return tuple(int(h[i:i + 2], 16) / 255 for i in (0, 2, 4))
    except Exception:
        return (0.49, 0.61, 1.0)


def system_prefers_dark():
    try:
        s = Gio.Settings.new("org.gnome.desktop.interface")
        keys = s.list_keys()
        if "color-scheme" in keys and s.get_string("color-scheme") == "prefer-dark":
            return True
        if "gtk-theme" in keys and "dark" in s.get_string("gtk-theme").lower():
            return True
    except Exception:
        pass
    try:
        kde = os.path.expanduser("~/.config/kdeglobals")
        if os.path.exists(kde):
            txt = open(kde).read().lower()
            if "colorscheme=" in txt and "dark" in txt.split("colorscheme=", 1)[1].split("\n", 1)[0]:
                return True
    except Exception:
        pass
    st = Gtk.Settings.get_default()
    if st is not None and st.get_property("gtk-application-prefer-dark-theme"):
        return True
    return True     # GlassDesk looks best dark; default to it


class Theme:
    def __init__(self, s):
        mode = s["theme"]
        self.dark = mode == "dark" or (mode == "auto" and system_prefers_dark())
        self.scale = max(0.6, min(2.0, s["ui_scale"] / 100.0))
        self.accent = hex_rgb(s["accent"])
        self.style = s["glass_style"]
        a = s["opacity"] / 100.0
        if self.style == "transparent":
            a *= 0.3
        self.radius = s["radius"]
        if self.dark:
            self.tint = (18 / 255, 20 / 255, 28 / 255, a)
            self.text = (0.957, 0.965, 0.988, 0.95)
            self.sub = (0.86, 0.886, 0.94, 0.62)
            self.border = (1, 1, 1, 0.22)
            self.hover = (1, 1, 1, 0.13)
            self.track = (1, 1, 1, 0.16)
        else:
            self.tint = (248 / 255, 249 / 255, 252 / 255, a)
            self.text = (0.07, 0.08, 0.11, 0.95)
            self.sub = (0.16, 0.18, 0.24, 0.66)
            self.border = (1, 1, 1, 0.3)
            self.hover = (0, 0, 0, 0.1)
            self.track = (0, 0, 0, 0.12)
        self.font = "Inter, Cantarell, Ubuntu, Noto Sans, DejaVu Sans, sans-serif"

    def s(self, v):
        return v * self.scale


THEMES = {
    "Ocean":    {"accent": "#38BDF8", "theme": "dark",  "opacity": 50, "glass_style": "frosted"},
    "Sunset":   {"accent": "#FB923C", "theme": "dark",  "opacity": 45, "glass_style": "frosted"},
    "Forest":   {"accent": "#4ADE80", "theme": "dark",  "opacity": 55, "glass_style": "blur"},
    "Midnight": {"accent": "#A78BFA", "theme": "dark",  "opacity": 75, "glass_style": "frosted"},
    "Frost":    {"accent": "#2563EB", "theme": "light", "opacity": 45, "glass_style": "frosted"},
    "Rose":     {"accent": "#F472B6", "theme": "dark",  "opacity": 50, "glass_style": "clear"},
}


# ------------------------------------------------------------------ categories
CATEGORIES = [
    ("apps", "Apps & Shortcuts", ".desktop .appimage .sh .run .exe .deb .rpm .flatpakref .bin .lnk .url"),
    ("folders", "Folders", ""),
    ("docs", "Documents", ".pdf .doc .docx .txt .rtf .odt .md .xls .xlsx .csv .ppt .pptx .odp .ods .epub .html .htm .json .tex"),
    ("images", "Images", ".png .jpg .jpeg .gif .bmp .webp .svg .ico .heic .tif .tiff .psd .avif .xcf .kra"),
    ("media", "Music & Video", ".mp3 .wav .flac .m4a .ogg .opus .aac .mp4 .mkv .avi .mov .wmv .webm"),
    ("archives", "Archives", ".zip .rar .7z .tar .gz .tgz .iso .xz .bz2 .zst"),
    ("other", "Other files", ""),
]
CAT_NAME = {k: n for k, n, _ in CATEGORIES}
_EXT = {}
for _k, _n, _e in CATEGORIES:
    for _x in _e.split():
        _EXT[_x] = _k


def category_of(path):
    if os.path.isdir(path):
        return "folders"
    name = path.lower()
    for ext in (".tar.gz", ".tar.xz", ".tar.bz2"):
        if name.endswith(ext):
            return "archives"
    return _EXT.get(os.path.splitext(name)[1], "other")


def rule_target(name, rules):
    for r in rules:
        pat = (r.get("pattern") or "").strip()
        if pat and fnmatch.fnmatch(name.lower(), pat.lower()):
            return r.get("fence")
    return None


# ------------------------------------------------------------------ wallpaper
def _uri_to_path(u):
    u = (u or "").strip().strip("'\"")
    if u.startswith("file://"):
        return urllib.parse.unquote(u[7:])
    return u


def wallpaper_path():
    if os.environ.get("GLASSDESK_PREVIEW_WALL"):
        return os.environ["GLASSDESK_PREVIEW_WALL"]
    tries = [("org.gnome.desktop.background", "picture-uri-dark"), ("org.gnome.desktop.background", "picture-uri"),
             ("org.cinnamon.desktop.background", "picture-uri"), ("org.mate.background", "picture-filename"),
             ("org.gnome.desktop.screensaver", "picture-uri")]
    src = Gio.SettingsSchemaSource.get_default()
    dark = system_prefers_dark()
    for schema, key in tries:
        try:
            if src is None or src.lookup(schema, True) is None:
                continue
            s = Gio.Settings.new(schema)
            if key not in s.list_keys():
                continue
            if key.endswith("-dark") and not dark:
                continue
            p = _uri_to_path(s.get_string(key))
            if p and os.path.isfile(p):
                return p
        except Exception:
            pass
    # KDE Plasma
    try:
        cfg = os.path.expanduser("~/.config/plasma-org.kde.plasma.desktop-appletsrc")
        if os.path.exists(cfg):
            for line in open(cfg, errors="ignore"):
                if line.startswith("Image="):
                    p = _uri_to_path(line.split("=", 1)[1])
                    if os.path.isdir(p):           # a wallpaper package
                        for sub in ("contents/images", "contents/images_dark"):
                            d = os.path.join(p, sub)
                            if os.path.isdir(d):
                                files = sorted(os.listdir(d))
                                if files:
                                    return os.path.join(d, files[-1])
                    if os.path.isfile(p):
                        return p
    except Exception:
        pass
    # XFCE
    try:
        out = subprocess.run(["xfconf-query", "-c", "xfce4-desktop", "-l"], capture_output=True, text=True, timeout=2).stdout
        for prop in out.split():
            if prop.endswith("last-image"):
                p = subprocess.run(["xfconf-query", "-c", "xfce4-desktop", "-p", prop], capture_output=True, text=True, timeout=2).stdout.strip()
                if os.path.isfile(p):
                    return p
    except Exception:
        pass
    # feh / nitrogen users
    try:
        fb = os.path.expanduser("~/.fehbg")
        if os.path.exists(fb):
            m = re.findall(r"'([^']+)'", open(fb).read())
            if m and os.path.isfile(m[-1]):
                return m[-1]
    except Exception:
        pass
    return None


def set_wallpaper(path):
    uri = "file://" + urllib.parse.quote(path)
    ok = False
    for schema, keys in (("org.gnome.desktop.background", ("picture-uri", "picture-uri-dark")),
                         ("org.cinnamon.desktop.background", ("picture-uri",)), ("org.mate.background", ("picture-filename",))):
        try:
            src = Gio.SettingsSchemaSource.get_default()
            if src is None or src.lookup(schema, True) is None:
                continue
            s = Gio.Settings.new(schema)
            for k in keys:
                if k in s.list_keys():
                    s.set_string(k, path if k == "picture-filename" else uri)
                    ok = True
        except Exception:
            pass
    if not ok and shutil.which("plasma-apply-wallpaperimage"):
        subprocess.Popen(["plasma-apply-wallpaperimage", path])
        ok = True
    if not ok and shutil.which("xfconf-query"):
        try:
            out = subprocess.run(["xfconf-query", "-c", "xfce4-desktop", "-l"], capture_output=True, text=True, timeout=2).stdout
            for prop in out.split():
                if prop.endswith("last-image"):
                    subprocess.run(["xfconf-query", "-c", "xfce4-desktop", "-p", prop, "-s", path], timeout=2)
                    ok = True
        except Exception:
            pass
    if not ok and shutil.which("feh"):
        subprocess.Popen(["feh", "--bg-fill", path])
        ok = True
    return ok


# ------------------------------------------------------------------ the normal desktop icons
def set_desktop_icons_hidden(hide):
    """Best effort: GNOME 'Desktop Icons NG', Nemo (Cinnamon), Caja (MATE), XFCE."""
    def gs(schema, key, val):
        try:
            src = Gio.SettingsSchemaSource.get_default()
            if src and src.lookup(schema, True):
                s = Gio.Settings.new(schema)
                if key in s.list_keys():
                    s.set_boolean(key, val)
                    return True
        except Exception:
            pass
        return False
    done = gs("org.nemo.desktop", "show-desktop-icons", not hide)
    done |= gs("org.mate.background", "show-desktop-icons", not hide)
    if shutil.which("gnome-extensions"):
        for ext in ("ding@rastersoft.com", "desktopicons-neo@darkdemon", "gtk4-ding@smedius.gitlab.com"):
            try:
                r = subprocess.run(["gnome-extensions", "disable" if hide else "enable", ext], capture_output=True, timeout=4)
                done |= r.returncode == 0
            except Exception:
                pass
    if shutil.which("xfconf-query"):
        try:
            subprocess.run(["xfconf-query", "-c", "xfce4-desktop", "-p", "/desktop-icons/style", "-n", "-t", "int", "-s", "0" if hide else "2"], timeout=3)
            done = True
        except Exception:
            pass
    return done


# ------------------------------------------------------------------ start at login
AUTOSTART = os.path.join(GLib.get_user_config_dir(), "autostart", "glassdesk.desktop")


def launcher_command():
    for c in (os.environ.get("APPIMAGE"), shutil.which("glassdesk")):
        if c:
            return c
    return "python3 -m glassdesk"


def set_autostart(on):
    try:
        if on:
            os.makedirs(os.path.dirname(AUTOSTART), exist_ok=True)
            with open(AUTOSTART, "w") as f:
                f.write("[Desktop Entry]\nType=Application\nName=GlassDesk\nComment=Glass desktop organizer and widgets\n"
                        "Exec=%s --startup\nIcon=glassdesk\nX-GNOME-Autostart-enabled=true\nX-GNOME-Autostart-Delay=3\nNoDisplay=false\n" % launcher_command())
        elif os.path.exists(AUTOSTART):
            os.remove(AUTOSTART)
    except Exception as e:
        log("autostart: %s" % e)


# ------------------------------------------------------------------ web
def http_get(url, timeout=15):
    req = urllib.request.Request(url, headers={"User-Agent": "GlassDesk/%s (Linux)" % VERSION, "Accept": "application/json"})
    with urllib.request.urlopen(req, timeout=timeout) as r:
        return r.read().decode("utf-8", "replace")


def http_json(url, timeout=15):
    return json.loads(http_get(url, timeout))


def in_background(work, done):
    """Runs work() on a thread, then done(result, error) back on the GTK thread."""
    def run():
        try:
            res, err = work(), None
        except Exception as e:
            res, err = None, e
        GLib.idle_add(lambda: (done(res, err), False)[1])
    threading.Thread(target=run, daemon=True).start()


def open_path(path):
    try:
        if path.endswith(".desktop"):
            info = Gio.DesktopAppInfo.new_from_filename(path)
            if info:
                info.launch([], None)
                return
        if os.access(path, os.X_OK) and os.path.isfile(path) and path.lower().endswith((".appimage", ".run", ".bin")):
            subprocess.Popen([path], cwd=os.path.dirname(path), start_new_session=True)
            return
        Gio.AppInfo.launch_default_for_uri(Gio.File.new_for_path(path).get_uri(), None)
    except Exception as e:
        try:
            subprocess.Popen(["xdg-open", path], start_new_session=True)
        except Exception:
            log("open: %s" % e)


def show_in_folder(path):
    uri = Gio.File.new_for_path(path).get_uri()
    try:
        bus = Gio.bus_get_sync(Gio.BusType.SESSION, None)
        bus.call_sync("org.freedesktop.FileManager1", "/org/freedesktop/FileManager1", "org.freedesktop.FileManager1",
                      "ShowItems", GLib.Variant("(ass)", ([uri], "")), None, Gio.DBusCallFlags.NONE, 3000, None)
    except Exception:
        open_path(os.path.dirname(path))


def notify(title, body):
    try:
        app = Gio.Application.get_default()
        if app is not None and app.get_is_registered():
            n = Gio.Notification.new(title)
            n.set_body(body)
            app.send_notification(None, n)
            return
    except Exception:
        pass
    if shutil.which("notify-send"):
        subprocess.Popen(["notify-send", "-a", "GlassDesk", "-i", "glassdesk", title, body])


def beep():
    for snd in ("/usr/share/sounds/freedesktop/stereo/complete.oga", "/usr/share/sounds/freedesktop/stereo/bell.oga"):
        if os.path.exists(snd) and shutil.which("paplay"):
            subprocess.Popen(["paplay", snd])
            return
    d = Gdk.Display.get_default()
    if d:
        d.beep()


QUOTES = [
    ("The secret of getting ahead is getting started.", "Mark Twain"), ("Well begun is half done.", "Aristotle"),
    ("It always seems impossible until it's done.", "Nelson Mandela"), ("Simplicity is the ultimate sophistication.", "Leonardo da Vinci"),
    ("Do what you can, with what you have, where you are.", "Theodore Roosevelt"), ("Quality is not an act, it is a habit.", "Aristotle"),
    ("The best way out is always through.", "Robert Frost"), ("Little by little, one travels far.", "J. R. R. Tolkien"),
    ("Happiness depends upon ourselves.", "Aristotle"), ("Energy and persistence conquer all things.", "Benjamin Franklin"),
    ("Everything you can imagine is real.", "Pablo Picasso"), ("Life is really simple, but we insist on making it complicated.", "Confucius"),
    ("Act as if what you do makes a difference. It does.", "William James"), ("In the middle of difficulty lies opportunity.", "Albert Einstein"),
    ("The journey of a thousand miles begins with one step.", "Lao Tzu"), ("Be yourself; everyone else is already taken.", "Oscar Wilde"),
    ("What we think, we become.", "Buddha"), ("Tell me and I forget. Teach me and I remember. Involve me and I learn.", "Benjamin Franklin"),
    ("You miss 100% of the shots you don't take.", "Wayne Gretzky"), ("Creativity is intelligence having fun.", "Albert Einstein"),
    ("Dream big and dare to fail.", "Norman Vaughan"), ("Order is the shape upon which beauty depends.", "Pearl S. Buck"),
    ("Done is better than perfect.", "Sheryl Sandberg"), ("Small deeds done are better than great deeds planned.", "Peter Marshall"),
]
