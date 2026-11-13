"""Widgets: clock, system monitor, calendar, notes, to-do, weather, photo frame,
countdown, quote of the day, GitHub stats, network meter and focus timer."""
import calendar, datetime, math, os, random, shutil, time, urllib.parse
import cairo
from gi.repository import Gdk, GdkPixbuf, GLib, Pango

from . import core
from .glass import GlassWindow, text, fill_round, rounded, with_alpha

GREEN = (0.18, 0.78, 0.55)


class Widget(GlassWindow):
    NAME = ""
    SETTING = ""

    zoomable = True

    def __init__(self, app, name, w, h):
        super().__init__(app, "w:" + name, w, h)
        self.timers = []
        self.zoom = app.settings["zoom"].get(self.key, 100) / 100.0
        self.apply_size()

    # ---- size: its own size at its zoom, or the stack's size, plus the tab strip when stacked
    def tab_h(self):
        return int(self.T.s(36)) if self.app.group_members(self.key) else 0

    def natural_size(self):
        return int(self.T.s(self.base_w)), int(self.T.s(self.base_h))

    def panel_size(self):
        gs = self.app.group_size(self.key)
        w, h = gs if gs else self.natural_size()
        return w, h + self.tab_h()

    def inner(self):
        x, y, w, h = super().inner()
        t = self.tab_h()
        return x, y + t, w, h - t

    def anim_top(self):
        return self.panel_rect()[1] + self.tab_h()

    def paint_overlay(self, cr):
        t = self.tab_h()
        if t:
            T = self.T
            x, y, w, h = self.panel_rect()
            cr.rectangle(x, y, w, t)
            cr.set_source_rgba(*((0, 0, 0, 0.16) if T.dark else (1, 1, 1, 0.18)))
            cr.fill()
            cr.rectangle(x + T.s(12), y + t - 1, w - T.s(24), 1)
            cr.set_source_rgba(T.text[0], T.text[1], T.text[2], 0.12)
            cr.fill()
            self.draw_tabs(cr, x + T.s(10), y + T.s(3), w - T.s(20), t - T.s(3))
        else:
            self.tab_rects = []
        super().paint_overlay(cr)

    def every(self, seconds, fn):
        def tick():
            if self.get_visible() and not self.app.paused:
                fn()
            return True
        self.timers.append(GLib.timeout_add_seconds(seconds, tick) if seconds >= 1 else GLib.timeout_add(int(seconds * 1000), tick))

    def menu_items(self):
        items = self.extra_menu() + [None] + self.app.stack_menu(self)
        if self.zoom != 1.0:
            items.append(("Reset size", self.reset_zoom))
        return items + [("Hide this widget", lambda: self.app.hide_widget(self.SETTING)),
                        ("GlassDesk settings", self.app.show_settings)]

    def reset_zoom(self):
        self.zoom = 1.0
        self.S["zoom"].pop(self.key, None)
        self.S.save()
        self.apply_size()

    def extra_menu(self):
        return []

    def big(self, cr, s, x, y, w, size, color=None, align="left", bold=False):
        return text(cr, s, x, y, w, size=self.T.s(size), color=color or self.T.text, align=align,
                    weight=Pango.Weight.BOLD if bold else Pango.Weight.LIGHT)

    def small(self, cr, s, x, y, w, size=9, color=None, align="left", bold=False, lines=1):
        return text(cr, s, x, y, w, size=self.T.s(size), color=color or self.T.sub, align=align, bold=bold,
                    wrap=lines > 1, lines=lines)


# ================================================================ clock
class ClockWidget(Widget):
    SETTING = "show_clock"

    def __init__(self, app):
        super().__init__(app, "clock", 320, 132)
        self._schedule()

    def _schedule(self):
        now = datetime.datetime.now()
        GLib.timeout_add(int((60 - now.second) * 1000 - now.microsecond / 1000 + 30), self._tick)

    def _tick(self):
        self.queue_draw()
        self._schedule()
        return False

    def paint(self, cr):
        x, y, w, h = self.inner()
        now = self.app.now()
        hr = now.hour
        greet = "Good morning" if 5 <= hr < 12 else "Good afternoon" if hr < 18 else "Good evening" if hr < 22 else "Good night"
        self.small(cr, greet, x, y, w, 9.5)
        self.big(cr, now.strftime("%H:%M"), x - self.T.s(2), y + self.T.s(14), w, 36)
        self.small(cr, now.strftime("%A, %B ") + str(now.day), x, y + h - self.T.s(16), w, 10, color=self.T.text, bold=True)
        self.small(cr, "Week %d" % now.isocalendar()[1], x, y + h - self.T.s(16), w, 9, align="right")


