# TaskbarStats — projectinstructies voor Claude

Windows-monitor (.NET 8, WinForms): een klein widget naast het systeemvak van de taakbalk, plus een groot
bureaublad-dashboard en een fullscreen "cockpit". Toont CPU, GPU, geheugen, netwerk, schijven, batterij, temperaturen;
waarden komen uit dezelfde bronnen als Taakbeheer. Openbaar op GitHub (`ericbruggema/TaskbarStats`, MIT). De gebruiker
praat Nederlands; UI en commentaar zijn Nederlands, met een Engelse variant voor het menu/de schermen.

## Commando's

```
build.bat                      # sluit de draaiende app, bouwt Release, start opnieuw (vraagt zelf om admin)
dotnet build -c Release        # alleen bouwen -> bin\Release\net8.0-windows\TaskbarStats.exe
build-installer.bat            # publish (self-contained, single file, gecomprimeerd) + Inno Setup -> installer\Output
```

- De app heeft `requireAdministrator` (LibreHardwareMonitor voor temperaturen). Je kunt hem dus niet zomaar vanuit een
  niet-elevated shell starten; een `taskkill` op de draaiende app vereist ook admin.
- Inno Setup is mogelijk niet geïnstalleerd (`winget install JRSoftware.InnoSetup`). De installer is nog nooit gebouwd/uitgeprobeerd.
- Versie staat op één plek: `<Version>` in `TaskbarStats.csproj` (het Over-scherm leest die uit). Ook `installer\TaskbarStats.iss` (AppVersion, bepaalt de bestandsnaam `TaskbarStats-Setup-<versie>.exe`) en `installer\Leesmij.txt` noemen hem.

## Architectuur (kort)

| Bestand | Rol |
|---------|-----|
| `Program.cs` | mutex (`TASKBARSTATS_INSTANCE` als achtervoegsel), `--autostart-on/off` (voor de installer) |
| `WidgetForm.cs` | het widget: layered window, tekenen, muis, **menu**, tooltip, meldingen, hotkeys, sampler-thread (groot bestand) |
| `Metrics.cs` | tellers: CPU (`Processor Information\% Processor Utility`), GPU via **PDH-wildcard** (`PdhWildcard`), geheugen, netwerk, schijf, VRAM, batterij, DXGI-namen; LibreHardwareMonitor: temps (widget) en `Sensors` (alleen als het fullscreen-scherm open is, elke 2 s, op de sampler-thread) |
| `DashboardForm.cs` | bureaublad-dashboard (tegels, masonry), plus `Ring`, `MetricHistory`, `DashContext` |
| `FullscreenForm.cs` | fullscreen cockpit: vast canvas 1920×1080 dat meeschaalt; overzicht + detail per tegel; Esc = terug/sluiten |
| `UsageTracker.cs` | verbruik per adapter/dag → `usage.json` (thread-veilig) |
| `ProcessSampler.cs` | top-processen; gebruik `SampleAsync()` |
| `AppSettings.cs` | JSON-instellingen in `%AppData%\TaskbarStats\settings.json` (`TASKBARSTATS_DATA` overschrijft de map) |
| `StartupManager.cs` | autostart = geplande taak met hoogste rechten (Run-sleutel werkt niet voor elevated apps) |
| `Loc.cs` | `Loc.Pick(nl, en)` en een kleine sleutel-tabel |

Alle vensters zijn **layered windows** met per-pixel alpha: tekenen naar een `Bitmap` (GDI+) en `UpdateLayeredWindow`.
Bij "transparant" is de achtergrond alpha 1 (onzichtbaar maar klikbaar); een `TransparencyKey` liet klikken doorvallen
zodat het rechtermuismenu soms niet verscheen.

## Regels die pijn hebben gedaan (niet opnieuw ontdekken)

**UI-thread vrijhouden.** Alles wat >~5 ms kost hoort niet op de UI-thread; slepen/menu gaat anders haperen.
- Metingen + verbruik draaien op de **sampler-thread** in `WidgetForm`; de UI-`Tick` tekent alleen. Nieuwe zware
  bemonstering: op die thread of async, en gedeelde data vergrendelen of atomair vervangen (nieuwe dictionary toewijzen).
- GPU-engines: **één PDH-query**, nooit een `PerformanceCounter` per instantie (kostte 150–780 ms per tik).
- `SetWindowPos(HWND_TOPMOST)` op een venster dat eigendom is van de taakbalk blokkeert 100–700 ms. Niet periodiek
  aanroepen; `KeepOnTop` doet het alleen als er echt iets bovenop staat, max. elke 2 s.
