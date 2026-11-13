# -*- coding: utf-8 -*-
"""Builds docs/TaskbarStats-Manual-en.pdf (English, A4). Dutch version: make_handleiding.py.

Usage (from the repo folder):  python docs/handleiding/make_manual_en.py
Requires reportlab and Pillow. The layout lives in manual_common.py.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from manual_common import L, H1, H2, P, B, N, box, codeblock, table, fig, side_by_side, build, \
    h1, toc1, toc2, small, bullet, md, lead, VERSION, ROOT
from reportlab.lib.units import cm
from reportlab.platypus import NextPageTemplate, PageBreak, Paragraph, KeepTogether, Spacer
from reportlab.platypus.tableofcontents import TableOfContents

OUT = os.path.join(ROOT, "docs", "TaskbarStats-Manual-en.pdf")
L.update(tip="Tip", note="Note", title="TaskbarStats - Manual",
         subject=f"Guide, installation, tips and tricks (version {VERSION})", cover_title="Manual",
         cover_lines=["Guide, installation, settings, tips and tricks", "for the lightweight Windows system monitor",
                      "next to the clock on your taskbar."],
         cover_foot=f"Version {VERSION}   ·   taskbarstats.com   ·   MIT licence",
         header=f"Manual  ·  version {VERSION}")

s = []
s += [NextPageTemplate("page"), PageBreak()]

# ---- contents
s.append(Paragraph("Contents", h1))
toc = TableOfContents()
toc.levelStyles = [toc1, toc2]
toc.dotsMinLevel = 0
s.append(toc)

# ---- 1. Introduction
s += H1("1. What is TaskbarStats?", new_page=True)
s.append(P("TaskbarStats is a small, lightweight system monitor for Windows 10 and 11. A tiny widget floats right next to the "
           "notification area of your taskbar and shows live **CPU, GPU, memory, network, disks, battery, FPS and temperatures**. "
           "Want more? There is a large desktop dashboard and a fullscreen \"cockpit\" with details, graphs and the full "
           "specifications of your PC.", lead))
s += fig("widget-digital.png", "The widget next to the clock: numbers, gauges, bars or graphs (all values in the screenshots are made up).")
s.append(H2("The three parts"))
s += table([
    ["Part", "What it is", "Open with"],
    ["Taskbar widget", "Small, always visible, next to the notification area. Tooltip with all the details.", "Starts automatically"],
    ["Desktop dashboard", "Large translucent window with tiles and graphs of the last 5 minutes; in front or behind your windows, click-through.",
     "Menu, or **Ctrl+Alt+D** (click-through)"],
    ["Fullscreen cockpit", "One dense overview across a whole screen; click a tile for depth. Includes a specifications page and an automatic tour.",
     "**Ctrl+Alt+F**"],
], [0.24, 0.52, 0.24])
s.append(H2("Why the numbers are right"))
s.append(P("The values come from the same sources as Task Manager, so you can compare them. Only the temperatures come from a "
           "separate library (LibreHardwareMonitor)."))
s += table([
    ["Item", "Source", "Remark"],
    ["CPU", "Windows performance counters (`Processor Information`)", "Same value as Task Manager. Optionally the boost-scaled "
                                                                     "\"Utility\" measure (almost 2× higher on a boosting CPU)."],
    ["Memory", "`GlobalMemoryStatusEx`", "Exactly the percentage Task Manager shows."],
    ["GPU", "`GPU Engine` performance counters", "Per GPU the busiest engine, in line with Task Manager. With two GPUs "
                                                 "(iGPU + dGPU) *Automatic* follows the busiest one."],
    ["Network", "`Network Interface`", "Per adapter or all adapters combined."],
    ["Temperature", "LibreHardwareMonitor", "Needs administrator rights. Without a CPU sensor the app falls back to the Windows "
                                            "thermal zone."],
    ["Ping", ".NET `Ping`", "Latency, jitter and packet loss to a target of your choice."],
    ["Specifications", "WMI, registry, Win32", "For the specifications page in fullscreen."],
], [0.17, 0.33, 0.50])
s += box("TaskbarStats is frugal: about 2% of one CPU core and roughly 60 MB of memory at rest. When the widget is hidden and the "
         "dashboard and fullscreen are closed, the app only measures once every 5 seconds.")

# ---- 2. Installation
s += H1("2. Installation")
s.append(H2("What you need"))
s += B(["**Windows 10 or 11**, 64-bit (x64).",
        "No separate .NET installation: the installer contains everything (a self-contained, compressed build).",
        "**Administrator rights.** They are needed for the temperature sensors; Windows asks once while installing.",
        "No account and no internet connection needed (only the optional plugins and the optional update check use the internet)."])
s.append(H2("Step by step"))
s += N(["Go to **github.com/ericbruggema/TaskbarStats/releases/latest** (or via **taskbarstats.com**, *Download* button).",
        "Under *Assets*, download `TaskbarStats-Setup-<version>.exe` and run it.",
        "Confirm the Windows prompt (UAC). If the file is not digitally signed, SmartScreen may warn you: "
        "choose **More info → Run anyway**.",
        "Pick the installer language (Dutch or English).",
        "If TaskbarStats is already installed, the installer tells you whether this is an **update**, a **reinstall** or an older version "
        "and asks Yes/No before overwriting.",
        "Read the Readme (explanation and credits) that the wizard shows.",
        "Pick the extra tasks: **desktop shortcut** and **optional plugins** (both off by default).",
        "On the *Start with Windows* page choose whether the app starts at logon (default **Yes** for a new installation; "
        "an update keeps your current choice).",
        "Click *Finish*: TaskbarStats starts and the widget appears left of the notification area. On the first start you see a welcome screen."])
s.append(Spacer(1, 4))
s.append(P("The Start menu gets a **TaskbarStats** folder with the app, the Readme, *Uninstall TaskbarStats* and shortcuts to the "
           "website and GitHub."))
s.append(H2("Start with Windows"))
s.append(P("Because the app asks for administrator rights, the normal Windows Run key does not work. TaskbarStats therefore creates a "
           "**scheduled task \"at logon\" with highest privileges**: you get no UAC prompt when you log in. You can switch it on or off later under "
           "*Settings → General → Start with Windows*."))
s.append(H2("Silent installation (for admins and scripts)"))
s += codeblock("""TaskbarStats-Setup-1.6.8.exe /SILENT /AUTOSTART=yes
TaskbarStats-Setup-1.6.8.exe /VERYSILENT /AUTOSTART=no /TASKS="plugins"
""")
s += table([
    ["Parameter", "Meaning"],
    ["`/SILENT`, `/VERYSILENT`", "Install without the wizard (no overwrite questions)."],
    ["`/AUTOSTART=yes|no`", "Set start with Windows on or off."],
    ["`/TASKS=\"plugins\"`", "Also install the optional plugins."],
    ["`/DIR=...`, `/GROUP=...`", "Custom folder / Start menu group. Ignored when there is an existing installation."],
], [0.32, 0.68])
s.append(H2("Updating"))
s.append(P("Download the new installer and run it: the old version is closed and replaced, and your settings stay. Want to be told about new "
           "versions? Turn on the **update check** (off by default; *Settings → General*). It looks at GitHub at most once every 24 hours, "
           "only reports that something newer exists, and never downloads or installs anything."))
s.append(H2("Uninstalling"))
s.append(P("Start menu → TaskbarStats → *Uninstall TaskbarStats*, or *Settings → Apps*. The uninstaller removes the autostart task and asks "
           "(Yes/No) whether your settings and usage log in `%AppData%\\TaskbarStats` may be removed as well."))
s.append(H2("Portable use (for example on a USB stick)"))
s.append(P("Create an empty file `portable.txt` next to `TaskbarStats.exe`. Settings, themes, language files and logs then live in the "
           "`data` folder next to it and nothing is written to `%AppData%`."))
s.append(H2("Building it yourself"))
s.append(P("With the .NET 8 SDK installed, double-click **`build.bat`** (it closes the running app, builds Release and starts it again). Manually:"))
s += codeblock("""dotnet build -c Release          # exe: bin\\Release\\net8.0-windows\\TaskbarStats.exe
build-installer.bat              # self-contained publish + Inno Setup -> installer\\Output""")

