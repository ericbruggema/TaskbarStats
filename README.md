# TaskbarStats

Een lichtgewicht CPU / GPU / geheugen / netwerk / temperatuur-monitor die naast het
systeemvak van de Windows-taakbalk zweeft — zoals TrafficMonitor, maar met waarden die
overeenkomen met Task Manager.

## Waarom de waarden hier wél kloppen

| Onderdeel | Bron | Waarom correct |
|-----------|------|----------------|
| CPU | `Processor Information\% Processor Utility` (totaal en per core) | Zelfde meting als Task Manager (incl. turbo). Valt terug op `Processor\% Processor Time` als die teller ontbreekt. |
| Geheugen | `GlobalMemoryStatusEx.dwMemoryLoad` | Exact het percentage dat Task Manager toont. |
| GPU | Som van alle `GPU Engine\Utilization Percentage` per GPU (LUID) | Telt 3D + copy + video-engines samen, precies zoals Task Manager. De tellers worden elke 5 s opnieuw opgebouwd omdat engine-instances met processen komen en gaan. |
| Netwerk | `Network Interface\Bytes Received/Sent per sec` | Per adapter of alle adapters samen. |
| Temperatuur | LibreHardwareMonitorLib | CPU/GPU-temperatuur; vereist administrator. |

GPU staat standaard op **automatisch**: het volgt de drukste GPU (handig bij een iGPU +
dGPU). Je kunt ook een vaste GPU kiezen in het menu.

## Bouwen

Vereist: Windows en de .NET 8 SDK (https://dotnet.microsoft.com/download).

Het makkelijkst: dubbelklik **`build.bat`**. Het script

1. vraagt zo nodig zelf om administrator-rechten (nodig om de draaiende app af te sluiten),
2. sluit `TaskbarStats.exe` af als die draait,
3. bouwt een Release-build,
4. start de app opnieuw als hij daarvoor draaide.

Handmatig kan ook:

```
dotnet build -c Release
```

De `.exe` staat daarna in `bin\Release\net8.0-windows\TaskbarStats.exe`.
Voor één zelfstandig bestand zonder .NET-installatie:

```
dotnet publish -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

## Installer (Inno Setup)

Eenmalig Inno Setup installeren: `winget install JRSoftware.InnoSetup`
(of https://jrsoftware.org/isdl.php). Daarna dubbelklik je **`build-installer.bat`**: dat
publiceert de app en compileert `installer\TaskbarStats.iss` tot
`installer\Output\TaskbarStats-Setup.exe`.

De installer:

- toont eerst het Leesmij (uitleg + credits, `installer\Leesmij.txt`) en installeert het mee;
- maakt een Startmenu-groep met **TaskbarStats**, **Leesmij (uitleg en credits)** en
  **TaskbarStats verwijderen**; optioneel ook een bureaubladsnelkoppeling;
- zet op verzoek autostart aan via een geplande taak met hoogste rechten (geen UAC-melding
  bij het inloggen; de app vraagt admin-rechten en start niet vanuit de Run-sleutel);
- sluit een draaiende versie af bij installeren/verwijderen;
- registreert een uninstaller (ook in Instellingen → Apps) die de taak verwijdert en vraagt of
  je instellingen en verbruikslog (`%AppData%\TaskbarStats`) ook weg mogen.

De app zelf heeft ook "Leesmij en credits" in het rechtermuisknop-menu. Aanpassen van naam,
credits of tekst: `installer\Leesmij.txt` en `#define Publisher` in het `.iss`-bestand.

## Gebruik

Start `TaskbarStats.exe`. Het widget verschijnt als zwevend, altijd-bovenliggend venster
direct links van het systeemvak. Het is eigenaar-venster van de taakbalk, dus het blijft
erboven staan. Sleep met de **linkermuisknop** om het te verplaatsen (de positie wordt
bij loslaten bewaard); het menu heeft "Reset positie".

**Rechtermuisknop** op het widget opent het menu:

- **Onderdelen**: CPU, GPU, geheugen, upload, download, CPU-/GPU-temperatuur.
- **Weergave**: per onderdeel digitaal (percentage), meter (donut) of balk; CPU eventueel per core.
- **Kleuren**: tekst, achtergrond, meter/balk, waarschuwing, kritiek, rand, plus drempels (bv. 85% / 95%).
- **Ververssnelheid**, **Taal** (NL/EN), **Transparante achtergrond**.
- **Netwerkadapter** en **GPU-bron**: achter elke adapter en GPU staat de actuele snelheid/het gebruik (live bijgewerkt terwijl het menu openstaat). GPU's worden met naam getoond.
- **Schijven**: lees/schrijfsnelheid per fysieke schijf (live), vrije/totale ruimte per station, en een keuze wat het widget toont: uit, totaal, alle schijven apart of één schijf. De totale lees/schrijfsnelheid zet je aan via *Onderdelen → Schijf lezen/schrijven*.
- **Tooltip**: houd de muis boven het widget voor alle details (RAM in GB, per-GPU, netwerk per adapter, schijven en schijfruimte, temperaturen).
- **Met Windows meestarten** (registersleutel `HKCU\...\Run`) en **Afsluiten**.

Het menu blijft open als je een optie aanklikt, zodat je meerdere dingen achter elkaar kunt
instellen. Het sluit als je buiten het menu klikt, Esc drukt, of de muis ongeveer een
seconde niet meer boven het menu (of een submenu) is.

Er draait maar één instantie tegelijk.

## Instellingen

Opgeslagen in `%AppData%\TaskbarStats\settings.json` (overleeft herbouw en
herinstallatie). Wijzigingen via het menu worden direct bewaard. Ook handmatig aan te
passen: `FontFamily`, `FontSize`, `WidgetHeight`, `TrayGap`, kleuren en drempels.

## Transparante achtergrond

Het venster is een *layered window* met per-pixel alpha. Bij "transparant" wordt de
achtergrond met alpha 1 getekend: onzichtbaar, maar nog steeds klikbaar. (Een
`TransparencyKey` liet klikken op de doorzichtige delen door naar het venster eronder,
waardoor het rechtermuisknop-menu soms niet verscheen.)

## Administrator-rechten

De app vraagt om administrator-rechten (via `app.manifest`). Dat is nodig voor de
temperatuur-sensoren (LibreHardwareMonitorLib). Heb je die niet nodig, dan mag je in
`app.manifest` `requestedExecutionLevel` op `asInvoker` zetten; temperatuur werkt dan niet.

## Automatisch starten met Windows

Gebruik "Met Windows meestarten" in het menu, of de optie in de installer. Wil je de
UAC-prompt bij het opstarten vermijden, maak dan een taak in Taakplanner met
"Met hoogste bevoegdheden uitvoeren".

## Code-overzicht

| Bestand | Rol |
|---------|-----|
| `src/Program.cs` | Startpunt, single-instance mutex. |
| `src/WidgetForm.cs` | Widget: tekenen naar een ARGB-bitmap (`UpdateLayeredWindow`), muis, menu. |
| `src/Metrics.cs` | Prestatietellers, geheugen, temperatuur. |
| `src/AppSettings.cs` | Instellingen (JSON). |
| `src/TaskbarHost.cs` | Zoekt de positie van het systeemvak. |
| `src/StartupManager.cs` | Autostart via het register. |
| `src/Loc.cs` | Nederlandse/Engelse menuteksten. |
