# Changelog

## 1.8.1

- **Stacks:** 4 or more files of the same type in a fence become one stack. Click it to open or close it.
- **Widget groups:** right-click a widget → Group with. Grouped widgets share one spot; scroll over it or click the dots to switch.
- **Organize desktop:** a preview of where every file goes, with a tick for each one. Unticked files go to an "Unsorted" fence. Open it from the tray menu or Settings → Organizer.
- **Layouts per monitor setup:** GlassDesk remembers a layout for each set of monitors and brings it back when you dock or undock.

- **New search:** Ctrl + Alt + F opens a launcher with a grid of icons on the blurred wallpaper, drawn in your theme.
- **Music widget:** shows the song playing in any app (Spotify, YouTube, Apple Music...) with its cover, progress and play / pause / next buttons. When nothing plays, it shows the last song you listened to.
- **Welcome screen:** shown once, the first time GlassDesk starts. It never comes back, even after updates.
- **Themed menus:** right-click menus and the Settings window take their colors from your theme.

- **Windows themes:** 10 new themes in their own section: Mica material, Acrylic material, Mica Alt, Windows Light, Windows Dark, Bloom, Glow, Sunrise, Flow and Captured Motion.
- Fixed: theme names in Settings were too big. Panels past the screen edge move back on screen. Fences resize from the right edge, bottom edge or corner.
- **16 themes** in a new gallery in Settings → Appearance: GlassDesk, Clean, Midnight, Ocean, Sunset, Forest, Rose, Aurora, Frost, Ember, Lavender, Mint, Neon, Graphite, Paper and Classic
- Every theme has its own glass colors for dark and light mode; Auto mode, accent color, glass style, tint and corners still work on top
- New effects: a soft accent glow inside the glass, a glowing neon edge (Neon), and solid flat panels (Clean, Paper)
- GlassDesk is the new default look; people updating keep their current look as "Classic"
- **Resize widgets:** drag a widget's bottom-right corner and everything inside grows or shrinks with it (60% to 220%). Right-click → Reset size, or Settings → Widgets → Reset all sizes
- **A more premium Settings window:** glowing sidebar, a colorful banner on every page, softer cards, and gradient switches, sliders and buttons in your accent color

## Linux (beta) fixes
- Panels that ended up off-screen (after changing the resolution or unplugging a monitor) now come back on screen, and GlassDesk re-arranges itself when the screen changes
- The Photo frame now shows your photo right away at startup, and when you turn it on after choosing a folder
- Broken shortcuts (links to deleted files) show up in the Other fence again, so you can delete them

## 1.7.2 for Android
- **GlassDesk for Android:** 11 home-screen widgets: Clock, System, Calendar, Notes, To-do, Weather, Quote, Countdown and three Fences of up to 8 apps
- **Two designs:** GlassDesk (frosted glass with a soft glow, the default) and Clean (bright and simple), each with light and dark themes and accent colors
- A settings app with live previews that follows the design you pick
- Tick off to-dos, open apps, refresh the weather and change the quote right from the home screen

## 1.7.2
- **GlassDesk for Linux (beta):** fences, all 12 widgets, glass blur, settings, search, tray icon, backup and start at login, as a .deb, an AppImage and a tar.gz installer
- New glassy logo and app icon

## 1.7.1
- Fixed: a grid of tiny squares on the glass panels (the grain texture was removed).

## 1.7.0
- New **GitHub stats** widget: stars, followers, repos, top repo and 14-day activity.
- New **Network meter** widget: live download/upload speed.
- New **Focus timer** widget: Pomodoro with notifications.

## 1.6.1
- Sharp, large icons in fences (no more blocky icons or big shortcut-arrow boxes)

## 1.6.0
- **Blur that always works:** GlassDesk now blurs your wallpaper itself and paints it behind every panel, instead of relying on Windows' blur (which didn't show on many PCs)
- The blur follows panels while you drag them and updates when your wallpaper changes (including slideshows and the wallpaper changer)
- Panels keep their soft shadow in every glass style

## 1.5.0
- **Backup & sync:** export your whole layout to a `.glassdesk` file and import it on any PC; automatic daily backups (last 7 kept)
- **Themes:** Ocean, Sunset, Forest, Midnight, Frost and Rose in one click
- **Wallpaper changer:** rotates your desktop background from a folder
- **Fence upgrades:** a color for each fence, small / medium / large icons, and "Roll up until I point at it"
- **New widgets:** Countdown and Quote of the day

## 1.4.1
- New **See-through** glass style: no blur, the wallpaper shows clearly, text gets a soft shadow

## 1.4.0
- **Real frosted glass:** panels now use Windows' acrylic blur (the Start menu's glass), so your wallpaper shows through blurred
- New glass drawing: light rim that catches the top-left and bottom-right corners, a diagonal sheen, a soft inner glow and fine frosted grain
- **Glass style** setting: Frosted, Light blur or Clear (Settings → Appearance)
- Clearer default tint (30%); "Glass opacity" is now called "Glass tint"
- Panels switch to the lighter blur while you drag them, so moving stays smooth

## 1.3.0
- **Rules:** send files to a fence by name, for example `*.pdf` goes to School or `Screenshot*` goes to Images
- **Sort by** name, type, date modified or size, for each fence (right-click the fence title)
- **Quick search:** press Ctrl + Alt + F, type a few letters, press Enter to open
- **Weather widget:** today and the next 3 days for your city, from Open-Meteo (free, no account)
- **To-do widget:** tick tasks off on the desktop; double-click to edit the list
- **Photo frame widget:** shows pictures from a folder, one at a time
- **Auto-updater:** checks GitHub Releases once a day and installs new versions with one click

## 1.2.0
- Make your own fences and drag icons into them
- Drag & drop: between fences, onto folders (moves the file), in from / out to File Explorer, onto the Recycle Bin
- Right-click an icon → **Move to fence**
- Windows desktop icons are hidden by default so fences don't overlap them; Recycle Bin lives in a fence
- New fences are placed in free space on the screen
- Fixed: system monitor (CPU / RAM / disk / battery) not updating
- Fixed: Organizer settings page showing blank
- `.html` files now sort into Documents

## 1.1.1
- Shows a message with the reason instead of closing silently, and writes `%AppData%\GlassDesk\error.log`
- More reliable fonts on Windows 11

## 1.1.0
- MSI installer (per-user, no admin needed)
- Starts automatically with Windows

## 1.0.0
- First release: glass fences, clock, system monitor, calendar and notes widgets, settings window
