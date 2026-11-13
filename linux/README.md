# GlassDesk for Linux (beta)

> **Beta:** this is the first Linux release. If something doesn't work, please open an issue on GitHub and attach `~/.config/glassdesk/error.log`.

Glass fences and widgets for your Linux desktop: the same app as GlassDesk for Windows, rebuilt for Linux with Python and GTK.

Works on **Ubuntu, Linux Mint, Debian, Pop!_OS, Zorin, Fedora, Arch / Manjaro, openSUSE** and others, with **GNOME, KDE Plasma, Cinnamon, XFCE, MATE** and most window managers.

## Install

Pick one:

| Your Linux | Download | How |
|---|---|---|
| Ubuntu, Mint, Debian, Pop!_OS, Zorin | `glassdesk_1.8.6_all.deb` | Double-click it, or `sudo apt install ./glassdesk_1.8.6_all.deb` |
| Any distro (one file) | `GlassDesk-1.8.6-x86_64.AppImage` | Right-click → Properties → *Allow executing*, then double-click. Or `chmod +x GlassDesk-*.AppImage && ./GlassDesk-*.AppImage` |
| Fedora, Arch, openSUSE, others | `GlassDesk-1.8.6-linux.tar.gz` | Unzip it, open a terminal in the folder, run `./install.sh` |

GlassDesk uses Python 3 with GTK 3, which almost every Linux desktop already has. If yours doesn't, the app tells you the one command to run:

```sh
sudo apt install python3-gi python3-gi-cairo gir1.2-gtk-3.0          # Ubuntu / Mint / Debian
sudo dnf install python3-gobject gtk3                                 # Fedora
sudo pacman -S python-gobject gtk3                                    # Arch / Manjaro
```

**Optional extras** (Ubuntu/Mint): `sudo apt install gir1.2-ayatanaappindicator3-0.1 gir1.2-keybinder-3.0` adds the tray icon and the Ctrl + Alt + F / Ctrl + Alt + G shortcuts. On GNOME, the tray icon also needs the *AppIndicator* extension (preinstalled on Ubuntu).

## What you get

- **Fences** sort `~/Desktop` into Apps, Folders, Documents, Images, Music & Video, Archives and Other files, live as files change.
- **Your own fences**, **folder fences** (show any folder, like Downloads), **rules** (`*.pdf` → School), sort, colors, icon sizes, roll up, "roll up until I point at it".
- **File stacks:** 4 or more files of the same type fold into one stack. Click it to open it.
- **Stack anything:** drag a fence onto a fence, or a widget onto a widget, and GlassDesk offers to stack them. Each stack gets tabs and a slide animation, and keeps one size.
- **Drag & drop** between fences, onto folder icons (moves the file), in from your file manager, and out to other apps.
- **14 widgets**: clock, system monitor, calendar, notes, to-do, weather, photo frame, countdown, quote of the day, GitHub stats, network meter, focus timer, **music** (what's playing in Spotify, Firefox, Chrome, VLC, Rhythmbox… through MPRIS, or your last song) and **Today** (day, week, month and year rings).
- **Resize** widgets from their corner (everything inside grows with them) and fences from any edge.
- **26 themes**, including Windows themes like *Mica material* and *Acrylic material*, in a gallery in Settings → Appearance. Settings and right-click menus follow your theme. **Match my wallpaper** takes the accent color from your wallpaper.
- **Search launcher** (`glassdesk --search` or Ctrl + Alt + F): your desktop as a grid of icons, plus a calculator, theme switching, commands and web search.
- **Organize desktop:** preview where every file goes and tick the ones to sort (tray menu).
- **A layout for each monitor setup**, so plugging a monitor in or out puts everything back.
- **Real frosted glass**: GlassDesk blurs your wallpaper itself, so it works on every desktop.
- A **welcome screen** the first time, wallpaper changer, backup & import, start at login.

## Using it

- Drag a panel's title (fences) or anywhere (widgets) to move it. Drag a fence's right or bottom edge to resize it, or a widget's bottom-right corner.
- Drop a panel onto another of the same kind to stack them, then click the tabs to switch.
- Double-click a fence title to roll it up. Double-click an icon to open it.
- Right-click anything for options. Settings is in the tray menu and every right-click menu.
- Commands: `glassdesk --search`, `glassdesk --toggle`, `glassdesk --settings`, `glassdesk --quit`.
  Without the optional keybinder package, add `glassdesk --search` as a custom shortcut in your system's keyboard settings.

## Good to know

- **Wayland:** GlassDesk runs through XWayland (automatic) because Wayland doesn't let apps place their own windows. Everything works the same.
- **Desktop icons:** Settings → General → *Hide the normal desktop icons* works on GNOME (Desktop Icons NG), Cinnamon, MATE and XFCE. On KDE, remove the desktop folder view yourself if you want.
- Settings live in `~/.config/glassdesk/`. Errors are written to `~/.config/glassdesk/error.log`.

**Uninstall:** `sudo apt remove glassdesk` (.deb), delete the AppImage, or run `./uninstall.sh` (tar.gz).
