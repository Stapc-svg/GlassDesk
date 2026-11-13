"""Quick search: type a few letters, press Enter to open."""
from gi.repository import Gtk, Gdk

from . import core


CSS = b"""
.gd-search { background: rgba(22,24,32,0.94); border-radius: 16px; border: 1px solid rgba(255,255,255,0.18); }
.gd-search entry { font-size: 17px; padding: 10px 14px; border-radius: 10px; background: rgba(255,255,255,0.08); color: #f4f6fc; border: none; box-shadow: none; }
.gd-search list, .gd-search row { background: transparent; color: #f4f6fc; }
.gd-search row:selected { background: rgba(124,156,255,0.35); border-radius: 8px; }
.gd-search .hint { color: rgba(220,226,240,0.6); font-size: 12px; }
"""


class SearchWindow(Gtk.Window):
    def __init__(self, app):
        super().__init__(title="Find on desktop")
        self.app = app
        vis = self.get_screen().get_rgba_visual()
        if vis:
            self.set_visual(vis)
        self.set_app_paintable(True)
        self.set_decorated(False)
        self.set_keep_above(True)
        self.set_skip_taskbar_hint(True)
        self.set_type_hint(Gdk.WindowTypeHint.DIALOG)
        self.set_position(Gtk.WindowPosition.CENTER_ALWAYS)
        self.set_default_size(560, 380)
        prov = Gtk.CssProvider()
        prov.load_from_data(CSS)
        Gtk.StyleContext.add_provider_for_screen(self.get_screen(), prov, Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION)
        box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=8)
        box.get_style_context().add_class("gd-search")
        box.set_border_width(14)
        self.entry = Gtk.Entry(placeholder_text="Find on your desktop…")
        self.entry.connect("changed", lambda *a: self.fill())
        self.entry.connect("activate", lambda *a: self.open_selected())
        self.list = Gtk.ListBox()
        self.list.connect("row-activated", lambda lb, row: self.open_row(row))
        sw = Gtk.ScrolledWindow()
        sw.set_vexpand(True)
        sw.add(self.list)
        hint = Gtk.Label(label="Enter to open  ·  ↑↓ to choose  ·  Esc to close", xalign=0)
        hint.get_style_context().add_class("hint")
        box.pack_start(self.entry, False, False, 0)
        box.pack_start(sw, True, True, 0)
        box.pack_start(hint, False, False, 0)
        self.add(box)
        self.connect("key-press-event", self.on_key)
        self.connect("focus-out-event", lambda *a: self.hide())
        self.connect("draw", self._clear)
        self.items = []

    def _clear(self, w, cr):
        cr.set_source_rgba(0, 0, 0, 0)
        import cairo
        cr.set_operator(cairo.OPERATOR_SOURCE)
        cr.paint()
        cr.set_operator(cairo.OPERATOR_OVER)
        return False

    def open(self):
        self.items = self.app.organizer.all_items()
        self.entry.set_text("")
        self.fill()
        self.show_all()
        self.present_with_time(Gtk.get_current_event_time() or 0)
        self.entry.grab_focus()

    def fill(self):
        q = self.entry.get_text().strip().lower()
        for c in self.list.get_children():
            self.list.remove(c)
        hits = [it for it in self.items if not q or q in it.name.lower()]
        hits.sort(key=lambda it: (not it.name.lower().startswith(q), it.name.lower()))
        for it in hits[:40]:
            row = Gtk.ListBoxRow()
            row.path = it.path
            hb = Gtk.Box(spacing=10)
            hb.set_border_width(6)
            pb = it.icon(28)
            if pb is not None:
                hb.pack_start(Gtk.Image.new_from_pixbuf(pb), False, False, 0)
            lb = Gtk.Label(label=it.name, xalign=0)
            lb.set_ellipsize(3)
            hb.pack_start(lb, True, True, 0)
            row.add(hb)
            self.list.add(row)
        self.list.show_all()
        first = self.list.get_row_at_index(0)
        if first:
            self.list.select_row(first)

    def open_row(self, row):
        if row is not None:
            core.open_path(row.path)
            self.hide()

    def open_selected(self):
        self.open_row(self.list.get_selected_row())

    def on_key(self, w, e):
        k = e.keyval
        if k == Gdk.KEY_Escape:
            self.hide()
            return True
        if k in (Gdk.KEY_Down, Gdk.KEY_Up):
            row = self.list.get_selected_row()
            i = row.get_index() if row else -1
            i = i + (1 if k == Gdk.KEY_Down else -1)
            nxt = self.list.get_row_at_index(max(0, i))
            if nxt:
                self.list.select_row(nxt)
            return True
        return False