# ================================================================ system monitor
class SystemWidget(Widget):
    SETTING = "show_system"

    def __init__(self, app):
        super().__init__(app, "system", 320, 178)
        self.cpu_hist = [0.0] * 40
        self.cpu = 0.0
        self._last = None
        self.mem = (0, 1)
        self.disk = (0, 1)
        self.batt = None
        self.sample()
        self.every(2, lambda: (self.sample(), self.queue_draw()))

    def sample(self):
        try:
            with open("/proc/stat") as f:
                v = list(map(int, f.readline().split()[1:]))
            idle, total = v[3] + (v[4] if len(v) > 4 else 0), sum(v[:8])
            if self._last:
                dt, di = total - self._last[0], idle - self._last[1]
                self.cpu = max(0.0, min(1.0, 1 - di / dt)) if dt > 0 else 0.0
                self.cpu_hist = self.cpu_hist[1:] + [self.cpu]
            self._last = (total, idle)
        except Exception:
            pass
        try:
            info = {}
            with open("/proc/meminfo") as f:
                for line in f:
                    k, val = line.split(":", 1)
                    info[k] = int(val.split()[0]) * 1024
            tot = info.get("MemTotal", 1)
            self.mem = (tot - info.get("MemAvailable", 0), tot)
        except Exception:
            pass
        try:
            du = shutil.disk_usage(os.path.expanduser("~"))
            self.disk = (du.used, du.total)
        except Exception:
            pass
        self.batt = None
        try:
            base = "/sys/class/power_supply"
            for n in os.listdir(base):
                p = os.path.join(base, n)
                if open(os.path.join(p, "type")).read().strip() == "Battery":
                    cap = int(open(os.path.join(p, "capacity")).read())
                    st = open(os.path.join(p, "status")).read().strip()
                    self.batt = (cap, st)
                    break
        except Exception:
            pass

    @staticmethod
    def gb(b):
        return "%.1f GB" % (b / 1024 ** 3)

    def paint(self, cr):
        T = self.T
        x, y, w, h = self.inner()
        self.title(cr, "System", x, y, w)
        self.small(cr, "live ●", x, y, w, 8, color=(GREEN[0], GREEN[1], GREEN[2], 0.9), align="right")
        yy = y + T.s(22)
        self.small(cr, "CPU", x, yy, w, 9.5, color=T.text, bold=True)
        self.small(cr, "%d%%" % round(self.cpu * 100), x, yy, w, 9.5, align="right")
        gy, gh = yy + T.s(18), T.s(20)
        cr.set_source_rgba(*with_alpha(T.accent, 1))
        cr.set_line_width(T.s(1.5))
        n = len(self.cpu_hist)
        for i, v in enumerate(self.cpu_hist):
            px, py = x + w * i / (n - 1), gy + gh - gh * v
            cr.line_to(px, py) if i else cr.move_to(px, py)
        cr.stroke_preserve()
        cr.line_to(x + w, gy + gh); cr.line_to(x, gy + gh); cr.close_path()
        g = cairo.LinearGradient(0, gy, 0, gy + gh)
        g.add_color_stop_rgba(0, T.accent[0], T.accent[1], T.accent[2], 0.35)
        g.add_color_stop_rgba(1, T.accent[0], T.accent[1], T.accent[2], 0)
        cr.set_source(g)
        cr.fill()
        rows = [("Memory", "%s / %s" % (self.gb(self.mem[0]), self.gb(self.mem[1])), self.mem[0] / max(1, self.mem[1])),
                ("Disk", "%s free" % self.gb(self.disk[1] - self.disk[0]), self.disk[0] / max(1, self.disk[1]))]
        if self.batt:
            rows.append(("Battery", "%d%% · %s" % (self.batt[0], self.batt[1].lower()), self.batt[0] / 100))
        yy = gy + gh + T.s(8)
        step = (y + h - yy) / len(rows)
        for lbl, val, frac in rows:
            self.small(cr, lbl, x, yy, w, 9.5, color=T.text, bold=True)
            self.small(cr, val, x, yy, w, 9, align="right")
            self.bar(cr, x, yy + T.s(17), w, T.s(4), frac)
            yy += step


# ================================================================ calendar
class CalendarWidget(Widget):
    SETTING = "show_calendar"

    def __init__(self, app):
        super().__init__(app, "calendar", 320, 252)
        self.offset = 0
        GLib.timeout_add_seconds(600, lambda: (self.queue_draw(), True)[1])

    def month(self):
        now = self.app.now()
        m = now.month - 1 + self.offset
        return now.year + m // 12, m % 12 + 1

    def arrows(self):
        x, y, w, h = self.inner()
        s = self.T.s(22)
        return (x + w - 2 * s - 4, y - 2, s, s), (x + w - s, y - 2, s, s)

    def paint(self, cr):
        T = self.T
        x, y, w, h = self.inner()
        yr, mo = self.month()
        self.small(cr, "%s %d" % (calendar.month_name[mo], yr), x, y, w, 10.5, color=T.text, bold=True)
        for i, (ax, ay, aw, ah) in enumerate(self.arrows()):
            fill_round(cr, ax, ay, aw, ah, aw / 2, T.hover)
            text(cr, "‹" if i == 0 else "›", ax, ay, aw, size=T.s(11), color=T.text, align="center", h=ah, valign="middle")
        top = y + T.s(28)
        cw = w / 7
        for i, d in enumerate(["Mo", "Tu", "We", "Th", "Fr", "Sa", "Su"]):
            text(cr, d, x + i * cw, top, cw, size=T.s(8), color=T.sub, align="center", bold=True)
        today = self.app.now().date()
        weeks = calendar.Calendar(0).monthdatescalendar(yr, mo)
        rh = (y + h - top - T.s(18)) / max(6, len(weeks))
        for r, wk in enumerate(weeks):
            for c, d in enumerate(wk):
                cx, cy = x + c * cw, top + T.s(18) + r * rh
                col = T.text if d.month == mo else with_alpha(T.sub[:3], 0.35)
                if d == today:
                    cr.arc(cx + cw / 2, cy + rh / 2, min(cw, rh) * 0.42, 0, 2 * math.pi)
                    cr.set_source_rgba(*with_alpha(T.accent, 1))
                    cr.fill()
                    col = (1, 1, 1, 1)
                text(cr, str(d.day), cx, cy, cw, size=T.s(9), color=col, align="center", h=rh, valign="middle",
                     bold=d == today)

    def is_drag_area(self, x, y):
        return not any(ax <= x < ax + aw and ay <= y < ay + ah for ax, ay, aw, ah in self.arrows())

    def on_click(self, x, y, b, e):
        for i, (ax, ay, aw, ah) in enumerate(self.arrows()):
            if ax <= x < ax + aw and ay <= y < ay + ah:
                self.offset += -1 if i == 0 else 1
                self.queue_draw()

    def on_double_click(self, x, y):
        self.offset = 0
        self.queue_draw()