# ---- 3. Using it
s += H1("3. Using the widget")
s.append(P("The widget is a floating window owned by the taskbar, so it stays neatly on top of it. Everything you can do with it:",
           lead))
s += table([
    ["Action", "What happens"],
    ["Drag (left mouse button)", "Moves the widget, also outside the taskbar. The position is remembered. *Lock position* fixes it."],
    ["Right-click", "Opens the menu (see below)."],
    ["Hover (≥ 2 s, adjustable)", "Tooltip with all details: CPU + clock speed, every GPU with VRAM, RAM in GB, heaviest programs, network per adapter with today's usage, disks and free space, temperatures."],
    ["Double-click", "Opens Task Manager by default (adjustable)."],
    ["Middle mouse button", "Copies all tooltip information to the clipboard (adjustable)."],
    ["Ctrl+Alt+W", "Click-through: the widget stays visible, but your clicks go straight through it."],
], [0.30, 0.70])
s += side_by_side("menu.png", "The right-click menu: short, and it stays open while you choose.",
                  "widget-compact.png", "The compact widget (labels on top, narrower).")
s.append(H2("The menu"))
s += B(["**Settings…** (bold, at the top): the settings window with tabs.",
        "**Theme**: quick pick to apply a theme with one click.",
        "**Desktop dashboard** and **Fullscreen dashboard**: each a submenu (show, click-through, lock, settings; "
        "fullscreen: open, tour, settings).",
        "**Stick to notification area**, **Widget click-through**.",
        "**Usage**: received/sent per network adapter (session, today, yesterday, 7 days, month) and the daily log with CSV export.",
        "**Copy info**, **Copy system information**, **Credits** (a calm end-credits roll with a few numbers from your own usage), "
        "**About** (with a button for the welcome screen) and **Exit**."])
