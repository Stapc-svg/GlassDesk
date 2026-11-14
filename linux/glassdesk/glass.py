"""Glass panels: transparent, undecorated windows that sit on the desktop and paint
a blurred copy of the wallpaper behind themselves (our own blur, so it works on
every desktop - GNOME, KDE, Cinnamon, XFCE, MATE...)."""
import math, os
import cairo
from gi.repository import Gtk, Gdk, GdkPixbuf, GLib, Pango, PangoCairo

from . import core


# ------------------------------------------------------------------ drawing helpers
def rounded(cr, x, y, w, h, r):
    r = max(0, min(r, w / 2, h / 2))
    cr.new_sub_path()
    cr.arc(x + w - r, y + r, r, -math.pi / 2, 0)
    cr.arc(x + w - r, y + h - r, r, 0, math.pi / 2)
    cr.arc(x + r, y + h - r, r, math.pi / 2, math.pi)
    cr.arc(x + r, y + r, r, math.pi, 3 * math.pi / 2)
    cr.close_path()


def fill_round(cr, x, y, w, h, r, rgba):
    rounded(cr, x, y, w, h, r)
    cr.set_source_rgba(*rgba)
    cr.fill()


def with_alpha(rgb, a):
    return (rgb[0], rgb[1], rgb[2], a)


def text(cr, s, x, y, w=None, size=11, bold=False, color=(1, 1, 1, 1), align="left", wrap=False,
         lines=1, family=None, h=None, valign="top", weight=None, shadow=False):
    """Draws text with Pango and returns (width, height) actually used."""
    lay = PangoCairo.create_layout(cr)
    fd = Pango.FontDescription.from_string(family or "Inter, Cantarell, Ubuntu, Noto Sans, DejaVu Sans")
    fd.set_absolute_size(size * Pango.SCALE * 1.333)
    if weight is not None:
        fd.set_weight(weight)
    elif bold:
        fd.set_weight(Pango.Weight.BOLD)
    lay.set_font_description(fd)
    lay.set_text(s, -1)
    if w is not None:
        lay.set_width(int(max(1, w) * Pango.SCALE))
        if wrap:
            lay.set_wrap(Pango.WrapMode.WORD_CHAR)
            if lines > 1:
                lay.set_height(-lines)
        lay.set_ellipsize(Pango.EllipsizeMode.END)
        lay.set_alignment({"left": Pango.Alignment.LEFT, "center": Pango.Alignment.CENTER, "right": Pango.Alignment.RIGHT}[align])
    tw, th = lay.get_pixel_size()
    if h is not None and valign == "middle":
        y = y + (h - th) / 2
    elif h is not None and valign == "bottom":
        y = y + h - th
    if shadow:
        cr.move_to(x, y + 1)
        cr.set_source_rgba(0, 0, 0, 0.45)
        PangoCairo.show_layout(cr, lay)
    cr.move_to(x, y)
    cr.set_source_rgba(*color)
    PangoCairo.show_layout(cr, lay)
    cr.new_path()            # don't leave a current point for the next arc to draw a line from
    return tw, th


def popup(widget, items, event=None):
    """items: list of (label, callback) | None (separator) | (label, [sub items]) | (label, cb, checked)."""
    menu = Gtk.Menu()
    def build(m, its):
        for it in its:
            if it is None:
                m.append(Gtk.SeparatorMenuItem())
                continue
            label, what = it[0], it[1]
            if isinstance(what, list):
                mi = Gtk.MenuItem(label=label)
                sub = Gtk.Menu()
                build(sub, what)
                mi.set_submenu(sub)
            elif len(it) > 2:
                mi = Gtk.CheckMenuItem(label=label)
                mi.set_active(bool(it[2]))
                mi.connect("activate", lambda _m, f=what: f())
            else:
                mi = Gtk.MenuItem(label=label)
                if what is None:
                    mi.set_sensitive(False)
                else:
                    mi.connect("activate", lambda _m, f=what: f())
            m.append(mi)
    build(menu, items)
    menu.show_all()
    menu.attach_to_widget(widget, None)
    menu.popup_at_pointer(event)
    widget._menu = menu      # keep it alive


