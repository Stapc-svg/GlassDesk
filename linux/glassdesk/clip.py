"""Clipboard widget: the last few texts you copied; click one to copy it again.
Kept in memory only (never written to disk) and forgotten when GlassDesk closes."""
import datetime, re
from gi.repository import Gdk, Gtk, GLib

from . import core
from .glass import text, fill_round, rounded, with_alpha
from .widgets import Widget


class ClipWidget(Widget):
    SETTING = "show_clip"
    KEEP, SHOW = 20, 5

    def __init__(self, app):
        super().__init__(app, "clip", 320, 252)
        self.items = []            # [(text, time)]
        self.last = None
        self.paused = False
        self.rows = []
        self.hot = -1
        self.flash = -1
        if not core.PREVIEW:
            self.cb = Gtk.Clipboard.get(Gdk.SELECTION_CLIPBOARD)
            self.cb.connect("owner-change", lambda *a: self._check())

    def sample(self):
        now = self.app.now()
        for i, s in enumerate(("Meeting moved to 15:30 – room 4B", "https://github.com/Stapc-svg/GlassDesk", "#7C9CFF",
                               "Thanks so much, see you tomorrow!", "/home/me/Documents/Report final.odt")):
            self.items.append((s, now - datetime.timedelta(minutes=i * 7 + 1)))

    def _check(self):
        if self.paused or self.app.paused:
            return
        # password managers mark private copies; skip those
        def got_targets(_cb, atoms, *_):
            names = [a.name() for a in (atoms or [])]
            if "x-kde-passwordManagerHint" in names or "ExcludeClipboardContentFromMonitorProcessing" in names:
                return
            self.cb.request_text(lambda _c, s, *_: self._add(s))
        try:
            self.cb.request_targets(got_targets)
        except Exception:
            pass

    def _add(self, s):
        if not s or s == self.last:
            return
        self.last = s
        if not s.strip() or len(s) > 20000:
            return
        self.items = [i for i in self.items if i[0] != s]
        self.items.insert(0, (s, self.app.now()))
        del self.items[self.KEEP:]
        self.queue_draw()

    def ago(self, t):
        d = (self.app.now() - t).total_seconds()
        return "now" if d < 60 else "%dm" % (d // 60) if d < 3600 else "%dh" % (d // 3600) if d < 86400 else "%dd" % (d // 86400)

    def paint(self, cr):
        T = self.T
        x, y, w, h = self.inner()
        self.title(cr, "Clipboard", x, y, w)
        right = "paused" if self.paused else ("" if not self.items else "%d item%s" % (len(self.items), "" if len(self.items) == 1 else "s"))
        text(cr, right, x, y, w, size=T.s(8.5), color=(1, 0.67, 0.27, 1) if self.paused else T.sub, align="right")
        self.rows = []
        if not self.items:
            self.small(cr, "Copy some text anywhere and it shows up here. Click it later to copy it again.", x, y + T.s(30), w, 10, lines=3)
            return
        yy, rh, gap = y + T.s(28), T.s(38), T.s(4)
        n = min(self.SHOW, max(1, int((y + h - yy + gap) // (rh + gap))))
        for i, (s, t) in enumerate(self.items[:n]):
            rx, rw = x - T.s(6), w + T.s(12)
            self.rows.append((rx, yy, rw, rh))
            flash = i == self.flash
            fill_round(cr, rx, yy, rw, rh, T.s(10), with_alpha(T.accent, 0.28) if flash else with_alpha(T.text[:3], 0.13 if i == self.hot else 0.055))
            st = s.strip()
            mark = (0.35, 0.67, 1) if st.lower().startswith("http") else core.hex_rgb(st) if re.fullmatch(r"#[0-9a-fA-F]{6}", st) \
                else (0.98, 0.75, 0.27) if st.startswith("/") or st.startswith("~/") else T.accent[:3]
            cr.set_source_rgb(*mark)
            cr.arc(rx + T.s(14), yy + rh / 2, T.s(4), 0, 6.2832)
            cr.fill()
            one = re.sub(r"\s+", " ", s.replace("\n", " ⏎ ")).strip()[:200]
            text(cr, "Copied!" if flash else one, rx + T.s(26), yy, rw - T.s(70), size=T.s(9.6), bold=flash, color=T.text, h=rh, valign="middle")
            text(cr, self.ago(t), rx + rw - T.s(44), yy, T.s(36), size=T.s(8), color=T.sub, align="right", h=rh, valign="middle")
            yy += rh + gap

    def _row_at(self, x, y):
        for i, (rx, ry, rw, rh) in enumerate(self.rows):
            if rx <= x <= rx + rw and ry <= y <= ry + rh:
                return i
        return -1

    def on_motion(self, x, y):
        i = self._row_at(x, y)
        if i != self.hot:
            self.hot = i
            self.queue_draw()

    def on_leave(self):
        if self.hot != -1:
            self.hot = -1
            self.queue_draw()

    def on_click(self, x, y, b, e):
        i = self._row_at(x, y)
        if b != 1 or i < 0 or i >= len(self.items):
            return
        s = self.items[i][0]
        self.last = s
        self.cb.set_text(s, -1)
        self.cb.store()
        self.items.pop(i)
        self.items.insert(0, (s, self.app.now()))
        self.flash = 0
        self.queue_draw()
        GLib.timeout_add(950, lambda: (setattr(self, "flash", -1), self.queue_draw(), False)[2])

    def extra_menu(self):
        return [("Resume" if self.paused else "Pause (don't remember copies)", lambda: (setattr(self, "paused", not self.paused), self.queue_draw())),
                ("Clear history", lambda: (self.items.clear(), self.queue_draw()))]