- Geen extern proces starten per menu-opening (`schtasks` wordt daarom gecachet). Netwerkschijven (`DriveInfo`) alleen async.
- Meten in plaats van gokken: een 15 ms-timer die de UI-hapering logt + `Stopwatch` rond verdachte stukken werkte goed.

**Menu (ContextMenuStrip).** Volgorde in WinForms: `ItemClicked` → `Closing` → pas daarna de `Click`-handler. "Menu blijft
open" wordt daarom in `OnDropDownItemClicked` bepaald (standaard open; items met `Tag = "close"` sluiten). Na een
taalwissel wordt het menu opnieuw opgebouwd en heropend (`ReopenMenu`). Het menu bouwt zich bij elke opening opnieuw op
(`RefreshMenu`); houd dat goedkoop. `Application.Exit()` niet gebruiken bij afsluiten (vensters sluiten zichzelf tijdens
het sluiten -> "Collection was modified"); `Close()` op het widget.

**Windows-details.** "Bureaublad weergeven" minimaliseert/verbergt vensters → het dashboard herstelt zichzelf
(`EnsureVisible`). Fullscreen-apps worden herkend met `SHQueryUserNotificationState`. Hotkeys: Ctrl+Alt+D (klik-door dashboard),
Ctrl+Alt+F (fullscreen). Widget verbergt zichzelf bij fullscreen (instelling `HideInFullscreen`). Perf-namen zijn Engels
(`PdhAddEnglishCounter`/`PerformanceCounter` gebruiken Engelse namen, ook op Nederlandse Windows).

## Testen zonder de gebruiker te storen

- Draai tests in een **tijdelijke kopie** (bijv. `%TEMP%\tstest`): kopieer `src`, `*.csproj`, `app.manifest`; zet in de kopie
  `requireAdministrator` op `asInvoker`; bouw naar een eigen map. Zet `TASKBARSTATS_DATA` (eigen datamap) en
  `TASKBARSTATS_INSTANCE` (andere mutex) zodat de echte instellingen en de draaiende app ongemoeid blijven. **Nooit** zomaar
  de echte `%AppData%\TaskbarStats\settings.json` laten schrijven (dat is een keer misgegaan).
- UI-automatisering: rechtsklik met `PostMessage(WM_RBUTTONDOWN/UP)` op het widget-venster; menu-items met UI Automation
  (`InvokePattern`, `ExpandCollapsePattern`); in de testkopie de "muis is weg"-timer (`_menuOutsideTicks >= 4`) uitzetten,
  want de testcursor beweegt niet. PowerShell is DPI-unaware: `SetProcessDPIAware()` aanroepen voor echte pixels.
- **Geen schermopnames van het hele scherm**: die lekken persoonlijke info (browser, muziek) en tijdens een vergrendelde sessie
  krijg je het vergrendelscherm. Teken vensters liever rechtstreeks naar een PNG (`Bitmap.Save` vóór `UpdateLayeredWindow`,
  of `OnPaint` naar een bitmap) en **anonimiseer** computernaam/gebruiker/programmanamen voor publicatie (zie `docs/screenshots`).
- Voorkom dat testprocessen blijven hangen; `Get-Process TaskbarStats` en alleen processen uit de tempmap stoppen, nooit de echte.

## Conventies

- Nederlandse commentaren en UI-teksten; elke nieuwe tekst met `Loc.Pick("nl", "en")`.
- Nieuwe instelling = property in `AppSettings` met default (oude `settings.json` blijft werken), opslaan via `Persist()`/`Relayout()`.
- Code sluit aan op de omringende stijl; geen onnodige abstracties. Bestanden hebben CRLF (`.gitattributes`).
- Repo is **openbaar**: geen persoonlijke gegevens, geen schermafbeeldingen met privé-inhoud. Commit-auteur gebruikt het
  GitHub-`noreply`-adres. Commit-berichten eindigen met de `Co-Authored-By`-regel uit de sessie.
- Documentatie bijwerken bij functionele wijzigingen: `README.md`, `installer\Leesmij.txt`, `WelcomeForm.cs`, dit bestand.

## Ideeën / nog te doen

- Tegels in het dashboard slepen/herordenen; thema's; controle op nieuwe versie via GitHub-releases; ping-/uptime-tegel.
- Installer bouwen en uitproberen (Inno Setup); een release met de installer op GitHub.
- Kleinere download: installer die .NET 8 controleert, of port naar .NET Framework 4.8 (zit in Windows).
- Meting van de `Metrics.Update` (nu ~20 ms op de sampler-thread) verder verlagen door meer tellers te bundelen in PDH-queries.
