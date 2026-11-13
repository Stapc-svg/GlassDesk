"""GlassDesk for Linux - settings, theme, desktop helpers, small web helpers."""
import json, os, re, shutil, subprocess, threading, time, urllib.request, urllib.parse, fnmatch, configparser

import gi
gi.require_version("Gtk", "3.0")
gi.require_version("Gdk", "3.0")
gi.require_version("GdkPixbuf", "2.0")
gi.require_version("PangoCairo", "1.0")
from gi.repository import GLib, Gio, Gtk, Gdk, GdkPixbuf

VERSION = "1.8.6"
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
    # 1.8
    "look": "glassdesk",            # theme id (see LOOKS)
    "show_music": True, "show_day": True,
    "last_song": "", "last_artist": "",
    "stacks": True,                 # 4+ files of one type fold into a stack
    "per_display": True,            # a layout for each monitor setup
    "display_key": "", "layouts": {},
    "groups": [],                   # stacked panels: [[key, key, ...]], the first one shows
    "wall_accent": False,           # accent color from the wallpaper
    "zoom": {},                     # widget key -> size in %
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
            if "look" not in data:
                self["look"] = "classic"      # people updating keep the look they had
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


def _c(h):
    return hex_rgb("%06X" % h)


def _look(id, name, desc, accent, dtop, dbot, ltop, lbot, glow2, mode, tint, radius, glass="frosted", group="", **flags):
    d = dict(id=id, name=name, desc=desc, accent="#%06X" % accent, dark=(_c(dtop), _c(dbot)), light=(_c(ltop), _c(lbot)),
             glow=glow2 >= 0, glow2=_c(glow2) if glow2 > 0 else None, mode=mode, tint=tint, radius=radius, glass=glass,
             group=group, solid=False, flat=False, neon=False)
    d.update(flags)
    return d


# the same 26 looks as GlassDesk for Windows
LOOKS = [
    _look("glassdesk", "GlassDesk", "Frosted navy glass with a soft glow", 0x7C9CFF, 0x1A2140, 0x0C1022, 0xFFFFFF, 0xECF0FA, 0xB06CFF, "dark", 45, 18),
    _look("clean", "Clean", "Bright, flat and rounded", 0x007AFF, 0x1C1C1E, 0x1C1C1E, 0xFFFFFF, 0xFFFFFF, -1, "auto", 95, 22, solid=True, flat=True),
    _look("midnight", "Midnight", "Deep violet glass", 0xA78BFA, 0x241446, 0x0E0820, 0xF4EEFF, 0xE4DAFA, 0xFF6FA8, "dark", 50, 16),
    _look("ocean", "Ocean", "Teal water and cyan light", 0x4FC3F7, 0x0A3450, 0x061628, 0xE8F8FF, 0xD2ECF8, 0x2DD4BF, "dark", 40, 18),
    _look("sunset", "Sunset", "Warm coral evening glow", 0xFF8A65, 0x5C2632, 0x28101E, 0xFFF0E8, 0xFFE0D6, 0xFFC46B, "dark", 38, 20),
    _look("forest", "Forest", "Green glass, fresh and calm", 0x5BD68A, 0x123C2C, 0x081A14, 0xECFAF0, 0xD6F0DE, 0xC6F68D, "dark", 42, 14),
    _look("rose", "Rose", "Soft pink and lilac", 0xF48FB1, 0x501E3A, 0x220C1C, 0xFFECF4, 0xFADAE8, 0xC77DFF, "dark", 38, 22),
    _look("aurora", "Aurora", "Northern lights, teal to purple", 0x2DD4BF, 0x102C48, 0x2C1242, 0xE6FAFF, 0xF0E4FF, 0xC77DFF, "dark", 40, 18),
    _look("frost", "Frost", "Icy white glass", 0x3B82F6, 0x1E2838, 0x121824, 0xF6FAFF, 0xE2ECFA, 0x7DD3FC, "light", 45, 22),
    _look("ember", "Ember", "Glowing coals, red and amber", 0xFF6B4A, 0x3A1410, 0x160806, 0xFFF1EC, 0xFFE2D8, 0xFFB347, "dark", 48, 16),
    _look("lavender", "Lavender", "Light lilac glass", 0x8B5CF6, 0x2A2140, 0x16122A, 0xF7F2FF, 0xEDE4FF, 0xF0ABFC, "light", 42, 20),
    _look("mint", "Mint", "Cool, fresh and light", 0x10B981, 0x123A34, 0x081C18, 0xF0FFF9, 0xDDF7EE, 0x67E8F9, "light", 42, 20),
    _look("neon", "Neon", "Black glass with a glowing edge", 0xFF2BD6, 0x0A0814, 0x04040A, 0xFAF5FF, 0xF0E8FA, 0x00E5FF, "dark", 70, 14, neon=True),
    _look("graphite", "Graphite", "Quiet gray, no color", 0x98989D, 0x2C2C30, 0x1C1C20, 0xF0F0F2, 0xE2E2E6, -1, "dark", 50, 12),
    _look("paper", "Paper", "Warm, solid and easy to read", 0xD97706, 0x24211C, 0x24211C, 0xFAF7F0, 0xFAF7F0, -1, "light", 95, 10, solid=True, flat=True),
    _look("classic", "Classic", "The original GlassDesk 1.7 look", 0x7C9CFF, 0x12141C, 0x12141C, 0xF8F9FC, 0xF8F9FC, -1, "auto", 30, 16),
    # Windows themes
    _look("mica", "Mica material", "Calm and mostly solid", 0x60CDFF, 0x202020, 0x1C1C1C, 0xF3F3F3, 0xEEEEEE, -1, "auto", 82, 8, group="windows"),
    _look("acrylic", "Acrylic material", "Fully blurred, see-through", 0x60CDFF, 0x2C2C2C, 0x202020, 0xFCFCFC, 0xF3F3F3, -1, "auto", 30, 8, glass="acrylic", group="windows"),
    _look("mica-alt", "Mica Alt", "Deeper, darker Mica", 0x60CDFF, 0x0A0A0A, 0x141414, 0xE8E8E8, 0xDADADA, -1, "dark", 88, 8, group="windows"),
    _look("win-light", "Windows Light", "Default light", 0x0078D4, 0x2B2B2B, 0x202020, 0xFFFFFF, 0xF3F3F3, -1, "light", 78, 8, group="windows"),
    _look("win-dark", "Windows Dark", "Default dark", 0x4CC2FF, 0x2B2B2B, 0x1F1F1F, 0xF9F9F9, 0xEFEFEF, -1, "dark", 78, 8, group="windows"),
    _look("bloom", "Bloom", "Blue Windows 11 flower", 0x0078D4, 0x0B2A5C, 0x061630, 0xEAF3FF, 0xD6E6FA, -1, "dark", 50, 8, glass="blur", group="windows"),
    _look("glow", "Glow", "Purple light, soft acrylic", 0xC48BFF, 0x2A1846, 0x140A26, 0xF6EEFF, 0xE8DAFA, -1, "dark", 48, 8, glass="blur", group="windows"),
    _look("sunrise", "Sunrise", "Warm orange morning", 0xFF9A3C, 0x4A2410, 0x200E06, 0xFFF3E8, 0xFFE4CC, -1, "light", 48, 8, glass="blur", group="windows"),
    _look("flow", "Flow", "Fresh blue-green waves", 0x00B7C3, 0x08343C, 0x041A1E, 0xE6FBFC, 0xCFF2F4, -1, "dark", 48, 8, glass="blur", group="windows"),
    _look("captured-motion", "Captured Motion", "Dark with deep pink motion", 0xE3008C, 0x2A0A20, 0x10040C, 0xFFEAF6, 0xF8D6EA, -1, "dark", 62, 8, group="windows"),
]
LOOK = {l["id"]: l for l in LOOKS}