s += box("The menu stays open when you click an option, so you can change several things in a row. It closes when you click outside it, "
         "press Esc, or when the mouse has been away from the menu for about a second.")
s.append(P("Only one instance runs at a time. If you start the app a second time, the running app opens its settings window."))

# ---- 4. Settings
s += H1("4. The settings window")
s.append(P("Every change is **applied and saved immediately**; you see the result at once on the widget, the dashboard and fullscreen. Controls "
           "that have no effect because of another choice are greyed out, with an explanation why. Settings of components that are currently "
           "off (for example the GPU) can be filled in already.", lead))
s += fig("settings-widget.png", "Settings → Widget → Components and layout.", width=11.5 * cm)
s += table([
    ["Tab", "What you set there"],
    ["Widget", "Subtabs: **Components and layout** (on/off and order with the arrow buttons), **Sources** (which GPU or automatic, which network adapter or all, "
               "disks, network drives, ping target), **Display** (number / gauge / bar / graph per item, per core, labels on top, compact, "
               "transparent), **Values** (formatting, processes, FPS), **Appearance** (font, size, height), **Advanced** (graph length, "
               "period, CPU measure, refresh rate)."],
    ["Colors", "Text, background, gauge/bar, warning, critical, border and thresholds (for example 85% / 95%). Optionally follow the Windows theme."],
    ["Dashboard", "Show, front/back, click-through, lock, opacity, scale 50-300%, 1-4 columns, order and on/off per tile, background image."],
    ["Fullscreen", "Which screen, order and on/off per tile (with a thumbnail of the layout), tour time, network tile per connection type, background."],
    ["Themes", "16 built-in themes, save/load/import/export your own, JSON editor, preview (10 s)."],
    ["General", "Language, start with Windows, lock/stick position, hide in fullscreen apps, mouse actions, tooltip delay, notifications, "
                "monthly limit, update check, backup."],
    ["Plugins", "Only visible when the plugins are installed (see chapter 8)."],
], [0.16, 0.84])
s.append(H2("Widget components"))
s.append(P("CPU, GPU, memory, upload, download, battery (laptops), disk read/write, CPU and GPU temperature, ping, and the extras: CPU clock speed, "
           "disk active (%), disk temperature, motherboard temperature, process count and FPS. You set the left-to-right order with the arrow "
           "buttons. Disk and motherboard temperature read sensors (administrator needed) and disappear by themselves when the PC offers no value."))
