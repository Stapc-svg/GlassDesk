# Changelog

## 2.2.0

- **Double-click to hide everything.** Double-click an empty spot on the desktop and every fence, widget and desktop icon fades away, for a clean wallpaper in screenshots or video calls. Double-click again to bring it all back. (Settings › General › Behavior.)
- **Peek at your desktop (Ctrl + Alt + Space).** Your fences and widgets come up on top of your open apps, with the apps dimmed behind them. Open a file, press Esc, click outside or press the shortcut again and they go back behind your windows.
- **Quick settings widget.** Volume and brightness sliders, and Wi-Fi, Bluetooth, dark mode and mute buttons, right on the desktop. Scroll over it to change the volume. Brightness works on laptop screens and on monitors that allow it (DDC/CI).
- **Game stats widget.** Graphics card load (any NVIDIA, AMD or Intel GPU, the same numbers as Task Manager), graphics memory, CPU, a load graph, and the PC's temperature. NVIDIA cards also show GPU temperature and fan speed. Off by default: turn it on in Settings › Widgets.
- **What's new after every update.** When GlassDesk updates, people who already used it see a window with what's new since the version they had. New users get the welcome screen instead. Settings › About › What's new shows it again.
- **Important updates pop up.** GlassDesk now looks for updates three times a day. If a release's title says "important update", its window opens straight away instead of a quiet notification, and it can't be skipped.
- **Small, Medium and Large widgets.** Right-click any widget and pick a size, like widgets on a Mac.
- **Smart cleanup — only when you say yes.** GlassDesk can find old screenshots, installers you downloaded weeks ago, desktop files you haven't opened in two months and huge old downloads. Nothing moves until you look at the list and press **Tidy**. Files go to plain folders (Old screenshots, Old installers, Old desktop files), big files to the Recycle Bin, and **Undo** puts everything back. Tray menu › Tidy up old files…, or turn on a weekly reminder in Settings › Organizer › Smart cleanup.

## 2.1.1

- **No more false virus warnings.** Microsoft Defender and some other antivirus apps flagged 2.1.0 as malware. It wasn't: the music widget used a small hidden PowerShell script to read which song is playing, and antivirus apps treat hidden PowerShell scripts as suspicious. GlassDesk now reads the song and its cover itself, with no script and no extra program running. The music widget works the same as before.
- The old `music-helper.ps1` file is deleted from GlassDesk's settings folder the first time 2.1.1 runs.

## 2.1.0

- **Quick search in liquid glass.** When you use liquid glass, the search window (Ctrl + Alt + F) is liquid glass too, and every app sits on its own little lens of glass. While the list scrolls, the tiles use the plain look so scrolling stays smooth on slower PCs.
- **The real album cover in the music widget.** The cover of the song or video that's playing (Spotify, YouTube in your browser, any player) now shows as a square sleeve, with the vinyl record sliding out of it while it plays. Many apps send the cover a few seconds after the title, so GlassDesk now keeps checking for a few seconds instead of giving up straight away.
- **Your own clock photos.** Clock background › **My photos**, then pick a folder. The clock shows one of your pictures and changes to the next every 30 minutes. You can also right-click the clock › Choose my clock photos….
- **Roll up widgets.** Right-click any widget › **Roll up** shrinks it to a small bar with its name (the clock bar still shows the time). Click the bar to open it again. **Roll up all widgets** and **Expand all widgets** do every widget at once.

## 2.0.0

GlassDesk 2.0 brings together everything since 1.9:

- **Liquid glass:** real lens bending, frost and a thin rainbow edge. Choose Regular or Clear. Turning it on now warns you first that it uses more memory (about 100–200 MB).
- **A style for each widget** (Glass or Liquid glass), each with its own design: Notes, Reminders-style to-dos, Activity rings, gauges and more.
- **Live desktop preview and widget pictures in Settings,** and a **Widgets tab** in quick search.
- **Online calendar, every drive in System, game screenshot fences, the clipboard widget,** and Chinese, Spanish and Russian.
- **A new animated welcome screen** shows what's new: the logo pops in, the title slides in, the four cards rise one after another, and a light sweeps across each. It appears once after updating.

## 1.20.1

- **No white patch on liquid glass.** The light glint in the top-left corner and the brightening of the rim are gone, so the edges keep the wallpaper's own colors.

## 1.20.0

