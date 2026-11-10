#!/bin/sh
# GlassDesk for Linux - installs for your user only (no sudo needed for GlassDesk itself).
set -e
SRC="$(cd "$(dirname "$0")" && pwd)"
LIB="$HOME/.local/share/glassdesk"
BIN="$HOME/.local/bin"
echo "Installing GlassDesk to $LIB"
mkdir -p "$LIB" "$BIN" "$HOME/.local/share/applications"
rm -rf "$LIB/glassdesk"
cp -r "$SRC/glassdesk" "$LIB/"
cp "$SRC/glassdesk.sh" "$LIB/glassdesk.sh"; chmod +x "$LIB/glassdesk.sh"
ln -sf "$LIB/glassdesk.sh" "$BIN/glassdesk"
for s in 48 64 128 256; do
  mkdir -p "$HOME/.local/share/icons/hicolor/${s}x${s}/apps"
  cp "$SRC/icons/glassdesk-$s.png" "$HOME/.local/share/icons/hicolor/${s}x${s}/apps/glassdesk.png"
done
sed "s|^Exec=glassdesk|Exec=$BIN/glassdesk|" "$SRC/glassdesk.desktop" > "$HOME/.local/share/applications/glassdesk.desktop"
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$HOME/.local/share/applications" >/dev/null 2>&1 || true
command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -q "$HOME/.local/share/icons/hicolor" >/dev/null 2>&1 || true

# GTK for Python is preinstalled on most desktops; install it if it's missing.
if ! python3 -c "import gi; gi.require_version('Gtk','3.0'); from gi.repository import Gtk; import cairo" >/dev/null 2>&1; then
  echo "GlassDesk needs Python GTK. Installing it (you may be asked for your password)..."
  if command -v apt >/dev/null 2>&1; then sudo apt install -y python3-gi python3-gi-cairo gir1.2-gtk-3.0
  elif command -v dnf >/dev/null 2>&1; then sudo dnf install -y python3-gobject gtk3
  elif command -v pacman >/dev/null 2>&1; then sudo pacman -S --needed --noconfirm python-gobject gtk3
  elif command -v zypper >/dev/null 2>&1; then sudo zypper install -y python3-gobject-Gdk typelib-1_0-Gtk-3_0
  else echo "Please install Python 3 GObject (python3-gi) and GTK 3 with your package manager."; fi
fi
# Optional extras: tray icon and global shortcuts
if command -v apt >/dev/null 2>&1; then
  echo "Optional: for the tray icon and Ctrl+Alt+F shortcut run:"
  echo "  sudo apt install gir1.2-ayatanaappindicator3-0.1 gir1.2-keybinder-3.0"
fi
echo
echo "Done! Start GlassDesk from your app menu, or run: glassdesk"
nohup "$BIN/glassdesk" >/dev/null 2>&1 &
