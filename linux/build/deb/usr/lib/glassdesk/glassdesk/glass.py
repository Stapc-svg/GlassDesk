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
                strength = {"frosted": 22, "blur": 12, "clear": 6, "transparent": 0}.get(style, 18)
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
        return self.app.theme

    @property
    def S(self):
        return self.app.settings

    def m(self):
        return int(self.T.s(self.MARGIN))

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
            self.paint(cr)
        except Exception as e:
            core.log("paint %s: %s" % (self.key, e))
        cr.restore()
        return True

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
        if win is not None and not core.PREVIEW_NO_BACKDROP:
            disp = Gdk.Display.get_default()
            mon = disp.get_monitor_at_window(win) if disp else None
            if mon is not None:
                g = mon.get_geometry()
                pb = Backdrop.get(g.width, g.height, T.style)
                if pb is not None:
                    wx, wy = self.get_position()
                    Gdk.cairo_set_source_pixbuf(cr, pb, -(wx - g.x), -(wy - g.y))
                    cr.paint()
                    drew = True
        if not drew:
            cr.set_source_rgba(0.12, 0.13, 0.18, 0.55 if T.dark else 0.25)
            cr.paint()
        # 2) tint
        cr.set_source_rgba(*T.tint)
        cr.paint()
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
        if not self.resizable_panel:
            return False
        px, py, pw, ph = self.panel_rect()
        c = self.T.s(16)
        return x > px + pw - c and y > py + ph - c

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
            if not self.S["lock_positions"] and self._in_resize_corner(e.x, e.y):
                self.begin_resize_drag(Gdk.WindowEdge.SOUTH_EAST, e.button, int(e.x_root), int(e.y_root), e.time)
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
                else:
                    self.on_drag_start(px, py, e)
                return True
        self.on_motion(e.x, e.y)
        if self.resizable_panel and not self.S["lock_positions"]:
            win = self.get_window()
            if win is not None:
                cur = "se-resize" if self._in_resize_corner(e.x, e.y) else None
                win.set_cursor(Gdk.Cursor.new_from_name(self.get_display(), cur) if cur else None)
        return True

    def on_drag_start(self, x, y, event):
        pass

    def _on_release(self, _w, e):
        if self._press is not None and e.button == 1:
            px, py = self._press[0], self._press[1]
            self._press = None
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
