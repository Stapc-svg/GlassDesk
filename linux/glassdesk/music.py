"""Music widget (what's playing in any player, through MPRIS) and the Today widget."""
import datetime, math, os, threading, urllib.parse, urllib.request
import cairo
from gi.repository import Gio, GLib, Gdk, GdkPixbuf, Pango

from . import core
from .glass import text, fill_round, rounded, with_alpha
from .widgets import Widget

MPRIS = "org.mpris.MediaPlayer2."
PLAYER = "org.mpris.MediaPlayer2.Player"


class MusicWidget(Widget):
    """Linux apps that play music (Spotify, Firefox, Chrome, Rhythmbox, VLC...) announce it over D-Bus (MPRIS)."""
    SETTING = "show_music"

    def __init__(self, app):
        super().__init__(app, "music", 360, 132)
        self.title = self.artist = self.status = ""
        self.pos = self.dur = 0.0
        self.pos_at = 0.0
        self.cover = None
        self.cover_url = None
        self.bus_name = None
        self.btns = {}
        self._sample = False
        try:
            self.bus = Gio.bus_get_sync(Gio.BusType.SESSION, None)
        except Exception:
            self.bus = None
        cached = os.path.join(core.CACHE_DIR, "music-cover")
        if os.path.exists(cached):
            try:
                self.cover = GdkPixbuf.Pixbuf.new_from_file_at_scale(cached, 256, 256, True)
            except Exception:
                pass
        self.every(1.5, self.poll)
        self.every(1, lambda: self.status == "Playing" and self.queue_draw())

    def sample(self):
        self._sample = True
        self.title, self.artist, self.status, self.pos, self.dur = "Neon Avenue", "Glass Echoes", "Playing", 72, 214
        self.pos_at = GLib.get_monotonic_time() / 1e6
        surf = cairo.ImageSurface(cairo.FORMAT_ARGB32, 200, 200)
        cr = cairo.Context(surf)
        g = cairo.LinearGradient(0, 0, 200, 200)
        g.add_color_stop_rgb(0, 0.47, 0.24, 0.86)
        g.add_color_stop_rgb(1, 1.0, 0.35, 0.59)
        cr.set_source(g)
        cr.paint()
        cr.set_source_rgba(1, 1, 1, 0.28)
        cr.set_line_width(6)
        for i in range(4):
            cr.arc(100, 100, 60 - i * 14, 0, 2 * math.pi)
            cr.stroke()
        self.cover = Gdk.pixbuf_get_from_surface(surf, 0, 0, 200, 200)

    # ---- reading the player
    def _call(self, name, path, iface, method, args=None, reply=None):
        return self.bus.call_sync(name, path, iface, method, args, reply, Gio.DBusCallFlags.NONE, 800, None)

    def _players(self):
        try:
            names = self._call("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "ListNames").unpack()[0]
        except Exception:
            return []
        return [n for n in names if n.startswith(MPRIS)]

    def _get(self, name, prop):
        try:
            return self._call(name, "/org/mpris/MediaPlayer2", "org.freedesktop.DBus.Properties", "Get",
                              GLib.Variant("(ss)", (PLAYER, prop))).unpack()[0]
        except Exception:
            return None

    def poll(self):
        if self._sample or self.bus is None:
            return
        players = self._players()
        best, best_status = None, None
        for n in players:
            st = self._get(n, "PlaybackStatus") or ""
            if best is None or (st == "Playing" and best_status != "Playing"):
                best, best_status = n, st
        if best is None:
            if self.status:
                self.status = ""
                self.queue_draw()
            return
        self.bus_name = best
        md = self._get(best, "Metadata") or {}
        title = str(md.get("xesam:title") or "").strip()
        artist = md.get("xesam:artist") or ""
        if isinstance(artist, list):
            artist = ", ".join(artist)
        self.dur = (md.get("mpris:length") or 0) / 1e6
        pos = self._get(best, "Position")
        self.pos = (pos or 0) / 1e6
        self.pos_at = GLib.get_monotonic_time() / 1e6
        self.status = best_status or "Stopped"
        url = md.get("mpris:artUrl")
        if title and (title != self.title or artist != self.artist):
            self.S["last_song"], self.S["last_artist"] = title, artist
            self.S.save()
            self._load_cover(url)
        self.title, self.artist = title, artist
        self.queue_draw()

    def _load_cover(self, url):
        self.cover_url = url
        if not url:
            self.cover = None
            return
        dest = os.path.join(core.CACHE_DIR, "music-cover")

        def work():
            try:
                os.makedirs(core.CACHE_DIR, exist_ok=True)
                if url.startswith("file://"):
                    src = urllib.parse.unquote(url[7:])
                    with open(src, "rb") as f:
                        data = f.read()
                else:
                    data = urllib.request.urlopen(url, timeout=10).read()
                with open(dest + ".tmp", "wb") as f:
                    f.write(data)
                os.replace(dest + ".tmp", dest)
                return True
            except Exception as e:
                core.log("cover: %s" % e)
                return False

        def done(ok, err=None):
            if ok and self.cover_url == url:
                try:
                    self.cover = GdkPixbuf.Pixbuf.new_from_file_at_scale(dest, 256, 256, True)
                except Exception:
                    self.cover = None
                self.queue_draw()
        core.in_background(work, done)

    # ---- buttons
    def send(self, method):
        if self.bus is None:
            return
        names = [self.bus_name] if self.bus_name else self._players()
        for n in names:
            try:
                self._call(n, "/org/mpris/MediaPlayer2", PLAYER, method)
                break
            except Exception:
                continue
        if method == "PlayPause" and self.status:
            self.status = "Paused" if self.status == "Playing" else "Playing"
            self.pos_at = GLib.get_monotonic_time() / 1e6
            self.queue_draw()
        GLib.timeout_add(400, lambda: (self.poll(), False)[1])

    def open_mixer(self):
        for cmd in (["pavucontrol"], ["gnome-control-center", "sound"], ["systemsettings", "kcm_pulseaudio"], ["xfce4-pulseaudio-plugin"]):
            try:
                import shutil, subprocess
                if shutil.which(cmd[0]):
                    subprocess.Popen(cmd)
                    return
            except Exception:
                pass

    def on_click(self, x, y, button, event):
        for k, (bx, by, bw, bh) in self.btns.items():
            if bx <= x < bx + bw and by <= y < by + bh:
                {"play": lambda: self.send("PlayPause"), "next": lambda: self.send("Next"),
                 "prev": lambda: self.send("Previous"), "vol": self.open_mixer}[k]()
                return

    def is_drag_area(self, x, y):
        for bx, by, bw, bh in self.btns.values():
            if bx <= x < bx + bw and by <= y < by + bh:
                return False
        return True

    def extra_menu(self):
        return [("Play / pause", lambda: self.send("PlayPause")), ("Next song", lambda: self.send("Next")),
                ("Previous song", lambda: self.send("Previous")), ("Volume mixer", self.open_mixer)]

    # ---- drawing
    def paint(self, cr):
        T = self.T
        x, y, w, h = self.inner()
        live = bool(self.status) and bool(self.title)
        title = self.title if live else self.S.get("last_song", "")
        artist = self.artist if live else self.S.get("last_artist", "")
        anything = bool(title)
        playing = self.status == "Playing"
        # cover
        cs = min(h, T.s(96))
        cx, cy = x - T.s(2), y + (h - cs) / 2
        cr.save()
        rounded(cr, cx, cy, cs, cs, T.s(14))
        cr.clip()
        if self.cover is not None and anything:
            pb = self.cover
            k = cs / min(pb.get_width(), pb.get_height())
            cr.translate(cx + (cs - pb.get_width() * k) / 2, cy + (cs - pb.get_height() * k) / 2)
            cr.scale(k, k)
            Gdk.cairo_set_source_pixbuf(cr, pb, 0, 0)
            cr.paint()
        else:
            g = cairo.LinearGradient(cx, cy, cx + cs, cy + cs)
            g.add_color_stop_rgb(0, *T.accent)
            g.add_color_stop_rgb(1, *T.accent2)
            cr.set_source(g)
            cr.paint()
            self._note(cr, cx + cs / 2, cy + cs / 2, cs * 0.22)
        cr.restore()
        tx = cx + cs + T.s(16)
        tw = x + w - tx
        label = ("NOW PLAYING" if playing else "PAUSED") if live else ("LAST PLAYED" if anything else "MUSIC")
        lc = with_alpha(T.accent, 1) if live and playing else T.sub
        lx = tx
        if live and playing:
            bw = T.s(2.6)
            for i, f in enumerate((0.55, 1.0, 0.7)):
                bh = T.s(10) * f
                fill_round(cr, tx + i * bw * 1.6, y + T.s(12) - bh, bw, bh, bw / 2, lc)
            lx = tx + T.s(15)
        text(cr, label, lx, y, tw, size=T.s(7.8), bold=True, color=lc, h=T.s(14), valign="middle")
        text(cr, title if anything else "Nothing played yet", tx, y + T.s(15), tw, size=T.s(11.6), bold=True, color=T.text)
        text(cr, (artist or "Unknown artist") if anything else "Play a song in Spotify, YouTube or any player",
             tx, y + T.s(37), tw, size=T.s(9.2), color=T.sub)
        # progress
        py = y + T.s(66)
        now = self.pos + ((GLib.get_monotonic_time() / 1e6 - self.pos_at) if playing else 0)
        if live and self.dur > 0:
            now = min(now, self.dur)
            fmt = lambda v: "%02d:%02d" % (int(v) // 60, int(v) % 60)
            lw = T.s(36)
            text(cr, fmt(now), tx, py - T.s(7), lw, size=T.s(7.8), color=T.sub)
            text(cr, fmt(self.dur), x + w - lw, py - T.s(7), lw, size=T.s(7.8), color=T.sub, align="right")
            bx, bw = tx + lw + T.s(4), tw - 2 * lw - T.s(8)
            fill_round(cr, bx, py - T.s(1.5), bw, T.s(4), T.s(2), T.track)
            f = max(0, min(1, now / self.dur))
            if f > 0.005:
                fill_round(cr, bx, py - T.s(1.5), max(T.s(4), bw * f), T.s(4), T.s(2), with_alpha(T.accent, 1))
                cr.new_path()
                cr.arc(bx + bw * f, py + T.s(0.5), T.s(4.5), 0, 2 * math.pi)
                cr.set_source_rgba(*T.text)
                cr.fill()
        else:
            fill_round(cr, tx, py - T.s(1.5), tw, T.s(4), T.s(2), T.track)
        # controls
        cyb = y + h - T.s(14)
        mid = tx + tw / 2
        d = T.s(32)
        self.btns = {"play": (mid - d / 2, cyb - d / 2, d, d),
                     "prev": (mid - T.s(40) - T.s(14), cyb - T.s(14), T.s(28), T.s(28)),
                     "next": (mid + T.s(40) - T.s(14), cyb - T.s(14), T.s(28), T.s(28)),
                     "vol": (x + w - T.s(26), cyb - T.s(13), T.s(26), T.s(26))}
        rounded(cr, mid - d / 2, cyb - d / 2, d, d, d / 2)
        g = cairo.LinearGradient(mid - d / 2, cyb - d / 2, mid + d / 2, cyb + d / 2)
        g.add_color_stop_rgb(0, *T.accent)
        g.add_color_stop_rgb(1, *T.accent2)
        cr.set_source(g)
        cr.fill()
        s = T.s(6.5)
        cr.set_source_rgba(1, 1, 1, 1)
        if playing:
            cr.rectangle(mid - s * 0.75, cyb - s, s * 0.5, s * 2)
            cr.rectangle(mid + s * 0.25, cyb - s, s * 0.5, s * 2)
        else:
            cr.move_to(mid - s * 0.55, cyb - s); cr.line_to(mid + s * 0.95, cyb); cr.line_to(mid - s * 0.55, cyb + s); cr.close_path()
        cr.fill()
        cr.set_source_rgba(*T.text)
        for key, fwd in (("prev", False), ("next", True)):
            bx, by, bw, bh = self.btns[key]
            ccx, ccy, s = bx + bw / 2, by + bh / 2, T.s(5.5)
            dd = 1 if fwd else -1
            cr.move_to(ccx - dd * s, ccy - s); cr.line_to(ccx + dd * s * 0.8, ccy); cr.line_to(ccx - dd * s, ccy + s); cr.close_path()
            cr.fill()
            cr.rectangle(ccx + s * 0.8 if fwd else ccx - s * 0.8 - T.s(2), ccy - s, T.s(2), s * 2)
            cr.fill()
        # speaker
        bx, by, bw, bh = self.btns["vol"]
        sx, sy, s = bx + bw / 2 - T.s(3), by + bh / 2, T.s(4.5)
        cr.set_source_rgba(*T.sub)
        cr.move_to(sx - s * 1.2, sy - s * 0.5); cr.line_to(sx - s * 0.4, sy - s * 0.5); cr.line_to(sx + s * 0.5, sy - s * 1.2)
        cr.line_to(sx + s * 0.5, sy + s * 1.2); cr.line_to(sx - s * 0.4, sy + s * 0.5); cr.line_to(sx - s * 1.2, sy + s * 0.5)
        cr.close_path()
        cr.fill()
        cr.set_line_width(T.s(1.4))
        for rr in (s * 0.9, s * 1.6):
            cr.new_path()
            cr.arc(sx + s * 0.6, sy, rr, -0.85, 0.85)
            cr.stroke()

    def _note(self, cr, x, y, s):
        cr.set_source_rgba(1, 1, 1, 0.92)
        for ex, ey in ((x - s * 0.6, y + s * 0.7), (x + s * 0.9, y + s * 0.5)):
            cr.save(); cr.translate(ex, ey); cr.scale(s * 0.45, s * 0.35); cr.arc(0, 0, 1, 0, 2 * math.pi); cr.restore(); cr.fill()
        cr.set_line_width(s * 0.22)
        cr.move_to(x - s * 0.2, y + s * 0.65); cr.line_to(x - s * 0.2, y - s * 0.9)
        cr.line_to(x + s * 1.3, y - s * 1.1); cr.line_to(x + s * 1.3, y + s * 0.45)
        cr.stroke()


class DayWidget(Widget):
    """Four rings: how much of the day, week, month and year has gone by."""
    SETTING = "show_day"

    def __init__(self, app):
        super().__init__(app, "day", 340, 150)
        self.every(60, self.queue_draw)

    @staticmethod
    def shift(c, deg):
        import colorsys
        h, l, s = colorsys.rgb_to_hls(*c)
        return colorsys.hls_to_rgb((h + deg / 360) % 1, l, s)

    def paint(self, cr):
        T = self.T
        x, y, w, h = self.inner()
        now = self.app.now()
        self.title(cr, "Today", x, y, w)
        text(cr, now.strftime("%A"), x, y, w, size=T.s(8.6), color=T.sub, align="right")
        day = (now.hour * 3600 + now.minute * 60 + now.second) / 86400
        week = (now.weekday() + day) / 7
        import calendar
        month = (now.day - 1 + day) / calendar.monthrange(now.year, now.month)[1]
        year = (now.timetuple().tm_yday - 1 + day) / (366 if calendar.isleap(now.year) else 365)
        cw = w / 4
        d = min(T.s(58), cw - T.s(14))
        top = y + T.s(28)
        for i, (v, name) in enumerate(((day, "Day"), (week, "Week"), (month, "Month"), (year, "Year"))):
            cx = x + cw * i + cw / 2
            cy = top + d / 2
            pw = T.s(6)
            rr = d / 2 - pw / 2
            cr.set_line_width(pw)
            cr.new_path()
            cr.arc(cx, cy, rr, 0, 2 * math.pi)
            cr.set_source_rgba(*T.track)
            cr.stroke()
            if v > 0.003:
                a, b = self.shift(T.accent, i * 22), self.shift(T.accent, i * 22 + 40)
                g = cairo.LinearGradient(cx - rr, cy - rr, cx + rr, cy + rr)
                g.add_color_stop_rgb(0, *a)
                g.add_color_stop_rgb(1, *b)
                cr.set_source(g)
                cr.set_line_cap(cairo.LINE_CAP_ROUND)
                cr.new_path()
                cr.arc(cx, cy, rr, -math.pi / 2, -math.pi / 2 + 2 * math.pi * min(1, v))
                cr.stroke()
                cr.set_line_cap(cairo.LINE_CAP_BUTT)
            text(cr, "%d%%" % int(v * 100), cx - d / 2, cy - d / 2, d, size=T.s(9.8), bold=True, color=T.text,
                 align="center", h=d, valign="middle")
            text(cr, name, cx - cw / 2, top + d + T.s(4), cw, size=T.s(8.6), color=T.sub, align="center")
