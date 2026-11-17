"""Pictures drawn behind and inside widgets: weather icons and skies, the clock's photos and
drawn landscape, the vinyl record, and the "half picture, half glass" fade."""
import math, os, random
import cairo
from gi.repository import Gdk, GdkPixbuf

from .glass import rounded

HERE = os.path.dirname(os.path.abspath(__file__))


def glow(cr, x, y, r, rgba):
    g = cairo.RadialGradient(x, y, 0, x, y, r)
    g.add_color_stop_rgba(0, *rgba)
    g.add_color_stop_rgba(1, rgba[0], rgba[1], rgba[2], 0)
    cr.set_source(g)
    cr.arc(x, y, r, 0, 2 * math.pi)
    cr.fill()


def _mix(a, b, t):
    return tuple(a[i] + (b[i] - a[i]) * t for i in range(3))


# ------------------------------------------------------------------ fade: picture on top, glass below
_cache = {}


def draw_faded(cr, x, y, w, h, radius, key, paint, top=0.30, bottom=0.62):
    """paint(cr, w, h) draws the picture once into a cached surface; it is then shown fully at the
    top and faded out between `top` and `bottom` (0..1 of the height)."""
    w, h = max(1, int(w)), max(1, int(h))
    k = (key, w, h)
    surf = _cache.get(k)
    if surf is None:
        if len(_cache) > 24:
            _cache.clear()
        surf = cairo.ImageSurface(cairo.FORMAT_ARGB32, w, h)
        c2 = cairo.Context(surf)
        try:
            paint(c2, w, h)
        except Exception as e:
            print("GlassDesk: scene", key, e)
        # darken the top a little so white text stays readable
        g = cairo.LinearGradient(0, 0, 0, h / 2)
        g.add_color_stop_rgba(0, 0, 0, 0, 0.3)
        g.add_color_stop_rgba(1, 0, 0, 0, 0)
        c2.set_source(g)
        c2.rectangle(0, 0, w, h / 2)
        c2.fill()
        _cache[k] = surf
    cr.save()
    rounded(cr, x, y, w, h, radius)
    cr.clip()
    mask = cairo.LinearGradient(0, y, 0, y + h)
    mask.add_color_stop_rgba(0, 0, 0, 0, 1)
    mask.add_color_stop_rgba(top, 0, 0, 0, 1)
    mask.add_color_stop_rgba((top + bottom) / 2, 0, 0, 0, 0.5)
    mask.add_color_stop_rgba(bottom, 0, 0, 0, 0)
    mask.add_color_stop_rgba(1, 0, 0, 0, 0)
    cr.set_source_surface(surf, x, y)
    cr.mask(mask)
    cr.restore()


