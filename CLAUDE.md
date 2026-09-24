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
- Inno Setup 6.7.3 is per gebruiker geïnstalleerd (`%LOCALAPPDATA%\Programs\Inno Setup 6`; `build-installer.bat` zoekt daar ook). Opnieuw: `winget install JRSoftware.InnoSetup --override "/CURRENTUSER /VERYSILENT /NORESTART"`.
- Versie staat op één plek: `<Version>` in `TaskbarStats.csproj` (het Over-scherm leest die uit). Ook `installer\TaskbarStats.iss` (AppVersion, bepaalt de bestandsnaam `TaskbarStats-Setup-<versie>.exe`) en `installer\Leesmij.txt` noemen hem.

## Architectuur (kort)

| Bestand | Rol |
|---------|-----|
| `Program.cs` | mutex (`TASKBARSTATS_INSTANCE` als achtervoegsel), `--autostart-on/off` (voor de installer) |
| `WidgetForm.cs` | het widget: layered window, tekenen, muis, **menu**, tooltip, meldingen, hotkeys, sampler-thread (groot bestand) |
| `Metrics.cs` | tellers: CPU (`Processor Information\% Processor Utility`), GPU via **PDH-wildcard** (`PdhWildcard`), geheugen, netwerk, schijf, VRAM, batterij, DXGI-namen; LibreHardwareMonitor: temps (widget) en `Sensors` (alleen als het fullscreen-scherm open is, elke 2 s, op de sampler-thread) |
| `DashboardForm.cs` | bureaublad-dashboard (tegels, masonry), plus `Ring`, `MetricHistory`, `DashContext` |
| `FullscreenForm.cs` | fullscreen cockpit: vast canvas 1920×1080 dat meeschaalt; overzicht + detail per tegel; Esc = terug/sluiten |
| `SettingsForm.cs`, `AppIcon.cs` | instellingenvenster met tabbladen (Widget incl. bronnen GPU/adapter/schijven, Kleuren, Dashboard, Fullscreen, Thema's, Algemeen); `SettingsHost` = callbacks naar het widget; `AppIcon` = ingebed `app.ico` (ook exe-icoon) voor vensters en systeemvak; elke wijziging gaat direct via `WidgetForm.ApplyAll()` naar widget/dashboard/fullscreen |
| `Themes.cs`, `Tiles.cs` | `ThemeData` (uiterlijk + indeling als JSON, meegeleverd + `%AppData%\TaskbarStats	hemes\`); `Tiles`: ids, volgorde (`DashOrder`/`FullOrder`) en zichtbaarheid van de 8 hoofdonderdelen |
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
- Meetteller-kosten: gebruik **PDH-wildcardqueries** (`PdhWildcard`) voor tellers met veel instanties (GPU-engines, netwerk, CPU-cores); losse `PerformanceCounter`-objecten kostten o.a. 39 ms/s voor het netwerk. Meten met een `Stopwatch` per blok in de testkopie (zie `scratchpad/prof_patch.py`-aanpak).
- Zuinig: sampler meet om de 5 s als niemand kijkt (`_idle` in `WidgetForm`), verbruik om de 5 s, werkset-trim elke minuut, `ConcurrentGarbageCollection=false`. Baseline in rust: ~2% van één kern, ~60 MB werkset.
- **UAC en netwerkstations**: een als administrator draaiend proces ziet de gekoppelde netwerkstations van de gewone sessie niet in `DriveInfo`. `Metrics.GetDriveSpaces(Network)` leest daarom ook `HKCU\Network` en vraagt de ruimte via het UNC-pad (`GetDiskFreeSpaceEx`).
- Het icoon moet als `EmbeddedResource` (`LogicalName="app.ico"`) in de csproj staan, anders valt `AppIcon` stil terug op het generieke Windows-icoon (zo ging het een keer mis). Controle: `WM_GETICON` op een venster lezen in een testkopie.
- Testscripts kopieren `app.ico` mee (anders faalt de build van de testkopie). Elevated testen: env-variabelen binnen het elevated script zetten (RunAs geeft ze niet door).
- Geen extern proces starten per menu-opening (`schtasks` wordt daarom gecachet). Netwerkschijven (`DriveInfo`) alleen async.
- Meten in plaats van gokken: een 15 ms-timer die de UI-hapering logt + `Stopwatch` rond verdachte stukken werkte goed.

**Menu (ContextMenuStrip).** Volgorde in WinForms: `ItemClicked` → `Closing` → pas daarna de `Click`-handler. "Menu blijft
open" wordt daarom in `OnDropDownItemClicked` bepaald (standaard open; items met `Tag = "close"` sluiten). Na een
taalwissel wordt het menu opnieuw opgebouwd en heropend (`ReopenMenu`). Het menu is kort gehouden (Instellingen, Thema-snelkeuze, Verbruik, Dashboard, Fullscreen, Kopieer/Welkom/Leesmij/Over, Afsluiten): alles rond uiterlijk/indeling zit in het **instellingenvenster** (`SettingsForm`); nieuwe uiterlijk-instellingen daar toevoegen en (als ze in een thema horen) ook in `ThemeData.Capture/ApplyTo`. Het menu bouwt zich bij elke opening opnieuw op
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
- UAC-meldingen kunnen niet worden geaccepteerd terwijl de sessie vergrendeld is (`Get-Process LogonUI`); elevated tests dan uitstellen.
  Een elevated proces kan niet door een niet-elevated PowerShell worden gestopt: laat de testkopie zichzelf afsluiten (`Environment.Exit`).
- PowerShell 5: `$(native command 2>&1 | Out-String) -match 'naam'` matcht ook de foutmelding (die de opdracht met de naam bevat); controleer de exitcode.
- Voorkom dat testprocessen blijven hangen; `Get-Process TaskbarStats` en alleen processen uit de tempmap stoppen, nooit de echte.

## Conventies

- Nederlandse commentaren en UI-teksten; elke nieuwe tekst met `Loc.Pick("nl", "en")`.
- Nieuwe instelling = property in `AppSettings` met default (oude `settings.json` blijft werken), opslaan via `Persist()`/`Relayout()`.
- Code sluit aan op de omringende stijl; geen onnodige abstracties. Bestanden hebben CRLF (`.gitattributes`).
- Repo is **openbaar**: geen persoonlijke gegevens, geen schermafbeeldingen met privé-inhoud. Commit-auteur gebruikt het
  GitHub-`noreply`-adres. Commit-berichten eindigen met de `Co-Authored-By`-regel uit de sessie.
- Documentatie bijwerken bij functionele wijzigingen: `README.md`, `installer\Leesmij.txt`, `WelcomeForm.cs`, dit bestand.

## Ideeën / nog te doen

- Tegels slepen i.p.v. pijlknoppen; thema per situatie automatisch (bv. op batterij); controle op nieuwe versie via GitHub-releases; ping-/uptime-tegel.
- **v1.2.1 is gepubliceerd** (installer elevated getest: installeren, Startmenu, register, autostart-taak, icoon in de exe, niet-stille verwijdering incl. datavraag Ja/Nee). Testtip uninstall: de Inno-uninstaller draait als `_unins.tmp` (proces `unins000` sluit direct); UIA ziet Inno-knoppen als Pane, dus stuur toetsen (`j`/`n`/Enter) na `AppActivate`. Back-up `%AppData%\TaskbarStats` vóór de test, want "Ja" wist die map. **v1.2.0/1.2.1 inhoud**: keuzeknoppen i.p.v. pulldowns, meer kolommen, afhankelijke bedieningselementen uitgeschakeld (`Dep` in `SettingsForm`), indelingsvoorbeeld, themavoorbeeld; menu-volgorde. Uit 1.2.0: instellingenvenster + thema's + volgorde/aan-uit van dashboard- en fullscreen-onderdelen + zuiniger meten (zie README). Alleen na akkoord van de gebruiker publiceren; screenshots altijd meenemen en bijwerken.
- **Release v1.1.0 is gepubliceerd.** Installer elevated getest: installeren, Startmenu, register, autostart-taak (HighestAvailable), verwijderen. Les: `[Code]`-`CurrentUninstallStepChanged` bleek bij het verwijderen niet te draaien; de taak wordt nu via `[UninstallRun]` (schtasks /Delete) weggehaald. Elevated testen kan via een script dat zichzelf met `Start-Process -Verb RunAs` start; omgevingsvariabelen gaan niet mee door RunAs, zet ze binnen het elevated script. Sensoren elevated: schijven (temp/SMART) werken; CPU-temperatuur/-vermogen en hoofdbord ontbreken op de Ryzen AI 7 350 met LibreHardwareMonitor 0.9.3 (waarschijnlijk niet ondersteund).
- Kleinere download: installer die .NET 8 controleert, of port naar .NET Framework 4.8 (zit in Windows).
- `Metrics.Update` kost nu ~12 ms per meting (netwerk-PDH ~7 ms is het grootste stuk); schijftellers en VRAM kunnen ook nog naar PDH.
