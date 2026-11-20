"""Liquid glass for Linux: the same effect as GlassDesk 2.0 for Windows.

A thick, rounded pane of glass. Near its edges the glass bends light like a lens, so what is just
outside the panel shows up squeezed and curved inside the rim. It is frosted most at the thick rim,
colors split slightly at the edge like a prism, and the result is made once per position and size.
Needs numpy (python3-numpy); without it GlassDesk falls back to frosted glass.
"""
import cairo

try:
    import numpy as np
except Exception:   # pragma: no cover
    np = None

AVAILABLE = np is not None
_walls = {}


def wall_array(pb, key):
    """A GdkPixbuf as a float array (h, w, 3), kept while that picture is in use."""
    if pb is None or np is None:
        return None
    a = _walls.get(key)
    if a is not None and a[0] is pb:
        return a[1]
    w, h, rs, n = pb.get_width(), pb.get_height(), pb.get_rowstride(), pb.get_n_channels()
    buf = np.frombuffer(pb.get_pixels(), dtype=np.uint8)
    arr = np.zeros((h, w, 3), dtype=np.float32)
    rows = buf[: rs * (h - 1) + w * n]
    full = np.zeros(rs * h, dtype=np.uint8)
    full[: rows.size] = rows
    arr[:] = full.reshape(h, rs)[:, : w * n].reshape(h, w, n)[:, :, :3]
    _walls[key] = (pb, arr)
    if len(_walls) > 4:
        _walls.pop(next(iter(_walls)))
    return arr


def _sdf(px, py, hw, hh, r):
    qx = np.abs(px) - (hw - r)
    qy = np.abs(py) - (hh - r)
    ox, oy = np.maximum(qx, 0), np.maximum(qy, 0)
    d = np.sqrt(ox * ox + oy * oy) + np.minimum(np.maximum(qx, qy), 0) - r
    corner = (qx > 0) & (qy > 0)
    l = np.sqrt(qx * qx + qy * qy) + 1e-6
    nx = np.where(corner, qx / l, np.where(qx > qy, 1.0, 0.0))
    ny = np.where(corner, qy / l, np.where(qx > qy, 0.0, 1.0))
    nx = np.where(px < 0, -nx, nx)
    ny = np.where(py < 0, -ny, ny)
    return d, nx, ny


def _box(v, r):
    """Box blur of a 2-D field (both directions, twice) with cumulative sums."""
    for _ in range(2):
        for axis in (0, 1):
            n = v.shape[axis]
            pad = np.concatenate([np.repeat(np.take(v, [0], axis), r, axis), v, np.repeat(np.take(v, [n - 1], axis), r, axis)], axis)
            c = np.cumsum(pad, axis=axis)
            c = np.concatenate([np.zeros_like(np.take(c, [0], axis)), c], axis)
            v = (np.take(c, range(2 * r + 1, n + 2 * r + 1), axis) - np.take(c, range(0, n), axis)) / (2 * r + 1)
    return v


def _sample(img, x, y):
    h, w = img.shape[:2]
    x = np.clip(x, 0, w - 1.001)
    y = np.clip(y, 0, h - 1.001)
    x0, y0 = x.astype(np.int32), y.astype(np.int32)
    fx, fy = (x - x0)[..., None], (y - y0)[..., None]
    a, b = img[y0, x0], img[y0, x0 + 1]
    c, d = img[y0 + 1, x0], img[y0 + 1, x0 + 1]
    return (a + (b - a) * fx) * (1 - fy) + (c + (d - c) * fx) * fy


def build(mid, rim, sx0, sy0, w, h, radius, scale, dark, frost):
    """Returns (cairo.ImageSurface, brightness 0..1) for a w x h panel whose top-left is at (sx0, sy0) on the wallpaper."""
    w, h = int(w), int(h)
    hw, hh = w / 2.0, h / 2.0
    r = min(radius, hw, hh)
    bevel = max(14 * scale, min(48 * scale, min(w, h) * 0.22))
    bevel = min(bevel, min(hw, hh) * 0.9)
    rb = min(min(hw, hh), max(r, bevel * 1.1))   # rounder corners for the bending: no diagonal seams
    ys, xs = np.mgrid[0:h, 0:w].astype(np.float32)
    px, py = xs + 0.5 - hw, ys + 0.5 - hh
    d, nx, ny = _sdf(px, py, hw, hh, rb)
    e = 1 - np.clip(-d / bevel, 0, 1)
    hgt = np.sqrt(np.maximum(0.0004, 1 - e * e))
    m = np.minimum(bevel * 1.5, e / hgt * bevel * 0.42)
    # lean toward the corners so lines in the wallpaper curve through the glass
    rl = np.sqrt((px / hw) ** 2 + (py / hh) ** 2) + 1e-6
    vx, vy = nx * 0.62 + px / hw / rl * 0.38, ny * 0.62 + py / hh / rl * 0.38
    vl = np.sqrt(vx * vx + vy * vy) + 1e-6
    dx, dy = vx / vl * m, vy / vl * m
    sr = max(1, int(round(bevel * 0.2)))
    dx, dy, e = _box(dx, sr), _box(dy, sr), _box(e, sr)
    sx, sy = sx0 + hw + px * 0.97, sy0 + hh + py * 0.97
    f = np.clip(frost + (1 - frost) * e * e * 1.2, 0, 1)[..., None]
    out = np.zeros((h, w, 3), dtype=np.float32)
    for ch, k in ((0, 0.93), (1, 1.0), (2, 1.08)):   # red bends least, blue most: a thin rainbow at the rim
        a = _sample(mid, sx + dx * k, sy + dy * k)[..., ch:ch + 1]
        b = _sample(rim, sx + dx * k, sy + dy * k)[..., ch:ch + 1]
        out[..., ch:ch + 1] = a + (b - a) * f
    avg = out.mean(axis=2, keepdims=True)
    out = avg + (out - avg) * 1.16
    lift = 2 if dark else 8
    out = out + (255 - out) * lift / 255.0
    out = np.clip(out, 0, 255)
    # coverage of the real (less rounded) shape, with a soft edge
    dr, _, _ = _sdf(px, py, hw, hh, r)
    cover = np.clip(0.5 - dr, 0, 1)
    flat = e <= 0.01
    bright = float((out[flat] @ np.array([0.3, 0.59, 0.11], dtype=np.float32)).mean() / 255) if flat.any() else 0.5
    # cairo ARGB32 is premultiplied BGRA in memory
    surf = cairo.ImageSurface(cairo.FORMAT_ARGB32, w, h)
    stride = surf.get_stride()
    data = np.ndarray(shape=(h, stride // 4, 4), dtype=np.uint8, buffer=surf.get_data())
    data[:, :w, 0] = (out[..., 2] * cover).astype(np.uint8)
    data[:, :w, 1] = (out[..., 1] * cover).astype(np.uint8)
    data[:, :w, 2] = (out[..., 0] * cover).astype(np.uint8)
    data[:, :w, 3] = (cover * 255).astype(np.uint8)
    surf.mark_dirty()
    return surf, bright