s.append(H2("Notifications and monthly limit"))
s.append(P("TaskbarStats shows a notification when the battery is almost empty (20% and 10%), a disk is almost full, CPU/GPU/memory stays critical for too "
           "long, or you reach 80% and 100% of your monthly network data limit. Extra thresholds (0 = off): CPU, GPU and disk temperature, memory "
           "use and network usage per day."))

# ---- 5. Display
s += H1("5. Display: styles, graphs and values")
s += fig("widget-stijlen.png", "Four display styles: numbers, gauges, bars and graphs - selectable per item.", width=13.5 * cm)
s.append(H2("Graphs"))
s.append(P("CPU, GPU, memory, temperatures, network (download and upload as two lines) and ping can be shown as a mini graph in the widget, "
           "covering the last 30, 60 or 120 seconds. Missing pings break the line with a red dot. The **length** is *very short*, *short*, "
           "*medium*, *long* or a custom width; it can differ per item "
           "(Settings → Widget → Advanced → *Graph length per item*, 16-160 px)."))
s += fig("widget-grafieklengte-per-onderdeel.png", "Every graph with its own length: network wide, CPU very short, memory 100 px.")
s.append(H2("Temperature"))
s.append(P("CPU and GPU temperature can be shown as a number, gauge or bar, or **small after the CPU/GPU cell** "
           "(for example `CPU 33%  55°`) when you want to save space."))
s += fig("widget-temperatuur.png", "Temperature as number, gauge, bar or merged with the CPU/GPU cell.", width=13 * cm)
s.append(H2("Value formatting (Settings → Widget → Values)"))
s += B(["Network speed in bytes (MB/s) or bits (Mb/s), automatic or fixed unit.",
        "Short values; leave out the unit or %-sign; swap upload and download.",
        "Memory as a percentage, used (GB) or available (GB).",
        "Disk read/write as `R / W` or as arrows (↓ read, ↑ write) - everywhere in widget, tooltip, dashboard and fullscreen."])
s.append(H2("Ping"))
s.append(P("Turn on *Ping (latency)* and the widget shows for example `PING 12 ms` (orange from 100 ms, red from 250 ms or when there is no reply). "
           "The target (default 1.1.1.1) is adjustable. The tooltip and the network details in fullscreen show min/average/max, "
           "jitter and packet loss over the last two minutes."))
s += fig("widget-ping.png", "Widget with ping and temperatures.")
s.append(H2("Processes"))
s.append(P("The number of running processes as `PROC 250`, or only the **apps** (processes with a visible window, like Apps in Task Manager), only the "
           "**background processes**, or both (`APPS/BG 5/245`). The tooltip also shows the thread count."))
s += fig("widget-processen.png", "Processes: total, apps, background or both.", width=11 * cm)
s.append(H2("Background image and Windows theme"))
s.append(P("Per window (widget, dashboard, fullscreen) you can pick a png/jpg/bmp as background with mode *Fill*, *Stretch*, *Fit*, *Tile* or *Center* "
           "and an opacity of 0-100%. Under *Colors* the widget can **follow the Windows theme**: dark/light, optionally with the Windows accent "
           "colour for gauges and bars."))
s += fig("widget-achtergrond.png", "Widget with a background image.")

# ---- 6. Dashboard & fullscreen
s += H1("6. Dashboard and fullscreen cockpit")
s.append(H2("Desktop dashboard"))
s.append(P("A large, translucent window with tiles for CPU (with bars per core), GPU per card with VRAM, memory, network (mini graph per adapter), "
           "disks, battery, heaviest programs and system. It is off by default: turn it on from the menu or *Settings → Dashboard*."))
s += B(["In front or behind, scale 50-300%, opacity, 1-4 columns, lock.",
        "**Click-through** (**Ctrl+Alt+D** or double-click the icon next to the clock): your clicks go through it, so it works like a living "
        "wallpaper. Because right-clicking no longer works then, these are the ways to turn it off again.",
        "The dashboard restores itself when you use *Show desktop*."])
