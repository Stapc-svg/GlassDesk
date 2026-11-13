#!/bin/sh
"$HOME/.local/bin/glassdesk" --quit >/dev/null 2>&1 || true
rm -rf "$HOME/.local/share/glassdesk" "$HOME/.local/bin/glassdesk" "$HOME/.local/share/applications/glassdesk.desktop" \
       "$HOME/.config/autostart/glassdesk.desktop"
for s in 48 64 128 256; do rm -f "$HOME/.local/share/icons/hicolor/${s}x${s}/apps/glassdesk.png"; done
echo "GlassDesk removed. Your settings are kept in ~/.config/glassdesk (delete that folder to remove them too)."
