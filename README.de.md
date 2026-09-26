# TaskbarStats

**Sprachen / Languages:** [English](README.md) · [Nederlands](README.nl.md) · **Deutsch**

![TaskbarStats live in the taskbar, and FPS on top of a game](docs/tour/promo/hero-loop-en.gif)

Ein leichtgewichtiger CPU-/GPU-/Speicher-/Netzwerk-/Datenträger-/Akku-/Temperatur-Monitor, der neben dem
Infobereich der Windows-Taskleiste schwebt – ähnlich wie TrafficMonitor, aber mit Werten, die
mit dem Task-Manager übereinstimmen. Dazu kommen ein großes Desktop-Dashboard und ein Vollbild-„Cockpit“.

## Demo videos

Four English demos of 45 to 60 seconds each. Download, install and run takes a few seconds in every one of them, so most of the time goes to the program itself:

| Video | For | What it shows |
|-------|-----|---------------|
| [Quick start (47 s)](https://github.com/ericbruggema/TaskbarStats/releases/download/v1.6.0/quick-start-en.mp4) | Everyone | Download, install, run, right-click menu, themes with backgrounds, JSON editor with preview, processes and FPS, dashboard and fullscreen |
| [For number lovers (57 s)](https://github.com/ericbruggema/TaskbarStats/releases/download/v1.6.0/nerds-en.mp4) | Statistics nerds | Every graph with its own length, four display styles, processes (all, apps, background), tooltip details, dashboard, fullscreen cockpit, specifications, JSON everything |
| [For gamers (51 s)](https://github.com/ericbruggema/TaskbarStats/releases/download/v1.6.0/gamers-en.mp4) | Gamers | FPS in the taskbar or floating on top of your game, 1% low, tiny overlay mode, click-through (Ctrl+Alt+W), GPU and CPU temperatures, ping, themes |
| [The complete tour (59 s)](https://github.com/ericbruggema/TaskbarStats/releases/download/v1.6.0/tour-en.mp4) | Everyone | The whole program, feature by feature |

More documentation is listed in the [docs index](docs/README.md).

## Für Gamer 🎮

Mach aus deiner Taskleiste einen kostenlosen FPS-Zähler, oder zieh das Widget über dein Spiel und lass es obenauf schweben.

![FPS, GPU-Last und -Temperatur, CPU und Ping schwebend über einem Spiel](docs/screenshots/gamers-overlay.png)

- **FPS des Spiels, das du gerade spielst** — gemessen wie bei PresentMon (Windows-Ereignisablaufverfolgung), für das Programm im Vordergrund. Funktioniert mit DirectX-Spielen (DXGI/D3D9) und Browsern. *Experimentell*: unter Einstellungen → Widget → Werte einschalten.
- **Mini-Overlay** — kompakt, Beschriftung oben, nur FPS, GPU und CPU:

![Mini-Overlay über einem Spiel](docs/screenshots/gamers-mini.png)

- **Durchklicken** (Strg+Alt+W): das Widget bleibt sichtbar, deine Mausklicks gehen aber direkt ans Spiel.
- **Alles, was Gamer wissen wollen**: GPU-Last, GPU- und CPU-Temperatur, Ping und ein Diagramm für den Übeltäter.
- **Such dir einen Look, der zu deinem Rechner passt**: Neon für Retro-Racing-Feeling, Matrix für Hacker, Game Boy für Nostalgie, Amber und CGA, wenn du die 80er vermisst.

![Zehn Designs, alle mit FPS](docs/screenshots/themes-fps.png)

Live ansehen: [For gamers (51 s, Englisch)](https://github.com/ericbruggema/TaskbarStats/releases/download/v1.6.0/gamers-en.mp4).

**Kein Gamer?** Dasselbe Widget zählt auch deine Prozesse (gesamt, sichtbare Apps, Hintergrund oder beides):

![Prozesse: gesamt, Apps, Hintergrund, beides](docs/screenshots/widget-processen.png)

## Screenshots

*Die Screenshots zeigen die englische Oberfläche (die Werte sind erfunden); einige ältere zeigen noch die niederländische. Das Programm gibt es auch auf Niederländisch und Deutsch.*

**Taskleisten-Widget** — Zahlen, Anzeigen, Balken oder Diagramme, mit FPS und Prozessanzahl neben CPU, GPU und Speicher:

![Widget mit Zahlen](docs/screenshots/widget-digital.png)
![Widget mit Anzeigen](docs/screenshots/widget-gauges.png)
![Vier Darstellungen: Zahlen, Anzeigen, Balken, Diagramme](docs/screenshots/widget-stijlen.png)
![Kompaktes Widget](docs/screenshots/widget-compact.png)

**Rechtsklickmenü** (kurz, und es bleibt offen, während du mehrere Dinge auswählst) und das **Einstellungsfenster** mit Tabs
(Erscheinungsbild, Farben, Dashboard, Vollbild, Designs), in dem jede Änderung sofort sichtbar ist:

![Menu](docs/screenshots/menu.png)
![Instellingen: widget](docs/screenshots/settings-widget.png)
![Instellingen: widget, subtabblad Weergave](docs/screenshots/settings-widget-weergave.png)
![Instellingen: widget, subtabblad Waarden (processen, FPS, schijf-labels)](docs/screenshots/settings-widget-waarden.png)
![Instellingen: widget, subtabblad Geavanceerd](docs/screenshots/settings-widget-geavanceerd.png)
![Instellingen: dashboard met volgorde](docs/screenshots/settings-dashboard.png)
![Instellingen: fullscreen met voorbeeld van de indeling](docs/screenshots/settings-fullscreen.png)
![Instellingen: algemeen](docs/screenshots/settings-general.png)
![Instellingen: thema's](docs/screenshots/settings-themes.png)

Einige Designs haben einen eigenen **Hintergrund**, der im Code gezeichnet wird (also keine Dateien): Matrix (fallende Zeichen), Neon (Raster), Sonnenuntergang (Sonne), Wald (Tannenwald mit Glühwürmchen), Ozean (Wellen), Liebe (Herzen, groß und klein), Dracula (Sterne), Amber, CGA und Game Boy (Bildröhren-Zeilen). Das Design setzt ihn gleichzeitig auf Widget, Dashboard und Vollbild; ein Design ohne Hintergrund entfernt ein solches Muster wieder (dein eigenes Bild bleibt bestehen). Im JSON eines Designs stehen `Background` (`matrix`, `grid`, `stars`, `sunset`, `waves`, `scanlines`, `hearts` oder `forest`) und `BackgroundOpacity` (5–100).

Am **14. Februar** startet das Programm vorübergehend mit dem Design „Liebe“ (nur im Arbeitsspeicher: `settings.json` behält dein eigenes Erscheinungsbild, und sobald du die Einstellungen öffnest oder selbst ein Design wählst, gehört es wieder dir). Ausschalten kannst du das unter Einstellungen → *Designs* (*Am 14. Februar das Love-Design zeigen (vorübergehend, beim Start)*).

![Widgets met de achtergronden van de thema's](docs/screenshots/themes-achtergronden.png)

Die mitgelieferten Designs, hier auf dem Desktop-Dashboard:

![Alle meegeleverde thema's](docs/screenshots/themes-fps.png)

**Desktop-Dashboard** (halbtransparent, skalierbar, Vordergrund oder Hintergrund, Durchklicken):

![Bureaublad-dashboard](docs/screenshots/dashboard.png)

**Vollbild-Dashboard** (Strg+Alt+F) – Übersicht, und ein Klick auf eine Kachel zeigt Details:

![Fullscreen overzicht](docs/screenshots/fullscreen-overview.png)
![Fullscreen CPU-details](docs/screenshots/fullscreen-cpu.png)
![Fullscreen netwerk-details](docs/screenshots/fullscreen-net.png)
![Fullscreen GPU-details met sensoren](docs/screenshots/fullscreen-gpu.png)
![Fullscreen schijf-details met temperatuur en gezondheid](docs/screenshots/fullscreen-disk.png)
![Fullscreen met eigen volgorde en een onderdeel uit (Neon-thema)](docs/screenshots/fullscreen-layout.png)

*(Computername und Programmnamen in den Screenshots sind anonymisiert.)*

## Warum die Werte hier tatsächlich stimmen

| Komponente | Quelle | Warum korrekt |
|------------|--------|---------------|
| CPU | `Processor Information\% Processor Time` (gesamt und pro Kern) | Derselbe Wert wie im Task-Manager. Optional (Einstellungen → Widget → *CPU-Messung*) `% Processor Utility`, das den Boost-Takt mit einrechnet und bei einer boostenden CPU fast doppelt so hoch ist. |
| Speicher | `GlobalMemoryStatusEx.dwMemoryLoad` | Genau der Prozentsatz, den der Task-Manager anzeigt. |
| GPU | Summe aller `GPU Engine\Utilization Percentage` pro GPU (LUID) | Zählt 3D-, Copy- und Video-Engines zusammen, genau wie der Task-Manager. Die Zähler werden alle 5 s neu aufgebaut, weil Engine-Instanzen mit Prozessen kommen und gehen. |
| Netzwerk | `Network Interface\Bytes Received/Sent per sec` | Pro Adapter oder alle Adapter zusammen. |
| Temperatur | LibreHardwareMonitorLib, mit ACPI-Thermal-Zone als CPU-Rückfall | CPU-/GPU-Temperatur; erfordert Administratorrechte. Hat LibreHardwareMonitor keinen CPU-Sensor (z. B. bei neuen Ryzen-Chips), wird die System-Thermal-Zone von Windows verwendet. |
| Ping | `System.Net.NetworkInformation.Ping` | Latenz, Jitter und Paketverlust zu einem einstellbaren Ziel. |
| Spezifikationen | WMI (`System.Management`), Registry, Win32 | Hardware-Inventar für die Spezifikationsseite. |

Die GPU steht standardmäßig auf **automatisch**: Sie folgt der am stärksten ausgelasteten GPU (praktisch bei iGPU +
dGPU). Du kannst in den Einstellungen auch eine feste GPU wählen (Tab Widget → Quellen).

## Bauen

Voraussetzung: Windows und das .NET 8 SDK (https://dotnet.microsoft.com/download).

Am einfachsten: Doppelklick auf **`build.bat`**. Das Skript

1. fragt bei Bedarf selbst nach Administratorrechten (nötig, um die laufende App zu beenden),
2. beendet `TaskbarStats.exe`, falls sie läuft,
3. baut einen Release-Build,
4. startet die App danach wieder.

Manuell geht es auch:

```
dotnet build -c Release
```

Die `.exe` liegt danach in `bin\Release\net8.0-windows\TaskbarStats.exe`.
Für eine einzelne eigenständige Datei ohne .NET-Installation:

```
dotnet publish -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true
```

## Testen, ohne deine Einstellungen anzutasten

Zwei Umgebungsvariablen lassen eine Testkopie neben der echten App laufen:

| Variable | Wirkung |
|----------|---------|
| `TASKBARSTATS_DATA` | Eigener Ordner für `settings.json` und `usage.json` (statt `%AppData%\TaskbarStats`). |
| `TASKBARSTATS_INSTANCE` | Suffix für den Single-Instance-Mutex, damit eine zweite Instanz starten kann. |
| `TASKBARSTATS_PSEUDO` | `1` zeigt die Testsprache *Pseudo (test)* in der Sprachliste (Kontrolle auf nicht übersetzte oder zu lange Texte). |

Die App verlangt Administratorrechte (`app.manifest`); für automatisierte Tests setzt du in einer temporären Kopie
`requireAdministrator` auf `asInvoker`. Siehe auch `CLAUDE.md`.

## Installer (Inno Setup)

Inno Setup einmalig installieren: `winget install JRSoftware.InnoSetup`
(oder https://jrsoftware.org/isdl.php). Danach doppelklickst du **`build-installer.bat`**: Das
veröffentlicht die App und kompiliert `installer\TaskbarStats.iss` zu
`installer\Output\TaskbarStats-Setup-<Version>.exe`.

Der Installer:

- zeigt zuerst die Liesmich (Erklärung + Credits, `installer\Leesmij.txt`) und installiert sie mit;
- legt eine Startmenü-Gruppe mit **TaskbarStats**, **Leesmij (Erklärung und Credits)** und
  **TaskbarStats deinstallieren** an; optional auch eine Desktop-Verknüpfung;
- schaltet auf Wunsch den Autostart über eine geplante Aufgabe mit höchsten Rechten ein (keine UAC-Meldung
  bei der Anmeldung; die App verlangt Administratorrechte und startet nicht über den Run-Schlüssel);
- beendet eine laufende Version beim Installieren/Deinstallieren;
- registriert einen Uninstaller (auch unter Einstellungen → Apps), der die Aufgabe entfernt und fragt, ob
  deine Einstellungen und das Nutzungsprotokoll (`%AppData%\TaskbarStats`) ebenfalls gelöscht werden dürfen.

Die App selbst hat außerdem „Credits“ im Rechtsklickmenü: ein ruhig scrollender Abspann mit den Credits, ein paar Zahlen aus deiner eigenen Nutzung (zum Beispiel, wie viel du heruntergeladen hast) und einem Scherz.

![Leesmij en credits](docs/screenshots/credits.png)

Name, Credits oder Text anpassen: `installer\Leesmij.txt`, `CreditsForm.cs` und `#define Publisher` in der `.iss`-Datei.

## Verwendung

Starte `TaskbarStats.exe`. Das Widget erscheint als schwebendes, immer im Vordergrund liegendes Fenster
direkt links vom Infobereich. Es ist ein Besitzer-Fenster der Taskleiste und bleibt daher
darüber. Ziehe es mit der **linken Maustaste**, um es zu verschieben (die Position wird
beim Loslassen gespeichert); in den Einstellungen (Tab Allgemein) und im Menü gibt es „An den Infobereich anheften“: Dann bleibt das Widget am Infobereich und folgt ihm, wenn er sich verschiebt (Ziehen schaltet das wieder aus).

Ein **Rechtsklick** auf das Widget öffnet ein kurzes Menü:

- **Einstellungen…** (fett, ganz oben) öffnet das Einstellungsfenster mit Tabs. Jede Änderung wird sofort angewendet und gespeichert;
  Bedienelemente, die durch eine andere Wahl keine Wirkung haben, werden deaktiviert:
  - *Widget*: Komponenten (CPU, GPU, Speicher, Up-/Download, Akku, Datenträger, Temperaturen) mit ihren **Quellen** (welche GPU oder automatisch, welcher
    Netzwerkadapter oder alle, Speicherplatz aus/gesamt/alle einzeln/ein Datenträger, Netzlaufwerke einbeziehen), Darstellung pro Komponente (digital, Anzeige oder Balken;
    CPU pro Kern), Akkuprozentsatz, Beschriftung oben, kompakt, transparent, Höhe, Schriftart/-größe und Aktualisierungsrate.
    Der Tab ist in Subtabs unterteilt: *Komponenten und Layout*, *Quellen*, *Darstellung*, *Werte*, *Erscheinungsbild* und *Erweitert* (Länge des Diagramms, auch pro Komponente, Zeitraum, Skala des Netzwerkdiagramms, CPU-Messung und Aktualisierungsrate). Auch *Dashboard*, *Vollbild* und *Allgemein* haben Subtabs. Einstellungen von Komponenten, die gerade ausgeschaltet sind (zum Beispiel GPU), kannst du ruhig schon ausfüllen; sie gelten, sobald du die Komponente einschaltest.
    Die **Reihenfolge der Komponenten im Widget** (Netzwerk, Datenträger-I/O, CPU, GPU, Speicher, Akku, Speicherplatz, Temperaturen) stellst du mit den Pfeiltasten ein; ausgeschaltete Komponenten stehen mit „(aus)“ dabei. Die Reihenfolge ist auch Teil der Designs.
  - *Farben*: Text, Hintergrund, Anzeige/Balken, Warnung, kritisch, Rand und Schwellenwerte (z. B. 85 % / 95 %).
  - *Dashboard*: anzeigen, Vorder-/Hintergrund, Durchklicken, sperren, Transparenz, Skalierung, Spalten und **Reihenfolge und An/Aus pro Hauptkomponente**.
  - *Vollbild*: welcher Bildschirm sowie Reihenfolge und An/Aus pro Hauptkomponente, mit einer Vorschau des Layouts.
  - *Designs*: 16 mitgelieferte Designs (Standard, Dunkel, Hell, Schwarz-Weiß, Liebe, CGA, Matrix, Amber, Game Boy, Dracula, Ozean, Sonnenuntergang, Wald, Minimal, Meters, Neon); eigenes Design speichern, laden, löschen, importieren und exportieren (siehe unten).
  - *Allgemein*: Sprache (Nederlands, English, Deutsch oder eine eigene Sprachdatei), mit Windows starten (geplante Aufgabe, keine UAC-Meldung), Position sperren und an den Infobereich anheften, bei Vollbild ausblenden, **Wartezeit des Tooltips** (mindestens 2 s, standardmäßig 2 s, oder aus), Benachrichtigungen (fast volle Festplatte, dauerhaft hohe Auslastung), Monatslimit für die Netzwerknutzung und das Nutzungsprotokoll.
- **Design**: Schnellauswahl, um mit einem Klick ein Design anzuwenden.
- **Nutzung**: empfangen/gesendet pro Netzwerkadapter (Sitzung, heute, gestern, 7 Tage, Monat) und das Protokoll pro Tag mit CSV-Export.
- **An den Infobereich anheften** (das Widget bleibt am Infobereich und folgt ihm).
- **Desktop-Dashboard** und **Vollbild-Dashboard**, jeweils ein Untermenü (Dashboard: anzeigen, Durchklicken, sperren, Einstellungen; Vollbild: öffnen/schließen, Tour, Einstellungen), dann *Nutzung*, *Infos kopieren*, *Systeminformationen kopieren*, *Credits*, *Über TaskbarStats* (mit einer Schaltfläche für den Willkommensbildschirm) und **Beenden**.
- **Tooltip**: Halte die Maus mindestens 2 s (einstellbar) über dem Widget, um alle Details zu sehen (RAM in GB, pro GPU, Netzwerk pro Adapter, Datenträger und Speicherplatz, Temperaturen).
- **Akku** (Laptops): stehender Akku mit Füllstand, Farbe und Blitz/Stecker; Prozentanzeige in, neben oder aus.

Außerdem: Ein Doppelklick öffnet den Task-Manager, die mittlere Maustaste kopiert die Tooltip-Informationen
in die Zwischenablage, und beim ersten Start erscheint ein Willkommensbildschirm.

Das Menü bleibt offen, wenn du eine Option anklickst, damit du mehrere Dinge nacheinander
einstellen kannst. Es schließt sich, wenn du außerhalb des Menüs klickst, Esc drückst oder die Maus etwa eine
Sekunde lang nicht mehr über dem Menü (oder einem Untermenü) ist.

Es läuft immer nur eine Instanz gleichzeitig.

## Desktop-Dashboard und Vollbild

**Desktop-Dashboard** (Einstellungen → *Dashboard*, oder Menü *Desktop-Dashboard*; standardmäßig aus): ein großes, halbtransparentes Fenster mit Kacheln
(CPU mit Balken pro Kern, GPU pro Karte mit VRAM, Speicher, Netzwerk mit einem Minidiagramm pro Adapter, Datenträger,
Akku, ressourcenhungrigste Programme, System) und Diagrammen der letzten 5 Minuten. Alles lässt sich einzeln ein- und ausschalten;
außerdem einstellbar: Vorder- oder Hintergrund, Skalierung 50–300 %, Transparenz, 1–4 Spalten, sperren und **Durchklicken**
(Klicks gehen hindurch; **Strg+Alt+D** oder ein Doppelklick auf das Symbol neben der Uhr schaltet das ein/aus, denn dann funktioniert
der Rechtsklick nicht mehr). Es bleibt sichtbar, wenn du „Desktop anzeigen“ benutzt.

**Vollbild-Dashboard** (**Strg+Alt+F**, oder Menü *Vollbild-Dashboard*): eine dichte Übersicht über alles auf einem ganzen Bildschirm
(wählbar, welcher). Ein Klick auf eine Kachel öffnet die ausführliche Ansicht (Diagramm bis 1 Stunde, min/Durchschnitt/max, pro Kern, pro GPU
mit VRAM, pro Adapter, pro Datenträger, Top-Programme, Systeminfos); **Esc** geht zurück und schließt aus der Übersicht heraus.
Die Taste **1/2/3** wählt das Diagramm: 1 Min. / 5 Min. / 1 Std.

**Spezifikationen** (Schaltfläche oben oder Taste **I**): eine Seite mit aller Hardware und allen Systeminfos – Computer, Hauptplatine und BIOS, Windows,
Prozessor (Kerne/Threads, Taktraten, Cache, Befehlssätze), Grafikkarten (Treiber, VRAM, Ausgänge), Speichermodule (Typ, Geschwindigkeit,
Hersteller), Datenträger (Typ, Zustand, Firmware) und Volumes, Bildschirme (Format, Bildwiederholrate), Netzwerkadapter, Akku (Verschleiß,
Ladezyklen), Sicherheit (Secure Boot, TPM), Audio, Eingabegeräte, Bluetooth- und USB-Geräte. Scrollen mit dem Mausrad.

![Fullscreen specificaties](docs/screenshots/fullscreen-specs.png)

**Automatische Tour**: Klicke dreimal auf eine leere Stelle im Vollbildschirm (oder drücke die Leertaste), und der Bildschirm geht alle Seiten durch
(Übersicht, jedes Detail, Spezifikationen). Die Zeit pro Seite (standardmäßig 10 s) stellst du über das Menü (*Vollbild-Tour*) oder in
Einstellungen → *Vollbild* ein. Ein Klick oder eine Taste beendet die Tour.

**Ping**: Schalte *Ping (Latenz)* unter Einstellungen → *Widget* ein; das Widget zeigt dann zum Beispiel `PING 12 ms` (orange ab 100 ms, rot ab 250 ms oder ohne Antwort). Das Ziel (standardmäßig 1.1.1.1) ist einstellbar. Im Tooltip und in den Netzwerkdetails des Vollbildschirms stehen min/Durchschnitt/max, Jitter und Paketverlust der letzten ca. 2 Minuten.

![Widget met ping en temperaturen](docs/screenshots/widget-ping.png)

**Temperaturanzeige**: CPU- und GPU-Temperatur kannst du (Einstellungen → *Widget* → *Darstellung*) als Zahl, Anzeige oder Balken darstellen oder – schmaler – klein hinter die CPU-/GPU-Zelle setzen (*Temperatur klein hinter der CPU-/GPU-Zelle (schmaler)*), zum Beispiel `CPU 33%  55°`. Auch das gehört zu den Designs.

![Temperatuur als cijfer, meter, balk of samengevoegd](docs/screenshots/widget-temperatuur.png)

**Wertformatierung** (Einstellungen → *Widget* → *Werte*, nur das Widget): Netzwerkgeschwindigkeit in Bytes (MB/s) oder Bits (Mb/s), automatische oder feste Einheit (KB/s oder MB/s), kurze Werte, Einheit oder %-Zeichen weglassen, Upload und Download vertauschen sowie der Speicher als Prozentsatz, belegt (GB) oder verfügbar (GB).

**Datenträger Lesen/Schreiben als Pfeile**: Unter Einstellungen → *Widget* → *Werte* wählst du bei *Laufwerk Lesen/Schreiben anzeigen als* `R / W` oder Pfeile (↓ Lesen, ↑ Schreiben); das gilt für das Widget, den Tooltip, das Dashboard und den Vollbildschirm.

**Laufende Prozesse** und **FPS** (Einstellungen → *Widget* → *Komponenten*): die Anzahl der Prozesse als `PROC 250`, oder nur die *Apps* (Prozesse mit einem sichtbaren Fenster, wie Apps im Task-Manager), nur die *Hintergrundprozesse* oder beides (`APPS/BG 5/245`); das wählst du unter *Werte*. Der Tooltip zeigt auch die Anzahl der Threads, und die Systemkachel des Dashboards und die Vollbildseite *System* zeigen die Anzahlen. **FPS (experimentell)** misst die Framerate des Programms im Vordergrund, wie PresentMon: Eine ETW-Sitzung lauscht auf die Present-Ereignisse von DirectX (DXGI/D3D9, mit DxgKrnl unter anderem für Vulkan) und zählt sie pro Prozess (nur Prozess-ID und Zeitpunkt, kein Inhalt). Es ist standardmäßig aus, braucht Administratorrechte, kostet etwas Prozessorleistung, solange es an ist, und zeigt nur dann einen Wert, wenn ein Programm Bilder zeichnet (Spiele, Videoplayer, Browser); der Tooltip nennt auch das 1-%-Low und das Programm.

**Zusätzliche Komponenten** für das Widget: CPU-Taktfrequenz, Datenträger aktiv (%), Datenträgertemperatur und Hauptplatinentemperatur. Die letzten beiden lesen Sensoren über LibreHardwareMonitor (Administrator nötig) und verschwinden von selbst, wenn es keinen Wert gibt.

**Diagramm als Darstellungsstil** (Länge sehr kurz, kurz, mittel, lang oder in Pixeln frei einstellbar; Zeitraum 30/60/120 s): CPU, GPU, Speicher, Temperaturen, Netzwerk (Download und Upload als zwei Linien, Skala automatisch oder fest) und Ping können als Mini-Verlaufsdiagramm im Widget erscheinen (letzte 30, 60 oder 120 Sekunden). Fehlende Pings (keine Antwort) unterbrechen die Linie mit einem roten Punkt.

![Widget met de nieuwe onderdelen, grafieken en opmaak](docs/screenshots/widget-nieuw.png)

![Grafiek in vier lengtes: zeer kort, kort, middel, lang](docs/screenshots/widget-grafieklengte.png)

**Länge pro Diagramm**: Jedes Diagramm im Widget kann eine eigene Länge haben, zum Beispiel das Netzwerk breit, die CPU sehr kurz und der Speicher auf 100 px. Unter Einstellungen → *Widget* → *Erweitert* → *Diagrammlänge pro Element* wählst du pro Komponente *Standard* (folgt der allgemeinen Länge), *Sehr kurz*, *Kurz*, *Mittel*, *Lang* oder *Benutzerdefiniert* mit einer eigenen Breite von 16 bis 160 px. Die Auswahl gehört zum Design und wird sofort gespeichert.

![Elke grafiek een eigen lengte: netwerk 140 px, ping standaard, CPU zeer kort, GPU lang, geheugen 100 px, temperaturen kort en standaard](docs/screenshots/widget-grafieklengte-per-onderdeel.png)

**Hintergrundbild**: Wähle pro Fenster (Widget, Dashboard und Vollbild; Einstellungen, Tabs *Widget*, *Dashboard* und *Vollbild*) ein png/jpg/bmp mit dem Modus *Füllen*, *Strecken*, *Anpassen*, *Kacheln* oder *Zentriert* und einer Deckkraft von 0–100 %. Das Bild liegt unter Text und Kacheln; fehlt die Datei, gilt der normale Hintergrund. Pfade gehören nicht zu den Designs.

![Widget met achtergrondafbeelding](docs/screenshots/widget-achtergrond.png)

**Windows-Design folgen** (Widget, Einstellungen → *Farben*): *Dunkel/Hell* wählt einen dunklen oder hellen Hintergrund mit automatisch lesbarem Text; *Dunkel/Hell + Akzent* verwendet zusätzlich die Akzentfarbe von Windows für Anzeigen und Balken. Ein Wechsel des Windows-Designs wird sofort übernommen.

**Mausaktionen und Durchklicken** (Einstellungen → *Allgemein*): Wähle, was ein Doppelklick und die mittlere Maustaste bewirken (Task-Manager, Dashboard, Vollbild, Einstellungen, Nutzungsprotokoll, Infos kopieren, Menü oder nichts). **Strg+Alt+W** (oder das Symbol neben der Uhr) lässt Klicks durch das Widget hindurchgehen.

**Mehr Benachrichtigungen**: Schwellenwerte für CPU-, GPU- und Datenträgertemperatur, Speichernutzung und Netzwerknutzung pro Tag (0 = aus).

**Updateprüfung** (optional, standardmäßig aus): Höchstens einmal pro 24 Stunden wird über GitHub geprüft, ob es eine neuere Version gibt; das wird nur gemeldet (Menüeintrag und Benachrichtigung), es wird nie etwas heruntergeladen oder installiert.

**Sensoren** (LibreHardwareMonitor, nur aktiv, solange der Vollbildschirm geöffnet ist): CPU-Leistung, -Temperaturen und -Taktraten,
GPU-Temperatur/-Leistung/-Takt/-Lüfter pro Karte, Datenträgertemperatur und -zustand (SMART), Hauptplatine und Lüfter,
Speicher und Akku. Für CPU, Hauptplatine und Datenträger sind Administratorrechte nötig (die App fragt sie bereits an); ohne
diese Rechte zeigt der Bildschirm die Sensoren, die verfügbar sind (GPU, Speicher, Akku), und eine kurze Erklärung.

**Dashboard und Vollbild haben denselben Aufbau** mit drei Subtabs: *Fenster*, *Komponenten und Layout* (mit einer Miniatur des Layouts; das Dashboard hat dort auch die Anzahl der Spalten) und *Hintergrund*. Der Tab Widget nennt seinen ersten Subtab ebenfalls *Komponenten und Layout*.

**Reihenfolge und Komponenten**: Im Einstellungsfenster (Tab *Dashboard* oder *Vollbild*) schaltest du die Hauptkomponenten (CPU, GPU, Speicher, Netzwerk, Datenträger, Akku, ressourcenhungrigste Programme, System) ein oder aus und verschiebst sie mit den Pfeiltasten. Das Desktop-Dashboard füllt seine Spalten in dieser Reihenfolge; der Vollbildschirm füllt Zeilen mit vier Spalten (die CPU ist zwei breit; Akku und System teilen sich eine Zelle) und passt die Breite an.

**Sprachen**: Die App gibt es auf Niederländisch, Englisch und Deutsch; beim ersten Start folgt sie der Sprache von Windows, sofern verfügbar (auch Regionalsprachen wie `pt-br`, die `pt` ergänzen). Texte stehen nicht im Code, sondern in `lang/<code>.json` (in die exe eingebettet): links der englische Quelltext, rechts die Übersetzung. Ein leeres Feld bedeutet „noch nicht übersetzt“ und zeigt Englisch. Das Hilfsprogramm `tools\LangTool` liest den Code richtig (Roslyn) und **prüft bei jedem Build**, ob alles stimmt: Ein Fehler in einer Sprachdatei lässt den Build fehlschlagen, fehlende Übersetzungen sind Warnungen.

Eine Sprache hinzufügen (zum Beispiel Französisch):

```
dotnet run --project tools\LangTool -c Release -- new fr "Français" --culture fr-FR
```

1. Das erzeugt `lang/fr.json` mit allen Texten leer. Trage die Werte rechts ein; die Schlüssel links und die Platzhalter (`{0}`, `{1}`) bleiben, wie sie sind; `@@…` hinter einem Schlüssel ist nur Kontext.
2. Plural (`"{0} day|{0} days"`): Gib so viele Formen, getrennt durch `|`, an, wie die Zeile `_plural` deiner Sprache verlangt (`one-other` ist der Standard; `zero-or-one-other` für Französisch und brasilianisches Portugiesisch, `one-few-many-other` für Polnisch und Russisch, `other` für Japanisch und Chinesisch).
3. Prüfen: `dotnet run --project tools\LangTool -c Release -- check` (Fehler: kaputte Platzhalter, doppelte Schlüssel, falsche Anzahl von Pluralformen, Leerzeichen am Anfang oder Ende, die nicht übereinstimmen). `status` zeigt, wie viel Prozent pro Sprache fertig sind.
4. Ausprobieren ohne Bauen: Lege die Datei in `%AppData%\TaskbarStats\lang\` ab und starte die App neu; die Sprache erscheint dann unter Einstellungen → *Allgemein*. Eine Datei dort überschreibt auch eine eingebaute Sprache.
5. Du willst sehen, was noch nicht übersetzt ist oder zu lang ausfällt? Starte die App mit der Umgebungsvariable `TASKBARSTATS_PSEUDO=1` und wähle die Sprache *Pseudo (test)*: Jeder Text erscheint dann mit Akzenten und 30 % länger in Klammern, sodass nicht übersetzte oder abgeschnittene Texte sofort auffallen.
6. Teile deine Übersetzung per Pull Request (`lang/fr.json`).

Für alle, die den Code ändern: Verwende nur `Loc.T("fester Text")`, `Loc.T("Text {0}", Wert)` und `Loc.P("{0} dag|{0} dagen", n)`; führe danach `LangTool sync` aus (legt neue Texte in allen Sprachen bereit) und übersetze die leeren Werte. Einen englischen Text ändern: `LangTool rename "alt" "neu"` (passt Code und alle Sprachdateien an). `LangTool` meldet auch Texte, die fest im Code stehen.

**Designs**: Ein Design ist eine kleine JSON-Datei mit Stil, Farben, Schriftart, Höhe, Dashboard- und Vollbild-Layout (keine Positionen oder Sprache). Eigene Designs liegen in `%AppData%\TaskbarStats\themes\` und lassen sich teilen: Exportiere ein Design und gib die Datei weiter, der andere importiert sie. Unter Einstellungen → *Designs* siehst du vom gewählten Design das **JSON in einem Editor**: Passe es von Hand an und wähle *JSON speichern* (bei einem Fehler im JSON bekommst du eine Meldung mit Zeile und Position). Auch ein mitgeliefertes Design kannst du so anpassen: Es wird dann als eigene Datei mit demselben Namen auf der Festplatte gespeichert und hat Vorrang vor dem mitgelieferten (*mitgeliefert, angepasst*); *Original wiederherstellen* löscht diese Datei wieder. Mit *Vorschau (10 s)* siehst du das gewählte Design (oder das JSON im Editor, auch wenn du es noch nicht gespeichert hast) 10 Sekunden lang auf Widget, Dashboard und Vollbild; während der Vorschau laufen die Werte auf und ab (von niedrig bis Warnung und kritisch) und die Darstellung wechselt alle 2 s: erst das Design selbst, dann Zahlen, Anzeige, Balken und Diagramm, sodass du auf einen Blick siehst, wie das Design in allen Stilen aussieht (es kommen dabei keine Benachrichtigungen). Danach kommt automatisch alles zurück, wie es war (ein weiterer Klick beendet die Vorschau früher).

Alles, was du kopierst oder exportierst (Infos in die Zwischenablage, Systeminformationen, Diagnose, Exportdateien von Designs, Einstellungen und die Nutzungs-CSV), versieht das Programm mit dem Link zu dieser Seite, damit der Empfänger weiß, woher es stammt; in den JSON-Dateien steht das als Feld `_generator`, das beim Import ignoriert wird.

**Systeminformationen kopieren**: Auf der Spezifikationsseite des Vollbildschirms (Taste I) gibt es die Schaltfläche *Alles kopieren  (C)* (oder Taste C), und im Rechtsklickmenü *Systeminformationen kopieren*: alle Spezifikationen (Computer, Betriebssystem, Prozessor, Speicher, Grafikkarte, Datenträger, Netzwerk, …) als reiner Text in der Zwischenablage, praktisch für eine Supportanfrage oder einen Forumsbeitrag.

**Netzlaufwerke** (verbundene Laufwerke) nimmst du über Einstellungen → *Widget* → *Quellen* → *Netzlaufwerke einbeziehen (auch in Dashboard und Vollbild)* mit; sie werden im
Hintergrund abgefragt, damit eine nicht erreichbare Freigabe die App nicht ausbremst. Weil die App als Administrator läuft und Windows dann die Laufwerksbuchstaben
deiner normalen Sitzung nicht sieht, liest die App die Verbindungen aus der Registry (`HKCU\Network`) und fragt den Speicherplatz über den UNC-Pfad ab.

## Einstellungen

Gespeichert in `%AppData%\TaskbarStats\settings.json` (übersteht Neubau und
Neuinstallation). Änderungen in den Einstellungen werden sofort gespeichert. Auch von Hand
anpassbar: `FontFamily`, `FontSize`, `WidgetHeight`, `TrayGap`, Farben und Schwellenwerte.

**Sicherung und portabler Betrieb**: Unter Einstellungen → *Allgemein* → *Sicherung* exportierst du alle Einstellungen in eine JSON-Datei, importierst sie wieder (zum Beispiel auf einem anderen PC) oder setzt alles auf die Standardwerte zurück (Designs und das Nutzungsprotokoll bleiben erhalten). Für den **portablen Betrieb** (zum Beispiel auf einem USB-Stick) legst du eine leere Datei `portable.txt` neben `TaskbarStats.exe` an: Einstellungen, Designs, Sprachdateien und Logs liegen dann im Ordner `data` daneben, und es wird nichts in `%AppData%` geschrieben.

**Wenn etwas schiefgeht**: Fehler werden in `diag.log` im Datenordner festgehalten (nur auf deinem eigenen PC, es wird nichts versendet). Ein Fehler in einem Timer oder einer Zeichenroutine lässt die App weiterlaufen; ein fataler Fehler in einem Hintergrund-Thread startet die App einmal neu. Unter *Über TaskbarStats* kopiert die Schaltfläche *Diagnose kopieren* Version, Windows, Bildschirme und die letzten Logzeilen (ohne Benutzer- oder Computernamen), um sie in eine Fehlermeldung einzufügen. **Absturzmeldung**: Ist die App beim letzten Mal abgestürzt, meldet sie das beim nächsten Start mit einer Ballonmeldung (klicke darauf für den Über-Bildschirm mit *Diagnose kopieren*). Ein Absturz wird am Flag des Fehlerfangnetzes erkannt oder, bei einem harten Absturz, an einem „Application Error“ im Windows-Ereignisprotokoll (mit dem Fehlercode in `diag.log`). Beenden über den Task-Manager oder ein Stromausfall wird nur ins Protokoll geschrieben, nicht als Absturz gemeldet. Startest du die App ein zweites Mal, öffnet die laufende App ihre Einstellungen.

## Transparenter Hintergrund

Das Fenster ist ein *Layered Window* mit Per-Pixel-Alpha. Bei „transparent“ wird der
Hintergrund mit Alpha 1 gezeichnet: unsichtbar, aber trotzdem anklickbar. (Ein
`TransparencyKey` ließ Klicks auf die durchsichtigen Bereiche zum Fenster darunter durch,
wodurch das Rechtsklickmenü manchmal nicht erschien.)

## Administratorrechte

Die App verlangt Administratorrechte (über `app.manifest`). Das ist für die
Temperatursensoren nötig (LibreHardwareMonitorLib). Brauchst du sie nicht, kannst du in
`app.manifest` `requestedExecutionLevel` auf `asInvoker` setzen; die Temperatur funktioniert dann nicht.

## Automatisch mit Windows starten

Verwende „Mit Windows starten“ in den Einstellungen (Tab Allgemein) oder die Option im Installer. Das legt eine
geplante Aufgabe „bei Anmeldung“ mit höchsten Rechten an, sodass keine UAC-Meldung erscheint.

## Code-Übersicht

| Datei | Rolle |
|-------|-------|
| `src/Program.cs` | Startpunkt, Single-Instance-Mutex, `--autostart-on/off` für den Installer. |
| `src/WidgetForm.cs` | Das Taskleisten-Widget: Zeichnen in eine ARGB-Bitmap (`UpdateLayeredWindow`), Maus, Menü, Tooltip, Benachrichtigungen, Tastenkürzel, Sampler-Thread. |
| `src/Metrics.cs` | Leistungsindikatoren (PDH-Wildcard für die GPU), Speicher, Akku, Temperatur, DXGI-GPU-Namen. |
| `src/DashboardForm.cs` | Desktop-Dashboard + `MetricHistory`/`Ring` (Diagrammverlauf) und `DashContext`. |
| `src/FullscreenForm.cs` | Vollbild-„Cockpit“: dichte Übersicht, Detailansichten pro Kachel, Spezifikationsseite und automatische Tour. |
| `src/HardwareInfo.cs` | Hardware-Inventar (WMI u. Ä.), einmalig asynchron für die Spezifikationsseite gesammelt. |
| `src/PingMonitor.cs` | Ping-Messung auf einem eigenen Thread (letzter, min/Durchschn./max, Jitter, Verlust). |
| `src/UsageTracker.cs` | Netzwerknutzung pro Adapter und pro Tag (`usage.json`), thread-sicher. |
| `src/ProcessSampler.cs` | Ressourcenhungrigste Prozesse (asynchron abgetastet). |
| `src/AppSettings.cs` | Einstellungen (JSON). |
| `src/TaskbarHost.cs` | Ermittelt die Position des Infobereichs/der Taskleiste. |
| `src/StartupManager.cs` | Autostart über eine geplante Aufgabe (höchste Rechte). |
| `src/SettingsForm.cs`, `src/Themes.cs`, `src/Tiles.cs` | Einstellungsfenster mit Tabs (live angewendet), Designs (mitgeliefert + eigene JSON-Dateien), Komponenten-IDs/Reihenfolge. |
| `src/LogForm.cs` | Fenster mit dem Nutzungsprotokoll. |
| `src/WelcomeForm.cs`, `src/AboutForm.cs` | Willkommensbildschirm und Über-Fenster. |
| `src/Loc.cs`, `lang/*.json` | Übersetzungen: `Loc.T("English text")`, `Loc.P` (Plural) mit einer JSON-Datei pro Sprache (siehe *Sprachen*). |
| `tools/LangTool` | Prüft, synchronisiert und benennt die Sprachtexte um (Roslyn); läuft bei jedem Build. |
| `src/Diag.cs`, `src/AppPaths.cs` | Logdatei, Absturz-Fangnetz, Diagnosebericht; wo die Daten liegen (`%AppData%`, `TASKBARSTATS_DATA` oder portabel). |
| `tests/TaskbarStats.Tests` | Automatische Tests (xUnit); `dotnet test tests\TaskbarStats.Tests -c Release`. |

## Leistung und Threads

Der UI-Thread macht nur Zeichnen und Menüs; die schwere Arbeit liegt woanders:

- Ein **Sampler-Thread** liest die Messwerte (`Metrics.Update`) und führt die Nutzung nach (`UsageTracker.Sample`);
  die UI zeichnet mit den letzten Werten. Gemeinsam genutzte Daten sind thread-sicher (gesperrt oder atomar ersetzt).
- **GPU, Netzwerk und CPU-Kerne** laufen über eine einzige PDH-Abfrage mit Platzhalter pro Zähler (z. B. `\GPU Engine(*)\Utilization Percentage`), nicht über
  einzelne `PerformanceCounter`-Objekte (die GPU kostete 150–780 ms pro Takt; das Netzwerk ging von ~39 auf ~7 ms).
- **Sparsam**: Bei verstecktem Widget ohne Dashboard/Vollbild wird nur alle 5 s gemessen, die Nutzung wird alle 5 s aktualisiert, der Arbeitssatz wird regelmäßig an Windows zurückgegeben und die Runtime läuft ohne zusätzlichen GC-Thread. Im Leerlauf gemessen: ~2 % eines Kerns und ~60 MB Arbeitssatz (vorher ~2–5 % und ~93 MB).
- Das Widget wird nicht alle 200 ms erneut nach oben gesetzt (`SetWindowPos` auf einem Fenster der Taskleiste kann 100+ ms blockieren);
  das geschieht nur, wenn wirklich ein anderes sichtbares Topmost-Fenster darüberliegt.
- Das Abtasten von Programmen (`ProcessSampler.SampleAsync`) und Netzlaufwerken (`DriveInfo`) läuft im Hintergrund.

## Lizenz und Credits

MIT-Lizenz (siehe [LICENSE](LICENSE)): Du darfst TaskbarStats frei verwenden, anpassen und weitergeben,
auch für eigene Projekte. Die einzige Bedingung ist, dass die Copyright-Zeile und der Lizenztext
erhalten bleiben – lass die Credits also bitte stehen. Danke!

Das Programm verwendet LibreHardwareMonitorLib (MPL-2.0), HidSharp (Apache-2.0) und .NET (MIT);
diese unterliegen ihren eigenen Lizenzen.
