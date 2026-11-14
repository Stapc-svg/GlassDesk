"""Glass windows drawn in your theme: the search launcher, the welcome screen and "Organize desktop"."""
import math, os, re, webbrowser
import cairo
from gi.repository import Gtk, Gdk, GLib, Gio, Pango, PangoCairo

from . import core
from .glass import Backdrop, text, fill_round, rounded, with_alpha


class GlassForm(Gtk.Window):
    """A borderless window painted like a GlassDesk panel: blurred wallpaper, the theme's tint and glow."""

    def __init__(self, app, w, h, title="GlassDesk"):
        super().__init__(title=title)
        self.app = app
        self.W, self.H = w, h
        vis = self.get_screen().get_rgba_visual()
        if vis is not None:
            self.set_visual(vis)
        self.set_app_paintable(True)
        self.set_decorated(False)
        self.set_default_size(w, h)
        self.set_size_request(w, h)
        self.set_position(Gtk.WindowPosition.CENTER)
        self.set_icon_name("glassdesk")
        self.add_events(Gdk.EventMask.BUTTON_PRESS_MASK | Gdk.EventMask.BUTTON_RELEASE_MASK | Gdk.EventMask.POINTER_MOTION_MASK |
                        Gdk.EventMask.SCROLL_MASK | Gdk.EventMask.SMOOTH_SCROLL_MASK | Gdk.EventMask.KEY_PRESS_MASK |
                        Gdk.EventMask.LEAVE_NOTIFY_MASK)
        self.connect("draw", self._draw)
        self.connect("motion-notify-event", self._motion)
        self.connect("button-press-event", self._press)
        self.connect("button-release-event", self._release)
        self.connect("scroll-event", self._scroll)
        self.connect("key-press-event", self._key)
        self.connect("leave-notify-event", lambda *a: (setattr(self, "mouse", (-1, -1)), self.queue_draw()))
        self.connect("configure-event", lambda *a: self.queue_draw())
        self.mouse = (-1, -1)
        self.hits = []          # [(x, y, w, h, callback)] made while painting
        self.fixed_pos = None   # (x, y) of the window on screen, for screenshots
        self.full_blur = False

    @property
    def T(self):
        return self.app.theme

    def s(self, v):
        return v * self.T.base_scale

    def radius(self):
        return self.s(max(8, min(18, self.app.settings["radius"])))

    # ---- painting
    def _draw(self, _w, cr):
        cr.set_operator(cairo.OPERATOR_SOURCE)
        cr.set_source_rgba(0, 0, 0, 0)
        cr.paint()
        cr.set_operator(cairo.OPERATOR_OVER)
        self.paint_all(cr)
        return True

    def paint_all(self, cr):
        self.hits = []
        w, h = self.W, self.H
        T = self.T
        r = self.radius()
        cr.save()
        rounded(cr, 0, 0, w, h, r)
        cr.clip()
        drew = False
        if not T.solid and not core.PREVIEW_NO_BACKDROP:
            disp = Gdk.Display.get_default()
            mon = disp.get_primary_monitor() or disp.get_monitor(0)
            g = mon.get_geometry()
            pb = Backdrop.get(g.width, g.height, "acrylic" if (T.acrylic or self.full_blur) else "frosted")
            if pb is not None:
                x, y = self.fixed_pos or self.get_position()
                Gdk.cairo_set_source_pixbuf(cr, pb, -(x - g.x), -(y - g.y))
                cr.paint()
                drew = True
        if not drew:
            cr.set_source_rgb(*T.ui_bg)
            cr.paint()
        a = 1.0 if T.solid else (max(0.53 if T.dark else 0.49, min(0.78, T.tint_top[3])) if self.full_blur
                                 else max(0.65 if T.dark else 0.6, T.tint_top[3]))
        g = cairo.LinearGradient(0, 0, 0, h)
        g.add_color_stop_rgba(0, *(T.tint_top[:3] + (a,)))
        g.add_color_stop_rgba(1, *(T.tint_bottom[:3] + (a,)))
        cr.set_source(g)
        cr.paint()
        for glow, gx, gy, rad in ((T.glow, w * 0.92, -h * 0.05, max(w, h) * 0.6), (T.glow2, w * 0.05, h * 1.05, max(w, h) * 0.45)):
            if glow:
                rg = cairo.RadialGradient(gx, gy, 0, gx, gy, rad)
                rg.add_color_stop_rgba(0, *glow)
                rg.add_color_stop_rgba(1, glow[0], glow[1], glow[2], 0)
                cr.set_source(rg)
                cr.paint()
        if not T.flat:
            g = cairo.LinearGradient(0, 0, 0, h * 0.45)
            g.add_color_stop_rgba(0, 1, 1, 1, 0.06 if T.dark else 0.16)
            g.add_color_stop_rgba(1, 1, 1, 1, 0)
            cr.set_source(g)
            cr.paint()
        cr.restore()
        rounded(cr, 0.5, 0.5, w - 1, h - 1, r)
        if T.neon:
            cr.set_source_rgba(T.accent[0], T.accent[1], T.accent[2], 0.8)
        else:
            cr.set_source_rgba(*((1, 1, 1, 0.18) if T.dark else (0, 0, 0, 0.12)))
        cr.set_line_width(1)
        cr.stroke()
        self.paint(cr)

    def paint(self, cr):
        pass

    @property
    def text_c(self):
        return self.T.ui_text + (1,)

    @property
    def sub_c(self):
        return self.T.ui_sub + (1,)

    def chip(self, cr, x, y, w, h, rad, hot=False):
        T = self.T
        rounded(cr, x, y, w, h, rad)
        cr.set_source_rgba(*((1, 1, 1, 0.13 if hot else 0.08) if T.dark else (1, 1, 1, 0.6 if hot else 0.45)))
        cr.fill_preserve()
        cr.set_source_rgba(*((1, 1, 1, 0.2 if hot else 0.11) if T.dark else (0, 0, 0, 0.13 if hot else 0.08)))
        cr.set_line_width(1)
        cr.stroke()

    def accent_fill(self, cr, x, y, w, h, rad, alpha=1.0):
        T = self.T
        rounded(cr, x, y, w, h, rad)
        g = cairo.LinearGradient(x, y, x + w, y + h)
        g.add_color_stop_rgba(0, *(T.accent + (alpha,)))
        g.add_color_stop_rgba(1, *(T.accent2 + (alpha,)))
        cr.set_source(g)
        cr.fill()

    def accent_button(self, cr, x, y, w, h, label, cb):
        self.accent_fill(cr, x, y, w, h, h / 2)
        if self.hot(x, y, w, h):
            fill_round(cr, x, y, w, h, h / 2, (1, 1, 1, 0.15))
        text(cr, label, x, y, w, size=self.s(10.2), bold=True, color=(1, 1, 1, 1), align="center", h=h, valign="middle")
        self.hit(x, y, w, h, cb)

    def plain_button(self, cr, x, y, w, h, label, cb):
        self.chip(cr, x, y, w, h, h / 2, self.hot(x, y, w, h))
        text(cr, label, x, y, w, size=self.s(10.2), bold=True, color=self.text_c, align="center", h=h, valign="middle")
        self.hit(x, y, w, h, cb)

    def close_x(self, cr, x, y, d):
        hot = self.hot(x, y, d, d)
        if hot:
            self.chip(cr, x, y, d, d, d / 2, True)
        cr.set_source_rgba(*(self.text_c if hot else self.sub_c))
        cr.set_line_width(self.s(1.6))
        cr.set_line_cap(cairo.LINE_CAP_ROUND)
        k = d * 0.18
        cx, cy = x + d / 2, y + d / 2
        cr.move_to(cx - k, cy - k); cr.line_to(cx + k, cy + k)
        cr.move_to(cx + k, cy - k); cr.line_to(cx - k, cy + k)
        cr.stroke()
        self.hit(x, y, d, d, self.finish)

    def hot(self, x, y, w, h):
        mx, my = self.mouse
        return x <= mx < x + w and y <= my < y + h

    def hit(self, x, y, w, h, cb):
        self.hits.append((x, y, w, h, cb))

    def finish(self):
        self.destroy()

    # ---- input
    def _motion(self, _w, e):
        self.mouse = (e.x, e.y)
        if getattr(self, "_dragging", None):
            return True
        self.queue_draw()
        win = self.get_window()
        if win is not None:
            on = any(x <= e.x < x + w and y <= e.y < y + h for x, y, w, h, _c in self.hits)
            win.set_cursor(Gdk.Cursor.new_from_name(self.get_display(), "pointer") if on else None)
        return True

    def _press(self, _w, e):
        if e.button == 1 and e.type == Gdk.EventType.BUTTON_PRESS:
            on = any(x <= e.x < x + w and y <= e.y < y + h for x, y, w, h, _c in self.hits)
            if not on and self.draggable(e.x, e.y):
                self.begin_move_drag(e.button, int(e.x_root), int(e.y_root), e.time)
        return True

    def draggable(self, x, y):
        return True

    def _release(self, _w, e):
        if e.button != 1:
            return True
        for x, y, w, h, cb in reversed(self.hits):
            if x <= e.x < x + w and y <= e.y < y + h:
                cb()
                break
        return True

    def _scroll(self, _w, e):
        dy = {Gdk.ScrollDirection.UP: -1, Gdk.ScrollDirection.DOWN: 1}.get(e.direction, 0)
        if e.direction == Gdk.ScrollDirection.SMOOTH:
            dy = e.delta_y
        if dy:
            self.on_scroll(dy)
        return True

    def on_scroll(self, dy):
        pass

    def _key(self, _w, e):
        if e.keyval == Gdk.KEY_Escape:
            self.finish()
            return True
        return self.on_key(e)

    def on_key(self, e):
        return False

    # ---- screenshots
    def render_png(self, path, pos=(0, 0)):
        self.fixed_pos = pos
        surf = cairo.ImageSurface(cairo.FORMAT_ARGB32, self.W, self.H)
        self.paint_all(cairo.Context(surf))
        surf.write_to_png(path)


