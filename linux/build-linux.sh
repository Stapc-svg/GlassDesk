#!/bin/sh
# Builds dist/glassdesk_<v>_all.deb, dist/GlassDesk-<v>-x86_64.AppImage and dist/GlassDesk-<v>-linux.tar.gz
set -e
cd "$(dirname "$0")"
V=$(python3 -c "import re;print(re.search(r'VERSION = \"([^\"]+)\"', open('glassdesk/core.py').read()).group(1))")
rm -rf dist build && mkdir -p dist build
find glassdesk -name __pycache__ -prune -exec rm -rf {} +
ICON=glassdesk/icons/glassdesk.png

# ---------- .deb (Ubuntu, Mint, Debian, Pop!_OS, Zorin, elementary...)
D=build/deb
mkdir -p $D/DEBIAN $D/usr/bin $D/usr/lib/glassdesk $D/usr/share/applications $D/usr/share/doc/glassdesk
cp -r glassdesk $D/usr/lib/glassdesk/
install -m755 packaging/glassdesk.sh $D/usr/bin/glassdesk
install -m644 packaging/glassdesk.desktop $D/usr/share/applications/
for s in 48 64 128 256 512; do
  mkdir -p $D/usr/share/icons/hicolor/${s}x${s}/apps
  convert $ICON -resize ${s}x${s} $D/usr/share/icons/hicolor/${s}x${s}/apps/glassdesk.png 2>/dev/null || cp $ICON $D/usr/share/icons/hicolor/${s}x${s}/apps/glassdesk.png
done
cp README.md $D/usr/share/doc/glassdesk/README.md
SIZE=$(du -sk $D/usr | cut -f1)
cat > $D/DEBIAN/control <<CTL
Package: glassdesk
Version: $V
Section: utils
Priority: optional
Architecture: all
Depends: python3 (>= 3.8), python3-gi, python3-gi-cairo, gir1.2-gtk-3.0
Recommends: gir1.2-ayatanaappindicator3-0.1, gir1.2-keybinder-3.0, libnotify-bin
Installed-Size: $SIZE
Maintainer: Stapc-svg <noreply@github.com>
Homepage: https://github.com/Stapc-svg/GlassDesk
Description: Glass desktop organizer with fences and widgets
 GlassDesk sorts the files on your desktop into frosted-glass fences
 (Apps, Folders, Documents, Images...) and adds widgets: clock, system
 monitor, calendar, notes, to-do, weather, music, GitHub stats, network
 meter and a focus timer, with 26 themes. Free and open source.
CTL
cat > $D/DEBIAN/postinst <<'PI'
#!/bin/sh
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database -q /usr/share/applications || true
command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -q /usr/share/icons/hicolor || true
exit 0
PI
chmod 755 $D/DEBIAN/postinst
find $D -type d -exec chmod 755 {} +
find $D/usr -type f -exec chmod 644 {} + ; chmod 755 $D/usr/bin/glassdesk
dpkg-deb --root-owner-group --build $D dist/glassdesk_${V}_all.deb >/dev/null

# ---------- tar.gz with install.sh (Fedora, Arch, openSUSE, anything)
T=build/GlassDesk-$V-linux
mkdir -p $T/icons
cp -r glassdesk $T/
cp packaging/glassdesk.sh packaging/glassdesk.desktop packaging/install.sh packaging/uninstall.sh $T/
cp README.md $T/README.md
for s in 48 64 128 256; do convert $ICON -resize ${s}x${s} $T/icons/glassdesk-$s.png; done
tar -C build -czf dist/GlassDesk-$V-linux.tar.gz GlassDesk-$V-linux

# ---------- AppImage (one file, runs on most distros that have Python GTK)
A=build/GlassDesk.AppDir
mkdir -p $A/usr/bin $A/usr/lib/glassdesk
cp -r glassdesk $A/usr/lib/glassdesk/
install -m755 packaging/glassdesk.sh $A/usr/bin/glassdesk
ln -s usr/bin/glassdesk $A/AppRun
cp packaging/glassdesk.desktop $A/glassdesk.desktop
convert $ICON -resize 256x256 $A/glassdesk.png
ln -s glassdesk.png $A/.DirIcon
if [ -x "${APPIMAGETOOL:-/tmp/appimagetool}" ]; then
  ARCH=x86_64 "${APPIMAGETOOL:-/tmp/appimagetool}" --appimage-extract-and-run -n $A dist/GlassDesk-$V-x86_64.AppImage >/dev/null 2>&1 || echo "AppImage step failed"
fi
ls -la dist