s += side_by_side("dashboard.png", "The desktop dashboard.", "settings-dashboard.png", "Settings → Dashboard: order and on/off per tile.")
s.append(H2("Fullscreen cockpit (Ctrl+Alt+F)"))
s.append(P("One dense overview of everything across a whole screen (you choose which). Click a tile for the in-depth view: "
           "graph up to 1 hour, min/average/max, per core, per GPU with VRAM, per adapter, per disk, top programs and system info."))
s += fig("fullscreen-overview.png", "The fullscreen overview.", width=14 * cm)
s += side_by_side("fullscreen-cpu.png", "CPU details.", "fullscreen-gpu.png", "GPU details with sensors.")
s += table([
    ["Key / action", "Function"],
    ["Esc", "One step back; from the overview it closes the screen."],
    ["1 / 2 / 3", "Graph period: 1 minute / 5 minutes / 1 hour."],
    ["I (or button at the top)", "Specifications page."],
    ["C (on the specifications page)", "Copy everything to the clipboard."],
    ["Space, or click 3× on an empty spot", "Start the automatic tour; a click or key stops it."],
    ["Mouse wheel", "Scroll (specifications, sensors)."],
], [0.38, 0.62])
s.append(H2("Specifications and sensors"))
s.append(P("The specifications page shows computer, motherboard and BIOS, Windows, processor (cores, clocks, cache, instruction sets), "
           "graphics cards (driver, VRAM, outputs), memory modules, disks (type, health, firmware), displays, "
           "network adapters, battery (wear, charge cycles), security (Secure Boot, TPM), audio, input devices, Bluetooth and USB. "
           "The detail pages show sensors via LibreHardwareMonitor: CPU power, temperatures and clocks, GPU temperature/power/fan, "
           "disk temperature and SMART health, motherboard, fans, memory and battery. These sensors are only active while the fullscreen screen is open."))
s += side_by_side("fullscreen-specs.png", "The specifications page (key I).", "fullscreen-disk.png", "Disk details with temperature and health.")
s.append(H2("Automatic tour"))
s.append(P("Click 3× on an empty spot in the fullscreen screen (or press the space bar): the screen walks through all pages "
           "(overview, every detail, specifications). The time per page (default 10 s) is set via the menu (*Fullscreen tour*) or under "
           "*Settings → Fullscreen*. Handy on a second monitor as a permanent info screen."))

# ---- 7. Themes & gamers
s += H1("7. Themes and gamers")
s.append(H2("Themes"))
s.append(P("A theme is a small JSON file with style, colours, font, height and the layout of dashboard and fullscreen (no screen positions and no language). "
           "**16 are built in**: Default, Dark, Light, Black & white, Love, CGA, Matrix, Amber, Game Boy, Dracula, Ocean, Sunset, Forest, Minimal, "
           "Gauges and Neon. Some have their own background drawn in code (falling Matrix characters, Neon grid, sun, stars, waves, hearts, "
           "pine forest with fireflies, scanlines)."))
s += fig("themes-fps.png", "The built-in themes, here with FPS in the widget.")
s += fig("themes-achtergronden.png", "Widgets with the theme backgrounds.", width=13 * cm)
s += B(["**Preview (10 s)**: the chosen theme (or the JSON in the editor, even unsaved) is shown for 10 seconds on widget, dashboard and "
        "fullscreen with rising and falling values and changing display styles. Afterwards everything returns to how it was.",
        "**JSON editor**: under *Themes* you see and edit the JSON of the chosen theme; on an error you get the line and position. "
        "You can also edit a built-in theme this way (*built in, modified*); **Reset to original** removes your version again.",
        "**Sharing**: export a theme and hand over the file; the other person imports it. Your own themes live in `%AppData%\\TaskbarStats\\themes\\`.",
        "On **14 February** the app temporarily starts with the Love theme (in memory only; your own look is kept). "
        "You can turn this off under *Themes*."])