# ================================================================ notes
class NotesWidget(Widget):
    SETTING = "show_notes"

    def __init__(self, app):
        super().__init__(app, "notes", 320, 176)

    def paint(self, cr):
        x, y, w, h = self.inner()
        self.title(cr, "Note", x, y, w)
        text(cr, self.S["note_text"] or "Double-click to write a note.", x, y + self.T.s(22), w, size=self.T.s(10.5),
             color=self.T.text, wrap=True, lines=max(1, int((h - self.T.s(22)) / self.T.s(18))))

    def on_double_click(self, x, y):
        t = self.app.ask_multiline("Note", self.S["note_text"])
        if t is not None:
            self.S["note_text"] = t
            self.S.save()
            self.queue_draw()

    def extra_menu(self):
        return [("Edit note…", lambda: self.on_double_click(0, 0))]


# ================================================================ to-do
class TodoWidget(Widget):
    SETTING = "show_todo"
    ROW = 28

    def __init__(self, app):
        super().__init__(app, "todo", 320, 120)

    def content_height(self, ph):
        n = max(1, len(self.S["todo"]))
        return int(self.T.s(14 * 2 + 24 + self.ROW * min(n, 12) + 4)) + self.tab_h()

    def rows(self):
        x, y, w, h = self.inner()
        top = y + self.T.s(24)
        return [(i, x, top + i * self.T.s(self.ROW), w, self.T.s(self.ROW)) for i in range(min(12, len(self.S["todo"])))]

    def paint(self, cr):
        T = self.T
        x, y, w, h = self.inner()
        items = self.S["todo"]
        self.title(cr, "To-do", x, y, w)
        self.small(cr, "%d left" % sum(1 for t in items if not t[1]), x, y, w, 8.5, align="right")
        if not items:
            self.small(cr, "Double-click to add tasks", x, y + T.s(28), w, 10)
        for i, rx, ry, rw, rh in self.rows():
            label, done = items[i]
            bs = T.s(15)
            bx, by = rx, ry + (rh - bs) / 2
            if done:
                fill_round(cr, bx, by, bs, bs, T.s(4), with_alpha(T.accent, 1))
                cr.set_source_rgba(1, 1, 1, 1)
                cr.set_line_width(T.s(1.8))
                cr.move_to(bx + bs * .25, by + bs * .52); cr.line_to(bx + bs * .43, by + bs * .7); cr.line_to(bx + bs * .76, by + bs * .3)
                cr.stroke()
            else:
                rounded(cr, bx + .5, by + .5, bs - 1, bs - 1, T.s(4))
                cr.set_source_rgba(*T.sub)
                cr.set_line_width(1.3)
                cr.stroke()
            tw, th = text(cr, label, rx + bs + T.s(10), ry, rw - bs - T.s(10), size=T.s(10), color=T.sub if done else T.text,
                          h=rh, valign="middle")
            if done:
                cr.rectangle(rx + bs + T.s(10), ry + rh / 2, min(tw, rw - bs - T.s(10)), 1)
                cr.set_source_rgba(*T.sub)
                cr.fill()

    def hit(self, x, y):
        for i, rx, ry, rw, rh in self.rows():
            if ry <= y < ry + rh and rx <= x < rx + T_s(self, 26):
                return i
        return None

    def is_drag_area(self, x, y):
        return self.hit(x, y) is None

    def on_click(self, x, y, b, e):
        i = self.hit(x, y)
        if i is not None:
            self.S["todo"][i][1] = not self.S["todo"][i][1]
            self.S.save()
            self.queue_draw()

    def on_double_click(self, x, y):
        cur = "\n".join(("[x] " if d else "") + t for t, d in self.S["todo"])
        res = self.app.ask_multiline("To-do list (one task per line, start with [x] when done)", cur)
        if res is not None:
            out = []
            for line in res.splitlines():
                line = line.strip()
                if not line:
                    continue
                done = line.lower().startswith("[x]")
                out.append([line[3:].strip() if done else line, done])
            self.S["todo"] = out
            self.S.save()
            self.apply_size()

    def extra_menu(self):
        return [("Edit list…", lambda: self.on_double_click(0, 0)),
                ("Clear finished tasks", self.clear_done)]

    def clear_done(self):
        self.S["todo"] = [t for t in self.S["todo"] if not t[1]]
        self.S.save()
        self.apply_size()


