#!/bin/sh
# GlassDesk launcher: finds the app files and a Python 3 that has GTK.
HERE="$(dirname "$(readlink -f "$0")")"
for d in "$GLASSDESK_HOME" "$HERE/../lib/glassdesk" "$HERE/../share/glassdesk" "$HERE"; do
  [ -n "$d" ] && [ -f "$d/glassdesk/app.py" ] && APPDIR="$(cd "$d" && pwd)" && break
done
for py in python3 python3.13 python3.12 python3.11 python3.10 python3.9 python3.8; do
  if command -v "$py" >/dev/null 2>&1 && "$py" -c "import gi; gi.require_version('Gtk','3.0'); from gi.repository import Gtk; import cairo" >/dev/null 2>&1; then
    exec env PYTHONPATH="$APPDIR${PYTHONPATH:+:$PYTHONPATH}" "$py" -m glassdesk "$@"
  fi
done
MSG="GlassDesk needs Python 3 with GTK. Install it with one of these, then start GlassDesk again:

Ubuntu / Mint / Debian:  sudo apt install python3-gi python3-gi-cairo gir1.2-gtk-3.0
Fedora:                  sudo dnf install python3-gobject gtk3
Arch / Manjaro:          sudo pacman -S python-gobject gtk3
openSUSE:                sudo zypper install python3-gobject-Gdk typelib-1_0-Gtk-3_0"
echo "$MSG" >&2
if command -v zenity >/dev/null 2>&1; then zenity --error --title=GlassDesk --text="$MSG" 2>/dev/null
elif command -v kdialog >/dev/null 2>&1; then kdialog --title GlassDesk --error "$MSG"
elif command -v notify-send >/dev/null 2>&1; then notify-send GlassDesk "GlassDesk needs Python 3 with GTK (python3-gi). See the terminal."
fi
exit 1