s += fig("settings-themes.png", "Settings → Themes.", width=11 * cm)
s.append(H2("For gamers"))
s.append(P("Turn your taskbar into a free FPS counter, or drag the widget over your game."))
s += fig("gamers-overlay.png", "FPS, GPU load and temperature, CPU and ping floating over a game.", width=13 * cm)
s += B(["**FPS** (experimental): measured like PresentMon, using Windows event tracing of the foreground program. Works with "
        "DirectX games (DXGI/D3D9), Vulkan (via DxgKrnl) and browsers. Turn it on under *Settings → Widget → Values*. Only a process id "
        "and timestamp are counted, no content. The tooltip also shows the **1% low** and the program.",
        "**Mini overlay**: compact, labels on top, only FPS, GPU and CPU.",
        "**Click-through** (Ctrl+Alt+W): the widget stays visible, mouse clicks go to the game.",
        "Turn *Hide in full screen* off if the widget should stay visible above a game."])
s += fig("gamers-mini.png", "Mini overlay over a game.", width=10 * cm)

# ---- 8. Plugins
s += H1("8. Plugins (optional)")
s.append(P("Plugins add extra values to the widget, the dashboard and fullscreen. They are off by default and are installed through the optional "
           "step *Optional plugins* in the installer (silent: `/TASKS=\"plugins\"`). Then you turn them on under "
           "*Settings → Plugins*.", lead))
s += table([
    ["Plugin", "Shows", "Data from"],
    ["Claude usage", "% of your 5-hour and weekly limit used and the time until it frees up (bar, ring or number; notification at the limit)",
     "The Claude Code sign-in on your PC (read only), sent to Anthropic (undocumented address)"],
    ["Weather", "Sun/clouds/rain, temperature, humidity, wind, chance of rain", "Open-Meteo (free, no key); only coordinates are sent"],
    ["Location (GPS)", "Place name, coordinates, accuracy, source, distance from home", "Windows location service, otherwise your IP address"],
    ["Uptime monitor", "\"3/3\" up; red plus a notification when something goes down", "Your own list of websites (HTTP), ports (TCP) and devices (ping)"],
    ["Script / REST", "Any value you choose", "A command, text file or JSON web address: Home Assistant, NAS, exchange rate, ..."],
], [0.19, 0.42, 0.39])
s += fig("plugins-widget.png", "Plugin examples in the widget.", width=8.5 * cm)
s.append(H2("Why it is safe"))
s.append(P("TaskbarStats runs as administrator and therefore **never executes plugin code**. The plugins run in a separate helper "
           "(`TaskbarStats.Plugins.exe`) that starts through a scheduled task **without administrator rights** and writes one small "
           "JSON file per plugin to `%AppData%\\TaskbarStats\\plugins\\`. The app only reads those files. The helper stops by itself "
           "when TaskbarStats closes or no plugin is on."))
s.append(H2("Setting up"))
s += B(["**Edit config…** opens a simple editor for the plugin's `.ini` file (`key = value`; the comment lines explain every option). "
        "Changes take effect within seconds; *Default* restores the original text.",
        "Under **Values** you choose per value: in the widget or not, text, bar or ring, and a colour.",
        "Dashboard and fullscreen have the **Plugins** tile; it only appears when there is something to show. In fullscreen it is a strip at the bottom; "
        "click it for the detail page."])
s += side_by_side("plugins-settings.png", "The Plugins tab.", "plugins-config-editor.png", "The config editor.")
s += box("For **Claude usage** you must be signed in with the Claude Code CLI (`claude`, then `/login`). The Anthropic address it uses "
         "is undocumented and may change; the plugin then shows the reason and keeps the last values faded. Your token only goes to "
         "api.anthropic.com and is never written to a file or log.", "note")
s.append(H2("Your own plugin, in any language"))
s.append(P("Write a file `%AppData%\\TaskbarStats\\plugins\\<name>.json` and write it again whenever the value changes. "
           "`ttl` is the number of seconds before a value is shown faded (stale), `level` is `ok`, `warn` or `crit`, "
           "and `notify: true` gives a notification at `crit`. It appears by itself under *Plugins → Values*."))