# ------------------------------------------------------------------ wallpaper blur
class Backdrop:
    """A blurred copy of the wallpaper for each monitor, made once and cached."""
    _cache = {}
    _path = None
    _mtime = 0

    @classmethod
    def check_changed(cls):
        p = core.wallpaper_path()
        m = os.path.getmtime(p) if p and os.path.exists(p) else 0
        if p != cls._path or m != cls._mtime:
            cls._path, cls._mtime = p, m
            cls._cache.clear()
            return True
        return False

    @classmethod
    def get(cls, mw, mh, style):
        if cls._path is None and cls._mtime == 0:
            cls.check_changed()
        key = (mw, mh, style)
        if key in cls._cache:
            return cls._cache[key]
        pb = None
        try:
            if cls._path:
                src = GdkPixbuf.Pixbuf.new_from_file(cls._path)
                # cover-fit the wallpaper to the monitor, like desktops do by default
                sw, sh = src.get_width(), src.get_height()
                k = max(mw / sw, mh / sh)
                cw, ch = int(sw * k + 0.5), int(sh * k + 0.5)
                full = src.scale_simple(max(1, cw), max(1, ch), GdkPixbuf.InterpType.BILINEAR)
                ox, oy = (cw - mw) // 2, (ch - mh) // 2
                pb = full.new_subpixbuf(max(0, ox), max(0, oy), min(mw, cw), min(mh, ch)).copy()
                strength = {"acrylic": 44, "frosted": 22, "blur": 12, "clear": 6, "transparent": 0}.get(style, 18)
                if strength:
                    # cheap, smooth blur: shrink a lot, then grow back in two smooth steps
                    small = pb.scale_simple(max(1, mw // strength), max(1, mh // strength), GdkPixbuf.InterpType.BILINEAR)
                    mid = small.scale_simple(max(1, mw // 3), max(1, mh // 3), GdkPixbuf.InterpType.BILINEAR)
                    pb = mid.scale_simple(mw, mh, GdkPixbuf.InterpType.BILINEAR)
        except Exception as e:
            core.log("backdrop: %s" % e)
            pb = None
        cls._cache[key] = pb
        return pb


# ------------------------------------------------------------------ the base window
class GlassWindow(Gtk.Window):
    MARGIN = 12            # room around the panel for its soft shadow

    def __init__(self, app, key, base_w, base_h, resizable=False):
        super().__init__(type=Gtk.WindowType.TOPLEVEL)
        self.app, self.key = app, key
        self.base_w, self.base_h = base_w, base_h
        self.resizable_panel = resizable
        self.wanted = True
        self.zoom = 1.0
        self._zt = None
        self.drop_hint = False
        self.tab_rects = []
        self._anim = None            # (start time, direction) of the switch slide
        self._moving_id = 0
        self.hover = None
        self._menu = None
        screen = self.get_screen()
        vis = screen.get_rgba_visual()
        if vis is not None:
            self.set_visual(vis)
        self.set_app_paintable(True)
        self.set_decorated(False)
        self.set_skip_taskbar_hint(True)
        self.set_skip_pager_hint(True)
        self.set_type_hint(Gdk.WindowTypeHint.UTILITY)
        self.set_keep_below(True)
        self.stick()
        self.set_accept_focus(False)
        self.set_focus_on_map(False)
        self.set_title("GlassDesk")
        self.set_wmclass("glassdesk", "GlassDesk")
        self.add_events(Gdk.EventMask.BUTTON_PRESS_MASK | Gdk.EventMask.BUTTON_RELEASE_MASK |
                        Gdk.EventMask.POINTER_MOTION_MASK | Gdk.EventMask.LEAVE_NOTIFY_MASK |
                        Gdk.EventMask.ENTER_NOTIFY_MASK | Gdk.EventMask.SCROLL_MASK | Gdk.EventMask.SMOOTH_SCROLL_MASK)
        self.connect("draw", self._on_draw)
        self.connect("button-press-event", self._on_press)
        self.connect("button-release-event", self._on_release)
        self.connect("motion-notify-event", self._on_motion)
        self.connect("leave-notify-event", self._on_leave)
        self.connect("enter-notify-event", self._on_enter)
        self.connect("scroll-event", self._on_scroll)
        self.connect("configure-event", self._on_configure)
        self.connect("delete-event", lambda *a: True)
        self._pos = (0, 0)
        self._press = None
        self.apply_size()

    # ---- geometry
    @property
    def T(self):
        t = self.app.theme
        if self.zoom == 1.0:
            return t
        if self._zt is None or self._zt[0] is not t or self._zt[1] != self.zoom:
            self._zt = (t, self.zoom, t.zoomed(self.zoom))
        return self._zt[2]

    @property
    def S(self):
        return self.app.settings

    def m(self):
        return int(self.app.theme.s(self.MARGIN))

    zoomable = False      # widgets: resizing makes everything inside bigger or smaller

    def panel_size(self):
        saved = self.S["positions"].get(self.key)
        if self.resizable_panel and saved and len(saved) == 4 and saved[2] > 40:
            return saved[2], saved[3]
        return int(self.T.s(self.base_w)), int(self.T.s(self.base_h))

    def apply_size(self):
        pw, ph = self.panel_size()
        ph = self.content_height(ph)
        m = self.m()
        self.resize(pw + 2 * m, ph + 2 * m)
        self.set_size_request(pw + 2 * m, ph + 2 * m)
        self.queue_draw()

    def content_height(self, ph):
        return ph

    def panel_rect(self):
        m = self.m()
        a = self.get_allocation()
        return m, m, a.width - 2 * m, a.height - 2 * m

    def inner(self):
        x, y, w, h = self.panel_rect()
        p = self.T.s(14)
        return x + p, y + p, w - 2 * p, h - 2 * p

    def place(self, x, y):
        m = self.m()
        self.move(int(x) - m, int(y) - m)
        self._pos = (int(x) - m, int(y) - m)

    def panel_origin(self):
        x, y = self.get_position()
        m = self.m()
        return x + m, y + m

    # ---- drawing
    def _on_draw(self, _w, cr):
        cr.set_operator(cairo.OPERATOR_SOURCE)
        cr.set_source_rgba(0, 0, 0, 0)
        cr.paint()
        cr.set_operator(cairo.OPERATOR_OVER)
        x, y, w, h = self.panel_rect()
        if w <= 0 or h <= 0:
            return True
        self.draw_chrome(cr, x, y, w, h)
        cr.save()
        rounded(cr, x, y, w, h, self.T.s(self.T.radius))
        cr.clip()
        try:
            t = self._anim_progress()
            if t >= 1:
                self.paint(cr)
            else:
                # switching inside a stack: the new content slides in and fades up
                e = 1 - (1 - t) ** 4
                top = self.anim_top()
                cr.save()
                cr.rectangle(x, y, w, top - y)
                cr.clip()
                self.paint(cr)
                cr.restore()
                cr.save()
                cr.rectangle(x, top, w, y + h - top)
                cr.clip()
                cr.translate(0, (1 - e) * self.T.s(28))     # the new content rises up into place
                cr.push_group()
                self.paint(cr)
                cr.pop_group_to_source()
                cr.paint_with_alpha(0.15 + 0.85 * e)
                cr.restore()
            self.paint_overlay(cr)
        except Exception as e:
            core.log("paint %s: %s" % (self.key, e))
        cr.restore()
        return True

    # ---- stacks: tabs, the drop hint and the switch animation
    def anim_top(self):
        return self.panel_rect()[1]

    def _anim_progress(self):
        if not self._anim:
            return 1
        t = (GLib.get_monotonic_time() / 1e6 - self._anim[0]) / 0.32
        if t >= 1:
            self._anim = None
            return 1
        return t

    def play_enter(self, direction, came_from=None):
        self._anim = (GLib.get_monotonic_time() / 1e6, 1 if direction >= 0 else -1, came_from)
        def tick(clock):
            self.queue_draw()
            return self._anim is not None
        # redraw in step with the screen's refresh, not on a fixed timer: no stutter
        self.add_tick_callback(lambda w, clock: tick(clock))

    def draw_tabs(self, cr, x, y, w, h):
        """One tab per panel in this stack; returns False when it isn't stacked."""
        self.tab_rects = []
        members = self.app.group_members(self.key)
        if len(members) < 2:
            return False
        T = self.T
        th = min(h - T.s(6), T.s(24))
        ty = y + (h - th) / 2
        sizes = []
        for key, name in members:
            lay = PangoCairo.create_layout(cr)
            fd = Pango.FontDescription.from_string(T.font)
            fd.set_absolute_size(T.s(9.4) * Pango.SCALE * 1.333)
            fd.set_weight(Pango.Weight.BOLD)
            lay.set_font_description(fd)
            lay.set_text(name, -1)
            sizes.append(min(T.s(150), lay.get_pixel_size()[0] + T.s(20)))
        want = sum(sizes) + T.s(6) * len(sizes)
        k = min(1.0, w / want) if want > 0 else 1
        cx = x
        cur_r = from_r = None
        came = self._anim[2] if self._anim else None
        for (key, name), tw in zip(members, sizes):
            tw *= k
            fill_round(cr, cx, ty, tw, th, th / 2, T.hover)
            if key == self.key:
                cur_r = (cx, tw)
            if key == came:
                from_r = (cx, tw)
            self.tab_rects.append(((cx, ty, tw, th), key))
            cx += tw + T.s(6) * k
        # the highlight glides from the tab you came from to the one you picked
        if cur_r:
            t = self._anim_progress()
            if t < 1 and from_r:
                e = 1 - (1 - t) ** 4
                cur_r = (from_r[0] + (cur_r[0] - from_r[0]) * e, from_r[1] + (cur_r[1] - from_r[1]) * e)
            fill_round(cr, cur_r[0], ty, cur_r[1], th, th / 2, with_alpha(T.accent, 0.42 if T.dark else 0.32))
        for ((tx, tyy, tw, thh), key), (k2, name) in zip(self.tab_rects, members):
            text(cr, name, tx + T.s(6), tyy, tw - T.s(12), size=T.s(9.4), bold=True,
                 color=T.text if key == self.key else T.sub, align="center", h=thh, valign="middle")
        return True

    def paint_overlay(self, cr):
        if self.drop_hint:
            T = self.T
            x, y, w, h = self.panel_rect()
            rounded(cr, x + 1.5, y + 1.5, w - 3, h - 3, T.s(T.radius) - 1.5)
            cr.set_source_rgba(T.accent[0], T.accent[1], T.accent[2], 0.2)
            cr.fill_preserve()
            cr.set_source_rgba(T.accent[0], T.accent[1], T.accent[2], 1)
            cr.set_line_width(T.s(3))
            cr.stroke()
            bw, bh = T.s(150), T.s(32)
            fill_round(cr, x + (w - bw) / 2, y + (h - bh) / 2, bw, bh, bh / 2, with_alpha(T.accent, 1))
            text(cr, "Drop to stack", x + (w - bw) / 2, y + (h - bh) / 2, bw, size=T.s(10), bold=True,
                 color=(1, 1, 1, 1), align="center", h=bh, valign="middle")

    def tab_at(self, x, y):
        for (tx, ty, tw, th), key in self.tab_rects:
            if tx <= x < tx + tw and ty <= y < ty + th:
                return key
        return None

    def draw_chrome(self, cr, x, y, w, h):
        T = self.T
        r = T.s(T.radius)
        # soft shadow
        m = self.m()
        for i in range(m, 0, -2):
            rounded(cr, x - i * 0.5, y - i * 0.3, w + i, h + i, r + i * 0.5)
            cr.set_source_rgba(0, 0, 0, 0.045 * (1 - i / (m + 1)))
            cr.fill()
        cr.save()
        rounded(cr, x, y, w, h, r)
        cr.clip()
        # 1) the blurred wallpaper that is right behind this panel
        drew = False
        win = self.get_window()
        if win is not None and not core.PREVIEW_NO_BACKDROP and not T.solid:
            disp = Gdk.Display.get_default()
            mon = disp.get_monitor_at_window(win) if disp else None
            if mon is not None:
                g = mon.get_geometry()
                pb = Backdrop.get(g.width, g.height, "acrylic" if T.acrylic else T.style)
                if pb is not None:
                    wx, wy = self.get_position()
                    Gdk.cairo_set_source_pixbuf(cr, pb, -(wx - g.x), -(wy - g.y))
                    cr.paint()
                    drew = True
        if not drew and not T.solid:
            cr.set_source_rgba(0.12, 0.13, 0.18, 0.55 if T.dark else 0.25)
            cr.paint()
        # 2) the theme's glass color, top to bottom, and its glow
        g = cairo.LinearGradient(0, y, 0, y + h)
        g.add_color_stop_rgba(0, *T.tint_top)
        g.add_color_stop_rgba(1, *T.tint_bottom)
        cr.set_source(g)
        cr.paint()
        big = max(w, h)
        for glow, gx, gy, rad in ((T.glow, x + w * 0.92, y - h * 0.05, big * 0.75), (T.glow2, x - w * 0.05, y + h * 1.05, big * 0.5)):
            if glow:
                rg = cairo.RadialGradient(gx, gy, 0, gx, gy, rad)
                rg.add_color_stop_rgba(0, *glow)
                rg.add_color_stop_rgba(1, glow[0], glow[1], glow[2], 0)
                cr.set_source(rg)
                cr.paint()
        if self.panel_color():
            c = self.panel_color()
            cr.set_source_rgba(c[0], c[1], c[2], 0.16)
            cr.paint()
        if T.flat:
            cr.restore()
            rounded(cr, x + 0.5, y + 0.5, w - 1, h - 1, r)
            cr.set_source_rgba(*((1, 1, 1, 0.12) if T.dark else (0, 0, 0, 0.08)))
            cr.set_line_width(1)
            cr.stroke()
            return
        if T.style == "frosted":
            cr.set_source_rgba(1, 1, 1, 0.045 if T.dark else 0.12)
            cr.paint()
        # 3) diagonal sheen
        g = cairo.LinearGradient(x, y, x + w * 0.9, y + h)
        g.add_color_stop_rgba(0, 1, 1, 1, 0.13 if T.dark else 0.22)
        g.add_color_stop_rgba(0.42, 1, 1, 1, 0.0)
        g.add_color_stop_rgba(1, 1, 1, 1, 0.04)
        cr.set_source(g)
        cr.paint()
        # 4) inner top glow
        g = cairo.LinearGradient(0, y, 0, y + T.s(28))
        g.add_color_stop_rgba(0, 1, 1, 1, 0.09)
        g.add_color_stop_rgba(1, 1, 1, 1, 0)
        cr.set_source(g)
        cr.paint()
        cr.restore()
        # 5) light-catching rim
        rounded(cr, x + 0.5, y + 0.5, w - 1, h - 1, r)
        g = cairo.LinearGradient(x, y, x + w, y + h)
        g.add_color_stop_rgba(0, 1, 1, 1, 0.55)
        g.add_color_stop_rgba(0.35, 1, 1, 1, 0.12)
        g.add_color_stop_rgba(0.7, 1, 1, 1, 0.08)
        g.add_color_stop_rgba(1, 1, 1, 1, 0.30)
        cr.set_source(g)
        cr.set_line_width(1.2)
        cr.stroke()
        if T.neon:
            rounded(cr, x + 1, y + 1, w - 2, h - 2, r)
            g = cairo.LinearGradient(x, y, x + w, y + h)
            g.add_color_stop_rgba(0, T.accent[0], T.accent[1], T.accent[2], 0.95)
            c2 = T.glow2 or T.accent
            g.add_color_stop_rgba(1, c2[0], c2[1], c2[2], 0.95)
            cr.set_source(g)
            cr.set_line_width(T.s(1.8))
            cr.stroke()

    def panel_color(self):
        return None

    def paint(self, cr):
        pass

    def title(self, cr, s, x, y, w):
        text(cr, s.upper(), x, y, w, size=self.T.s(8.2), bold=True, color=self.T.sub)

    def bar(self, cr, x, y, w, h, frac, color=None):
        fill_round(cr, x, y, w, h, h / 2, self.T.track)
        frac = max(0.0, min(1.0, frac))
        if frac > 0:
            fill_round(cr, x, y, max(h, w * frac), h, h / 2, color or with_alpha(self.T.accent, 1))

    # ---- input
    def is_drag_area(self, x, y):
        return True

    def on_click(self, x, y, button, event):
        pass

    def on_double_click(self, x, y):
        pass

    def on_motion(self, x, y):
        pass

    def on_scroll(self, dy):
        pass

    def menu_items(self):
        return []

    def _in_resize_corner(self, x, y):
        return self.resize_zone(x, y) is not None

    def resize_zone(self, x, y):
        """Which edge a drag here would resize: the right edge, the bottom edge or the corner."""
        if not (self.resizable_panel or self.zoomable):
            return None
        px, py, pw, ph = self.panel_rect()
        c, e = self.app.theme.s(20), self.app.theme.s(7)
        if not (px <= x <= px + pw + 1 and py <= y <= py + ph + 1):
            return None
        right, bottom = x >= px + pw - e, y >= py + ph - e
        if (x > px + pw - c and y > py + ph - c) or (right and bottom):
            return Gdk.WindowEdge.SOUTH_EAST
        if self.zoomable:
            return None          # widgets keep their shape: corner only
        if right:
            return Gdk.WindowEdge.EAST
        if bottom:
            return Gdk.WindowEdge.SOUTH
        return None

    def _on_press(self, _w, e):
        if e.type == Gdk.EventType._2BUTTON_PRESS:
            if e.button == 1:
                self._press = None
                self.on_double_click(e.x, e.y)
            return True
        if e.type != Gdk.EventType.BUTTON_PRESS:
            return True
        if e.button == 3:
            items = self.menu_items()
            if items:
                popup(self, items, e)
            return True
        if e.button == 1:
            zone = self.resize_zone(e.x, e.y)
            if not self.S["lock_positions"] and zone is not None:
                self.begin_resize_drag(zone, e.button, int(e.x_root), int(e.y_root), e.time)
                return True
            self._press = (e.x, e.y, e.x_root, e.y_root, e.time, e.button)
        return True

    def _on_motion(self, _w, e):
        if self._press is not None and (e.state & Gdk.ModifierType.BUTTON1_MASK):
            px, py, rx, ry, t, b = self._press
            if abs(e.x_root - rx) + abs(e.y_root - ry) > 6:
                self._press = None
                if self.is_drag_area(px, py) and not self.S["lock_positions"]:
                    self.begin_move_drag(b, int(rx), int(ry), t)
                    self._watch_move()
                else:
                    self.on_drag_start(px, py, e)
                return True
        self.on_motion(e.x, e.y)
        if (self.resizable_panel or self.zoomable) and not self.S["lock_positions"]:
            win = self.get_window()
            if win is not None:
                z = self.resize_zone(e.x, e.y)
                cur = {Gdk.WindowEdge.SOUTH_EAST: "se-resize", Gdk.WindowEdge.EAST: "e-resize", Gdk.WindowEdge.SOUTH: "s-resize"}.get(z)
                win.set_cursor(Gdk.Cursor.new_from_name(self.get_display(), cur) if cur else None)
        return True

    def on_drag_start(self, x, y, event):
        pass

    def _watch_move(self):
        """While the window manager moves this panel, light up the panel it would stack onto."""
        if self._moving_id:
            return
        disp = Gdk.Display.get_default()
        dev = disp.get_default_seat().get_pointer()
        root = Gdk.get_default_root_window()
        def poll():
            _w, x, y, mask = root.get_device_position(dev)
            if mask & Gdk.ModifierType.BUTTON1_MASK:
                self.app.drag_over(self, x, y)
                return True
            self._moving_id = 0
            self.app.drag_ended(self, x, y)
            return False
        self._moving_id = GLib.timeout_add(80, poll)

    def _on_release(self, _w, e):
        if self._press is not None and e.button == 1:
            px, py = self._press[0], self._press[1]
            self._press = None
            key = self.tab_at(px, py)
            if key is not None:
                if key != self.key:
                    self.app.switch_to(self, key)
                return True
            self.on_click(px, py, 1, e)
        return True

    def _on_leave(self, _w, e):
        if e.detail != Gdk.NotifyType.INFERIOR:
            self.hover = None
            self.on_leave()
            self.queue_draw()
        return False

    def _on_enter(self, _w, e):
        self.on_enter()
        return False

    def on_enter(self):
        pass

    def on_leave(self):
        pass

    def _on_scroll(self, _w, e):
        dy = 0
        if e.direction == Gdk.ScrollDirection.UP:
            dy = -1
        elif e.direction == Gdk.ScrollDirection.DOWN:
            dy = 1
        elif e.direction == Gdk.ScrollDirection.SMOOTH:
            dy = e.delta_y
        if dy:
            self.on_scroll(dy)
        return True

    def _on_configure(self, _w, e):
        if (e.x, e.y) != self._pos:
            self._pos = (e.x, e.y)
            self.queue_draw()           # the wallpaper behind us changed
        m = self.m()
        if self.zoomable and self.get_mapped():
            # resized from the corner: zoom everything inside to fit the new width
            base = self.app.theme.s(self.base_w)
            z = round(max(0.6, min(2.2, (e.width - 2 * m) / base)) / 0.02) * 0.02 if base else 1.0
            group_w = self.app.group_size(self.key)
            if not group_w and abs(z - self.zoom) > 0.011:
                self.zoom = z
                pct = int(round(z * 100))
                if pct == 100:
                    self.S["zoom"].pop(self.key, None)
                else:
                    self.S["zoom"][self.key] = pct
                self.S.save()
                if getattr(self, "_snap_id", 0):
                    GLib.source_remove(self._snap_id)
                self._snap_id = GLib.timeout_add(400, lambda: (setattr(self, "_snap_id", 0), self.apply_size(), False)[2])
        old = self.S["positions"].get(self.key)
        nw, nh = e.width - 2 * m, e.height - 2 * m
        new = [e.x + m, e.y + m] + ([nw, nh] if self.resizable_panel else [])
        if self.resizable_panel and old and len(old) == 4 and self.collapsed_height_override():
            new[3] = old[3]
        if old != new and self.get_mapped():
            self.S["positions"][self.key] = new
            self.S.save()
        return False

    def collapsed_height_override(self):
        return False

    def redraw(self):
        self.queue_draw()