def T_s(widget, v):
    return widget.T.s(v)


# ================================================================ weather
WMO = {0: ("☀", "Clear"), 1: ("🌤", "Mostly clear"), 2: ("⛅", "Partly cloudy"), 3: ("☁", "Cloudy"), 45: ("🌫", "Fog"), 48: ("🌫", "Fog"),
       51: ("🌦", "Drizzle"), 53: ("🌦", "Drizzle"), 55: ("🌦", "Drizzle"), 61: ("🌧", "Rain"), 63: ("🌧", "Rain"), 65: ("🌧", "Heavy rain"),
       66: ("🌧", "Freezing rain"), 67: ("🌧", "Freezing rain"), 71: ("🌨", "Snow"), 73: ("🌨", "Snow"), 75: ("❄", "Heavy snow"), 77: ("🌨", "Snow"),
       80: ("🌦", "Showers"), 81: ("🌧", "Showers"), 82: ("⛈", "Heavy showers"), 85: ("🌨", "Snow showers"), 86: ("🌨", "Snow showers"),
       95: ("⛈", "Thunderstorm"), 96: ("⛈", "Thunderstorm"), 99: ("⛈", "Thunderstorm")}


class WeatherWidget(Widget):
    SETTING = "show_weather"

    def __init__(self, app):
        super().__init__(app, "weather", 320, 176)
        self.data = None
        self.status = ""
        self.busy = False
        GLib.timeout_add_seconds(1800, lambda: (self.fetch(), True)[1])
        GLib.idle_add(lambda: (self.fetch(), False)[1])

    def sample(self):
        self.data = {"temp": 21, "code": 1, "hi": 24, "lo": 15, "days": [("Fri", 2, 23, 14), ("Sat", 61, 19, 13), ("Sun", 0, 25, 15)]}
        self.S["weather_city"] = self.S["weather_city"] or "Tel Aviv"

    def fetch(self):
        if core.PREVIEW or self.busy:
            return
        lat, lon = self.S["weather_lat"], self.S["weather_lon"]
        if lat is None:
            self.status = "Double-click to choose your city"
            self.queue_draw()
            return
        self.busy = True
        unit = "fahrenheit" if self.S["weather_units"] == "f" else "celsius"
        url = ("https://api.open-meteo.com/v1/forecast?latitude=%s&longitude=%s&current=temperature_2m,weather_code"
               "&daily=weather_code,temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=4&temperature_unit=%s" % (lat, lon, unit))

        def done(j, err):
            self.busy = False
            if err:
                self.status = "Can't reach the weather service right now"
            else:
                d = j["daily"]
                days = []
                for i in range(1, min(4, len(d["time"]))):
                    dd = datetime.date.fromisoformat(d["time"][i])
                    days.append((dd.strftime("%a"), d["weather_code"][i], round(d["temperature_2m_max"][i]), round(d["temperature_2m_min"][i])))
                self.data = {"temp": round(j["current"]["temperature_2m"]), "code": j["current"]["weather_code"],
                             "hi": round(d["temperature_2m_max"][0]), "lo": round(d["temperature_2m_min"][0]), "days": days}
                self.status = ""
            self.queue_draw()
        core.in_background(lambda: core.http_json(url), done)

    def paint(self, cr):
        T = self.T
        x, y, w, h = self.inner()
        self.title(cr, "Weather", x, y, w)
        self.small(cr, self.S["weather_city"], x, y, w, 9, align="right")
        if not self.data:
            self.small(cr, self.status or "Loading…", x, y + T.s(30), w, 10.5, lines=3)
            return
        d = self.data
        icon, desc = WMO.get(d["code"], ("☁", ""))
        text(cr, icon, x, y + T.s(18), T.s(56), size=T.s(30), color=T.text)
        u = "°"
        self.big(cr, "%d%s" % (d["temp"], u), x + T.s(60), y + T.s(16), T.s(120), 30)
        self.small(cr, desc, x + T.s(160), y + T.s(26), w - T.s(160), 10, color=T.text, bold=True)
        self.small(cr, "H %d%s  L %d%s" % (d["hi"], u, d["lo"], u), x + T.s(160), y + T.s(44), w - T.s(160), 9)
        cw = w / max(1, len(d["days"]))
        yy = y + h - T.s(44)
        cr.rectangle(x, yy - T.s(8), w, 1)
        cr.set_source_rgba(T.text[0], T.text[1], T.text[2], 0.1)
        cr.fill()
        for i, (name, code, hi, lo) in enumerate(d["days"]):
            cx = x + i * cw
            text(cr, name, cx, yy, cw, size=T.s(8.5), color=T.sub, align="center", bold=True)
            text(cr, WMO.get(code, ("☁", ""))[0] + "  %d° / %d°" % (hi, lo), cx, yy + T.s(18), cw, size=T.s(9), color=T.text, align="center")

    def on_double_click(self, x, y):
        city = self.app.ask_text("Weather", "Your city", self.S["weather_city"])
        if not city:
            return
        self.status = "Looking up %s…" % city
        self.data = None
        self.queue_draw()

        def done(j, err):
            res = (j or {}).get("results") if not err else None
            if not res:
                self.status = "Couldn't find \"%s\". Double-click to try again." % city
                self.queue_draw()
                return
            r = res[0]
            self.S["weather_city"] = r.get("name", city)
            self.S["weather_lat"], self.S["weather_lon"] = r["latitude"], r["longitude"]
            self.S.save()
            self.fetch()
        core.in_background(lambda: core.http_json("https://geocoding-api.open-meteo.com/v1/search?count=1&name=" + urllib.parse.quote(city)), done)

    def extra_menu(self):
        f = self.S["weather_units"] == "f"
        return [("Change city…", lambda: self.on_double_click(0, 0)), ("Refresh now", self.fetch),
                ("Use Fahrenheit", lambda: (self.S.__setitem__("weather_units", "c" if f else "f"), self.S.save(), self.fetch()), f)]