s += codeblock("""{
  "plugin": "mytool", "title": "My tool", "updated": "2026-11-07T10:00:00Z", "ttl": 600, "status": "ok",
  "items": [
    { "key": "queue", "label": "Build queue", "short": "Queue", "value": 3, "min": 0, "max": 10,
      "unit": "", "text": "3 jobs", "detail": "last run 10:31", "level": "ok", "widget": true, "notify": false }
  ]
}""")

# ---- 9. Tips
s += H1("9. Tips and tricks")
s.append(H2("Shortcuts and mouse"))
s += table([
    ["Shortcut / action", "Function"],
    ["**Ctrl+Alt+D**", "Dashboard click-through on/off"],
    ["**Ctrl+Alt+F**", "Open/close the fullscreen cockpit"],
    ["**Ctrl+Alt+W**", "Widget click-through on/off (also via the icon next to the clock)"],
    ["Esc (fullscreen)", "Back / close"],
    ["1 / 2 / 3 (fullscreen)", "Graph 1 min / 5 min / 1 hour"],
    ["I / C (fullscreen)", "Specifications / copy everything"],
    ["Space or 3× click (fullscreen)", "Automatic tour"],
    ["Double-click / middle-click on the widget", "Task Manager / copy tooltip (changeable under *General*)"],
], [0.38, 0.62], first_bold=False)
s.append(H2("Handy tricks"))
tips = [
    ("The menu stays open", "Click several options in a row; the menu only closes when you click outside it, press Esc or the mouse is away."),
    ("Widget lost or crooked?", "Choose *Stick to notification area* in the menu: the widget moves next to the notification area and follows it when that moves. "
                                "Dragging turns it off again."),
    ("Test-drive a theme", "Use *Preview (10 s)* instead of choosing right away: you see the theme in all styles and everything jumps back by itself."),
    ("Wallpaper with numbers", "Put the dashboard on *background* + *click-through* (Ctrl+Alt+D) with a nice theme and a background image."),
    ("Save space", "Use *Compact*, put the temperature small after the CPU/GPU cell and pick short graph lengths per item."),
    ("Two GPUs", "Leave the GPU on *Automatic*: the busiest one counts, and every GPU gets its own colour so you see which one the number belongs to. "
                 "To follow just one, pick a fixed GPU under *Sources*."),
    ("Network in bits", "Providers quote Mb/s: choose bits instead of bytes under *Values*."),
    ("Compare with Task Manager", "You can switch the CPU measure to *Utility*; that is roughly what some tools show, but on a boosting CPU it is almost twice as high as Task Manager."),
    ("Help for support", "On the specifications page, key **C** copies all specifications as plain text - ideal for a forum post. In *About*, *Copy diagnostics* copies the version, Windows and the last log lines (without user or computer name)."),
    ("New PC?", "*Settings → General → Backup*: export all your settings to one JSON file and import it on the other PC. You can also reset to defaults (themes and usage log stay)."),
    ("Info screen", "Fullscreen on a second monitor with the automatic tour makes a fine permanent info screen."),
    ("Share a theme", "Export a theme and email the file; the other person imports it. Everything the app exports or copies mentions the program and its link."),
    ("Your own language", "Put a language file in `%AppData%\\TaskbarStats\\lang\\` and restart the app. With `TASKBARSTATS_PSEUDO=1` and the language *Pseudo (test)* you see at once which texts are untranslated or too long."),
    ("Network drives", "Turn on *Include network drives* under *Sources*; they are queried in the background, so an unreachable share does not slow the app down."),
    ("Home Assistant, NAS, exchange rate...", "Use the *Script / REST* plugin: a command, text file or JSON web address can put any value you want on the widget."),
]
for t, d in tips:
    s.append(KeepTogether([Paragraph(f"<b>{md(t)}</b> - {md(d)}", bullet, bulletText="•")]))
s += box("Want the app to never touch the internet? Leave the plugins and the update check off: then TaskbarStats only uses the network to measure usage and ping.")