- **More realistic liquid glass.**
  - **Blur:** the glass is now frosted, most at the thick rounded rim and least in the flat middle, like real thick glass. **Regular** is frosted all over, like Apple's default glass; **Clear** stays sharp in the middle with soft, blurry edges.
  - **Curved distortion:** the bending now also leans toward the corners and is smoothed like the surface of a liquid. Straight lines in your wallpaper curve as they pass through the edge, instead of turning into straight stripes along the sides.
  - **No corner seams:** the diagonal crease that showed in the corners of each panel is gone.
- **Settings scrolls cleanly.** Scrolling in Settings could leave smeared, doubled rows (repeated labels). The page now redraws in one go on every scroll step.

## 1.19.0

- **Real liquid glass.** The glass now works like a thick, clear lens. Near the rounded edges you see what's just *outside* the panel, squeezed into the rim, the way a drop of water or Apple's Liquid Glass bends light. Before, it pulled from inside and mostly looked like a tinted box. Colors look a little richer through the glass, a thin rainbow fringe shows at the edges, and a small glint sits only where the rim faces the light (top left and bottom right), with no white frame.
- **Clearer glass.** Much less gray tint: the bending does the work. On bright or busy wallpapers, use Liquid glass type › **Regular**, which is softly frosted like Apple's default glass.
- **Removed** the mouse-following light and the melting bridges between panels.

## 1.18.0

Four ideas taken from Apple's Liquid Glass, rebuilt for Windows:

- **Regular and Clear liquid glass.** Settings › Appearance (or Widgets) › Liquid glass type. **Regular** bends a softly blurred picture and is a little frosted, so it's easier to read. **Clear** shows the most of your wallpaper.
- **The light follows your mouse.** As the mouse gets near a liquid glass panel, a bright light slides along the rim toward it, with a softer glint on the opposite side, like tilting an iPhone.
- **Press bounce and shimmer.** Clicking a liquid glass panel squishes it slightly, it springs back, and a band of light sweeps across it.
- **Panels melt together.** Liquid glass panels close to each other join with a liquid bridge: thin when they're a little apart, wide when they're almost touching. It follows live while you drag. Turn it off with "Melt nearby panels together".

## 1.17.3

- **Settings scrolls smoothly again.** The desktop preview and the widget pictures were redrawing every panel over and over, even while you scrolled. Now:
  - they pause while the page is scrolling,
  - they only update when they are on screen,
  - each panel's picture is reused until that panel actually changes,
  - and each widget picture is shrunk once and then just copied, instead of being scaled again on every scroll step.
- **Widget pictures work for widgets that are off.** Widgets you never turned on had no size yet, so their picture stayed empty. They are now drawn at their normal size, faded.

## 1.17.2

- **Liquid glass for everything.** Settings › Appearance › Glass style now has a fifth choice, **Liquid**. It turns every panel into clear liquid glass that bends the wallpaper at its edges, fences included. Before, Liquid glass could only be picked per widget, from right-click › Style.
- The last thin white line along the top edge of panels is gone too.

## 1.17.1

- **No more white borders.** The white line around every widget and fence is gone, in both the Glass and the Liquid glass styles. Liquid glass keeps its bent edges and only a soft shine inside the glass.

## 1.17.0

- **Real liquid glass.** The macOS style is now called **Liquid glass** and works like a thick, clear pane of glass. Your wallpaper bends around the rounded edges like through a lens, colors split slightly at the rim like a prism, and light catches the edges: brightest at the top left, with a softer glint on the opposite corner. The middle stays clear.
- On bright wallpapers the glass gets slightly darker behind the text, so words stay readable. On dark wallpapers it stays completely clear.
- The bent glass is worked out once for each position and size and then reused, so it costs nothing while the widgets update.

## 1.16.0

- **See each widget in Settings, not just its name.** On the Widgets page, every widget's row has a small live picture of it next to its switch, in the style you picked (Glass or macOS). Widgets that are off are shown faded, so you can see what turning one on will add.

## 1.15.0

- **Smooth scrolling in quick search (Ctrl + Alt + F).** Scrolling used to stutter and jump a whole row at a time. Now the list glides, the glass background is drawn once instead of on every step, and icons load in the background while the window is open, so scrolling never waits for one. You can also drag the scroll bar or click its track to jump.
- **A Widgets tab in quick search.** It shows a small live picture of every widget. Click one (or press Enter) to put it on your desktop or take it off: a check means it's on, a plus means it's off. Typing filters the widgets too.

## 1.14.0

- **See your desktop in Settings.** The Appearance, Widgets and Organizer pages start with a live picture of your desktop: your wallpaper with every fence and widget exactly where it is and how it looks. Turn a widget on, switch it to the macOS style or change the glass, and the picture changes within a second. Point at a panel in the picture to see its name.