# ================================================================ search launcher
class Calc:
    """A tiny calculator for the search box: + - * / ^ %, brackets and decimals."""

    @staticmethod
    def eval(q):
        q = q.replace(" ", "").replace("×", "*").replace("÷", "/").replace(",", ".")
        if len(q) < 3 or not re.fullmatch(r"[0-9.+\-*/^%()]+", q) or not re.search(r"[+\-*/^%]", q) or not re.search(r"\d", q):
            return None
        q = re.sub(r"(\d+(?:\.\d+)?)%(?=$|[+\-*/)])", r"(\1/100)", q)
        q = q.replace("^", "**")
        try:
            v = eval(q, {"__builtins__": {}}, {})
            if isinstance(v, (int, float)) and math.isfinite(v):
                return v
        except Exception:
            pass
        return None


class Launcher(GlassForm):
    COLS = 7

    def __init__(self, app):
        super().__init__(app, int(700 * app.theme.base_scale), int(500 * app.theme.base_scale), "Find on desktop")
        self.set_keep_above(True)
        self.set_skip_taskbar_hint(True)
        self.set_type_hint(Gdk.WindowTypeHint.DIALOG)
        self.set_position(Gtk.WindowPosition.CENTER_ALWAYS)
        self.query = ""
        self.items = []
        self.shown = []
        self.acts = []
        self.sel = 0
        self.scroll = 0
        self.caret = True
        self.full_blur = True
        self.apps = set()
        self.filter = 0                 # 0 everything, 1 desktop, 2 apps
        self.dragging = False
        self._press_item = None
        self._dragged = None
        self.connect("drag-data-get", self._drag_get)
        self.connect("drag-end", self._drag_end)
        self.connect("drag-failed", lambda *a: (setattr(self, "_drop_failed", True), False)[1])
        self._had_focus = False
        self.connect("focus-in-event", lambda *a: setattr(self, "_had_focus", True))
        self.connect("focus-out-event", self._focus_out)
        self._blink = GLib.timeout_add(530, self._tick)
        self.connect("destroy", lambda *a: GLib.source_remove(self._blink))

    def _focus_out(self, *a):
        # close when you click somewhere else, but not in the moment it opens (the window manager may bounce focus)
        if not self.dragging and self._had_focus and GLib.get_monotonic_time() / 1e6 - getattr(self, "_opened", 0) > 0.6:
            self.finish()
        return False

    def _tick(self):
        self.caret = not self.caret
        self.queue_draw()
        return True

    @staticmethod
    def installed_apps():
        """Every app in your app menu (its .desktop file), the same list your desktop's menu shows."""
        from .fences import Item
        out = []
        try:
            infos = Gio.AppInfo.get_all()
        except Exception:
            infos = []
        for info in infos:
            try:
                if not info.should_show():
                    continue
                fn = info.get_filename() if hasattr(info, "get_filename") else None
                if fn and os.path.exists(fn):
                    out.append(Item(fn))
            except Exception:
                continue
        return out

    def load_items(self):
        items = sorted(self.app.organizer.all_items(), key=lambda it: it.name.lower())
        names = {it.name.lower() for it in items}
        apps = []
        for it in sorted(self.installed_apps(), key=lambda it: it.name.lower()):
            if it.name.lower() not in names:
                names.add(it.name.lower())
                apps.append(it)
        self.apps = set(apps)
        self.counts = (len(items) + len(apps), len(items), len(apps))
        self.all_items = items + apps
        self.items = self.all_items

    def open(self):
        self.load_items()
        self.set_query("")
        self._opened = GLib.get_monotonic_time() / 1e6
        self.show_all()
        self.present_with_time(Gtk.get_current_event_time() or 0)

    def finish(self):
        self.hide()
        GLib.idle_add(lambda: (self.destroy(), False)[1])

    def set_filter(self, f):
        self.filter = f
        self.items = [i for i in self.all_items if f == 0 or (f == 2) == (i in self.apps)]
        self.set_query(self.query)

    def set_query(self, q):
        self.query = q
        ql = q.strip().lower()
        starts = [i for i in self.items if not ql or i.name.lower().startswith(ql)]
        has = [i for i in self.items if ql and not i.name.lower().startswith(ql) and ql in i.name.lower()]
        loose = []
        if len(ql) >= 2:
            pat = re.compile(".*".join(map(re.escape, ql)))
            loose = [i for i in self.items if i not in starts and i not in has and pat.search(i.name.lower())]
        self.shown = starts + has + loose
        self.sel, self.scroll = 0, 0
        self.build_acts()
        self.queue_draw()

    def build_acts(self):
        self.acts = []
        q = self.query.strip()
        if not q:
            return
        v = Calc.eval(q)
        if v is not None:
            res = ("%.10g" % v) if abs(v) >= 1e15 or (v and abs(v) < 1e-6) else ("{:,.8f}".format(v).rstrip("0").rstrip("."))
            self.acts.append(("=", "= " + res, "Enter to copy", lambda: (Gtk.Clipboard.get(Gdk.SELECTION_CLIPBOARD).set_text(res.replace(",", ""), -1), self.finish())))
        ql = q.lower()
        for look in core.LOOKS:
            if len(self.acts) < 3 and len(ql) >= 3 and (look["name"].lower().startswith(ql) or ("theme " + look["name"].lower()).startswith(ql)):
                self.acts.append(("◐", "Switch to the %s theme" % look["name"], look["desc"],
                                  lambda l=look: (self.finish(), core.apply_look(self.app.settings, l), self.app.settings_changed())))
        cmds = [("settings", "Open Settings", self.app.show_settings), ("organize", "Organize desktop", self.app.show_organize),
                ("hide", "Hide or show all panels", self.app.toggle_all), ("new fence", "Make a new fence", self.app.new_fence_dialog),
                ("dark", "Dark mode", lambda: self._mode("dark")), ("light", "Light mode", lambda: self._mode("light"))]
        for key, title, fn in cmds:
            if len(self.acts) < 3 and len(ql) >= 3 and key.startswith(ql):
                self.acts.append(("›", title, "GlassDesk command", lambda f=fn: (self.finish(), f())))
        if len(self.acts) < 3:
            self.acts.append(("⌕", "Search the web for “%s”" % q, "Enter" if not self.shown else "Ctrl + Enter",
                              lambda: (self.finish(), webbrowser.open("https://www.google.com/search?q=" + __import__("urllib.parse").parse.quote(q)))))

    def _mode(self, m):
        self.app.settings["theme"] = m
        self.app.settings_changed()

    # layout
    def pad(self):
        return self.s(16)

    def search_rect(self):
        p = self.pad()
        return p, p, self.W - 2 * p - self.s(48), self.s(46)

    def act_rect(self, i):
        sx, sy, sw, sh = self.search_rect()
        h = self.s(46)
        return self.pad(), sy + sh + self.s(12) + i * (h + self.s(6)), self.W - 2 * self.pad(), h

    def foot_top(self):
        return self.H - self.s(54)

    def grid_rect(self):
        sx, sy, sw, sh = self.search_rect()
        top = sy + sh + self.s(14) + len(self.acts) * (self.s(46) + self.s(6))
        return self.pad(), top, self.W - 2 * self.pad(), self.foot_top() - top - self.s(4)

    def cell(self):
        gx, gy, gw, gh = self.grid_rect()
        return (gw - self.s(10)) / self.COLS, self.s(96)

    def max_scroll(self):
        gx, gy, gw, gh = self.grid_rect()
        cw, ch = self.cell()
        rows = math.ceil(len(self.shown) / self.COLS)
        return max(0, rows * ch - gh)

    def paint(self, cr):
        T = self.T
        tc, sc = self.text_c, self.sub_c
        sx, sy, sw, sh = self.search_rect()
        self.chip(cr, sx, sy, sw, sh, sh / 2, True)
        # magnifier
        ic, ix, iy = self.s(18), sx + self.s(18), sy + (sh - self.s(18)) / 2
        cr.set_source_rgb(*T.accent)
        cr.set_line_width(self.s(2.2))
        cr.set_line_cap(cairo.LINE_CAP_ROUND)
        cr.arc(ix + ic * 0.36, iy + ic * 0.36, ic * 0.36, 0, 2 * math.pi)
        cr.stroke()
        cr.move_to(ix + ic * 0.64, iy + ic * 0.64); cr.line_to(ix + ic * 0.95, iy + ic * 0.95)
        cr.stroke()
        tx = ix + ic + self.s(14)
        kbd = "Ctrl + Alt + F"
        kw = self.s(96)
        kx = sx + sw - kw - self.s(14)
        self.chip(cr, kx, sy + (sh - self.s(24)) / 2, kw, self.s(24), self.s(7))
        text(cr, kbd, kx, sy + (sh - self.s(24)) / 2, kw, size=self.s(8.6), color=tc, align="center", h=self.s(24), valign="middle")
        if self.query:
            tw, _th = text(cr, self.query, tx, sy, kx - tx - self.s(10), size=self.s(12.5), color=tc, h=sh, valign="middle")
        else:
            text(cr, "Search your desktop and apps…", tx, sy, kx - tx - self.s(10), size=self.s(12.5), color=sc, h=sh, valign="middle")
            tw = 0
        if self.caret:
            cx = min(kx - self.s(10), tx + tw + self.s(2))
            cr.set_source_rgb(*T.accent)
            cr.set_line_width(self.s(1.8))
            cr.move_to(cx, sy + sh * 0.28); cr.line_to(cx, sy + sh * 0.72)
            cr.stroke()
        self.close_x(cr, sx + sw + self.s(10), sy + (sh - self.s(36)) / 2, self.s(36))
        # quick actions
        for i, (glyph, title, hint, fn) in enumerate(self.acts):
            ax, ay, aw, ah = self.act_rect(i)
            first = i == 0 and (not self.shown or glyph == "=")
            if first:
                self.accent_fill(cr, ax, ay, aw, ah, self.s(12), 0.3)
                rounded(cr, ax, ay, aw, ah, self.s(12))
                cr.set_source_rgba(*(T.accent + (0.6,)))
                cr.set_line_width(1.2)
                cr.stroke()
            else:
                self.chip(cr, ax, ay, aw, ah, self.s(12), self.hot(ax, ay, aw, ah))
            gs = self.s(28)
            self.accent_fill(cr, ax + self.s(10), ay + (ah - gs) / 2, gs, gs, self.s(8))
            text(cr, glyph, ax + self.s(10), ay + (ah - gs) / 2, gs, size=self.s(13), bold=True, color=(1, 1, 1, 1), align="center", h=gs, valign="middle")
            hx = ax + aw - self.s(230)
            text(cr, title, ax + self.s(50), ay, hx - ax - self.s(54), size=self.s(13 if glyph == "=" else 10.6), bold=True, color=tc, h=ah, valign="middle")
            text(cr, hint, hx, ay, self.s(218), size=self.s(8.8), color=sc, align="right", h=ah, valign="middle")
            self.hit(ax, ay, aw, ah, fn)
        # grid
        gx, gy, gw, gh = self.grid_rect()
        cw, ch = self.cell()
        cr.save()
        cr.rectangle(gx, gy, gw, gh)
        cr.clip()
        if not self.shown and not self.acts:
            text(cr, "Your desktop is empty" if not self.query else "Nothing on your desktop matches “%s”" % self.query,
                 gx, gy, gw, size=self.s(12), color=sc, align="center", h=gh, valign="middle")
        isz = int(self.s(44))
        for i, it in enumerate(self.shown):
            r, c = divmod(i, self.COLS)
            x0, y0 = gx + c * cw, gy + r * ch - self.scroll
            if y0 + ch < gy or y0 > gy + gh:
                continue
            bx, by, bw, bh = x0 + self.s(3), y0 + self.s(3), cw - self.s(6), ch - self.s(6)
            if i == self.sel:
                self.accent_fill(cr, bx, by, bw, bh, self.s(12), 0.28)
                rounded(cr, bx, by, bw, bh, self.s(12))
                cr.set_source_rgba(*(T.accent + (0.6,)))
                cr.set_line_width(1.2)
                cr.stroke()
            elif self.hot(bx, by, bw, bh) and by >= gy:
                self.chip(cr, bx, by, bw, bh, self.s(12))
            pb = it.icon(isz)
            if pb is not None:
                Gdk.cairo_set_source_pixbuf(cr, pb, x0 + (cw - pb.get_width()) / 2, y0 + self.s(12) + (isz - pb.get_height()) / 2)
                cr.paint()
            else:
                self.accent_fill(cr, x0 + (cw - isz) / 2, y0 + self.s(12), isz, isz, self.s(10))
                text(cr, it.name[:1].upper(), x0 + (cw - isz) / 2, y0 + self.s(12), isz, size=self.s(14), bold=True,
                     color=(1, 1, 1, 1), align="center", h=isz, valign="middle")
            text(cr, it.name, x0 + self.s(6), y0 + self.s(18) + isz, cw - self.s(12), size=self.s(9), color=tc, align="center")
            if by + bh > gy and by < gy + gh:
                self.hit(max(gx, bx), max(gy, by), bw, min(by + bh, gy + gh) - max(gy, by), lambda it=it: self.open_item(it))
        cr.restore()
        ms = self.max_scroll()
        if ms > 0:
            th = max(self.s(30), gh * gh / (gh + ms))
            fill_round(cr, self.W - self.pad() / 2 - self.s(3), gy + (gh - th) * self.scroll / ms, self.s(4), th, self.s(2), tc[:3] + (0.25,))
        # footer
        ft = self.foot_top()
        cr.rectangle(self.pad(), ft, self.W - 2 * self.pad(), 1)
        cr.set_source_rgba(*((1, 1, 1, 0.1) if T.dark else (0, 0, 0, 0.07)))
        cr.fill()
        # filter chips: everything / desktop / installed apps
        counts = getattr(self, "counts", (len(self.items), len(self.items), 0))
        cx = self.pad()
        ch = self.s(30)
        cy = ft + (self.H - ft - ch) / 2
        for i, label in enumerate(("All", "Desktop", "Apps")):
            lab = "%s  %d" % (label, counts[i])
            cw = self.s(16) + len(lab) * self.s(7.2)
            if i == self.filter:
                self.accent_fill(cr, cx, cy, cw, ch, ch / 2, 0.85)
            else:
                self.chip(cr, cx, cy, cw, ch, ch / 2, self.hot(cx, cy, cw, ch))
            text(cr, lab, cx, cy, cw, size=self.s(8.8), color=(1, 1, 1, 1) if i == self.filter else tc, align="center", h=ch, valign="middle")
            self.hit(cx, cy, cw, ch, lambda i=i: self.set_filter(i))
            cx += cw + self.s(6)
        text(cr, "Drag an app onto a fence to add it", cx + self.s(8), ft, self.W - cx - self.s(170), size=self.s(8.8), color=sc,
             h=self.H - ft, valign="middle")
        bw, bh = self.s(140), self.s(34)
        bx, by = self.W - self.pad() - bw, ft + (self.H - ft - bh) / 2
        self.chip(cr, bx, by, bw, bh, self.s(9), self.hot(bx, by, bw, bh))
        text(cr, "⚙  Open Settings", bx, by, bw, size=self.s(9.4), color=tc, align="center", h=bh, valign="middle")
        self.hit(bx, by, bw, bh, lambda: (self.finish(), self.app.show_settings()))

    # ---- drag an app or file out onto a fence
    def _item_at(self, x, y):
        gx, gy, gw, gh = self.grid_rect()
        if not (gx <= x < gx + gw and gy <= y < gy + gh):
            return None
        cw, ch = self.cell()
        c, r = int((x - gx) // cw), int((y - gy + self.scroll) // ch)
        i = r * self.COLS + c
        return self.shown[i] if 0 <= c < self.COLS and 0 <= i < len(self.shown) else None

    def _press(self, w, e):
        if e.button == 1 and e.type == Gdk.EventType.BUTTON_PRESS:
            self._press_item = self._item_at(e.x, e.y)
            self._press_at = (e.x, e.y)
        return super()._press(w, e)

    def _motion(self, w, e):
        it = self._press_item
        if it is not None and not self.dragging and e.state & Gdk.ModifierType.BUTTON1_MASK and \
                (abs(e.x - self._press_at[0]) > 6 or abs(e.y - self._press_at[1]) > 6):
            self._start_drag(it, e)
            return True
        return super()._motion(w, e)

    def _start_drag(self, it, e):
        self.dragging = True
        self._dragged = it
        self._drop_failed = False
        tl = Gtk.TargetList.new([Gtk.TargetEntry.new("text/uri-list", 0, 0)])
        # apps are always copied (a shortcut on your desktop), never moved
        acts = Gdk.DragAction.COPY if it in self.apps else Gdk.DragAction.COPY | Gdk.DragAction.LINK
        ctx = self.drag_begin_with_coordinates(tl, acts, 1, e, int(e.x), int(e.y))
        pb = it.icon(int(self.s(48)))
        if pb is not None and ctx is not None:
            Gtk.drag_set_icon_pixbuf(ctx, pb, pb.get_width() // 2, pb.get_height() // 2)
        # see-through and click-through while you drag, so you can see and reach your fences
        self.set_opacity(0.25)
        try:
            import cairo as _c
            self.input_shape_combine_region(_c.Region())
        except Exception:
            pass

    def _drag_get(self, _w, ctx, data, info, t):
        if self._dragged is not None:
            data.set_uris([Gio.File.new_for_path(self._dragged.path).get_uri()])

    def _drag_end(self, *a):
        self.dragging = False
        self._press_item = None
        if not getattr(self, "_drop_failed", False):
            self.finish()
            return
        self.set_opacity(1)
        try:
            self.input_shape_combine_region(None)
        except Exception:
            pass
        self.present()

    def _release(self, w, e):
        it = self._press_item
        self._press_item = None
        if it is None and self._item_at(e.x, e.y) is not None:
            return True      # the mouse went down somewhere else
        return super()._release(w, e)

    def open_item(self, it, reveal=False):
        if reveal:
            core.show_in_folder(it.path)
        else:
            core.open_path(it.path)
        self.finish()

    def move(self, d):
        if not self.shown:
            return
        self.sel = max(0, min(len(self.shown) - 1, self.sel + d))
        gx, gy, gw, gh = self.grid_rect()
        cw, ch = self.cell()
        top = (self.sel // self.COLS) * ch
        if top < self.scroll:
            self.scroll = top
        elif top + ch > self.scroll + gh:
            self.scroll = top + ch - gh
        self.queue_draw()

    def on_scroll(self, dy):
        self.scroll = max(0, min(self.max_scroll(), self.scroll + dy * self.cell()[1]))
        self.queue_draw()

    def on_key(self, e):
        k = e.keyval
        ctrl = e.state & Gdk.ModifierType.CONTROL_MASK
        if k == Gdk.KEY_BackSpace:
            if ctrl:
                t = self.query.rstrip()
                self.set_query(t[:t.rfind(" ") + 1] if " " in t else "")
            else:
                self.set_query(self.query[:-1])
        elif k in (Gdk.KEY_Return, Gdk.KEY_KP_Enter):
            if ctrl or (self.acts and (not self.shown or self.acts[0][0] == "=")):
                if self.acts:
                    self.acts[0][3]()
            elif self.shown:
                self.open_item(self.shown[self.sel], bool(e.state & Gdk.ModifierType.SHIFT_MASK))
        elif k == Gdk.KEY_Right:
            self.move(1)
        elif k == Gdk.KEY_Left:
            self.move(-1)
        elif k == Gdk.KEY_Down:
            self.move(self.COLS)
        elif k == Gdk.KEY_Up:
            self.move(-self.COLS)
        elif ctrl and k in (Gdk.KEY_v, Gdk.KEY_V):
            t = Gtk.Clipboard.get(Gdk.SELECTION_CLIPBOARD).wait_for_text()
            if t:
                self.set_query(self.query + t.splitlines()[0])
        else:
            ch = Gdk.keyval_to_unicode(k)
            if ch and not ctrl and ch >= 32:
                self.set_query(self.query + chr(ch))
            else:
                return False
        self.caret = True
        return True


# ================================================================ welcome screen
class WelcomeWindow(GlassForm):
    TIPS = [("Fences", "Your files sorted into glass boxes. Drag a fence's edge to resize it."),
            ("Widgets", "Clock, weather, music, to-dos and more. Right-click one for options."),
            ("Quick search", "Press Ctrl + Alt + F to find and open anything on your desktop."),
            ("Themes", "26 looks, from GlassDesk glass to Mica and Acrylic. Settings → Appearance.")]

    def __init__(self, app):
        super().__init__(app, int(640 * app.theme.base_scale), int(500 * app.theme.base_scale), "Welcome to GlassDesk")
        self.set_keep_above(True)

    def paint(self, cr):
        T = self.T
        tc, sc = self.text_c, self.sub_c
        pad = self.s(34)
        ls = self.s(64)
        lx, ly = pad, pad + self.s(6)
        rg = cairo.RadialGradient(lx + ls / 2, ly + ls / 2, 0, lx + ls / 2, ly + ls / 2, ls * 1.3)
        rg.add_color_stop_rgba(0, *(T.accent + (0.35,)))
        rg.add_color_stop_rgba(1, *(T.accent + (0,)))
        cr.set_source(rg)
        cr.paint()
        icon = os.path.join(os.path.dirname(os.path.abspath(__file__)), "icons", "glassdesk.png")
        try:
            from gi.repository import GdkPixbuf
            pb = GdkPixbuf.Pixbuf.new_from_file_at_scale(icon, int(ls), int(ls), True)
            Gdk.cairo_set_source_pixbuf(cr, pb, lx, ly)
            cr.paint()
        except Exception:
            self.accent_fill(cr, lx, ly, ls, ls, self.s(16))
        tx = lx + ls + self.s(18)
        text(cr, "Welcome to GlassDesk", tx, ly - self.s(2), self.W - tx - self.s(60), size=self.s(21), bold=True, color=tc)
        text(cr, "Your desktop, organized in beautiful glass.", tx, ly + self.s(38), self.W - tx - self.s(40), size=self.s(11), color=sc)
        self.close_x(cr, self.W - self.s(50), self.s(14), self.s(36))
        bh = self.s(42)
        by = self.H - pad - bh
        gap = self.s(14)
        top = ly + ls + self.s(30)
        tw = (self.W - 2 * pad - gap) / 2
        th = (by - self.s(26) - top - gap) / 2
        for i, (title, desc) in enumerate(self.TIPS):
            x, y = pad + (i % 2) * (tw + gap), top + (i // 2) * (th + gap)
            self.chip(cr, x, y, tw, th, self.s(14))
            d = self.s(40)
            ix, iy = x + self.s(16), y + self.s(16)
            self.accent_fill(cr, ix, iy, d, d, self.s(11))
            self.tip_icon(cr, i, ix, iy, d)
            text(cr, title, ix + d + self.s(14), y + self.s(14), tw - d - self.s(44), size=self.s(11.4), bold=True, color=tc)
            text(cr, desc, ix + d + self.s(14), y + self.s(37), tw - d - self.s(46), size=self.s(9.4), color=sc, wrap=True, lines=3)
        text(cr, "GlassDesk " + core.VERSION, pad, by, self.s(200), size=self.s(9), color=sc, h=bh, valign="middle")
        sw = self.s(150)
        self.accent_button(cr, self.W - pad - sw, by, sw, bh, "Get started", self.finish)
        ow = self.s(140)
        self.plain_button(cr, self.W - pad - sw - self.s(12) - ow, by, ow, bh, "Open Settings",
                          lambda: (self.finish(), self.app.show_settings()))

    def tip_icon(self, cr, i, x, y, d):
        cx, cy, s = x + d / 2, y + d / 2, d * 0.24
        cr.set_source_rgba(1, 1, 1, 1)
        cr.set_line_width(self.s(2))
        cr.set_line_cap(cairo.LINE_CAP_ROUND)
        if i == 0:
            for j in range(4):
                rounded(cr, cx - s + (j % 2) * s * 1.1, cy - s + (j // 2) * s * 1.1, s * 0.9, s * 0.9, self.s(2.5))
                cr.stroke()
        elif i == 1:
            cr.arc(cx, cy, s, 0, 2 * math.pi); cr.stroke()
            cr.move_to(cx, cy); cr.line_to(cx, cy - s * 0.6); cr.move_to(cx, cy); cr.line_to(cx + s * 0.45, cy + s * 0.2); cr.stroke()
        elif i == 2:
            cr.arc(cx - s * 0.25, cy - s * 0.25, s * 0.75, 0, 2 * math.pi); cr.stroke()
            cr.move_to(cx + s * 0.35, cy + s * 0.35); cr.line_to(cx + s, cy + s); cr.stroke()
        else:
            for a, (ox, oy) in zip((1, 0.8, 0.6), ((-0.5, -0.4), (0.5, -0.4), (0, 0.45))):
                cr.set_source_rgba(1, 1, 1, a)
                cr.arc(cx + ox * s, cy + oy * s, s * 0.52, 0, 2 * math.pi)
                cr.fill()

    def on_key(self, e):
        if e.keyval in (Gdk.KEY_Return, Gdk.KEY_KP_Enter):
            self.finish()
            return True
        return False


# ================================================================ organize desktop
class OrganizeWindow(GlassForm):
    """See where every file goes and tick the ones to sort. Unticked files go to an "Unsorted" fence."""
    UNSORTED = "unsorted"

    def __init__(self, app):
        super().__init__(app, int(900 * app.theme.base_scale), int(640 * app.theme.base_scale), "Organize desktop")
        self.set_keep_above(True)
        self.scroll = 0
        self.content_h = 0
        org = app.organizer
        groups = org.assign()
        self.groups, self.on = [], set()
        by = {}
        for fid in org.fence_ids():
            if org.folder_of(fid) or fid == self.UNSORTED:
                continue
            items = list(groups.get(fid, []))
            g = {"id": fid, "title": org.fence_name(fid), "items": items}
            self.groups.append(g)
            by[fid] = g
            self.on.update(it.path for it in items)
        for it in groups.get(self.UNSORTED, []):
            fid = core.category_of(it.path)
            g = by.get(fid)
            if g is None:
                g = {"id": fid, "title": core.CAT_NAME.get(fid, fid), "items": []}
                self.groups.append(g)
                by[fid] = g
            g["items"].append(it)
        self.groups = [g for g in self.groups if g["items"]]
        self.groups.sort(key=lambda g: -len(g["items"]))

    def draggable(self, x, y):
        return y < self.s(96)

    def total(self):
        return sum(len(g["items"]) for g in self.groups)

    def body(self):
        pad = self.s(26)
        top = self.s(96)
        return pad, top, self.W - 2 * pad, self.H - self.s(66) - top - self.s(8)

    def paint(self, cr):
        T = self.T
        tc, sc = self.text_c, self.sub_c
        pad = self.s(26)
        text(cr, "Organize desktop", pad, self.s(22), self.W - 2 * pad, size=self.s(18), bold=True, color=tc)
        text(cr, "Ticked files go into their fence. Unticked files go to an “Unsorted” fence.", pad, self.s(58),
             self.W - 2 * pad - self.s(60), size=self.s(10.2), color=sc)
        self.close_x(cr, self.W - self.s(50), self.s(16), self.s(36))
        bx, by_, bw, bh = self.body()
        cr.save()
        cr.rectangle(bx, by_, bw, bh)
        cr.clip()
        gap = self.s(16)
        cw = (bw - gap - self.s(8)) / 2
        col_y = [by_, by_]
        tile_w = tile_h = self.s(82)
        per = max(1, int((cw - self.s(24)) // tile_w))
        for g in self.groups:
            col = 0 if col_y[0] <= col_y[1] else 1
            rows = math.ceil(len(g["items"]) / per)
            ch = self.s(56) + rows * tile_h + self.s(12)
            x, y = bx + col * (cw + gap), col_y[col] - self.scroll
            col_y[col] += ch + gap
            if y + ch < by_ or y > by_ + bh:
                continue
            self.chip(cr, x, y, cw, ch, self.s(14))
            n = sum(1 for it in g["items"] if it.path in self.on)
            self.check(cr, x + self.s(16), y + self.s(18), self.s(20), 2 if n == len(g["items"]) else 1 if n else 0, True)
            if y >= by_ - 1:
                self.hit(x, y, cw, self.s(52), lambda g=g: self.toggle_all(g))
            text(cr, g["title"], x + self.s(48), y + self.s(12), cw - self.s(170), size=self.s(10.6), bold=True, color=tc, h=self.s(32), valign="middle")
            pw = self.s(104)
            fill_round(cr, x + cw - pw - self.s(16), y + self.s(16), pw, self.s(24), self.s(12), T.accent + (0.25,))
            text(cr, "%d / %d selected" % (n, len(g["items"])), x + cw - pw - self.s(16), y + self.s(16), pw, size=self.s(8.4),
                 color=tc, align="center", h=self.s(24), valign="middle")
            gx = x + (cw - per * tile_w) / 2
            ip = int(self.s(34))
            for i, it in enumerate(g["items"]):
                tx = gx + (i % per) * tile_w
                ty = y + self.s(52) + (i // per) * tile_h
                on = it.path in self.on
                if on:
                    fill_round(cr, tx + 3, ty + 3, tile_w - 6, tile_h - 6, self.s(10), T.accent + (0.15,))
                if self.hot(tx, ty, tile_w, tile_h):
                    fill_round(cr, tx + 3, ty + 3, tile_w - 6, tile_h - 6, self.s(10), (1, 1, 1, 0.08) if T.dark else (0, 0, 0, 0.05))
                pb = it.icon(ip)
                if pb is not None:
                    Gdk.cairo_set_source_pixbuf(cr, pb, tx + (tile_w - pb.get_width()) / 2, ty + self.s(10))
                    cr.paint_with_alpha(1 if on else 0.4)
                text(cr, it.name, tx + self.s(4), ty + self.s(14) + ip, tile_w - self.s(8), size=self.s(8.2),
                     color=tc if on else sc, align="center", wrap=True, lines=2)
                self.check(cr, tx + tile_w - self.s(22), ty + self.s(5), self.s(16), 2 if on else 0, False)
                if ty + tile_h > by_ and ty < by_ + bh:
                    self.hit(tx, max(ty, by_), tile_w, min(ty + tile_h, by_ + bh) - max(ty, by_), lambda it=it: self.toggle(it))
        self.content_h = max(col_y) - by_
        cr.restore()
        ms = max(0, self.content_h - bh)
        if ms:
            th = max(self.s(30), bh * bh / self.content_h)
            fill_round(cr, self.W - pad / 2 - self.s(3), by_ + (bh - th) * self.scroll / ms, self.s(4), th, self.s(2), tc[:3] + (0.25,))
        ft = self.H - self.s(66)
        cr.rectangle(pad, ft, self.W - 2 * pad, 1)
        cr.set_source_rgba(*((1, 1, 1, 0.1) if T.dark else (0, 0, 0, 0.07)))
        cr.fill()
        tot = self.total()
        text(cr, "%d fences  ·  %d files  ·  %d stay unsorted" % (len(self.groups), tot, tot - len(self.on)), pad, ft, self.W / 2,
             size=self.s(9.4), color=sc, h=self.H - ft, valign="middle")
        bh2 = self.s(40)
        y2 = ft + (self.H - ft - bh2) / 2
        gw = self.s(210)
        self.accent_button(cr, self.W - pad - gw, y2, gw, bh2, "Start organizing · %d" % len(self.on), self.apply)
        self.plain_button(cr, self.W - pad - gw - self.s(122), y2, self.s(110), bh2, "Cancel", self.finish)

    def check(self, cr, x, y, d, state, square):
        T = self.T
        rounded(cr, x, y, d, d, self.s(5) if square else d / 2)
        if state:
            g = cairo.LinearGradient(x, y, x + d, y + d)
            g.add_color_stop_rgb(0, *T.accent)
            g.add_color_stop_rgb(1, *T.accent2)
            cr.set_source(g)
            cr.fill()
        else:
            cr.set_source_rgba(*((0, 0, 0, 0.16) if T.dark else (1, 1, 1, 0.6)))
            cr.fill_preserve()
            cr.set_source_rgba(*(self.text_c[:3] + (0.45,)))
            cr.set_line_width(self.s(1.4))
            cr.stroke()
        cr.set_source_rgba(1, 1, 1, 1)
        cr.set_line_width(self.s(1.8))
        cr.set_line_cap(cairo.LINE_CAP_ROUND)
        if state == 2:
            cr.move_to(x + d * 0.27, y + d * 0.52); cr.line_to(x + d * 0.44, y + d * 0.68); cr.line_to(x + d * 0.74, y + d * 0.34)
            cr.stroke()
        elif state == 1:
            cr.move_to(x + d * 0.3, y + d / 2); cr.line_to(x + d * 0.7, y + d / 2)
            cr.stroke()

    def toggle(self, it):
        self.on.symmetric_difference_update({it.path})
        self.queue_draw()

    def toggle_all(self, g):
        paths = {it.path for it in g["items"]}
        if paths <= self.on:
            self.on -= paths
        else:
            self.on |= paths
        self.queue_draw()

    def on_scroll(self, dy):
        bh = self.body()[3]
        self.scroll = max(0, min(max(0, self.content_h - bh), self.scroll + dy * self.s(80)))
        self.queue_draw()

    def apply(self):
        S = self.app.settings
        U = self.UNSORTED
        for g in self.groups:
            for it in g["items"]:
                if it.path in self.on:
                    if S["pins"].get(it.name) == U:
                        S["pins"].pop(it.name, None)
                else:
                    S["pins"][it.name] = U
        anything = U in S["pins"].values()
        has = any(c["id"] == U for c in S["custom_fences"])
        if anything and not has:
            S["custom_fences"].append({"id": U, "name": "Unsorted"})
        if not anything and has:
            S["custom_fences"] = [c for c in S["custom_fences"] if c["id"] != U]
        self.finish()
        self.app.settings_changed()