def apply_look(s, look):
    """Picking a theme sets its colors, glass, tint, corners and mode. All can still be changed after."""
    s["look"] = look["id"]
    s["accent"] = look["accent"]
    s["glass_style"] = "blur" if look["glass"] == "acrylic" else look["glass"]
    s["opacity"] = look["tint"]
    s["radius"] = look["radius"]
    s["theme"] = look["mode"]


def mix(a, b, t):
    return tuple(a[i] + (b[i] - a[i]) * t for i in range(3))


class Theme:
    def __init__(self, s, zoom=1.0):
        mode = s["theme"]
        self.dark = mode == "dark" or (mode == "auto" and system_prefers_dark())
        self.base_scale = max(0.6, min(2.0, s["ui_scale"] / 100.0))
        self.scale = self.base_scale * zoom
        self.accent = hex_rgb(s["accent"])
        self.accent2 = mix(self.accent, (0.69, 0.42, 1.0), 0.45)
        look = LOOK.get(s.get("look") or "classic") or LOOK["classic"]
        self.look = look["id"]
        self.solid, self.flat, self.neon = look["solid"], look["flat"], look["neon"]
        self.style = s["glass_style"]
        self.acrylic = look["id"] == "acrylic"
        a = s["opacity"] / 100.0
        if self.style == "transparent":
            a *= 0.3
        if self.solid:
            a = 1.0
        a2 = a if look["id"] == "classic" or self.solid else min(1.0, a + 0.16)
        top, bot = look["dark"] if self.dark else look["light"]
        self.tint_top, self.tint_bottom = top + (a2,), bot + (a2,)
        self.glow = (self.accent + (0.36 if self.dark else 0.21,)) if look["glow"] else None
        self.glow2 = (look["glow2"] + (0.2 if self.dark else 0.14,)) if look["glow"] and look["glow2"] else None
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
        if self.solid:
            self.sub = (0.6, 0.6, 0.62, 1) if self.dark else (0.43, 0.43, 0.45, 1)
        # colors for the app's own windows (Settings, search, menus): follow the theme
        if self.dark:
            self.ui_bg = mix(bot, top, 0.3); self.ui_side = mix(bot, (0, 0, 0), 0.15)
            self.ui_card = mix(top, (1, 1, 1), 0.035); self.ui_line = mix(top, (1, 1, 1), 0.11)
            self.ui_text = (0.94, 0.95, 0.98); self.ui_sub = mix((0.63, 0.65, 0.73), top, 0.15)
        else:
            self.ui_bg = mix(bot, top, 0.4); self.ui_side = mix(bot, (0, 0, 0), 0.03)
            self.ui_card = mix(top, (1, 1, 1), 0.65); self.ui_line = mix(bot, (0, 0, 0), 0.09)
            self.ui_text = (0.08, 0.09, 0.13); self.ui_sub = (0.39, 0.42, 0.5)
        self.font = "Inter, Cantarell, Ubuntu, Noto Sans, DejaVu Sans, sans-serif"

    def s(self, v):
        return v * self.scale

    def zoomed(self, z):
        import copy
        t = copy.copy(self)
        t.scale = self.base_scale * z
        return t


THEMES = {l["name"]: l for l in LOOKS}


def rgb_hex(c):
    return "#%02X%02X%02X" % tuple(int(max(0, min(1, v)) * 255) for v in c[:3])


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