# ================================================================ photo frame
class PhotoWidget(Widget):
    SETTING = "show_photos"
    EXT = (".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp")

    def __init__(self, app):
        super().__init__(app, "photos", 320, 220)
        self.files, self.idx, self.pb = [], 0, None
        self.load_list()
        GLib.timeout_add_seconds(max(10, int(self.S["photo_interval"])), lambda: (self.next() if self.get_visible() and not app.paused else None, True)[1])

    def load_list(self):
        d = self.S["photo_folder"] or os.environ.get("GLASSDESK_SAMPLE_PHOTO_DIR", "")
        self.files = []
        if d and os.path.isdir(d):
            self.files = sorted(os.path.join(d, n) for n in os.listdir(d) if n.lower().endswith(self.EXT))
            random.shuffle(self.files)
        self.idx = 0
        self.show()

    def show(self):
        self.pb = None
        if self.files:
            try:
                # Use the planned panel size: before the window is shown (at startup, or while the
                # widget is turned off) its allocation is 1x1, which made the photo fail to load.
                w, h = self.panel_size()
                w, h = max(w, 64), max(h, 64)
                self.pb = GdkPixbuf.Pixbuf.new_from_file_at_scale(self.files[self.idx % len(self.files)], int(w * 1.6), int(h * 1.6), True)
            except Exception:
                self.pb = None
        self.queue_draw()

    def next(self):
        if self.files:
            self.idx = (self.idx + 1) % len(self.files)
            self.show()

    def paint(self, cr):
        x, y, w, h = self.panel_rect()
        if self.pb is None:
            ix, iy, iw, ih = self.inner()
            self.title(cr, "Photo frame", ix, iy, iw)
            self.small(cr, "Double-click to choose a folder of pictures", ix, iy + self.T.s(30), iw, 10.5, lines=3)
            return
        pw, ph = self.pb.get_width(), self.pb.get_height()
        k = max(w / pw, h / ph)
        cr.save()
        cr.translate(x + (w - pw * k) / 2, y + (h - ph * k) / 2)
        cr.scale(k, k)
        Gdk.cairo_set_source_pixbuf(cr, self.pb, 0, 0)
        cr.get_source().set_filter(cairo.FILTER_GOOD)
        cr.paint()
        cr.restore()

    def on_click(self, x, y, b, e):
        self.next()

    def on_double_click(self, x, y):
        d = self.app.choose_folder("Choose a folder of pictures", self.S["photo_folder"])
        if d:
            self.S["photo_folder"] = d
            self.S.save()
            self.load_list()

    def extra_menu(self):
        return [("Next photo", self.next), ("Choose folder…", lambda: self.on_double_click(0, 0))]


# ================================================================ countdown
class CountdownWidget(Widget):
    SETTING = "show_countdown"

    def __init__(self, app):
        super().__init__(app, "countdown", 320, 124)
        GLib.timeout_add_seconds(1800, lambda: (self.queue_draw(), True)[1])

    def target(self):
        try:
            return datetime.date.fromisoformat(self.S["countdown_date"])
        except Exception:
            today = self.app.now().date()
            return datetime.date(today.year + 1, 1, 1)

    def paint(self, cr):
        T = self.T
        x, y, w, h = self.inner()
        days = (self.target() - self.app.now().date()).days
        self.title(cr, "Countdown", x, y, w)
        tw, th = self.big(cr, str(abs(days)), x, y + T.s(16), w, 34, color=with_alpha(T.accent, 1))
        self.small(cr, ("days until " if days >= 0 else "days since ") + (self.S["countdown_title"] or "the big day"),
                   x + tw + T.s(10), y + T.s(36), w - tw - T.s(10), 10.5, color=T.text, bold=True)
        self.small(cr, self.target().strftime("%A, %d %B %Y"), x, y + h - T.s(14), w, 9)

    def on_double_click(self, x, y):
        t = self.app.ask_text("Countdown", "What are you counting down to?", self.S["countdown_title"])
        if t is None:
            return
        d = self.app.ask_text("Countdown", "Date (YYYY-MM-DD)", self.S["countdown_date"] or self.target().isoformat())
        if d:
            try:
                datetime.date.fromisoformat(d.strip())
                self.S["countdown_date"] = d.strip()
            except Exception:
                pass
        self.S["countdown_title"] = t
        self.S.save()
        self.queue_draw()

    def extra_menu(self):
        return [("Change countdown…", lambda: self.on_double_click(0, 0))]


