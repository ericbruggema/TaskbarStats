# PLAN: recreating TaskbarStats (or your own version of it) with Claude Code

TaskbarStats is a .NET 8 WinForms monitor for Windows: a small widget next to the taskbar notification area, a
large desktop dashboard and a fullscreen "cockpit". It was built entirely with Claude Code, in a few days, over
roughly 50 commits (versions 1.0.0 to 1.4.1, see the appendix). This document is the plan behind it: what to build,
in which order, which prompts to give Claude Code, how to check each step, and which mistakes we already made so
that you do not have to.

You do not need to copy TaskbarStats. The plan is written so you can substitute your own idea (a GPU-only overlay, a
network meter, a Pomodoro widget) and keep the same skeleton: a layered window, a background sampler, JSON settings,
themes, a tray/menu, an installer.

Contents

1. [What you will build](#1-what-you-will-build)
2. [Prerequisites and setup](#2-prerequisites-and-setup)
3. [Phased build plan](#3-phased-build-plan)
4. [How to work with Claude on this kind of project](#4-how-to-work-with-claude-on-this-kind-of-project)
5. [Localisation approach](#5-localisation-approach)
6. [Release and distribution](#6-release-and-distribution)
7. [Lessons learned and what to do differently](#7-lessons-learned-and-what-to-do-differently)
8. [Appendix: timeline 1.0 to 1.4.1](#8-appendix-timeline-10-to-141)

---

## 1. What you will build

### Feature list (as of 1.4.1)

- **Taskbar widget**: a floating, always-on-top, per-pixel-alpha window that sits left of the notification area.
  Shows CPU, GPU, memory, network up/down, disk I/O and free space, battery, CPU/GPU temperatures, ping. Each item
  can be shown as digits, a gauge, a bar or a mini history graph. Tooltip with details, drag to move, "stick to
  tray", mouse actions, click-through hotkey.
- **Values that match Task Manager**: CPU from `Processor Information\% Processor Time`, memory from
  `GlobalMemoryStatusEx`, GPU as the sum of all `GPU Engine` counters per adapter, network from
  `Network Interface` counters.
- **Desktop dashboard**: a large translucent window of tiles (CPU with per-core bars, GPU per card, memory,
  network per adapter, disks, battery, top processes, system) with history graphs; foreground or background,
  scalable, click-through, survives "Show desktop".
- **Fullscreen cockpit**: a fixed 1920x1080 canvas that scales to the screen; overview plus a detail page per tile,
  sensors (LibreHardwareMonitor), a hardware specifications page (WMI/registry/Win32) and an automatic tour.
- **Settings window** with tabs and sub-tabs; every change is applied and saved immediately.
- **Themes**: JSON files (16 shipped plus user themes in `%AppData%`), import/export.
- **Notifications and hotkeys**: high load, nearly full disk, temperature thresholds, monthly network limit;
  Ctrl+Alt+D / F / W.
- **Network usage log** per adapter and day, CSV export.
- **Localisation**: English source text, Dutch and German shipped, user language files in `%AppData%`.
- **Installer** (Inno Setup), autostart through a scheduled task with highest privileges, opt-in update check.

### Final architecture

About 9,700 lines of C# in `src/`. Big classes are `partial`, and each later feature lives in its own file, which
made parallel work possible (phase 13 and section 4).

| File | Role |
|------|------|
| `Program.cs` | Entry point, single-instance mutex (name suffix from `TASKBARSTATS_INSTANCE`), `--autostart-on/off` for the installer |
| `WidgetForm.cs` (+ `.Fmt/.Graph/.Actions/.WinTheme/.Stick.cs`) | The widget: layered window, drawing to a bitmap, mouse, menu, tooltip, notifications, hotkeys, sampler thread |
| `Metrics.cs` (+ `Metrics.Extra.cs`) | Counters: CPU, GPU via one PDH wildcard query, memory, network, disk, VRAM, battery, DXGI adapter names; LibreHardwareMonitor for temperatures and sensors |
| `DashboardForm.cs` | Desktop dashboard (tiles, masonry layout), plus `Ring`, `MetricHistory`, `DashContext` |
| `FullscreenForm.cs` | Fullscreen cockpit: overview, detail pages, specs page, tour |
| `SettingsForm.cs` (+ `.Sub/.Fmt/.Graph/.Actions/.Bg/.WinTheme.cs`), `AppIcon.cs` | Tabbed settings window; `SettingsHost` = callbacks into the widget; embedded icon |
| `Themes.cs`, `Tiles.cs` | `ThemeData` (look and layout as JSON); tile ids, order and visibility |
| `AppSettings.cs` (+ partials) | JSON settings in `%AppData%\TaskbarStats\settings.json` (override folder with `TASKBARSTATS_DATA`) |
| `HardwareInfo.cs`, `PingMonitor.cs` | Specs page data (WMI, collected once, async, cached) and ping on its own thread |
| `UsageTracker.cs`, `ProcessSampler.cs` | Per-adapter/day usage to `usage.json`; top processes (`SampleAsync`) |
| `StartupManager.cs`, `TaskbarHost.cs` | Autostart via scheduled task; finds the tray/taskbar rectangle |
| `Loc.cs`, `lang/*.json` | Translations: `Loc.T("English text")`, one JSON per language |
| `UpdateChecker.cs`, `BgImage.cs` | Opt-in GitHub release check; background image layer |
| `WelcomeForm.cs`, `AboutForm.cs`, `CreditsForm.cs`, `LogForm.cs` | Small windows |
| `installer/TaskbarStats.iss`, `build.bat`, `build-installer.bat`, `tools/check-lang.ps1` | Packaging and checks |

---

## 2. Prerequisites and setup

| Need | Notes |
|------|-------|
| Windows 11 | The counters, DXGI and taskbar behaviour were all verified on Windows 11. Windows 10 probably works but is untested. |
| .NET 8 SDK | `winget install Microsoft.DotNet.SDK.8` |
| Claude Code | Run it in the project folder from the first command. |
| Git and `gh` | `git`, and the GitHub CLI for releases (`gh release create`). |
| Inno Setup 6 | `winget install JRSoftware.InnoSetup --override "/CURRENTUSER /VERYSILENT /NORESTART"` installs per user; `build-installer.bat` searches `Program Files` and `%LOCALAPPDATA%\Programs`. |
| An editor / IDE | Optional; Claude Code does most of the editing. |

NuGet packages the project ended up with (see `TaskbarStats.csproj`):
`System.Diagnostics.PerformanceCounter` 8.0.0 (some counters, category enumeration),
`LibreHardwareMonitorLib` 0.9.3 (temperatures and sensors, needs administrator),
`System.Management` 8.0.0 (WMI for the specs page). Do not upgrade LibreHardwareMonitorLib to 0.9.6 without also
moving `System.Management` to 10 or newer (found the hard way, see phase 3).

Project settings worth copying from the csproj: `net8.0-windows`, `UseWindowsForms`,
`ApplicationHighDpiMode=PerMonitorV2`, `ConcurrentGarbageCollection=false`, `TieredPGO=false`,
`System.GC.ConserveMemory=5`, and `<Version>` as the single source of the version number.

### Give Claude a CLAUDE.md from day one

`CLAUDE.md` in the repo root is read by Claude Code at the start of every session. It is the project's memory:
commands, architecture, rules learned the hard way, how to test. Start it on day one with the skeleton below and
add a line every time something bites you. Treat it like code: commit it, keep it short enough to read.

```markdown
# <YourApp> project instructions for Claude

<One paragraph: what the app is, target platform, language of code comments and of the UI, licence, public/private repo.>

## Commands
build.bat                 # close running app, build Release, restart
dotnet build -c Release   # build only
build-installer.bat       # publish + installer (once phase 12 exists)

- The app needs / does not need administrator rights: <state it, and what that implies for starting and killing it>.
- Version lives in ONE place: <Version> in the csproj. Other places that mention it: <list>.

## Architecture (short)
| File | Role |
|------|------|
(one row per file, keep it current)

## Rules that hurt (do not rediscover)
- UI thread: anything that takes more than ~5 ms does not belong there. Sampling runs on its own thread; the UI only draws.
- (add one bullet per painful bug: symptom, cause, fix, and how you measured it)

## Testing without disturbing the user
- Test in a TEMP COPY of the source, built to its own folder.
- Set <APP>_DATA (own data folder) and <APP>_INSTANCE (other mutex name) so the real settings and the running app stay untouched.
- Never let a test write the real settings file.
- No full-screen screenshots (they leak personal info). Render windows to a bitmap and anonymise.
- Only stop processes that live in the temp folder, never the user's real one.

## Conventions
- Comments in <language>. Every new visible string goes through the localisation function.
- New setting = property with a default (old settings files keep working) + persist call.
- Match the surrounding style; no speculative abstractions. Files use CRLF (.gitattributes).
- Repo is public: no personal data, no screenshots with private content.
- Commit messages end with the Co-Authored-By line from the session.
- Update docs (README, installer readme, welcome screen, this file) on every functional change.
- Publish releases only after explicit approval from the user.

## Ideas / to do
```

TaskbarStats' real `CLAUDE.md` is Dutch (the author's language); yours can be in whatever language you talk to
Claude in. Language of the CLAUDE.md does not matter, consistency does.

---

## 3. Phased build plan

Each phase ends with something you can run and check. Commit after each phase. The prompts below are starting
points: paste them, then steer. Replace `<App>` with your name. Where a prompt says "see CLAUDE.md", Claude
will already have read the file if you set it up as described.

Rough effort in the real project: phases 1-6 were the first day (version 1.0.0), phases 7-9 and 12 came with 1.1
to 1.3, and the remaining ones filled 1.2 to 1.4.

### Phase 1: Skeleton, single instance, build scripts

- **Goal**: an empty WinForms app that starts once, has a manifest, an embedded icon and a build script.
- **Files**: `<App>.csproj`, `app.manifest`, `app.ico`, `src/Program.cs`, `build.bat`, `.gitignore`, `.gitattributes`, `LICENSE`, `CLAUDE.md`.
- **Prompt**:

```text
Create a .NET 8 WinForms project "<App>" (net8.0-windows, UseWindowsForms, Nullable, ImplicitUsings,
ApplicationHighDpiMode PerMonitorV2, single <Version> property in the csproj). Program.cs: a named Mutex so only
one instance runs, with an optional suffix from the environment variable <APP>_INSTANCE (so a test copy can run
next to the real app). Add app.manifest with requestedExecutionLevel level="requireAdministrator" (I need
sensor access later) and Windows 10/11 compatibility. Embed app.ico with <EmbeddedResource Include="app.ico"
LogicalName="app.ico" /> and also set <ApplicationIcon>. Add build.bat that self-elevates, kills the running
exe, runs dotnet build -c Release, then starts the exe. Add .gitignore (bin, obj, installer/Output), a
.gitattributes with CRLF, an MIT LICENSE, and a CLAUDE.md following the skeleton I paste below.
```

- **Acceptance**: `dotnet build -c Release` succeeds; starting the exe twice gives one process; with
  `<APP>_INSTANCE=test` a second one starts; the icon shows in Explorer.
- **Pitfalls**:
  - The icon must be an `EmbeddedResource` with `LogicalName="app.ico"`; otherwise the icon class silently falls
    back to the generic Windows icon (this happened). Check with `WM_GETICON` on a window in a test copy.
  - With `requireAdministrator` you cannot start the app from a normal shell without a UAC prompt, and `taskkill`
    on it needs admin as well. Decide early if you really need admin (we did: LibreHardwareMonitor).
  - Do not use `Application.Exit()` on shutdown later; see phase 6.

### Phase 2: A layered, per-pixel-alpha widget window

- **Goal**: a borderless, always-on-top window placed left of the notification area that draws text into a
  bitmap and pushes it with `UpdateLayeredWindow`; movable with the mouse; position remembered.
- **Files**: `WidgetForm.cs`, `TaskbarHost.cs`, `AppSettings.cs` (stub).
- **Prompt**:

```text
Add WidgetForm: a borderless tool window (WS_EX_LAYERED | WS_EX_TOOLWINDOW, no taskbar button) that is
positioned directly left of the Windows notification area. TaskbarHost.cs finds the taskbar and tray rectangle
(FindWindow "Shell_TrayWnd" / "TrayNotifyWnd"). Draw everything with GDI+ into a 32-bit ARGB Bitmap and show it
with UpdateLayeredWindow (per-pixel alpha), not with OnPaint. For now draw a fake "CPU 12%  MEM 40%" text.
Left mouse drag moves the window; on mouse-up store the position in settings. When the background is
"transparent", draw it with alpha 1, not 0, so the window still receives clicks. Do NOT use TransparencyKey.
Make all sizes DPI-aware.
```

- **Acceptance**: widget shows next to the tray at 100%, 150% and 200% scaling; text is crisp; dragging works;
  right-click on a transparent area still hits the window.
- **Pitfalls**:
  - A `TransparencyKey` lets clicks fall through the transparent parts, so the right-click menu sometimes did not
    appear. Use alpha 1.
  - `SetWindowPos(HWND_TOPMOST)` on a window that is owned by the taskbar can block 100-700 ms. Never call it
    periodically. `KeepOnTop` only does it when another visible topmost window really covers the widget, at most
    every 2 seconds.
  - Draw sizes from DPI, not from constants (`dee1eb9` added "DPI-fitting sizes").

### Phase 3: Metrics through PDH counters (including the GPU wildcard)

- **Goal**: numbers that match Task Manager, at low cost.
- **Files**: `Metrics.cs` (contains `PdhWildcard` and `DxgiNames`), `Metrics.Extra.cs` later.
- **Prompt**:

```text
Create Metrics.cs. Requirements: values must equal Task Manager on Windows 11.
- CPU total and per core: PDH wildcard query "\Processor Information(*)\% Processor Time". (Also offer
  "% Processor Utility" as an option; on a boosting CPU it reads about 1.9x higher than Task Manager.)
- Memory: GlobalMemoryStatusEx.dwMemoryLoad.
- GPU: ONE PDH query "\GPU Engine(*)\Utilization Percentage" via PdhAddEnglishCounter + PdhCollectQueryData +
  PdhGetFormattedCounterArray. Sum all engine instances per adapter (the LUID is in the instance name
  "luid_0x..._phys_0_eng_..."). Rebuild the query every ~5 s because engine instances come and go with processes.
- Adapter names: DXGI (CreateDXGIFactory1, GetDesc1) to map LUID -> "NVIDIA GeForce ...".
- Network: "\Network Interface(*)\Bytes Received/sec" and "Bytes Sent/sec" as PDH wildcard queries.
- Wrap PDH in a small class PdhWildcard (TryCreate, Collect, read array, Dispose).
Do NOT create a PerformanceCounter object per instance. All counter names are English, also on a localised
Windows (use the *English* counter APIs).
Then add a small console-free test hook: a debug log that prints Stopwatch timings per block of Metrics.Update.
```

- **Acceptance**: side by side with Task Manager, CPU/GPU/memory agree within a couple of percent; one
  `Metrics.Update` costs on the order of 10 ms (real project: ~12 ms, of which network PDH ~7 ms).
- **Pitfalls**:
  - One `PerformanceCounter` per GPU engine instance cost 150-780 ms per tick. One PDH wildcard query fixed it.
    The network also went from ~39 ms/s (separate counters) to ~7 ms with wildcards.
  - Perf counter names are English on all locales; use `PdhAddEnglishCounter`.
  - CPU: "Processor Utility" is not what Task Manager shows on our test machine; the time-based value is. We measured
    it instead of assuming (commit `91156c0`).
  - Battery, disk and VRAM are still on slower APIs; the CLAUDE.md "to do" list says they can go to PDH too.

### Phase 4: Sampler thread versus UI thread

- **Goal**: the UI thread only draws; all measuring happens elsewhere; low idle cost.
- **Files**: `WidgetForm.cs`, `Metrics.cs`, `UsageTracker.cs`, `ProcessSampler.cs`.
- **Prompt**:

```text
Move all measuring out of the UI thread. Create a dedicated background thread ("sampler", IsBackground, BelowNormal
priority) in WidgetForm that calls Metrics.Update and UsageTracker.Sample in a loop; the UI timer only reads the
latest values and draws. Shared data must be locked or replaced atomically (assign a new Dictionary instead of
mutating one). Add an _idle flag: when the widget is hidden and no dashboard/fullscreen is open, sample every
5 s instead of at the configured rate. Trim the working set once a minute. ProcessSampler exposes SampleAsync();
never call anything that touches DriveInfo for network drives on the UI thread. Add a 15 ms timer in a debug
build that logs when the UI thread was blocked longer than 30 ms, so we can measure jank.
```

- **Acceptance**: dragging and opening the menu stay smooth while sampling; idle CPU is ~2% of one core and
  working set about 60 MB (real numbers; yours will differ, but measure them).
- **Pitfalls**:
  - Anything over ~5 ms on the UI thread makes dragging and the menu stutter.
  - A timer that calls `Application.DoEvents()` inside a step can re-enter itself (test harness lesson).
  - Do not fire a new external process on every menu open (`schtasks` result is cached).

### Phase 5: Settings JSON with instant save

- **Goal**: an `AppSettings` class serialised to `%AppData%\<App>\settings.json`, saved atomically on every change.
- **Files**: `AppSettings.cs` and later partial files, `Program.cs`.
- **Prompt**:

```text
Create AppSettings: a class of properties with defaults, serialised with System.Text.Json (indented, enums as
strings, null skipped). Load() reads %AppData%\<App>\settings.json unless <APP>_DATA points to another folder (used by
tests); a corrupt or missing file yields defaults. Save() writes to settings.json.tmp and then File.Move(...,
overwrite: true) under a lock, so a crash never leaves a half-written file. New setting = new property with a
default, so old files keep working. Expose Persist() (save) and Relayout() (save + redraw). Wire the widget position
into it.
```

- **Acceptance**: delete/corrupt the file: app still starts; move the widget, restart: same place; kill the
  process mid-save (simulated): no half file.
- **Pitfalls**: an early test run wrote the real settings file. Always set `<APP>_DATA` in tests (section 4).
  Atomic saving was added later (`d5ee8a3`); do it from the start.

### Phase 6: Tray/menu, tooltip, context menu behaviour

- **Goal**: right-click menu that stays open while you choose several options, a delayed tooltip, clean shutdown.
- **Files**: `WidgetForm.cs`.
- **Prompt**:

```text
Add a ContextMenuStrip to the widget. Keep it short: Settings (bold), Theme quick-pick, Usage, Dashboard,
Fullscreen, Copy info, Read-me & credits / About, Stick to tray, Exit. The menu stays open after a click on
checkable items and closes when the user clicks outside, presses Esc, or the mouse has been away from the menu
for about 1 second. Rebuild the menu on every open (RefreshMenu) but keep that cheap: no external processes, no
network. Add a tooltip after a configurable delay (minimum 2 s) with full details. Middle click copies the
tooltip text to the clipboard, double click opens Task Manager. Exit must call Close() on the widget, not
Application.Exit().
```

- **Acceptance**: pick five toggles in a row without the menu closing; Esc closes; exit leaves no process.
- **Pitfalls**:
  - WinForms event order is `ItemClicked` -> `Closing` -> `Click` handler. "Keep open" therefore has to be decided
    in `OnDropDownItemClicked` (default open; items with `Tag = "close"` close), not in `Click`.
  - After a language change the menu is rebuilt and reopened (`ReopenMenu`).
  - `Application.Exit()` closes windows while they are being closed and throws "Collection was modified".
  - The menu only stays short if all appearance and layout options live in the settings window (phase 7).

### Phase 7: Settings window and themes

- **Goal**: a tabbed settings window with live apply; themes as JSON.
- **Files**: `SettingsForm.cs` (+ `SettingsForm.Sub.cs`), `Themes.cs`, `Tiles.cs`, `AppIcon.cs`.
- **Prompt**:

```text
Add SettingsForm with tabs (Widget, Colors, Dashboard, Fullscreen, Themes, General). Every control change goes
straight through a SettingsHost callback object (WidgetForm.ApplyAll) into widget, dashboard and fullscreen and is
saved; there is no OK/Apply. Use segmented buttons instead of dropdowns for short choices and multi-column layouts.
Controls that depend on another choice are disabled (a Dep helper), and if an option is genuinely overridden by
another one (e.g. Compact overrides label placement) grey it out with a Why(() => reason) explanation. Options for
things that are switched off stay editable.
ThemeData holds all look-and-layout properties (no window positions, no language, no file paths) and has
Capture(settings) and ApplyTo(settings). Themes are JSON: shipped ones in code, user ones in
%AppData%\<App>\themes\, with save, load, delete, import and export. Tiles.cs owns the tile ids and the order and
visibility of the main items (DashOrder / FullOrder).
```

- **Acceptance**: change a colour: widget updates immediately; save a theme, restart, load it; export and
  import on a second data folder.
- **Pitfalls**:
  - Every new appearance setting must also be added to `ThemeData.Capture/ApplyTo` if it belongs in a theme.
  - The settings window grew from a few controls to a large file; split it into partial files per feature as soon as
    it passes ~1000 lines (phase 13).
  - Sub-tabs (`Subs()` in `SettingsForm.Sub.cs`) were added in 1.4 when one tab got too long; plan for them.

### Phase 8: Desktop dashboard

- **Goal**: a big translucent tile window with history graphs.
- **Files**: `DashboardForm.cs`, `Tiles.cs`.
- **Prompt**:

```text
Add DashboardForm: a layered window (same drawing approach as the widget) with tiles for CPU (per-core bars), GPU per
adapter with VRAM, memory, network (a mini graph per adapter), disks, battery, top processes and system info.
Masonry layout with 1-4 columns in the order from Tiles.DashOrder. MetricHistory + Ring keep the last 5 minutes.
Options: foreground or background, scale 50-300%, opacity, lock, click-through (Ctrl+Alt+D toggles it, because
right-click no longer works when it is click-through). It must survive "Show desktop": restore itself when Windows
hides or minimises it.
```

- **Acceptance**: background mode stays under normal windows; toggling click-through with the hotkey works;
  "Show desktop" and back leaves the dashboard visible and does not flicker.
- **Pitfalls**:
  - "Show desktop" hides/minimises windows; the dashboard heals itself with `EnsureVisible`.
  - The shell also raises the desktop window (Progman) above all normal windows. `HWND_BOTTOM` made the dashboard
    vanish behind it, and `HWND_TOP` alone did not work. `PlaceAboveDesktop` (every 0.4 s) goes TOPMOST->NOTOPMOST
    to get on top and drops back when Progman is back. Test with `Shell.Application.ToggleDesktop()` and check
    z-order and `WindowFromPoint`.
  - The first attempt decided the state by z-distance and the dashboard flickered (`3200e24`). The stable rule is
    "are there program windows below the desktop window".
  - Full-screen apps are detected with `SHQueryUserNotificationState`; the widget hides itself then (setting
    `HideInFullscreen`).

### Phase 9: Fullscreen cockpit

- **Goal**: a screen-filling dashboard with overview + detail per tile.
- **Files**: `FullscreenForm.cs`, `Metrics.cs` (`Sensors`).
- **Prompt**:

```text
Add FullscreenForm: a borderless window on a chosen monitor with a fixed 1920x1080 design canvas that is scaled to
the screen (draw at design size, scale the graphics). Overview: a dense grid of tiles (four columns; CPU is two
wide; battery and system share a cell), order and visibility from Tiles.FullOrder. Click a tile for a detail page (graph
up to 1 hour, min/avg/max, per core, per GPU with VRAM, per adapter, per disk, top processes). Keys 1/2/3 pick the graph
range (1 min / 5 min / 1 h); Esc goes back, and closes from the overview. Add LibreHardwareMonitor sensors (CPU power,
temperatures, clocks, GPU temp/power/clock/fan, disk temperature and SMART health, motherboard, fans) that are
sampled only while this screen is open, every 2 s, on the sampler thread.
```

- **Acceptance**: works at 1080p and at 4K; sensors appear only when the screen is open; Esc behaviour as
  described.
- **Pitfalls**:
  - LibreHardwareMonitor 0.9.3 (and 0.9.6) gives no usable CPU temperature on the Ryzen AI 7 350 we tested on
    (0.9.6: only zeros). `Metrics.UpdateTemperatures` falls back to the ACPI thermal zone
    (PDH `Thermal Zone Information\High Precision Temperature`, highest zone, about every 2 s). NVIDIA GPUs work
    ("GPU Core"); otherwise hot spot/SoC. Always keep a fallback and a clear message in the UI for missing sensors.
  - 0.9.6 requires `System.Management` >= 10, so do not upgrade casually.
  - Only enable sensors while they are visible; they are expensive.
  - Never call WMI on the UI thread.
  - The helper window (specs, tour) has to appear above the fullscreen window and adapt to screen and scaling
    (commits `426d487`, `589fcb6`).

### Phase 10: Hardware specifications page and ping

- **Goal**: a specs page inside the cockpit, and ping as a widget item.
- **Files**: `HardwareInfo.cs`, `PingMonitor.cs`, `FullscreenForm.cs`.
- **Prompt**:

```text
Add HardwareInfo: collects computer, motherboard/BIOS, Windows, processor (cores, threads, clocks, cache, instruction
sets), graphics cards (driver, VRAM), memory modules, disks (type, health, firmware) and volumes, displays, network
adapters, battery (wear, cycles), security (Secure Boot, TPM), audio, input devices, Bluetooth and USB devices via
WMI, registry and Win32. Collect once, asynchronously, cache for 30 s; every block is independent so one failing WMI
class does not empty the page and blocks appear as soon as they are ready, with retry. Show it as a scrollable
page in the fullscreen window (key I). Add PingMonitor on its own thread: last, min/avg/max, jitter, packet loss over the last ~2
minutes for a configurable host, shown as a widget item (amber at 100 ms, red at 250 ms or no reply), in the
tooltip and in the network detail page.
```

- **Acceptance**: opening the page never freezes the UI; unplug the network: ping goes red, UI stays smooth.
- **Pitfalls**: WMI on the UI thread; a single throwing query taking down the page (`efd2e32` made it robust per
  block); publishing a screenshot of the specs page that shows network addresses (later removed, `c056fa5`).

### Phase 11: Notifications, hotkeys, mouse actions

- **Goal**: alerts and global hotkeys.
- **Files**: `WidgetForm.cs`, `WidgetForm.Actions.cs`, `AppSettings.Actions.cs`.
- **Prompt**:

```text
Add notifications: nearly full disk, sustained high CPU load, plus thresholds for CPU/GPU/disk temperature, memory
and daily network usage (0 = off). Register global hotkeys with RegisterHotKey: Ctrl+Alt+D (dashboard
click-through), Ctrl+Alt+F (fullscreen), Ctrl+Alt+W (widget click-through). Let the user choose what double click and
middle click do (Task Manager, dashboard, fullscreen, settings, usage log, copy info, menu, nothing). Alerts keep
running in idle mode (the sampler still runs every 5 s).
```

- **Acceptance**: each hotkey works when another app has focus; click-through widget can be toggled back with the
  hotkey or the notification-area icon.
- **Pitfalls**: click-through makes right-click impossible, so there must always be a second way back (hotkey and
  icon). Hotkeys can collide with other apps; report failure instead of ignoring `RegisterHotKey` returning false.

### Phase 12: Localisation

- **Goal**: all visible text goes through `Loc.T("English text")`; translations in JSON.
- **Files**: `Loc.cs`, `lang/nl.json`, `lang/de.json`, `tools/check-lang.ps1`, csproj `EmbeddedResource` line.
- **Prompt**:

```text
Add Loc.cs. The English text in the code is the key: Loc.T("Graph length"). Translations live in lang/<code>.json
({"English text": "translation"}, plus "_name": "Native language name"), embedded via
<EmbeddedResource Include="lang\*.json" LogicalName="lang.%(Filename).json" />. A file with the same name in
%AppData%\<App>\lang overrides or adds a language. Missing text falls back to English and is recorded in
Loc.Missing. "text@@context" separates same-English-different-meaning texts (everything after @@ is never
shown). {0},{1} placeholders go through string.Format; a broken translation falls back to English instead of
throwing. Loc.N("text") marks strings in arrays for later translation. Detect language from Windows on first run.
Write tools/check-lang.ps1 that lists missing, unused and mismatched-placeholder/whitespace entries per language
and exits 1 on errors. Then convert all existing hard-coded strings.
```

- **Acceptance**: switching language updates the open windows and menu; a deliberately broken JSON file does not
  crash; `tools\check-lang.ps1` reports 0 missing for the shipped languages.
- **Pitfalls**: the first version (1.1-1.4) had Dutch/English strings mixed in the code with `Loc.Pick(nl, en)`;
  moving to keys plus JSON was a large refactor (`5cd5c4a`). Start with it in phase 1. See section 5.

### Phase 13: Making it extensible and parallel

- **Goal**: split large classes into `partial` files so several agents can work without merge conflicts.
- **Files**: `WidgetForm.*.cs`, `Metrics.Extra.cs`, `AppSettings.*.cs`, `SettingsForm.*.cs`.
- **Prompt**:

```text
Make WidgetForm, Metrics, AppSettings, ThemeData, SettingsForm partial. Do not change behaviour. From now on new
features go in their own partial files (WidgetForm.Fmt.cs, .Graph.cs, ...), with as few edits as possible in the
big files (one hook line each). Build must still pass.
```

- **Acceptance**: identical behaviour; five agents can each add a feature file with only hook-line overlaps.
- **Pitfalls**: hook lines still conflict when merged (e.g. the same `OnShown` line); resolve by hand. See section 4.
  In the real project this step (`c7cf6f8`) was the preparation for 1.4.0 (five features in parallel).

### Phase 14: Installer, autostart, updates

- **Goal**: a signed-ish (see section 6) single installer with autostart and uninstall that cleans up.
- **Files**: `installer/<App>.iss`, `installer/Leesmij.txt`, `build-installer.bat`, `StartupManager.cs`, `UpdateChecker.cs`, `Program.cs`.
- **Prompt**:

```text
Add StartupManager: autostart as a scheduled task "at logon" with highest privileges (a Run-key entry does not
work for an app that requires administrator). Program.cs handles --autostart-on / --autostart-off for the installer.
Add installer/<App>.iss for Inno Setup 6: PrivilegesRequired=admin, x64compatible, InfoBeforeFile readme, Start menu
group, optional desktop icon, task "startup" that runs --autostart-on/off after install, [UninstallRun] that runs taskkill and
schtasks /Delete /TN "<App>" /F, a PrepareToInstall that closes a running instance, and at uninstall a Yes/No
question whether to delete %AppData%\<App>. build-installer.bat runs dotnet publish -c Release -r win-x64
--self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
-p:EnableCompressionInSingleFile=true and then ISCC.exe. Add UpdateChecker: opt-in, at most once per 24 h, reads
the latest GitHub release, only notifies, never downloads. TASKBARSTATS_UPDATE_URL-style env variable for tests.
```

- **Acceptance**: install (elevated), Start menu entry, registry uninstall entry, scheduled task exists with highest
  privileges, exe has the icon, silent and normal uninstall both work, the data question appears on normal uninstall.
- **Pitfalls**:
  - `[Code]` `CurUninstallStepChanged` did not run for removing the task; use `[UninstallRun]` with `schtasks`.
  - The Inno uninstaller runs as `_unins.tmp`; the `unins000` process exits immediately. UIA sees Inno buttons as
    Pane, so send keys after `AppActivate`.
  - Back up `%AppData%\<App>` before testing uninstall: answering Yes wipes it.
  - UAC prompts cannot be accepted while the session is locked (`Get-Process LogonUI` exists): postpone elevated tests.

### Phase 15 (continuous): Tests and documentation

Not a phase but a habit: after each phase run the checks in its "Acceptance" line inside a temporary copy
(section 4), update `README.md`, `CLAUDE.md` and the screenshots, then commit.

---

## 4. How to work with Claude on this kind of project

### Test in a temporary copy

This is the most valuable rule in the project. The app must run as administrator and must never touch the user's
real settings or the running instance.

1. Copy `src`, the csproj, `app.manifest`, `app.ico` (the build fails without it) into a temp folder such as
   `%TEMP%\tstest`.
2. In the copy, change `requireAdministrator` to `asInvoker`, so it starts without UAC.
3. Build to an own output folder.
4. Set `TASKBARSTATS_DATA` (own data folder) and `TASKBARSTATS_INSTANCE` (own mutex suffix) before starting.
   Never let a test write the real `%AppData%\TaskbarStats\settings.json`; that went wrong once.
5. Drive the UI from a PowerShell script: right-click by `PostMessage(WM_RBUTTONDOWN/UP)` on the widget window,
   menu items with UI Automation (`InvokePattern`, `ExpandCollapsePattern`). Call `SetProcessDPIAware()` in the
   script so coordinates are real pixels. In the test copy disable the "mouse left the menu" timer
   (`_menuOutsideTicks >= 4`), because the test cursor never moves.
6. Let the test copy exit itself (`Environment.Exit`); a non-elevated shell cannot stop an elevated process. Only
   ever stop processes that live in the temp folder, never the user's real one.
7. If a test needs elevation, the script starts itself with `Start-Process -Verb RunAs` and sets its environment
   variables inside the elevated script (RunAs does not pass them on).

The test harness pattern that worked: a patch script (Python/PowerShell) that inserts a test hook after a known
anchor line in the copy (e.g. the `OnShown` line), so the tested code is the real code plus a timer that runs steps.
Keep the anchor lines stable; when you move them, update the patch scripts (this happened at 1.4.0).

### Screenshots without leaking anything

- No full-screen captures: they contain browser tabs, music, notifications, and if the session is locked you get the
  lock screen.
- Render each window to a PNG directly: `Bitmap.Save` just before `UpdateLayeredWindow`, or `Control.DrawToBitmap` /
  `OnPaint` to a bitmap in the test copy.
- Anonymise computer name, user name, program names and network addresses before publication (the shipped
  screenshots in `docs/screenshots` state that). Check the specs page and the network pages twice.

### Measure instead of guessing

Performance bugs here were never where the intuition pointed. The method that worked: a 15 ms timer on the UI
thread that logs any gap longer than a threshold (that is your jank logger), plus a `Stopwatch` around each suspect
block in the test copy. This found the per-instance `PerformanceCounter` cost (150-780 ms per tick), the
`SetWindowPos` TOPMOST stall (100-700 ms) and the network counter cost (39 -> 7 ms). Ask Claude to add the timing
first and to report numbers before it "optimises".

The same goes for correctness: compare against Task Manager side by side, and note the measured ratio in
CLAUDE.md (the `% Processor Utility` versus `% Processor Time` finding is stored there).

### Several agents in parallel worktrees

Version 1.4.0 was built by five parallel agents, one per feature (value formatting and extra items, graph style,
background image, follow-the-Windows-theme, mouse actions plus notifications plus update check). What made it work:

- Prepare with phase 13 (partial classes). Each agent adds its own files and only hook lines in the shared ones.
- Each agent works in its own git worktree (`isolation: "worktree"` in Claude Code, or `git worktree add`).
- Each agent gets: the task, the file boundaries, the rule "add settings as properties with defaults, add
  strings via `Loc.T`, follow CLAUDE.md, test in a temp copy with its own `TASKBARSTATS_INSTANCE`", and "do not
  commit to main".
- You (or a main session) merge them one by one (the history shows the merge commits `bc6f49c`, `d8698fc`,
  `33ad7ea`, `1beda20`), resolve hook-line conflicts, rebuild, and test the union.
- Give every agent a different test data folder and mutex suffix, or they collide.
- Do not run parallel agents on the same large file without partial-class separation; the merge cost eats the gain.

### Keep CLAUDE.md as the project memory

After every painful bug, add a two-line entry to "Rules that hurt": symptom, cause, fix, how it was measured. The
log shows CLAUDE.md edits in the same commit as the fix (for example the Show-desktop commits). A future session (or
an agent in a worktree) starts with this knowledge. Keep the architecture table in sync with the files. Keep a
"to do / ideas" section with what was deliberately not built and why, so you do not rebuild ideas you rejected.

Claude Code also has a per-user auto-memory (outside the repo) that can store preferences (for example, "videos and
gifs always in English"). Put project facts in `CLAUDE.md` (shared, versioned) and personal preferences in memory.

### Release discipline

- Publish (tag, GitHub release, push) only after explicit approval from you. Tell Claude that in `CLAUDE.md`.
- Test the installer elevated: install, Start menu, registry entry, autostart task (highest privileges), icon in the
  exe, uninstall (silent and normal, data question).
- Put the SHA-256 of the installer in the release notes (`Get-FileHash` / `certutil -hashfile`).
- Refresh screenshots (and the gif/video) for every release; update `README.md`, `installer\Leesmij.txt` and the
  welcome screen text.
- Bump the version in the csproj and the `.iss` (`AppVersion`) and the readme in one commit ("Version x.y.z").
- Commit messages end with the `Co-Authored-By` line Claude Code gives you; the commit author uses your GitHub
  `noreply` address on a public repo.

### Habits that saved time

- One feature per commit, with the commit message saying what changed for the user.
- Ask for a plan first on anything that touches z-order, threads or counters; ask for measurements after.
- When a fix turns out to be wrong (Show-desktop went through three commits), record the final rule, not the
  history, in CLAUDE.md.
- Prefer commands Claude can run itself (`dotnet build`) and give it the way to check results itself
  (a script that prints numbers) so it can iterate without you.

---

## 5. Localisation approach

**Concept**

- The English text in the code is the key: `Loc.T("Graph length")`. No resource files with symbolic names, no
  designer-generated strings; the code stays readable and a missing translation shows English.
- One JSON file per language, `lang/<code>.json`: `{ "_name": "Deutsch", "English text": "Übersetzung" }`. They are
  embedded in the exe (`EmbeddedResource` with `LogicalName="lang.<code>.json"`).
- External override: a file with the same code in `%AppData%\<App>\lang\` overrides an embedded language or adds a
  new one, so users can translate without rebuilding. The settings window lists embedded and external languages
  (the name comes from `_name`).
- Fallbacks: missing text = English; a broken JSON file is ignored; a translation with wrong `{0}` placeholders
  falls back to English rather than throwing.
- Context: `"Display@@screen"` vs `"Display@@show"`; everything after `@@` is never shown.
- Placeholders `{0}`, `{1}` go through `string.Format`; literal braces must be doubled.
- `Loc.N("text")` marks a string in an array or table as translatable without translating it there; translate at
  use with `Loc.T(variable)`.
- First run: the language is taken from Windows when a matching file exists.
- Check script: `tools\check-lang.ps1 [-Lang nl]` scans `Loc.T/N("...")` in `src/*.cs` with a regex and reports
  missing, unused, placeholder mismatches and leading/trailing whitespace differences per language; exit code 1
  on placeholder/whitespace errors.
- Convention: any change to an English text must also rename the key in every language file.

**Why it worked**: translating was done by Claude in one pass per language (Dutch, German), and the check script
made it verifiable. **What did not**: the regex check does not see strings built in interpolations or by
concatenation, and renaming keys by hand across files is error-prone.

### Language tooling (v1.5, in progress)

> This section is a placeholder. The localisation system is being reworked right now; another person will fill in
> the details. The intentions, as known today:
>
> - A Roslyn-based `LangTool` (`tools/LangTool`, uses `Microsoft.CodeAnalysis.CSharp`) that reads the C# code as a
>   syntax tree instead of using a regex, so it also finds texts in interpolations and concatenated strings.
> - Commands: `sync` (add missing keys, mark or remove unused ones), `check` (validate), `rename` (rename a key in
>   the code and in all language files in one go), `status` (per language coverage).
> - Plural forms.
> - A pseudo-locale for testing layout and untranslated text.
> - Build-time validation, so a broken or incomplete language file fails the build instead of reaching users.
>
> TODO: usage examples, file formats, CI integration, and how this replaces `tools\check-lang.ps1`.

---

## 6. Release and distribution

### Packaging

- `build-installer.bat` publishes self-contained, single file, compressed:
  `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true`.
  Users do not need .NET installed. The price is size (the runtime is inside). Ideas not built: an installer that
  checks for .NET 8, or a port to .NET Framework 4.8 (part of Windows) for a tiny download.
- Inno Setup script (`installer/<App>.iss`): `AppId` GUID (keep it forever, it identifies upgrades),
  `AppVersion` (drives the file name `<App>-Setup-<version>.exe`), `PrivilegesRequired=admin` because the app requires
  administrator and the scheduled task needs it, `ArchitecturesAllowed=x64compatible`, `Compression=lzma2`,
  `SolidCompression=yes`, `WizardStyle=modern`, two languages (`compiler:Languages\Dutch.isl`, `compiler:Default.isl`),
  `InfoBeforeFile` for the readme, tasks for autostart and desktop icon.
- Autostart is a scheduled task with highest privileges, created by the app itself (`--autostart-on/off`). A
  `Run`-key entry does not work for apps that require elevation.
- Uninstall: taskkill and `schtasks /Delete` through `[UninstallRun]`, plus an optional prompt to delete the data
  folder.

### Versioning

One `<Version>` in the csproj; `AppVersion` in the `.iss`, `installer\Leesmij.txt` and README mention it as well.
Version bump is its own commit. The About window reads the version from the assembly.

### Code signing

Unsigned installers trigger Windows SmartScreen ("Windows protected your PC") and antivirus false positives,
especially for an app that requires administrator and reads hardware sensors. Options:

| Option | Notes |
|--------|-------|
| **SignPath Foundation** | Free code signing for qualifying open-source projects (OSI-approved licence such as MIT, public repo, a maintained project, signing performed from a CI build that they verify). Requires an application and a GitHub Actions (or other supported CI) build. This is the natural path for a project like this and has not been done for TaskbarStats yet. |
| Azure Artifact Signing (formerly Trusted Signing) | Microsoft's managed signing service; check current eligibility and price for individuals. |
| Commercial OV/EV certificate | Works everywhere; costs money and needs identity validation; EV gives immediate SmartScreen reputation. |
| Sign nothing | Simplest. Publish the SHA-256 of every release, keep the source public, and tell users what SmartScreen will say. This is what the project does today. |

Whichever you choose, sign both the exe inside the installer and the installer itself.

### GitHub releases and README

- Tag `vX.Y.Z`, create a release with `gh release create`, attach the installer (and mp4 videos, which are
  git-ignored under `docs/tour`), and paste the SHA-256 of the installer in the notes.
- README: an animated loop gif on top, a tour gif, screenshots per feature, a table "why the values are correct"
  (source counter per metric), build instructions, the two test environment variables, installer notes, how to add a
  language. Gifs live in the repo, large mp4 files go on the release page and are referenced from the README.
- All promo material (tour, intros, gifs) in English only.
- Keep private content out: anonymised screenshots, no personal names or e-mail in files, commit author uses the
  GitHub noreply address.
- The update check is opt-in and only reads the GitHub releases API; it notifies, it never downloads.

---

## 7. Lessons learned and what to do differently

### Lessons learned

1. The UI thread is sacred. Move measuring to its own thread on day one; the UI draws only.
2. `PerformanceCounter` per instance is a trap; PDH wildcard queries are 5-100 times cheaper.
3. Never call `SetWindowPos(HWND_TOPMOST)` periodically on a window owned by the taskbar; it can block for hundreds of ms.
4. Layered windows: use alpha 1, not a transparency key, or you lose clicks and the context menu.
5. Elevated processes do not see the drive letters of the normal session: read `HKCU\Network` and query the UNC
   path; do network drives asynchronously.
6. "Show desktop" is a shell feature with its own z-order rules (Progman comes to the top); `HWND_BOTTOM` hides the
   dashboard, decide state by which program windows are below, not by z-distance.
7. WinForms `ContextMenuStrip` event order is `ItemClicked`, `Closing`, `Click`; menu behaviour must be decided early.
8. Hardware sensor libraries have holes (no CPU temperature on a new Ryzen with LibreHardwareMonitor 0.9.3/0.9.6); build a
   fallback (ACPI thermal zone) and show honest text when a value is missing.
9. Do not upgrade a native-ish dependency without checking its dependencies (0.9.6 needs `System.Management` >= 10).
10. The icon must be an embedded resource with an explicit `LogicalName`, and it has to be verified (`WM_GETICON`).
11. Match Task Manager by measuring, not by reading documentation (`Processor Utility` versus `Processor Time`).
12. Test in a temporary copy with its own data folder and mutex; never let a test touch the real settings.
13. Screenshots leak: render windows to bitmaps, anonymise, review before committing to a public repo.
14. Idle cost matters for a background app: sample every 5 s when nobody is looking, no extra GC thread, trim the working set.
15. Keep the context menu short and push everything else into the settings window; settings apply live.
16. Disable only options that are truly overridden, and explain why (`Why(...)`); leave options for switched-off
    features editable.
17. Elevated-test limits: UAC cannot be accepted on a locked session; elevated processes cannot be stopped by a
    normal shell.
18. Inno Setup: use `[UninstallRun]` for cleanup steps that must always run; keep `AppId` constant.
19. Parallel agents need parallel-friendly code (partial classes) and separate test data folders.
20. Publishing needs a human yes; screenshots and docs are part of the release.

### What to do differently next time

- **Localise from the first commit.** Keys as English text plus JSON from phase 1 avoids the `Loc.Pick(nl, en)`
  detour and the big conversion commit.
- **Split into partial files (or real classes) earlier.** `WidgetForm` and `SettingsForm` grew into very large
  files before they were split. Set a size limit in CLAUDE.md.
- **Write the test harness once, keep it in the repo.** The test scripts (`scratchpad/prof_patch.py`-style patchers)
  were re-created per test round. A small `tools/testcopy.ps1` that makes the temp copy, flips the manifest, sets the
  env vars and cleans up would save time and prevent the "wrote the real settings" accident.
- **Automate the jank logger and the timing blocks** as a debug-only, compile-time flag instead of patching them
  into a test copy.
- **Decide about administrator rights up front.** It shapes autostart, installer, testing and shutdown. Consider
  making sensors an optional elevated helper process so the main app can run as a normal user.
- **Set up CI and signing early.** A GitHub Actions build (build, `check-lang`, publish, installer) and a signing
  route (SignPath Foundation) would give reproducible, signed releases.
- **Plan the settings window structure** (tabs and sub-tabs, dependencies, live apply) before adding options; it was
  reorganised three times (1.2.0, 1.2.1, 1.4.0+).
- **Consider a unit-testable core.** Formatting, themes, settings and localisation can be tested without a UI;
  right now most verification is manual or through UI automation.
- **Move the remaining slower counters (disk, VRAM) to PDH wildcard queries**, and lower `Metrics.Update` from ~12 ms.
- **Keep the readme in one language** (the project README mixes Dutch and English); decide who your audience is.
- **Pin what you measured** (baseline CPU %, working set, update cost) in a table so regressions are visible.

---

## 8. Appendix: timeline 1.0 to 1.4.1

Derived from `git log` (all dates 2026; the whole history spans 23 to 25 September). Commit messages are in Dutch in
the repo; summaries here are in English.

| Version | Date | Main content | Key commits |
|---------|------|--------------|-------------|
| 1.0.0 | 23 Sep | First version: widget, metrics, tray menu, settings JSON. MIT licence, compressed exe, README. | `00f5edc`, `56fecda` |
| (1.0.x) | 23 Sep | Dashboards, performance fixes, network drives, docs and screenshots. | `96338b9` |
| 1.1.0 | 24 Sep | Sensors in the fullscreen screen (LibreHardwareMonitor), first installer (Inno Setup). Installer fixes: autostart task removed via `[UninstallRun]`, correct uninstall procedure with data prompt, taskkill path. | `b08c8e9`, `c866790`, `470329e`, `ad3a41f` |
| 1.2.0 | 24 Sep | Tabbed settings window, themes, order and on/off for dashboard and fullscreen tiles; more economical measuring (PDH for network and cores, idle mode, GC settings), shorter menu. | `9e5ea77`, `e326458` |
| 1.2.1 | 24 Sep | Choice buttons instead of dropdowns, multi-column layout, disabled dependent controls, layout preview; twelve new themes; network drives visible as administrator; own icon; theme quick-pick in the menu; widget item order; tooltip delay; the icon really embedded. | `34f97e5`, `78afea9`, `b338cc0`, `c1ac457`, `ef5533c`, `29d5fc0`, `dee1eb9`, `834a355` |
| 1.3.0 | 24 Sep | Specifications page (key I) and automatic tour in the fullscreen screen, ping in the widget, CPU temperature via the ACPI thermal-zone fallback, temperature styles and merging, credits screen. | `731dde2`, `ff75b0d`, `18db302`, `8d6537d` |
| 1.3.x | 24 Sep | Small polish: helper window above fullscreen, fullscreen exit button, extras in the widget. | `589fcb6`, `426d487`, `71fea5a`, `90cfce6` |
| 1.4.0 | 25 Sep | Five parallel worktree agents, merged: value formatting and extra items, graph display style, background image per window, follow the Windows theme, mouse actions and click-through, extra notifications, opt-in update check. CPU percent defaults to time-based like Task Manager. Classes made `partial` first. | `91156c0`, `c7cf6f8`, `87d735b`, `e4da5e2`, `4794573`, `0fa76dd`, `43fc2e4`, merges `bc6f49c`, `d8698fc`, `33ad7ea`, `1beda20`, release commit `2fdfaa2` |
| after 1.4.0 | 25 Sep | Settings sub-tabs (Widget, Dashboard, Fullscreen, General), grey-out with explanations, graph length, "stick to tray" replaces "Reset position", atomic settings save, welcome screen from the About window. | `d4706a3`, `cbda9ba`, `777c156`, `efd2e32`, `06a6d8f`, `d5ee8a3` |
| Localisation | 25 Sep | Strings moved to `lang/*.json` with English source keys, external language folder, fallback to English, language from Windows, `tools/check-lang.ps1`; German added. | `5cd5c4a`, `b109c1e` |
| Docs and video | 25 Sep | English tour gif and mp4 on the release, intro clips (story, montage, loop, square), README rework, screenshot updated (no network addresses). | `85fb714`, `c056fa5`, `d266ea1`, `14ee04d` |
| 1.4.1 | 25 Sep | Dashboard stays visible and no longer flickers on "Show desktop". | `7e3a049`, `3200e24`, `2cd0d1e` |

Numbers for orientation: about 50 commits, around 9,700 lines of C# in `src/`, two shipped translations, 16 shipped
themes, idle cost about 2% of one core and about 60 MB working set.