## 1.13.0

- **The macOS style is now see-through glass**, like the new "liquid glass" look on a Mac: the blurred desktop shows through, light catches the top edge, and a soft shadow lifts it off the wallpaper.
- **Every widget has its own macOS design and color**, like the Mac app it stands for:
  - **Notes** is a yellow paper note with ruled lines and a folded corner. The first line is the title.
  - **To-do** looks like Reminders: a big count of what's left and blue circles.
  - **Today** uses Activity-style rings inside each other (day, week, month, year).
  - **System** shows round gauges like the Batteries widget, one for each drive.
  - Clock in orange, calendar and countdown in red, music in pink, focus in orange, quote in purple, network in light blue, GitHub in indigo, all with titles in that color.

## 1.12.0

- **Choose a style for each widget.** Right-click any widget › Style and pick **Glass** (the GlassDesk look) or **macOS** (solid rounded cards like the widgets on a Mac). Settings › Widgets › Widget style switches all of them at once.
- In the macOS style the clock becomes a round clock face with the day and time beside it, the weather card turns into the sky (blue by day, dark at night, grey when it rains), and the calendar marks today in red.
- In the Glass style, the clock's Style menu also lets you pick Photos, Drawn sky or No picture, and the weather's Style menu turns its picture on or off.

## 1.11.0

Ideas and bug reports from the XDA thread:

- **Recycle Bin icon stays right.** It could show empty while it was full until GlassDesk was restarted. GlassDesk now watches the Recycle Bin folders on every drive and changes the icon the moment something is deleted or the bin is emptied, and it also checks the bin folders directly when Windows gives the wrong answer.
- **Every drive in the System monitor.** Internal drives and USB drives each get their own bar with free space, and the widget grows or shrinks when you plug a drive in or out. Settings › Widgets › System drives turns this off.
- **Online calendar.** Connect Google Calendar, Outlook or iCloud with the calendar's private iCal (.ics) link (right-click the calendar › Connect a calendar…). Days with events get a dot, and the next two events show under the month. It refreshes every 15 minutes, and repeating events are included.
- **Game screenshots as fences.** Settings › Organizer › Game screenshots finds the Xbox Game Bar, NVIDIA, AMD and Windows screenshot folders and adds them as folder fences in one click.

## 1.10.0

