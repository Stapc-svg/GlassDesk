"""Fences: glass boxes that sort the files on your desktop."""
import math, os, shutil, time, urllib.parse, uuid
import cairo
from gi.repository import Gtk, Gdk, GdkPixbuf, Gio, GLib

from . import core
from .glass import GlassWindow, text, fill_round, rounded, with_alpha, popup

URI_TARGETS = [Gtk.TargetEntry.new("text/uri-list", 0, 0)]


class Item:
    __slots__ = ("path", "name", "is_dir", "mtime", "size", "ext", "_icon", "_icon_size")

    def __init__(self, path):
        self.path = path
        st = os.stat(path)
        self.is_dir = os.path.isdir(path)
        self.mtime, self.size = st.st_mtime, (0 if self.is_dir else st.st_size)
        self.ext = "" if self.is_dir else os.path.splitext(path)[1].lower()
        self.name = os.path.basename(path)
        if path.endswith(".desktop"):
            try:
                info = Gio.DesktopAppInfo.new_from_filename(path)
                if info:
                    self.name = info.get_display_name() or self.name
            except Exception:
                pass
        self._icon, self._icon_size = None, 0

    def icon(self, size):
        if self._icon is not None and self._icon_size == size:
            return self._icon
        pb = None
        try:
            if self.ext in (".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".svg", ".ico", ".tif", ".tiff", ".avif") and self.size < 25_000_000:
                f = Gio.File.new_for_path(self.path)
                info = f.query_info("thumbnail::path", Gio.FileQueryInfoFlags.NONE, None)
                src = info.get_attribute_byte_string("thumbnail::path") or self.path
                pb = GdkPixbuf.Pixbuf.new_from_file_at_scale(src, size, size, True)
        except Exception:
            pb = None
        if pb is None:
            gicon = None
            try:
                if self.path.endswith(".desktop"):
                    info = Gio.DesktopAppInfo.new_from_filename(self.path)
                    gicon = info.get_icon() if info else None
                if gicon is None:
                    info = Gio.File.new_for_path(self.path).query_info("standard::icon", Gio.FileQueryInfoFlags.NONE, None)
                    gicon = info.get_icon()
                theme = Gtk.IconTheme.get_default()
                ii = theme.lookup_by_gicon(gicon, size, Gtk.IconLookupFlags.FORCE_SIZE) if gicon else None
                pb = ii.load_icon() if ii else None
            except Exception:
                pb = None
            if pb is None:
                try:
                    pb = Gtk.IconTheme.get_default().load_icon("folder" if self.is_dir else "text-x-generic", size, Gtk.IconLookupFlags.FORCE_SIZE)
                except Exception:
                    pb = None
        self._icon, self._icon_size = pb, size
        return pb


class Organizer:
    """Reads the desktop folder and decides which fence each item goes in."""

    def __init__(self, app):
        self.app = app
        self.items = {}            # path -> Item
        self.monitors = []
        self._refresh_id = 0

    @property
    def S(self):
        return self.app.settings

    def fence_ids(self):
        ids = [k for k, _n, _e in core.CATEGORIES if self.S["categories"].get(k, True)]
        ids += [c["id"] for c in self.S["custom_fences"]]
        ids += [f["id"] for f in self.S["folder_fences"]]
        return [i for i in ids if i not in self.S["hidden_fences"]]

    def fence_name(self, fid):
        opt = self.S["fence_opts"].get(fid, {})
        if opt.get("name"):
            return opt["name"]
        for c in self.S["custom_fences"]:
            if c["id"] == fid:
                return c["name"]
        for f in self.S["folder_fences"]:
            if f["id"] == fid:
                return f.get("name") or os.path.basename(f["path"].rstrip("/")) or f["path"]
        return core.CAT_NAME.get(fid, fid)

    def folder_of(self, fid):
        for f in self.S["folder_fences"]:
            if f["id"] == fid:
                return f["path"]
        return None

    def watch(self):
        for m in self.monitors:
            m.cancel()
        self.monitors = []
        dirs = [core.desktop_dir()] + [f["path"] for f in self.S["folder_fences"] if os.path.isdir(f["path"])]
        for d in dirs:
            try:
                mon = Gio.File.new_for_path(d).monitor_directory(Gio.FileMonitorFlags.WATCH_MOVES, None)
                mon.connect("changed", lambda *a: self.refresh_soon())
                self.monitors.append(mon)
            except Exception as e:
                core.log("watch %s: %s" % (d, e))

    def refresh_soon(self):
        if not self._refresh_id:
            self._refresh_id = GLib.timeout_add(350, self._refresh)

    def _refresh(self):
        self._refresh_id = 0
        self.app.refresh_fences()
        return False

    def _list(self, folder):
        out = []
        try:
            for n in os.listdir(folder):
                if n.startswith(".") or n.endswith("~"):
                    continue
                p = os.path.join(folder, n)
                try:
                    old = self.items.get(p)
                    st = os.stat(p)
                    if old is not None and old.mtime == st.st_mtime:
                        out.append(old)
                    else:
                        it = Item(p)
                        self.items[p] = it
                        out.append(it)
                except OSError:
                    pass
        except OSError:
            pass
        return out

    def assign(self):
        ids = set(self.fence_ids())
        groups = {i: [] for i in ids}
        for it in self._list(core.desktop_dir()):
            fid = self.S["pins"].get(it.name)
            if fid not in ids:
                fid = core.rule_target(it.name, self.S["rules"])
            if fid not in ids:
                fid = core.category_of(it.path)
                if fid not in ids:
                    fid = "other" if "other" in ids else None
            if fid:
                groups[fid].append(it)
        for f in self.S["folder_fences"]:
            if f["id"] in ids:
                groups[f["id"]] = self._list(f["path"])
        return groups

    def all_items(self):
        return [it for g in self.assign().values() for it in g]


class Fence(GlassWindow):
    TITLE = 38

    def __init__(self, app, fid):
        self.fid = fid
        self.items = []
        self.scroll = 0
        self.hover_title = False
        self.drop_hot = False
        self.peeking = False
        super().__init__(app, "fence:" + fid, 340, 230, resizable=True)
        self._drag_item = None
        self.drag_dest_set(Gtk.DestDefaults.ALL, URI_TARGETS, Gdk.DragAction.COPY | Gdk.DragAction.MOVE | Gdk.DragAction.LINK)
        self.connect("drag-data-received", self._on_drop)
        self.connect("drag-motion", self._on_drag_motion)
        self.connect("drag-leave", lambda *a: self._set_hot(False))
        self.connect("drag-data-get", self._on_drag_get)
        self.connect("drag-end", lambda *a: setattr(self, "_drag_item", None))

    # ---- options
    @property
    def opt(self):
        return self.S["fence_opts"].setdefault(self.fid, {})

    @property
    def collapsed(self):
        if self.opt.get("peek"):
            return not self.peeking
        return bool(self.opt.get("collapsed"))

    def collapsed_height_override(self):
        return self.collapsed

    def panel_size(self):
        w, h = super().panel_size()
        saved = self.S["positions"].get(self.key)
        if not (saved and len(saved) == 4):
            # no size chosen yet: fit the items (between 1 and 4 rows)
            cw, ch = self.cell()
            cols = max(1, int((w - self.T.s(20)) // cw))
            rows = max(1, min(4, math.ceil(len(self.items) / cols)))
            h = int(self.T.s(self.TITLE + 18) + rows * ch)
        if self.collapsed:
            return w, int(self.T.s(self.TITLE + 8))
        return w, h

    def icon_px(self):
        return int(self.T.s({"small": 32, "medium": 44, "large": 60}.get(self.opt.get("icon", "medium"), 44)))

    def cell(self):
        ip = self.icon_px()
        return int(max(self.T.s(84), ip + self.T.s(36))), int(ip + self.T.s(44))

    def set_items(self, items):
        key = self.opt.get("sort", "name")
        if key == "type":
            items.sort(key=lambda i: (not i.is_dir, i.ext, i.name.lower()))
        elif key == "date":
            items.sort(key=lambda i: -i.mtime)
        elif key == "size":
            items.sort(key=lambda i: -i.size)
        else:
            items.sort(key=lambda i: (not i.is_dir, i.name.lower()))
        n_old = len(self.items)
        self.items = items
        saved = self.S["positions"].get(self.key)
        if n_old != len(items) and not (saved and len(saved) == 4):
            self.apply_size()
        self.queue_draw()

    # ---- layout
    def title_rect(self):
        x, y, w, h = self.panel_rect()
        return x, y, w, self.T.s(self.TITLE)

    def grid_rect(self):
        x, y, w, h = self.panel_rect()
        p = self.T.s(10)
        t = self.T.s(self.TITLE)
        return x + p, y + t + self.T.s(4), w - 2 * p, h - t - self.T.s(12)

    def cols(self):
        gx, gy, gw, gh = self.grid_rect()
        cw, ch = self.cell()
        return max(1, int(gw // cw))

    def item_at(self, x, y):
        if self.collapsed:
            return None
        gx, gy, gw, gh = self.grid_rect()
        if not (gx <= x < gx + gw and gy <= y < gy + gh):
            return None
        cw, ch = self.cell()
        cols = self.cols()
        pad = (gw - cols * cw) / 2
        c = int((x - gx - pad) // cw)
        r = int((y - gy + self.scroll) // ch)
        if c < 0 or c >= cols:
            return None
        i = r * cols + c
        return i if 0 <= i < len(self.items) else None

    def max_scroll(self):
        gx, gy, gw, gh = self.grid_rect()
        cw, ch = self.cell()
        rows = math.ceil(len(self.items) / self.cols())
        return max(0, rows * ch - gh)

    # ---- paint
    def paint(self, cr):
        T = self.T
        x, y, w, h = self.panel_rect()
        color = self.opt.get("color")
        if color:
            c = core.hex_rgb(color)
            cr.set_source_rgba(c[0], c[1], c[2], 0.16)
            cr.paint()
        tx, ty, tw, th = self.title_rect()
        if self.hover_title:
            cr.rectangle(tx, ty, tw, th)
            cr.set_source_rgba(*T.hover)
            cr.fill()
        dot = core.hex_rgb(color) if color else T.accent
        cr.arc(tx + T.s(16), ty + th / 2, T.s(3.5), 0, 2 * math.pi)
        cr.set_source_rgba(dot[0], dot[1], dot[2], 1)
        cr.fill()
        name = self.app.organizer.fence_name(self.fid)
        if self.app.organizer.folder_of(self.fid):
            name = "📁 " + name
        text(cr, name, tx + T.s(28), ty, tw - T.s(80), size=T.s(10.2), bold=True, color=T.text, h=th, valign="middle")
        text(cr, str(len(self.items)), tx, ty, tw - T.s(30), size=T.s(9), color=T.sub, align="right", h=th, valign="middle")
        # chevron
        cx, cy = tx + tw - T.s(16), ty + th / 2
        cr.set_source_rgba(*T.sub)
        cr.set_line_width(T.s(1.6))
        d = T.s(4)
        if self.collapsed:
            cr.move_to(cx - d, cy - d / 2); cr.line_to(cx, cy + d / 2); cr.line_to(cx + d, cy - d / 2)
        else:
            cr.move_to(cx - d, cy + d / 2); cr.line_to(cx, cy - d / 2); cr.line_to(cx + d, cy + d / 2)
        cr.stroke()
        if self.collapsed:
            return
        cr.rectangle(tx + T.s(12), ty + th - 1, tw - T.s(24), 1)
        cr.set_source_rgba(T.text[0], T.text[1], T.text[2], 0.1)
        cr.fill()
        gx, gy, gw, gh = self.grid_rect()
        cr.save()
        cr.rectangle(gx - 2, gy, gw + 4, gh)
        cr.clip()
        if not self.items:
            hint = "Drop files here" if self.fid not in core.CAT_NAME else "Nothing here yet"
            text(cr, hint, gx, gy, gw, size=T.s(9.5), color=T.sub, align="center", h=gh, valign="middle")
        cw, ch = self.cell()
        cols = self.cols()
        pad = (gw - cols * cw) / 2
        ip = self.icon_px()
        for i, it in enumerate(self.items):
            r, c = divmod(i, cols)
            cx0 = gx + pad + c * cw
            cy0 = gy + r * ch - self.scroll
            if cy0 + ch < gy or cy0 > gy + gh:
                continue
            if i == self.hover:
                fill_round(cr, cx0 + 2, cy0 + 2, cw - 4, ch - 4, T.s(10), T.hover)
            pb = it.icon(ip)
            if pb is not None:
                Gdk.cairo_set_source_pixbuf(cr, pb, cx0 + (cw - pb.get_width()) / 2, cy0 + T.s(6) + (ip - pb.get_height()) / 2)
                cr.paint()
            text(cr, it.name, cx0 + T.s(4), cy0 + T.s(8) + ip, cw - T.s(8), size=T.s(8.4), color=T.text,
                 align="center", wrap=True, lines=2, shadow=T.style == "transparent")
        cr.restore()
        ms = self.max_scroll()
        if ms > 0:
            frac = gh / (gh + ms)
            bh = max(T.s(24), gh * frac)
            by = gy + (gh - bh) * (self.scroll / ms)
            fill_round(cr, x + w - T.s(6), by, T.s(3), bh, T.s(1.5), with_alpha(T.text[:3], 0.3))
        if self.drop_hot:
            rounded(cr, x + 2, y + 2, w - 4, h - 4, T.s(T.radius) - 2)
            cr.set_source_rgba(T.accent[0], T.accent[1], T.accent[2], 0.9)
            cr.set_line_width(2.5)
            cr.stroke()
        if not self.S["lock_positions"]:
            cr.set_source_rgba(T.text[0], T.text[1], T.text[2], 0.25)
            for k in range(3):
                for j in range(k + 1):
                    cr.arc(x + w - T.s(6) - k * T.s(4), y + h - T.s(6) - j * T.s(4), 1, 0, 2 * math.pi)
                    cr.fill()

    # ---- input
    def is_drag_area(self, x, y):
        tx, ty, tw, th = self.title_rect()
        return ty <= y < ty + th

    def on_motion(self, x, y):
        tx, ty, tw, th = self.title_rect()
        ht = ty <= y < ty + th
        hv = self.item_at(x, y)
        if ht != self.hover_title or hv != self.hover:
            self.hover_title, self.hover = ht, hv
            self.queue_draw()

    def on_leave(self):
        self.hover_title = False
        if self.opt.get("peek") and self.peeking:
            self.peeking = False
            self.apply_size()

    def on_enter(self):
        if self.opt.get("peek") and not self.peeking:
            self.peeking = True
            self.apply_size()

    def on_scroll(self, dy):
        old = self.scroll
        self.scroll = max(0, min(self.max_scroll(), self.scroll + dy * self.T.s(40)))
        if self.scroll != old:
            self.queue_draw()

    def on_click(self, x, y, button, event):
        if self.is_drag_area(x, y):
            return
        i = self.item_at(x, y)
        if i is not None and self.S["single_click"]:
            core.open_path(self.items[i].path)

    def on_double_click(self, x, y):
        if self.is_drag_area(x, y):
            if not self.opt.get("peek"):
                self.opt["collapsed"] = not self.opt.get("collapsed")
                self.S.save()
                self.apply_size()
            return
        i = self.item_at(x, y)
        if i is not None:
            core.open_path(self.items[i].path)

    def on_drag_start(self, x, y, event):
        i = self.item_at(x, y)
        if i is None:
            return
        self._drag_item = self.items[i]
        tl = Gtk.TargetList.new(URI_TARGETS)
        ctx = self.drag_begin_with_coordinates(tl, Gdk.DragAction.MOVE | Gdk.DragAction.COPY | Gdk.DragAction.LINK, 1, event, -1, -1)
        pb = self._drag_item.icon(self.icon_px())
        if pb is not None and ctx is not None:
            Gtk.drag_set_icon_pixbuf(ctx, pb, pb.get_width() // 2, pb.get_height() // 2)

    def _on_drag_get(self, _w, ctx, data, info, t):
        if self._drag_item is not None:
            data.set_uris([Gio.File.new_for_path(self._drag_item.path).get_uri()])

    def _set_hot(self, v):
        if self.drop_hot != v:
            self.drop_hot = v
            self.queue_draw()

    def _on_drag_motion(self, _w, ctx, x, y, t):
        self._set_hot(True)
        i = self.item_at(x, y)
        if i != self.hover:
            self.hover = i
            self.queue_draw()
        return False

    def _on_drop(self, _w, ctx, x, y, data, info, t):
        self._set_hot(False)
        paths = []
        for u in data.get_uris() or []:
            p = Gio.File.new_for_uri(u).get_path()
            if p:
                paths.append(p)
        if not paths:
            return
        copy = ctx.get_selected_action() == Gdk.DragAction.COPY
        i = self.item_at(x, y)
        target = self.items[i] if i is not None else None
        if target is not None and target.is_dir and target.path not in paths:
            self.app.move_files(paths, target.path, copy)
            return
        folder = self.app.organizer.folder_of(self.fid)
        desk = core.desktop_dir()
        if folder:
            self.app.move_files([p for p in paths if os.path.dirname(p) != folder], folder, copy)
            return
        outside = [p for p in paths if os.path.dirname(os.path.abspath(p)) != os.path.abspath(desk)]
        if outside:
            self.app.move_files(outside, desk, copy)
        for p in paths:
            self.app.pin(os.path.basename(p), self.fid)
        self.app.refresh_fences()

    def item_menu(self, it):
        others = [(self.app.organizer.fence_name(f), (lambda f=f: (self.app.pin(it.name, f), self.app.refresh_fences())))
                  for f in self.app.organizer.fence_ids() if f != self.fid and not self.app.organizer.folder_of(f)]
        items = [("Open", lambda: core.open_path(it.path)),
                 ("Show in file manager", lambda: core.show_in_folder(it.path)),
                 ("Copy path", lambda: Gtk.Clipboard.get(Gdk.SELECTION_CLIPBOARD).set_text(it.path, -1)),
                 None]
        if not self.app.organizer.folder_of(self.fid):
            items.append(("Move to fence", others or [("(no other fences)", None)]))
            if it.name in self.S["pins"]:
                items.append(("Back to " + core.CAT_NAME.get(core.category_of(it.path), "its fence"),
                              lambda: (self.S["pins"].pop(it.name, None), self.S.save(), self.app.refresh_fences())))
        items += [("Rename…", lambda: self.app.rename_file(it.path)),
                  ("Move to Trash", lambda: self.app.trash_file(it.path))]
        return items

    def menu_items(self):
        x, y = self.get_pointer()
        i = self.item_at(x, y)
        if i is not None:
            return self.item_menu(self.items[i])
        opt = self.opt
        def setopt(k, v):
            opt[k] = v
            self.S.save()
            self.app.refresh_fences()
            self.apply_size()
        colors = [("Default", None), ("Blue", "#4F8DFF"), ("Purple", "#A78BFA"), ("Pink", "#F472B6"), ("Red", "#F87171"),
                  ("Orange", "#FB923C"), ("Yellow", "#FACC15"), ("Green", "#4ADE80"), ("Teal", "#2DD4BF")]
        items = [("Rename…", self.rename),
                 ("Sort by", [(lbl, (lambda k=k: setopt("sort", k)), opt.get("sort", "name") == k)
                              for lbl, k in (("Name", "name"), ("Type", "type"), ("Date modified", "date"), ("Size", "size"))]),
                 ("Color", [(lbl, (lambda c=c: setopt("color", c)), opt.get("color") == c) for lbl, c in colors]),
                 ("Icon size", [(lbl, (lambda k=k: setopt("icon", k)), opt.get("icon", "medium") == k)
                                for lbl, k in (("Small", "small"), ("Medium", "medium"), ("Large", "large"))]),
                 ("Roll up until I point at it", lambda: setopt("peek", not opt.get("peek")), opt.get("peek", False)),
                 None]
        if not self.app.organizer.folder_of(self.fid):
            items.append(("Add a rule to this fence…", lambda: self.app.add_rule_dialog(self.fid)))
        items += [("New fence…", self.app.new_fence_dialog),
                  ("New folder fence…", self.app.new_folder_fence_dialog),
                  None,
                  ("Hide this fence", lambda: self.app.hide_fence(self.fid))]
        if self.fid not in core.CAT_NAME:
            items.append(("Delete this fence", lambda: self.app.delete_fence(self.fid)))
        items += [None, ("GlassDesk settings", self.app.show_settings)]
        return items

    def rename(self):
        name = self.app.ask_text("Rename fence", "Fence name", self.app.organizer.fence_name(self.fid))
        if name:
            self.opt["name"] = name.strip()
            self.S.save()
            self.queue_draw()