# ---- 10. Troubleshooting
s += H1("10. Troubleshooting")
s += table([
    ["Problem", "Solution"],
    ["The menu or tooltip does not appear", "Click the widget first and try again."],
    ["The widget is off-screen", "Start TaskbarStats from the Start menu, then right-click → *Stick to notification area*."],
    ["No temperatures", "The app must run as administrator (default). If your processor has no CPU sensor, the app uses the Windows thermal zone. "
                        "Some sensors (like the motherboard) do not exist on every PC."],
    ["GPU value looks wrong", "Pick a fixed GPU under *Sources* instead of *Automatic* and compare with the 3D engine in Task Manager."],
    ["FPS stays empty", "FPS is experimental, off by default and needs administrator rights. A value only appears while a program is "
                        "drawing frames (game, video player, browser)."],
    ["A UAC prompt at logon", "Use *Start with Windows* (scheduled task with highest privileges) instead of your own shortcut in Startup."],
    ["Defender reports a vulnerable driver (Winring0)", "That happened with old versions (1.6.0-1.6.3). Update to 1.6.4 or newer: it contains no driver any more."],
    ["The Claude plugin shows no numbers", "Sign in with the Claude Code CLI (`claude`, then `/login`). Claude's connector file is never used."],
    ["Usage is not completely right", "Traffic while the app is not running is not counted."],
    ["The app crashed", "On the next start a balloon notification appears; click it for the About screen with *Copy diagnostics*. Errors are in `diag.log` in the data folder - on your own PC only."],
    ["Taskbar restarts or Explorer crashes", "The widget notices a new taskbar and attaches itself again; if not, close the app and start it again."],
], [0.32, 0.68])
s.append(H2("Where is my data?"))
s += table([
    ["File / folder", "Contents"],
    ["`%AppData%\\TaskbarStats\\settings.json`", "Your settings (survives rebuilds and reinstalls)"],
    ["`%AppData%\\TaskbarStats\\usage.json`", "Network usage per day"],
    ["`%AppData%\\TaskbarStats\\themes\\`", "Your own themes"],
    ["`%AppData%\\TaskbarStats\\lang\\`", "Custom language files"],
    ["`%AppData%\\TaskbarStats\\plugins\\`", "Plugin output and configs"],
    ["`%AppData%\\TaskbarStats\\diag.log`", "Error log (stays on your PC)"],
    ["Portable: `data` folder next to the exe", "Everything above, when `portable.txt` sits next to the exe"],
], [0.45, 0.55], first_bold=False)
s += box("TaskbarStats sends no data to the developer. Only the optional plugins (weather, location, Claude usage, uptime, REST) and the "
         "optional update check connect to the internet, and only when you turn them on.")

# ---- 11. About
s += H1("11. Licence, credits and contact")
s.append(P("TaskbarStats is free to use under the **MIT licence**: you may use, modify and share it, also in your own projects. "
           "The only condition is that the copyright line and licence text are kept - so please leave the credits in."))
s.append(P("**Idea, design and testing by** Eric Bruggema. **Built with** Claude Code (Anthropic)."))
s += table([
    ["Third-party software", "Licence", "Used for"],
    ["LibreHardwareMonitorLib", "MPL-2.0", "Temperature and other sensors"],
    ["HidSharp", "Apache-2.0", "Device communication"],
    [".NET 8 / Windows Forms", "MIT", "The program itself"],
    ["Inno Setup", "Inno Setup License", "The installer"],
], [0.38, 0.22, 0.40])
s.append(P("These components remain under their own licences."))
s.append(H2("Links"))
s += table([
    ["Website", "taskbarstats.com"],
    ["Downloads and source code", "github.com/ericbruggema/TaskbarStats"],
    ["Report bugs / ideas", "github.com/ericbruggema/TaskbarStats/issues"],
    ["Demo videos (English)", "github.com/ericbruggema/TaskbarStats/tree/main/docs/tour/promo"],
    ["LinkedIn", "linkedin.com/in/eric-bruggema-28a81a186"],
    ["Facebook", "facebook.com/eric.bruggema"],
], [0.32, 0.68], header=False)
s.append(Spacer(1, 10))
s.append(P(f"This manual belongs to TaskbarStats {VERSION}. The screenshots show the English interface with made-up values; "
           "computer and program names are anonymised.", small))


if __name__ == "__main__":
    build(s, OUT)
