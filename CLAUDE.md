# TaskbarStats — projectinstructies voor Claude

Windows-monitor (.NET 8, WinForms): een klein widget naast het systeemvak van de taakbalk, plus een groot
bureaublad-dashboard en een fullscreen "cockpit". Toont CPU, GPU, geheugen, netwerk, schijven, batterij, temperaturen;
waarden komen uit dezelfde bronnen als Taakbeheer. Openbaar op GitHub (`ericbruggema/TaskbarStats`, MIT). De gebruiker
praat Nederlands; commentaar in de code is Nederlands. UI-teksten staan in het Engels in de code (`Loc.T`) met vertalingen in `lang/*.json` (nu nl en de; Engels is de brontekst).

## Commando's

```
build.bat                      # sluit de draaiende app, bouwt Release, start opnieuw (vraagt zelf om admin)
dotnet build -c Release        # alleen bouwen -> bin\Release\net8.0-windows\TaskbarStats.exe (controleert ook de taalbestanden: fout = build mislukt)
dotnet test tests\TaskbarStats.Tests -c Debug   # unit tests (Debug: bin\Release is vergrendeld als de app daar draait)
dotnet run --project tools\LangTool -c Release -- check|sync|rename|new|status   # taalbeheer
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
| `Metrics.cs` | tellers: CPU (standaard `Processor Information\% Processor Time`, optie `CpuUtility` = `% Processor Utility`; gemeten: Taakbeheer op Win11 25H2 toont de tijd-gebaseerde waarde, Utility was ~1,9x hoger door boost), GPU via **PDH-wildcard** (`PdhWildcard`), geheugen, netwerk, schijf, VRAM, batterij, DXGI-namen; LibreHardwareMonitor: temps (widget) en `Sensors` (alleen als het fullscreen-scherm open is, elke 2 s, op de sampler-thread) |
| `DashboardForm.cs` | bureaublad-dashboard (tegels, masonry), plus `Ring`, `MetricHistory`, `DashContext` |
| `FullscreenForm.cs` | fullscreen cockpit: vast canvas 1920×1080 dat meeschaalt; overzicht + detail per tegel; Esc = terug/sluiten |
| `SettingsForm.cs`, `AppIcon.cs` | instellingenvenster met tabbladen (Widget incl. bronnen GPU/adapter/schijven, Kleuren, Dashboard, Fullscreen, Thema's, Algemeen); `SettingsHost` = callbacks naar het widget; `AppIcon` = ingebed `app.ico` (ook exe-icoon) voor vensters en systeemvak; elke wijziging gaat direct via `WidgetForm.ApplyAll()` naar widget/dashboard/fullscreen |
| `Themes.cs`, `Tiles.cs` | `ThemeData` (uiterlijk + indeling als JSON, meegeleverd + `%AppData%\TaskbarStats	hemes\`); `Tiles`: ids, volgorde (`DashOrder`/`FullOrder`) en zichtbaarheid van de 8 hoofdonderdelen |
| `WidgetForm.Fmt/Graph/Actions/WinTheme.cs`, `Metrics.Extra.cs`, `AppSettings.*.cs`, `SettingsForm.*.cs`, `BgImage.cs`, `UpdateChecker.cs` | v1.4: de grote klassen zijn `partial`; nieuwe functies staan in eigen bestanden. Waardeopmaak (`AppSettings.Format`, alleen widget), extra items cpufreq/diskbusy/disktemp/mobotemp (`Metrics.Extra`), grafiekstijl (`DisplayStyle.Graph`, `NetStyle`/`PingStyle`), muisacties + klik-door (Ctrl+Alt+W), extra meldingen, `UpdateChecker` (opt-in, `TASKBARSTATS_UPDATE_URL` voor tests), `BgLayer` (achtergrondafbeelding widget/dashboard/fullscreen, gecachet, pad niet in thema's), Windows-thema volgen (`Effective*()`; testen met `TASKBARSTATS_FAKE_THEME`) |
| `HardwareInfo.cs`, `PingMonitor.cs` | specificatiepagina in het fullscreen-scherm (WMI/registry/Win32, één keer async verzameld en gecachet, 30 s) en ping-meting op eigen thread (widget-onderdeel "ping", tooltip, netwerk-details) |
| `UsageTracker.cs` | verbruik per adapter/dag → `usage.json` (thread-veilig) |
| `ProcessSampler.cs` | top-processen; gebruik `SampleAsync()` |
| `AppSettings.cs` | JSON-instellingen in `%AppData%\TaskbarStats\settings.json` (`TASKBARSTATS_DATA` overschrijft de map) |
| `StartupManager.cs` | autostart = geplande taak met hoogste rechten (Run-sleutel werkt niet voor elevated apps) |
| `Loc.cs`, `lang/*.json`, `tools/LangTool` | Vertalingen: `Loc.T("English text", args…)` (de Engelse tekst is de sleutel; `{0}` = `string.Format`; `"text@@ctx"` bij dezelfde tekst met andere betekenis; `Loc.N` markeert teksten in arrays; `Loc.P("{0} day\|{0} days", n)` meervoud met een `_plural`-regel per taal). Talen: ingebedde `lang/<code>.json` plus eigen bestanden in `%AppData%\TaskbarStats\lang`; leeg of ontbrekend = Engels; taal automatisch uit Windows bij eerste start; regiobestanden (`pt-br`) vullen de hoofdtaal aan; meta-sleutels `_name`/`_culture`/`_plural`; testtaal `qps` (`TASKBARSTATS_PSEUDO=1`). `tools/LangTool` (Roslyn) leest de code, draait bij **elke build** (MSBuild-target `CheckLang`, uitschakelen met `-p:SkipLangCheck=true`) en in tests/CI. `Loc.Pick(nl,en)` bestaat nog alleen voor de privé-onderdelen |
| `Diag.cs`, `AppPaths.cs`, `AppSettings.Backup.cs` | `Diag`: `diag.log` in de datamap, crashvangnet (UI-fout = loggen en doordraaien, fatale fout = één herstart met `--restarted`), `Diag.Swallow(ex)` in plaats van een lege `catch { }`, `Diag.Report` (Over-scherm, knop *Copy diagnostics*). Sessiebewaking: `Diag.BeginSession()` (in `Main`) leest `session.lock` (blijft alleen staan bij een onnette afsluiting) en `crash.pending` (geschreven door de fatale-foutafhandelaar) en zoekt bij een harde crash in het Windows-gebeurtenislogboek (`wevtutil`, ID 1000/1023/1026 met TaskbarStats.exe); `Diag.PreviousCrash` laat `WidgetForm.ShowCrashNotice` een ballon tonen. Taskmanager-kill of stroomuitval = alleen een WARN-regel, geen crashmelding. `AppPaths.DataDir`: `TASKBARSTATS_DATA` → portable (`portable.txt` naast de exe → map `data`) → `%AppData%\TaskbarStats`. Backup: export/import/reset van instellingen (`CopyFrom`). Een tweede start van de app opent de instellingen van de eerste (benoemde `EventWaitHandle`; een venstersbericht komt niet aan omdat het widget eigendom van de taakbalk is) |
| `tests/TaskbarStats.Tests` | xUnit: Loc, instellingen, thema's, updatecheck, verbruik, Diag, LangTool, backup. Draait in CI (`.github/workflows/ci.yml`) |

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
taalwissel wordt het menu opnieuw opgebouwd en heropend (`ReopenMenu`). Het menu is kort gehouden (Instellingen, Thema-snelkeuze, Verbruik, Dashboard, Fullscreen, Kopieer/Leesmij en credits/Over (met knop Welkomstscherm), Vastplakken aan systeemvak, Afsluiten): alles rond uiterlijk/indeling zit in het **instellingenvenster** (`SettingsForm`); nieuwe uiterlijk-instellingen daar toevoegen en (als ze in een thema horen) ook in `ThemeData.Capture/ApplyTo`. Het menu bouwt zich bij elke opening opnieuw op
(`RefreshMenu`); houd dat goedkoop. `Application.Exit()` niet gebruiken bij afsluiten (vensters sluiten zichzelf tijdens
het sluiten -> "Collection was modified"); `Close()` op het widget.

**Windows-details.** "Bureaublad weergeven" minimaliseert/verbergt vensters → het dashboard herstelt zichzelf
(`EnsureVisible`). Bovendien haalt de shell dan het bureaubladvenster (Progman) boven alle gewone vensters: `HWND_BOTTOM` liet het dashboard erachter verdwijnen. `PlaceAboveDesktop` (elke 0,4 s) gaat er dan via TOPMOST→NOTOPMOST bovenop (`HWND_TOP` alleen werkt niet) en zakt weer als Progman terug is; "getoond" = er staat een zichtbaar programmavenster van een ander proces ONDER Progman (`AppWindowBelow`; tellen van vensters onder Progman werkt niet: er zitten altijd ~18 verborgen hulpvensters onder en dat gaf knipperen); testen met `Shell.Application.ToggleDesktop()` en z-volgorde/`WindowFromPoint`. Fullscreen-apps worden herkend met `SHQueryUserNotificationState`. Hotkeys: Ctrl+Alt+D (klik-door dashboard),
Ctrl+Alt+F (fullscreen). Widget verbergt zichzelf bij fullscreen (instelling `HideInFullscreen`). Perf-namen zijn Engels
(`PdhAddEnglishCounter`/`PerformanceCounter` gebruiken Engelse namen, ook op Nederlandse Windows).

**Temperatuur.** LibreHardwareMonitor 0.9.3 (en 0.9.6, getest) geeft op de Ryzen AI 7 350 geen bruikbare CPU-sensor (0.9.6: alleen nullen). `Metrics.UpdateTemperatures` valt daarom terug op de ACPI-thermal zone (PDH `Thermal Zone Information\High Precision Temperature`, ~elke 2 s, hoogste zone); GPU: NVIDIA "GPU Core", anders hot spot/SoC. NVIDIA-GPU werkt gewoon. 0.9.6 vraagt System.Management >= 10, dus niet zomaar upgraden. WMI (`HardwareInfo`) nooit op de UI-thread.

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

- Nederlandse commentaren; elke nieuwe zichtbare tekst als `Loc.T("English text")` (alleen vaste teksten; nooit `$"…"` of een variabele erin; `// lang-dynamic` alleen voor uitzonderingen, `// nolang` voor bewust vaste tekst). Daarna `LangTool sync`, de lege waarden in `lang/nl.json` en `lang/de.json` vertalen en `LangTool check`. Een Engelse tekst wijzigen: `LangTool rename "oud" "nieuw"`, nooit met de hand. Een tekst met een aantal (dag/dagen): `Loc.P`.
- Lege `catch { }` bestaat niet meer: gebruik `catch (Exception dex) { Diag.Swallow(dex); }` (of vang gericht) zodat een fout een spoor in `diag.log` achterlaat.
- Nieuwe instelling = property in `AppSettings` met default (oude `settings.json` blijft werken), opslaan via `Persist()`/`Relayout()`.
- Code sluit aan op de omringende stijl; geen onnodige abstracties. Bestanden hebben CRLF (`.gitattributes`).
- Repo is **openbaar**: geen persoonlijke gegevens, geen schermafbeeldingen met privé-inhoud. Commit-auteur gebruikt het
  GitHub-`noreply`-adres. Commit-berichten eindigen met de `Co-Authored-By`-regel uit de sessie.
- Documentatie bijwerken bij functionele wijzigingen: `README.md`, `installer\Leesmij.txt`, `WelcomeForm.cs`, dit bestand.

## Ideeën / nog te doen

- Na 1.4.0: instellingen Widget hebben subtabs (`SettingsForm.Sub.cs`: Onderdelen/Bronnen/Weergave/Waarden/Uiterlijk/Geavanceerd; ook Dashboard/Fullscreen/Algemeen hebben subtabs via `Subs()`; instellingen van uitgezette onderdelen blijven bewerkbaar (niet uitschakelen); alleen echt overschreven opties (Compact->labels, Windows-thema->kleuren) worden grijs met `Why(() => reden)`-uitleg), grafieklengte (`GraphLength`), welkomstscherm uit het menu (knop in Over), en `StickToTray` (vervangt "Reset positie"; `WidgetForm.Stick.cs`, `FollowTray` in `Tick`, verslepen zet het uit, nieuwe installaties starten vastgeplakt). Testharnas: een timer die met `Application.DoEvents()` in een stap zit kan zichzelf re-entrant starten (stappen lopen dan door elkaar).
- **Grafieklengte per onderdeel** (na 1.5.0): `AppSettings.GraphSizes` (sleutels `AppSettings.GraphKeys`: cpu, gpu, mem, cputemp, gputemp, net, ping; geen regel of `Len = null` = volgt de algemene `GraphLength`/`GraphWidthPx`), gelezen via `GraphWidthFor(key)` in `WidgetForm.GraphCell`; zit ook in `ThemeData`; UI: `SettingsForm.GraphSizeRow` (Geavanceerd).
- **v1.4.0** (gebouwd door vijf parallelle agents in worktrees, daarna gemerged): waardeopmaak, extra items, grafiekstijl, achtergrondafbeelding, Windows-thema volgen, muisacties/klik-door, extra meldingen (incl. schijftemperatuur), updatecontrole. Testharnas-ankers: `ActionsOnShown();` (was `SyncDashboard();`) in de OnShown-hook van de patchscripts. Niet gebouwd (bewust): per-item kleur/label, tray-waarden, taakbalk-embedden, plugins, Lite/portable, kleinere installer.
- **v1.3.0 is gepubliceerd**: specificatiepagina (toets I), fullscreen-tour (3× klikken op leeg of spatie; `TourSeconds`, ook in het menu), ping (`ShowPing`, `PingHost`), CPU-temperatuur via ACPI-terugval, temperatuurstijl/-samenvoegen (`CpuTempStyle`, `GpuTempStyle`, `TempMerge`), aftiteling bij Leesmij en credits.
- Tegels slepen i.p.v. pijlknoppen; thema per situatie automatisch (bv. op batterij); controle op nieuwe versie via GitHub-releases; ping-/uptime-tegel.
- **v1.2.1 is gepubliceerd** (installer elevated getest: installeren, Startmenu, register, autostart-taak, icoon in de exe, niet-stille verwijdering incl. datavraag Ja/Nee). Testtip uninstall: de Inno-uninstaller draait als `_unins.tmp` (proces `unins000` sluit direct); UIA ziet Inno-knoppen als Pane, dus stuur toetsen (`j`/`n`/Enter) na `AppActivate`. Back-up `%AppData%\TaskbarStats` vóór de test, want "Ja" wist die map. **v1.2.0/1.2.1 inhoud**: keuzeknoppen i.p.v. pulldowns, meer kolommen, afhankelijke bedieningselementen uitgeschakeld (`Dep` in `SettingsForm`), indelingsvoorbeeld, themavoorbeeld; menu-volgorde. Uit 1.2.0: instellingenvenster + thema's + volgorde/aan-uit van dashboard- en fullscreen-onderdelen + zuiniger meten (zie README). Alleen na akkoord van de gebruiker publiceren; screenshots altijd meenemen en bijwerken.
- **Release v1.1.0 is gepubliceerd.** Installer elevated getest: installeren, Startmenu, register, autostart-taak (HighestAvailable), verwijderen. Les: `[Code]`-`CurrentUninstallStepChanged` bleek bij het verwijderen niet te draaien; de taak wordt nu via `[UninstallRun]` (schtasks /Delete) weggehaald. Elevated testen kan via een script dat zichzelf met `Start-Process -Verb RunAs` start; omgevingsvariabelen gaan niet mee door RunAs, zet ze binnen het elevated script. Sensoren elevated: schijven (temp/SMART) werken; CPU-temperatuur/-vermogen en hoofdbord ontbreken op de Ryzen AI 7 350 met LibreHardwareMonitor 0.9.3 (waarschijnlijk niet ondersteund).
- Kleinere download: installer die .NET 8 controleert, of port naar .NET Framework 4.8 (zit in Windows).
- `Metrics.Update` kost nu ~12 ms per meting (netwerk-PDH ~7 ms is het grootste stuk); schijftellers en VRAM kunnen ook nog naar PDH.