def photo(name, w, h):
    """Cover-crops one of the bundled CC0 photos (scenes/*.jpg) into a w x h pixbuf."""
    path = os.path.join(HERE, "scenes", name + ".jpg")
    pb = GdkPixbuf.Pixbuf.new_from_file(path)
    sc = max(w / pb.get_width(), h / pb.get_height())
    sw, sh = int(pb.get_width() * sc + 1), int(pb.get_height() * sc + 1)
    pb = pb.scale_simple(sw, sh, GdkPixbuf.InterpType.BILINEAR)
    return pb.new_subpixbuf((sw - w) // 2, (sh - h) // 2, w, h)


def paint_photo(name):
    def p(cr, w, h):
        Gdk.cairo_set_source_pixbuf(cr, photo(name, w, h), 0, 0)
        cr.paint()
    return p


def photo_for_hour(hr):
    return "morning" if 5 <= hr < 10 else "day" if 10 <= hr < 17 else "sunset" if 17 <= hr < 20 else "night"


# ------------------------------------------------------------------ weather icons
def kind(code, day):
    if code >= 95: return "storm"
    if 71 <= code <= 77 or code in (85, 86): return "snow"
    if 51 <= code <= 67 or 80 <= code <= 82: return "rain"
    if code in (45, 48): return "fog"
    if code == 3: return "overcast" if day else "overcast-night"
    if code in (1, 2): return "partly" if day else "partly-night"
    return "clear" if day else "clear-night"


def weather_icon(cr, x, y, s, c, day=True):
    """Drawn weather icon in a s x s box (same look as the Windows version)."""
    cloudy = c != 0
    rain = 51 <= c <= 67 or 80 <= c <= 82
    snow = 71 <= c <= 77 or c in (85, 86)
    storm = c >= 95
    fog = c in (45, 48)
    if c <= 2:
        k = 0.18 if c == 0 else 0.08
        d = 0.64 if c == 0 else 0.5
        cx, cy, r = x + s * (k + d / 2), y + s * (k + d / 2), s * d / 2
        if day:
            g = cairo.LinearGradient(cx - r, cy - r, cx + r, cy + r)
            g.add_color_stop_rgb(0, 1, 0.84, 0.4)
            g.add_color_stop_rgb(1, 1, 0.59, 0.24)
            cr.set_source(g)
        else:
            cr.set_source_rgb(0.87, 0.89, 1)
        cr.arc(cx, cy, r, 0, 2 * math.pi)
        cr.fill()
    if cloudy and not fog:
        cr.set_source_rgb(*((0.59, 0.64, 0.73) if storm or rain else (0.93, 0.95, 0.98)))
        for ex, ey, ew, eh in ((.14, .38, .42, .36), (.32, .24, .44, .44), (.56, .40, .34, .32)):
            cr.save()
            cr.translate(x + s * (ex + ew / 2), y + s * (ey + eh / 2))
            cr.scale(s * ew / 2, s * eh / 2)
            cr.arc(0, 0, 1, 0, 2 * math.pi)
            cr.restore()
            cr.fill()
        cr.rectangle(x + s * .3, y + s * .52, s * .44, s * .2)
        cr.fill()
    cr.set_line_cap(cairo.LINE_CAP_ROUND)
    if fog:
        cr.set_source_rgba(0.86, 0.89, 0.94, 0.86)
        cr.set_line_width(s * .07)
        for i in range(3):
            cr.move_to(x + s * (.15 + i * .05), y + s * (.35 + i * .17))
            cr.line_to(x + s * (.85 - i * .05), y + s * (.35 + i * .17))
            cr.stroke()
    if rain:
        cr.set_source_rgb(0.43, 0.67, 1)
        cr.set_line_width(s * .06)
        for i in range(3):
            cr.move_to(x + s * (.32 + i * .16), y + s * .8)
            cr.line_to(x + s * (.27 + i * .16), y + s * .95)
            cr.stroke()
    if snow:
        cr.set_source_rgb(1, 1, 1)
        for i in range(3):
            cr.arc(x + s * (.32 + i * .16), y + s * .85, s * .045, 0, 2 * math.pi)
            cr.fill()
    if storm:
        cr.set_source_rgb(1, 0.81, 0.28)
        pts = [(.52, .62), (.4, .86), (.5, .84), (.44, 1), (.62, .76), (.52, .78)]
        cr.move_to(x + s * pts[0][0], y + s * pts[0][1])
        for px, py in pts[1:]:
            cr.line_to(x + s * px, y + s * py)
        cr.close_path()
        cr.fill()
    cr.new_path()


# ------------------------------------------------------------------ weather skies
SKY = {"clear": ((0.15, 0.47, 0.9), (0.55, 0.78, 1)), "partly": ((0.2, 0.5, 0.86), (0.63, 0.78, 0.94)),
       "overcast": ((0.38, 0.43, 0.52), (0.63, 0.67, 0.73)), "rain": ((0.2, 0.24, 0.33), (0.38, 0.43, 0.52)),
       "storm": ((0.12, 0.1, 0.2), (0.27, 0.24, 0.39)), "snow": ((0.55, 0.63, 0.75), (0.84, 0.88, 0.93)),
       "fog": ((0.51, 0.54, 0.59), (0.75, 0.77, 0.8))}
NIGHT = ((0.04, 0.06, 0.18), (0.14, 0.19, 0.38))


def _cloud(cr, x, y, size, rgba, rnd):
    for _ in range(6):
        px = x + (rnd.random() - 0.5) * size * 1.4
        py = y + (rnd.random() - 0.5) * size * 0.35
        r = size * (0.35 + rnd.random() * 0.3)
        g = cairo.RadialGradient(px, py, r * 0.3, px, py, r)
        g.add_color_stop_rgba(0, *rgba)
        g.add_color_stop_rgba(1, rgba[0], rgba[1], rgba[2], 0)
        cr.set_source(g)
        cr.save()
        cr.translate(px, py)
        cr.scale(1, 0.8)
        cr.arc(0, 0, r, 0, 2 * math.pi)
        cr.restore()
        cr.fill()


def paint_weather(k):
    def p(cr, w, h):
        night = k.endswith("night")
        top, bot = NIGHT if night else SKY.get(k, SKY["clear"])
        g = cairo.LinearGradient(0, 0, 0, h)
        g.add_color_stop_rgb(0, *top)
        g.add_color_stop_rgb(1, *bot)
        cr.set_source(g)
        cr.paint()
        rnd = random.Random(sum(map(ord, k)))
        u = min(w, h * 2) / 330.0
        if night:
            for _ in range(70):
                s = (0.8 + rnd.random() * 1.8) * u
                cr.set_source_rgba(1, 1, 1, (80 + rnd.randint(0, 170)) / 255)
                cr.arc(rnd.random() * w, rnd.random() * h * 0.7, s / 2, 0, 2 * math.pi)
                cr.fill()
            mx, my, mr = w * 0.8, h * 0.24, 16 * u
            glow(cr, mx, my, mr * 4, (0.75, 0.8, 1, 0.28))
            cr.set_fill_rule(cairo.FILL_RULE_EVEN_ODD)
            cr.arc(mx, my, mr, 0, 2 * math.pi)
            cr.new_sub_path()
            cr.arc(mx + mr * 0.62, my - mr * 0.3, mr, 0, 2 * math.pi)
            cr.clip()
            cr.arc(mx, my, mr, 0, 2 * math.pi)
            cr.set_source_rgb(0.93, 0.95, 1)
            cr.fill()
            cr.reset_clip()
            cr.set_fill_rule(cairo.FILL_RULE_WINDING)
        if k in ("clear", "partly"):
            sx, sy = w * 0.8, h * 0.22
            glow(cr, sx, sy, 90 * u, (1, 0.9, 0.63, 0.6))
            glow(cr, sx, sy, 34 * u, (1, 0.96, 0.82, 0.86))
            cr.set_line_cap(cairo.LINE_CAP_ROUND)
            cr.set_line_width(10 * u)
            cr.set_source_rgba(1, 0.98, 0.86, 0.11)
            for i in range(8):
                a = i * math.pi / 4 + 0.2
                cr.move_to(sx, sy)
                cr.line_to(sx + math.cos(a) * 140 * u, sy + math.sin(a) * 140 * u)
                cr.stroke()
        n = 2 if k.startswith("clear") else 4 if k.startswith("partly") else 0 if k == "fog" else 7
        cc = (0.27, 0.25, 0.38) if k == "storm" else (0.44, 0.48, 0.56) if k == "rain" else (0.75, 0.77, 0.82) if k.startswith("overcast") \
            else (0.27, 0.32, 0.47) if night else (0.98, 0.99, 1)
        ca = 0.47 if k.startswith("clear") else 0.86
        for _ in range(n):
            _cloud(cr, (rnd.random() * 1.1 - 0.05) * w, (0.08 + rnd.random() * 0.42) * h, (46 + rnd.random() * 50) * u, cc + (ca,), rnd)
        if k in ("rain", "storm"):
            cr.set_line_width(1.3 * u)
            for _ in range(90):
                x, y, l = rnd.random() * (w + 40) - 20, rnd.random() * h, (10 + rnd.random() * 14) * u
                cr.set_source_rgba(0.78, 0.86, 1, (70 + rnd.randint(0, 90)) / 255)
                cr.move_to(x, y)
                cr.line_to(x - l * 0.28, y + l)
                cr.stroke()
        if k == "storm":
            bx, by, bs = w * 0.93, h * 0.12, 0.12 * h
            glow(cr, bx - bs * 0.05, by + bs * 0.8, 70 * u, (0.78, 0.75, 1, 0.47))
            pts = [(0, 0), (-0.3, 0.9), (-0.08, 0.9), (-0.32, 1.8), (0.22, 0.7), (0, 0.7), (0.2, 0)]
            cr.move_to(bx, by)
            for px, py in pts[1:]:
                cr.line_to(bx + bs * px, by + bs * py)
            cr.close_path()
            cr.set_source_rgba(1, 0.94, 0.67, 0.6)
            cr.fill()
        if k == "snow":
            for _ in range(80):
                s = (1.5 + rnd.random() * 3.2) * u
                cr.set_source_rgba(1, 1, 1, (150 + rnd.randint(0, 105)) / 255)
                cr.arc(rnd.random() * w, rnd.random() * h, s / 2, 0, 2 * math.pi)
                cr.fill()
        if k == "fog":
            for i in range(6):
                y = h * (0.12 + i * 0.14)
                g = cairo.LinearGradient(0, y, 0, y + 24 * u)
                g.add_color_stop_rgba(0, 1, 1, 1, 0)
                g.add_color_stop_rgba(0.5, 0.94, 0.95, 0.96, 0.43)
                g.add_color_stop_rgba(1, 1, 1, 1, 0)
                cr.set_source(g)
                cr.rectangle(0, y, w, 24 * u)
                cr.fill()
    return p


# ------------------------------------------------------------------ the clock's drawn landscape
def paint_sky(cr, x, y, w, h, radius, hour):
    at = [0, 5, 6.5, 9, 16.5, 18.5, 20.5, 24]
    sky = [((10, 14, 40), (30, 38, 84)), ((18, 22, 60), (70, 60, 120)), ((90, 110, 200), (255, 150, 110)),
           ((56, 140, 236), (160, 210, 255)), ((56, 140, 236), (160, 210, 255)), ((90, 70, 170), (255, 130, 90)),
           ((14, 18, 50), (50, 44, 100)), ((10, 14, 40), (30, 38, 84))]
    i = 0
    while i < len(at) - 2 and hour >= at[i + 1]:
        i += 1
    f = (hour - at[i]) / (at[i + 1] - at[i])
    top = tuple(c / 255 for c in _mix(sky[i][0], sky[i + 1][0], f))
    bot = tuple(c / 255 for c in _mix(sky[i][1], sky[i + 1][1], f))
    night = 1.0 if hour < 5.5 or hour > 19.5 else (7 - hour) / 1.5 if hour < 7 else (hour - 18) / 1.5 if hour > 18 else 0.0
    night = max(0.0, min(1.0, night))
    cr.save()
    rounded(cr, x, y, w, h, radius)
    cr.clip()
    g = cairo.LinearGradient(0, y, 0, y + h)
    g.add_color_stop_rgba(0, *(top + (0.92,)))
    g.add_color_stop_rgba(1, *(bot + (0.92,)))
    cr.set_source(g)
    cr.paint()
    if night > 0.05:
        rnd = random.Random(7)
        for _ in range(46):
            sx, sy, sz = x + rnd.random() * w, y + rnd.random() * h * 0.65, 0.8 + rnd.random() * 1.6
            cr.set_source_rgba(1, 1, 1, night * (90 + rnd.randint(0, 150)) / 255)
            cr.arc(sx, sy, sz / 2, 0, 2 * math.pi)
            cr.fill()
    sun = 6 <= hour < 19
    p = (hour - 6) / 13 if sun else ((hour + 24 - 19) % 24) / 11
    bx, by = x + w * (0.56 + 0.38 * p), y + h - h * (0.34 + 0.42 * math.sin(math.pi * p))
    if sun:
        glow(cr, bx, by, 66, (1, 0.84, 0.55, 0.55))
        cr.set_source_rgb(1, 0.93, 0.7)
        cr.arc(bx, by, 15, 0, 2 * math.pi)
        cr.fill()
    else:
        glow(cr, bx, by, 42, (0.78, 0.82, 1, 0.27))
        cr.set_fill_rule(cairo.FILL_RULE_EVEN_ODD)
        cr.arc(bx, by, 12, 0, 2 * math.pi)
        cr.new_sub_path()
        cr.arc(bx + 7, by - 3, 12, 0, 2 * math.pi)
        cr.set_source_rgb(0.93, 0.94, 1)
        cr.fill()
        cr.set_fill_rule(cairo.FILL_RULE_WINDING)
    for base, amp, seed, col, a in ((0.62, 0.20, 3, _mix(bot, top, 0.55), 0.78), (0.78, 0.16, 11, _mix(top, (0, 0, 0), 0.55), 0.92)):
        rnd = random.Random(seed)
        pts = [(x + w * k / 9, y + h * (base - amp * rnd.random())) for k in range(10)]
        cr.move_to(x, y + h)
        cr.line_to(*pts[0])
        for k in range(1, len(pts)):
            (x0, y0), (x1, y1) = pts[k - 1], pts[k]
            cr.curve_to((x0 + x1) / 2, y0, (x0 + x1) / 2, y1, x1, y1)
        cr.line_to(x + w, y + h)
        cr.close_path()
        cr.set_source_rgba(*(col + (a,)))
        cr.fill()
    g = cairo.LinearGradient(0, y, 0, y + h * 0.6)
    g.add_color_stop_rgba(0, 0, 0, 0, 0.24)
    g.add_color_stop_rgba(1, 0, 0, 0, 0)
    cr.set_source(g)
    cr.rectangle(x, y, w, h * 0.6)
    cr.fill()
    cr.restore()


# ------------------------------------------------------------------ vinyl record
def vinyl(cr, x, y, d, cover, angle, playing, tone, accent):
    """A black record with grooves, a still reflection, the cover turning in the middle and a tone arm."""
    cx, cy, r = x + d / 2, y + d / 2, d / 2
    glow(cr, cx, cy, d * 0.75, tone + (0.43 if playing else 0.24,))
    g = cairo.RadialGradient(cx, cy, 0, cx, cy, r)
    g.add_color_stop_rgb(0, 0.13, 0.13, 0.16)
    g.add_color_stop_rgb(1, 0.04, 0.04, 0.05)
    cr.set_source(g)
    cr.arc(cx, cy, r, 0, 2 * math.pi)
    cr.fill()
    cr.set_line_width(1)
    k = 0.38
    while k < 0.97:
        cr.set_source_rgba(1, 1, 1, 0.16 if k > 0.9 else 0.085)
        cr.arc(cx, cy, r * k, 0, 2 * math.pi)
        cr.stroke()
        k += 0.055
    cr.set_source_rgba(1, 1, 1, 0.07)
    for a0 in (200, 20):
        cr.move_to(cx, cy)
        cr.arc(cx, cy, r, math.radians(a0), math.radians(a0 + 40))
        cr.close_path()
        cr.fill()
    lr = r * 0.46
    cr.save()
    cr.arc(cx, cy, lr, 0, 2 * math.pi)
    cr.clip()
    cr.translate(cx, cy)
    cr.rotate(math.radians(angle))
    if cover is not None:
        s = min(cover.get_width(), cover.get_height())
        sc = 2 * lr / s
        cr.scale(sc, sc)
        Gdk.cairo_set_source_pixbuf(cr, cover, -cover.get_width() / 2, -cover.get_height() / 2)
        cr.paint()
    else:
        g = cairo.LinearGradient(-lr, -lr, lr, lr)
        g.add_color_stop_rgb(0, *accent)
        g.add_color_stop_rgb(1, 0.69, 0.42, 1)
        cr.set_source(g)
        cr.paint()
    cr.restore()
    cr.set_source_rgb(0.07, 0.07, 0.09)
    cr.arc(cx, cy, 3, 0, 2 * math.pi)
    cr.fill()
    px, py = x + d - d * 0.06, y + d * 0.06
    a = math.radians(118 if playing else 100)
    tx, ty = px + math.cos(a) * d * 0.5, py + math.sin(a) * d * 0.5
    cr.set_line_cap(cairo.LINE_CAP_ROUND)
    cr.set_line_width(2.6)
    cr.set_source_rgba(0.82, 0.84, 0.88, 0.9)
    cr.move_to(px, py)
    cr.line_to(tx, ty)
    cr.stroke()
    cr.set_source_rgb(0.78, 0.8, 0.84)
    cr.arc(px, py, 5, 0, 2 * math.pi)
    cr.fill()
    cr.set_source_rgb(0.59, 0.6, 0.64)
    cr.rectangle(tx - 3, ty - 3, 6, 7)
    cr.fill()