# ================================================================ quote of the day
class QuoteWidget(Widget):
    SETTING = "show_quote"

    def __init__(self, app):
        super().__init__(app, "quote", 320, 136)
        self.extra = 0

    def current(self):
        n = self.app.now()
        return core.QUOTES[(n.timetuple().tm_yday + n.year * 7 + self.extra) % len(core.QUOTES)]

    def paint(self, cr):
        T = self.T
        x, y, w, h = self.inner()
        q, who = self.current()
        self.title(cr, "Quote of the day", x, y, w)
        text(cr, "“", x - T.s(2), y + T.s(10), T.s(30), size=T.s(30), color=with_alpha(T.accent, 1), bold=True)
        text(cr, q, x + T.s(24), y + T.s(24), w - T.s(24), size=T.s(10.5), color=T.text, wrap=True, lines=3)
        self.small(cr, "— " + who, x + T.s(24), y + h - T.s(14), w - T.s(24), 9)

    def next(self):
        self.extra += 1
        self.queue_draw()

    def on_double_click(self, x, y):
        self.next()

    def extra_menu(self):
        return [("Another quote", self.next)]


# ================================================================ GitHub stats
class GitHubWidget(Widget):
    SETTING = "show_github"

    def __init__(self, app):
        super().__init__(app, "github", 320, 236)
        self.st, self.status, self.busy = None, "", False
        GLib.timeout_add_seconds(1800, lambda: (self.fetch(), True)[1])
        GLib.idle_add(lambda: (self.fetch(), False)[1])

    def sample(self):
        self.st = {"login": "glassdesk-dev", "stars": 1270, "followers": 318, "repos": 24, "top": "GlassDesk", "top_stars": 842,
                   "days": [2, 0, 5, 3, 7, 1, 0, 4, 9, 6, 2, 8, 5, 11]}

    @staticmethod
    def parse(user, repos, events, today):
        st = {"login": user.get("login", ""), "followers": user.get("followers", 0), "repos": user.get("public_repos", 0),
              "stars": 0, "top": "", "top_stars": 0, "days": [0] * 14}
        for r in repos or []:
            if r.get("fork"):
                continue
            s = r.get("stargazers_count", 0)
            st["stars"] += s
            if s > st["top_stars"] or not st["top"]:
                st["top"], st["top_stars"] = r.get("name", ""), s
        for e in events or []:
            try:
                t = datetime.datetime.strptime(e["created_at"][:10], "%Y-%m-%d").date()
            except Exception:
                continue
            ago = (today - t).days
            if 0 <= ago <= 13:
                n = 1
                if e.get("type") == "PushEvent":
                    n = max(1, int((e.get("payload") or {}).get("size") or 1))
                st["days"][13 - ago] += n
        return st

    def fetch(self):
        if core.PREVIEW or self.busy:
            return
        u = (self.S["github_user"] or "").strip()
        if not u:
            self.status = "Double-click to add your GitHub username"
            self.queue_draw()
            return
        self.busy = True
        q = urllib.parse.quote(u)

        def work():
            base = "https://api.github.com/users/" + q
            return self.parse(core.http_json(base), core.http_json(base + "/repos?per_page=100&sort=pushed"),
                              core.http_json(base + "/events/public?per_page=100"), datetime.datetime.utcnow().date())

        def done(st, err):
            self.busy = False
            if err:
                code = getattr(err, "code", 0)
                self.status = ('No GitHub user called "%s"' % u) if code == 404 else \
                    "GitHub says: too many lookups, trying again later" if code == 403 else "Can't reach GitHub right now"
            else:
                self.st, self.status = st, ""
            self.queue_draw()
        core.in_background(work, done)

    @staticmethod
    def k(n):
        return "%dk" % (n // 1000) if n >= 10000 else ("%.1fk" % (n / 1000)) if n >= 1000 else str(n)

    def paint(self, cr):
        T = self.T
        x, y, w, h = self.inner()
        self.title(cr, "GitHub", x, y, w)
        st = self.st
        if st is None or self.status:
            self.small(cr, self.status or ("Loading…" if self.busy else "Double-click to add your GitHub username"),
                       x, y + T.s(30), w, 10.5, lines=3)
            if st is None:
                return
        self.small(cr, "@" + st["login"], x, y, w, 8.5, align="right")
        cw = w / 3
        top = y + T.s(22)
        for i, (v, lbl) in enumerate(((st["stars"], "stars"), (st["followers"], "followers"), (st["repos"], "repos"))):
            self.big(cr, self.k(v), x + i * cw, top, cw, 24, color=with_alpha(T.accent, 1) if i == 0 else T.text)
            self.small(cr, lbl, x + i * cw, top + T.s(34), cw, 8.5)
        yy = top + T.s(58)
        self.small(cr, "★ " + (st["top"] or "—"), x, yy, w - T.s(70), 10, color=T.text, bold=True)
        self.small(cr, "%s stars" % self.k(st["top_stars"]), x, yy, w, 9, align="right")
        yy += T.s(26)
        tot = sum(st["days"])
        self.small(cr, "Last 14 days", x, yy, w, 8.5, bold=True)
        self.small(cr, "%d contribution%s" % (tot, "" if tot == 1 else "s"), x, yy, w, 8.5, align="right")
        bh, by, gap = T.s(38), yy + T.s(18), T.s(4)
        bw = (w - gap * 13) / 14
        mx = max(1, max(st["days"]))
        for i, d in enumerate(st["days"]):
            hh = T.s(3) if d == 0 else max(T.s(5), bh * d / mx)
            fill_round(cr, x + i * (bw + gap), by + bh - hh, bw, hh, min(bw / 2, T.s(3)),
                       T.track if d == 0 else with_alpha(T.accent, 0.45 + 0.55 * d / mx))

    def on_double_click(self, x, y):
        self.choose_user()

    def choose_user(self):
        u = self.app.ask_text("GitHub stats", "Your GitHub username", self.S["github_user"])
        if not u:
            return
        u = u.strip().lstrip("@").replace("https://github.com/", "").strip("/")
        if u:
            self.S["github_user"] = u
            self.S.save()
            self.st, self.status = None, ""
            self.fetch()

    def extra_menu(self):
        return [("Open profile", lambda: core.open_path("https://github.com/" + self.S["github_user"]) if self.S["github_user"] else None),
                ("Change username…", self.choose_user), ("Refresh now", self.fetch)]


# ================================================================ network meter
class NetworkWidget(Widget):
    SETTING = "show_network"

    def __init__(self, app):
        super().__init__(app, "network", 320, 168)
        self.last, self.down, self.up = None, 0.0, 0.0
        self.hd, self.hu = [0.0] * 40, [0.0] * 40
        self.adapter = ""
        self.sample()
        self.every(2, lambda: (self.sample(), self.queue_draw()))

    def set_sample(self):
        rnd = random.Random(3)
        self.hd = [0.25 + 0.2 * math.sin(i * .4) + rnd.random() * .25 for i in range(40)]
        self.hu = [0.08 + rnd.random() * .1 for i in range(40)]
        self.down, self.up, self.adapter = 4.8 * 1048576, 0.6 * 1048576, "Wi-Fi"

    def sample(self):
        rx = tx = 0
        name = ""
        try:
            for line in open("/proc/net/dev").readlines()[2:]:
                iface, data = line.split(":", 1)
                iface = iface.strip()
                if iface == "lo" or iface.startswith(("docker", "veth", "br-", "virbr", "tun", "tap", "wg")):
                    continue
                v = data.split()
                if int(v[0]) == 0 and int(v[8]) == 0:
                    continue
                rx += int(v[0]); tx += int(v[8])
                if not name or iface.startswith("w"):
                    name = "Wi-Fi" if iface.startswith("w") else "Ethernet" if iface.startswith(("e", "en")) else iface
        except Exception:
            return
        now = time.monotonic()
        if self.last:
            dt = max(0.5, now - self.last[2])
            self.down, self.up = max(0, (rx - self.last[0]) / dt), max(0, (tx - self.last[1]) / dt)
            self.hd = self.hd[1:] + [self.scale(self.down)]
            self.hu = self.hu[1:] + [self.scale(self.up)]
        self.last = (rx, tx, now)
        self.adapter = name

    @staticmethod
    def scale(bps):
        return 0 if bps <= 1024 else max(0, min(1, math.log10(bps / 1024) / 5))

    @staticmethod
    def speed(bps):
        if bps < 1024:
            return "0 KB/s"
        if bps < 1048576:
            return ("%.1f KB/s" if bps < 10240 else "%.0f KB/s") % (bps / 1024)
        return ("%.1f MB/s" if bps < 10485760 else "%.0f MB/s") % (bps / 1048576)

    def paint(self, cr):
        T = self.T
        x, y, w, h = self.inner()
        self.title(cr, "Network", x, y, w)
        self.small(cr, self.adapter or "offline", x, y, w, 8.5, align="right")
        half = w / 2 - T.s(8)
        self.row(cr, "↓ Download", self.speed(self.down), self.hd, x, y + T.s(24), half, with_alpha(T.accent, 1))
        self.row(cr, "↑ Upload", self.speed(self.up), self.hu, x + half + T.s(16), y + T.s(24), half, (GREEN[0], GREEN[1], GREEN[2], 1))

    def row(self, cr, label, value, hist, x, y, w, c):
        T = self.T
        self.small(cr, label, x, y, w, 9, bold=True)
        self.big(cr, value, x, y + T.s(15), w, 17)
        sy, sh = y + T.s(48), T.s(34)
        n = len(hist)
        cr.move_to(x, sy + sh)
        for i, v in enumerate(hist):
            cr.line_to(x + w * i / (n - 1), sy + sh - 1 - (sh - 2) * v)
        cr.line_to(x + w, sy + sh)
        cr.close_path()
        g = cairo.LinearGradient(0, sy, 0, sy + sh)
        g.add_color_stop_rgba(0, c[0], c[1], c[2], 0.42)
        g.add_color_stop_rgba(1, c[0], c[1], c[2], 0)
        cr.set_source(g)
        cr.fill()
        for i, v in enumerate(hist):
            px, py = x + w * i / (n - 1), sy + sh - 1 - (sh - 2) * v
            cr.line_to(px, py) if i else cr.move_to(px, py)
        cr.set_source_rgba(*c)
        cr.set_line_width(T.s(1.6))
        cr.stroke()


# ================================================================ focus timer
class FocusWidget(Widget):
    SETTING = "show_focus"

    def __init__(self, app):
        super().__init__(app, "focus", 320, 150)
        self.running, self.on_break, self.sessions = False, False, 0
        self.left = self.total()
        self._tid = 0

    def total(self):
        return max(1, int(self.S["break_minutes" if self.on_break else "focus_minutes"])) * 60

    def set_sample(self):
        self.running, self.left, self.sessions = True, 14 * 60 + 32, 3

    def toggle(self):
        self.running = not self.running
        if self.running and not self._tid:
            self._tid = GLib.timeout_add_seconds(1, self.tick)
        self.queue_draw()

    def reset(self):
        self.running, self.on_break = False, False
        self.left = self.total()
        self.queue_draw()

    def tick(self):
        if not self.running:
            self._tid = 0
            return False
        self.left -= 1
        if self.left <= 0:
            if not self.on_break:
                self.sessions += 1
            self.on_break = not self.on_break
            self.left = self.total()
            self.running = False
            self._tid = 0
            core.beep()
            if self.on_break:
                core.notify("Focus done — take a break", "Nice work! Click the timer to start a %d-minute break." % self.S["break_minutes"])
            else:
                core.notify("Break's over", "Click the timer to start the next focus block.")
            self.queue_draw()
            return False
        self.queue_draw()
        return True

    def paint(self, cr):
        T = self.T
        x, y, w, h = self.inner()
        self.title(cr, "Break" if self.on_break else "Focus", x, y, w)
        self.small(cr, "%d session%s today" % (self.sessions, "" if self.sessions == 1 else "s"), x, y, w, 8.5, align="right")
        d = T.s(80)
        cx, cy, r = x + d / 2, y + T.s(22) + d / 2, d / 2 - T.s(4)
        cr.set_line_width(T.s(7))
        cr.arc(cx, cy, r, 0, 2 * math.pi)
        cr.set_source_rgba(*T.track)
        cr.stroke()
        frac = 1 - self.left / self.total()
        col = (GREEN[0], GREEN[1], GREEN[2], 1) if self.on_break else with_alpha(T.accent, 1)
        if frac > 0.002:
            cr.set_line_cap(cairo.LINE_CAP_ROUND)
            cr.arc(cx, cy, r, -math.pi / 2, -math.pi / 2 + 2 * math.pi * frac)
            cr.set_source_rgba(*col)
            cr.stroke()
        s = T.s(10)
        cr.set_source_rgba(*T.text)
        if self.running:
            cr.rectangle(cx - s * .7, cy - s, s * .5, s * 2); cr.rectangle(cx + s * .2, cy - s, s * .5, s * 2)
        else:
            cr.move_to(cx - s * .6, cy - s); cr.line_to(cx + s, cy); cr.line_to(cx - s * .6, cy + s); cr.close_path()
        cr.fill()
        tx = x + d + T.s(18)
        self.big(cr, "%02d:%02d" % (self.left // 60, self.left % 60), tx, y + T.s(24), w - d - T.s(18), 32)
        msg = ("Relax for a bit" if self.on_break else "Stay on one thing") if self.running else \
            ("Click to continue" if self.left < self.total() else "Click to start")
        self.small(cr, msg, tx, y + T.s(72), w - d - T.s(18), 10)

    def on_click(self, x, y, b, e):
        self.toggle()

    def extra_menu(self):
        return [("Start / pause", self.toggle), ("Reset", self.reset)]

    def settings_changed(self):
        if not self.running:
            self.left = self.total()
            self.queue_draw()


from .music import MusicWidget, DayWidget

NAMES = {"show_clock": "Clock", "show_system": "System", "show_calendar": "Calendar", "show_notes": "Notes",
         "show_todo": "To-do", "show_weather": "Weather", "show_photos": "Photos", "show_countdown": "Countdown",
         "show_quote": "Quote", "show_github": "GitHub", "show_network": "Network", "show_focus": "Focus",
         "show_music": "Music", "show_day": "Today"}

ALL = [ClockWidget, SystemWidget, CalendarWidget, NotesWidget, TodoWidget, WeatherWidget, PhotoWidget,
       CountdownWidget, QuoteWidget, GitHubWidget, NetworkWidget, FocusWidget, MusicWidget, DayWidget]