- **GlassDesk speaks your language** (asked for in issue #24): 简体中文 (Chinese), Español and Русский, besides English. It follows your Windows language automatically, or pick one in Settings › General › Language. Menus, Settings, widgets, the welcome screen, search and theme descriptions are translated, and dates follow the language too. Chinese uses Microsoft YaHei so every character shows up.
- Translations are plain text files in `assets/lang/` (English, a tab, the translation), so anyone can add or fix a language.

## 1.9.10

- **New Clipboard widget:** shows the last things you copied (links, colors, file paths and text, each with a little colored mark). Click one to copy it again. It's kept in memory only, never saved to disk, and copies from password managers are skipped. Right-click to pause it or clear the history.

## 1.9.9

- **Smooth fade-in:** panels and widgets fade in gently, one after another, instead of popping up.
- **Smooth fade-out:** Ctrl + Alt + G (show / hide panels) fades everything out before hiding it.
- **Hover glow:** a panel's edge lights up softly in your accent color and its shadow deepens while the mouse is over it.
- **Smooth scrolling in Settings:** the mouse wheel glides the page instead of jumping.
- All of this is switched off in power-saving mode.

## 1.9.8

- **Weather pictures:** the weather widget shows a sky that matches the weather: sun, a few clouds, overcast, rain, thunderstorm, snow, fog, and a starry night with a crescent moon. Turn it off in Settings › Widgets › Weather picture.
- **Half picture, half glass:** the weather picture and the clock photo cover the top of the widget and fade smoothly into the glass, so the lower part stays clear and easy to read.

## 1.9.7

- **Photo backgrounds for the clock:** free public-domain (CC0) photos that change with the time of day: green hills in the morning, snowy mountains by day, a city sunset in the evening and city lights at night. Choose **Photos**, **Drawn sky** or **Off** in Settings › Widgets › Clock background. Photo credits: assets/scenes/CREDITS.md.

## 1.9.6

- **New weather design:** city and today's high/low, a big glowing icon and temperature, glass chips for humidity, wind and chance of rain, a sunrise-to-sunset arc showing where the sun is now, and the next 5 hours.
- **Vinyl music widget:** the cover now sits on a spinning vinyl record with a tone arm that lowers while a song plays, and a soft glow in the cover's colors.
- **Sky scene clock:** the clock sits on a little landscape whose sky, sun, moon and stars follow the real time of day. Turn it off in Settings › Widgets › Sky scene.

## 1.9.5

- **No more white title bars:** the Settings window and the small pop-up boxes now use your theme's colors for their title bar. GlassDesk was misreading the Windows version (Windows reports "Windows 8" to apps unless asked properly), which also switched off dark title bars and rounded corners on Windows 10/11.
- **Weather has a backup service:** if Open-Meteo can't be reached, GlassDesk asks wttr.in instead. Errors are written to error.log in the GlassDesk folder.

## 1.9.4

- **Glass settings window:** the Settings window now shows your blurred wallpaper through it, like the desktop panels.
- **Glass scroll bar:** a thin, rounded scroll bar that matches the theme replaces the white Windows one. Drag it, click above/below it, or scroll over it.
- **Updates just work:** GlassDesk now always checks its official GitHub page for new versions. No need to type a GitHub address any more. It finds the newest Windows installer even when the latest release is for Linux or Android.
- **Recycle Bin icon fixed:** the full/empty icon now updates within a few seconds when you delete or empty files anywhere, and works on PCs where Windows reported the bin as empty by mistake.

## 1.9.3

- **Weather works with "City, Country":** typing "Tel Aviv, Israel" or "Paris France" now finds the place (before, only the plain city name worked).
- **Weather recovers by itself:** if there's no internet when the PC starts, the widget tries again every minute instead of waiting 30 minutes. Weather in a stack keeps updating too.
- **Softer theme previews:** the bars in the theme cards are see-through glass instead of bright white.

## 1.9.2

- Fixed: switching tabs in one stack made every other stack lose its tabs and come apart.

## 1.9.1

- Switching tabs in a stack: the new content now rises up into place instead of sliding in from the side.

## 1.9.0

- **All your apps in search:** Ctrl + Alt + F now lists every app installed on your PC (from the Start menu), not only your desktop. Filter with **All**, **Desktop** and **Apps**.
- **Drag apps onto fences:** drag an app from search onto any fence. GlassDesk puts a shortcut on your desktop in that fence (the app itself is never moved).
- **Blurrier search:** the search window now fully blurs what is behind it.

## 1.8.9

- Fixed: switching inside a stack could make the whole stack disappear, or show an empty or old picture for a moment. A panel is now fully drawn before it appears.

## 1.8.8

- **Smoother stack switching:** the tab highlight glides from the old tab to the new one, the content slides in smoothly, and there is no more flash or jump when you switch.

## 1.8.7

- Fixed: stacked fences and widgets sometimes showed no tabs after GlassDesk started, until you rescanned the desktop.

## Linux 2.1.0

Everything from GlassDesk 2.1 for Windows, now on Linux:

- **Quick search in liquid glass.** With Glass style › Liquid glass, the search window is liquid glass, and every app sits on its own little lens of glass.
- **The real album cover in the music widget**, as a sleeve with the vinyl record sliding out of it while a song plays. It works with any player that shares the song (Spotify, Firefox, Chrome, VLC, Rhythmbox).
- **Your own clock photos.** Clock background › **My photos**, then choose a folder (Settings, or right-click the clock › Choose my clock photos…). A new photo shows every 30 minutes.
- **Roll up widgets.** Right-click a widget › **Roll up** shrinks it to a small bar (the clock's bar shows the time). Click the bar to open it again. **Roll up all widgets** and **Expand all widgets** are in the same menu.

## Android 2.0

- **New Liquid glass design:** clear, thick glass like iOS 26. It's nearly see-through, brighter toward the rounded edge like a lens, with a soft shade inside the bottom edge and a faint prism fringe on the rim. Pick **Clear** or **Regular** (a little frosted, easier to read on busy wallpapers) under Appearance › Glass type, with the iOS accent colors.
- A **"New in GlassDesk 2.0"** card at the top of the Widgets tab, until you tap it.
- Android doesn't let home-screen widgets see the wallpaper behind them, so the widgets can't bend it like on Windows and Linux. The glass is drawn to look thick and clear instead.

## Linux 2.0.0

- **Liquid glass** comes to Linux: Settings › Appearance › Glass style › **Liquid glass**. The wallpaper bends at each panel's rounded edges like a thick lens, with frost and a thin rainbow fringe at the rim. Liquid glass type lets you choose **Regular** (a little frosted) or **Clear**. Before turning it on, GlassDesk warns that it uses about 100–200 MB more memory. It needs python3-numpy, which the .deb now recommends; without numpy, the option is hidden.
- **Animated welcome screen** for 2.0, shown once after updating.
- Includes everything from Linux 1.9.8–1.9.10 (new weather, clock backgrounds, vinyl music, clipboard, fades).

## Linux 1.9.10

- **New Clipboard widget:** the last things you copied; click one to copy it again. Memory only, never saved; copies marked private by password managers are skipped. Right-click to pause or clear.

## Linux 1.9.9

- **Smooth fade-in** of panels and widgets, one after another; **fade-out** before hiding them all.
- **Hover glow:** the edge lights up softly in your accent color and the shadow deepens under the mouse.
- Needs a compositor (GNOME, KDE, Cinnamon…); without one, panels simply appear.

## Linux 1.9.8

Everything from Windows 1.9.3–1.9.8, now on Linux:

- **New weather widget:** city with today's high/low, a big glowing icon and temperature, glass chips for humidity, wind and chance of rain, a sunrise-to-sunset arc, and the next 5 hours.
- **Weather pictures:** a drawn sky that matches the weather (sun, clouds, rain, storm, snow, fog, starry night) covers the top of the widget and fades into the glass. Settings › Widgets › Weather picture.
- **Weather that works more often:** "City, Country" names are found, it retries every minute after an error, and wttr.in is used when Open-Meteo can't be reached.
- **Clock backgrounds:** free CC0 photos for morning, day, sunset and night that fade into the glass, or a drawn sky with sun, moon, stars and mountains. Settings › Widgets › Clock background.
- **Vinyl music widget:** the cover spins on a vinyl record while a song plays, with a tone arm and a glow in the cover's colors.
- **Glass Settings window:** the blurred wallpaper shows through the Settings window (with a compositor), with a thin glass scroll bar.
- **Softer theme previews:** see-through bars instead of bright white.
- **Updates:** the update check finds the newest release that has a Linux download, even when the latest release is for Windows or Android.

## Linux 1.9.2

- **All your apps in search:** the search window lists every app in your app menu, not only your desktop. Filter with **All**, **Desktop** and **Apps**.
- **Drag apps onto fences:** drag an app from search onto a fence to put a launcher for it there (the app itself is never moved).
- **Blurrier search** window.
- Switching tabs in a stack: the highlight glides, the content rises up into place, and switching one stack no longer breaks the others.

## Linux 1.8.6

Everything from GlassDesk 1.8 for Windows, now on Linux:

- **26 themes** in a gallery (Settings → Appearance), including Windows themes like *Mica material* and *Acrylic material*. Settings and right-click menus follow your theme. **Match my wallpaper** takes the accent color from your wallpaper.
- **File stacks:** 4+ files of one type fold into a stack inside their fence.
- **Stacks of fences and widgets:** drag one onto another, then click the tabs to switch, with a slide animation. A stack keeps one size.
- **Music widget** (any MPRIS player: Spotify, Firefox, Chrome, VLC, Rhythmbox…) and **Today widget**.
- **New search launcher** with a calculator, theme switching, commands and web search.
- **Organize desktop** preview, **resizable widgets**, fences that resize from any edge, panels kept on screen, and **a layout for each monitor setup**.
- A **welcome screen** the first time GlassDesk starts.

## 1.8.6

- **Switch animation:** switching tabs in a stack slides the new widget or fence in with a fade.
- **Same size:** all widgets in a stack are now the same size (the biggest one).
- Switching tabs is faster.

## 1.8.5

- **Stacked widgets show tabs** too, like stacked fences. Click a tab to switch. The dots and mouse-wheel switching are gone.

## 1.8.4

- **Stacked fences show tabs:** the title shows a tab for every fence in the stack. Click a tab to switch. Every fence in a stack keeps the same size.

## 1.8.3

- **Drag to stack:** drag a widget onto another widget (or a fence onto another fence) and GlassDesk offers to stack them. Click the top of a stack or its dots to flip to the next one; widgets also flip with the mouse wheel.
- **Fence stacks:** fences can be stacked too.
- **Acrylic material** now blurs the wallpaper fully.

## 1.8.2

- **Smarter search:** type a sum (12*(3+4)) to get the answer, a theme name to switch to it, a command (settings, organize, dark, light, new fence), or press Ctrl + Enter to search the web.
- **Today widget:** four rings showing how much of the day, week, month and year has passed.
- **Match my wallpaper:** the accent color can come from your wallpaper and change when it does (Settings → Appearance).

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
