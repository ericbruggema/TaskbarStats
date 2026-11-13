# -*- coding: utf-8 -*-
"""Bouwt docs/TaskbarStats-Handleiding.pdf (Nederlands, A4).

Gebruik (vanuit de repo-map):  python docs/handleiding/make_handleiding.py
Vereist: reportlab en Pillow. De opmaak staat in manual_common.py.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from manual_common import *  # noqa: F401,F403
from manual_common import L, H1, H2, H3, P, B, N, box, codeblock, table, fig, side_by_side, build,     h1, toc1, toc2, small, bullet, body, md, VERSION, ROOT
from reportlab.platypus import NextPageTemplate, PageBreak, Paragraph, KeepTogether, Spacer
from reportlab.platypus.tableofcontents import TableOfContents

OUT = os.path.join(ROOT, "docs", "TaskbarStats-Handleiding.pdf")
L.update(tip="Tip", note="Let op", title="TaskbarStats - Handleiding",
         subject=f"Uitleg, installatie, tips en trucs (versie {VERSION})", cover_title="Handleiding",
         cover_lines=["Uitleg, installatie, instellingen, tips en trucs", "voor de lichtgewicht Windows-systeemmonitor",
                      "naast de klok van je taakbalk."],
         cover_foot=f"Versie {VERSION}   ·   taskbarstats.com   ·   MIT-licentie",
         header=f"Handleiding  ·  versie {VERSION}")

# ================================================================= INHOUD
s = []
s += [NextPageTemplate("page"), PageBreak()]

# ---- inhoudsopgave
s.append(Paragraph("Inhoud", h1))
toc = TableOfContents()
toc.levelStyles = [toc1, toc2]
toc.dotsMinLevel = 0
s.append(toc)

# ---- 1. Kennismaking
s += H1("1. Wat is TaskbarStats?", new_page=True)
s.append(P("TaskbarStats is een kleine, lichtgewicht systeemmonitor voor Windows 10 en 11. Een klein widget zweeft "
           "direct naast het systeemvak van je taakbalk en toont live **CPU, GPU, geheugen, netwerk, schijven, "
           "batterij, FPS en temperaturen**. Wil je meer? Dan is er een groot bureaublad-dashboard en een "
           "fullscreen \"cockpit\" met details, grafieken en alle specificaties van je pc.", lead))
s += fig("widget-digital.png", "Het widget naast de klok: cijfers, meters, balken of grafieken (de waarden in alle screenshots zijn verzonnen).")
s.append(H2("De drie onderdelen"))
s += table([
    ["Onderdeel", "Wat het is", "Openen met"],
    ["Taakbalk-widget", "Klein, altijd zichtbaar, naast het systeemvak. Tooltip met alle details.", "Start vanzelf"],
    ["Bureaublad-dashboard", "Groot, halfdoorzichtig venster met tegels en grafiekjes van de laatste 5 minuten; "
                             "voor- of achtergrond, klik-door.", "Menu of **Ctrl+Alt+D** (klik-door)"],
    ["Fullscreen-cockpit", "Eén dicht overzicht op een heel scherm; klik op een tegel voor diepte. Met "
                           "specificatiepagina en automatische tour.", "**Ctrl+Alt+F**"],
], [0.24, 0.52, 0.24])
s.append(H2("Waarom de cijfers kloppen"))
s.append(P("De waarden komen uit dezelfde bronnen als Taakbeheer, dus je kunt ze vergelijken. Alleen de temperaturen "
           "komen uit een aparte bibliotheek (LibreHardwareMonitor)."))
s += table([
    ["Onderdeel", "Bron", "Opmerking"],
    ["CPU", "Windows-prestatietellers (`Processor Information`)", "Zelfde waarde als Taakbeheer. Optioneel de boost-schaalbare "
                                                                 "\"Utility\"-meting (bijna 2× zo hoog op een boostende CPU)."],
    ["Geheugen", "`GlobalMemoryStatusEx`", "Exact het percentage uit Taakbeheer."],
    ["GPU", "Prestatietellers `GPU Engine`", "Per GPU de drukste engine, in lijn met Taakbeheer. Met twee GPU's "
                                             "(iGPU + dGPU) volgt *Automatisch* de drukste."],
    ["Netwerk", "`Network Interface`", "Per adapter of alle adapters samen."],
    ["Temperatuur", "LibreHardwareMonitor", "Administrator nodig. Zonder CPU-sensor valt de app terug op de "
                                            "thermal zone van Windows."],
    ["Ping", ".NET `Ping`", "Latency, jitter en pakketverlies naar een doel naar keuze."],
    ["Specificaties", "WMI, registry, Win32", "Voor de specificatiepagina in fullscreen."],
], [0.17, 0.33, 0.50])
s += box("TaskbarStats is zuinig: in rust ongeveer 2% van één processorkern en circa 60 MB geheugen. Is het widget "
         "verborgen en zijn dashboard en fullscreen dicht, dan meet de app maar eens per 5 seconden.")

# ---- 2. Installatie
s += H1("2. Installatie")
s.append(H2("Wat heb je nodig?"))
s += B(["**Windows 10 of 11**, 64-bit (x64).",
        "Geen aparte .NET-installatie: de installer bevat alles (zelfstandige, gecomprimeerde build).",
        "**Administratorrechten.** Die zijn nodig voor de temperatuursensoren; Windows vraagt er één keer om bij het installeren.",
        "Geen account, geen internetverbinding nodig (internet gebruiken alleen de optionele plugins en de optionele updatecontrole)."])
s.append(H2("Stap voor stap"))
s += N(["Ga naar **github.com/ericbruggema/TaskbarStats/releases/latest** (of via **taskbarstats.com**, knop *Download*).",
        "Download onder *Assets* het bestand `TaskbarStats-Setup-<versie>.exe` en start het.",
        "Bevestig de Windows-melding (UAC). Is het bestand niet digitaal ondertekend, dan kan SmartScreen waarschuwen: "
        "kies **Meer info → Toch uitvoeren**.",
        "Kies de taal van de installer (Nederlands of English).",
        "Staat er al een installatie, dan zegt de installer of het een **update**, **herinstallatie** of een oudere versie is "
        "en vraagt Ja/Nee voor het overschrijven.",
        "Lees het Leesmij (uitleg en credits) dat de wizard toont.",
        "Kies de extra taken: **bureaubladsnelkoppeling** en **optionele plugins** (beide standaard uit).",
        "Kies op de pagina *Meestarten met Windows* of de app bij het inloggen moet starten (standaard **Ja** bij een nieuwe "
        "installatie; een update behoudt je huidige keuze).",
        "Klik op *Voltooien*: TaskbarStats start en het widget verschijnt links van het systeemvak. Bij de eerste start zie "
        "je een welkomstscherm."])
s.append(Spacer(1, 4))
s.append(P("In het Startmenu staat een map **TaskbarStats** met de app, het Leesmij, *TaskbarStats verwijderen* en "
           "snelkoppelingen naar de website en GitHub."))
s.append(H2("Meestarten met Windows"))
s.append(P("Omdat de app administratorrechten vraagt, werkt de gewone Run-sleutel van Windows niet. TaskbarStats maakt daarom "
           "een **geplande taak \"bij inloggen\" met hoogste rechten**: je krijgt bij het inloggen géén UAC-melding. "
           "Later aan- of uitzetten kan bij *Instellingen → Algemeen → Met Windows meestarten*."))
s.append(H2("Stil installeren (voor beheerders en scripts)"))
s += codeblock("""TaskbarStats-Setup-1.6.8.exe /SILENT /AUTOSTART=yes
TaskbarStats-Setup-1.6.8.exe /VERYSILENT /AUTOSTART=no /TASKS="plugins"
""")
s += table([
    ["Parameter", "Betekenis"],
    ["`/SILENT`, `/VERYSILENT`", "Zonder wizard installeren (geen vragen over overschrijven)."],
    ["`/AUTOSTART=yes|no`", "Meestarten met Windows wel of niet instellen."],
    ["`/TASKS=\"plugins\"`", "De optionele plugins meeinstalleren."],
    ["`/DIR=...`, `/GROUP=...`", "Eigen map / Startmenu-groep. Bij een bestaande installatie worden die genegeerd."],
], [0.32, 0.68])
s.append(H2("Updaten"))
s.append(P("Download de nieuwe installer en start hem: de oude versie wordt afgesloten en vervangen, je instellingen "
           "blijven staan. Wil je gewaarschuwd worden bij een nieuwe versie? Zet dan de **updatecontrole** aan "
           "(standaard uit; *Instellingen → Algemeen*). Die kijkt hooguit 1× per 24 uur op GitHub, meldt alleen dát er "
           "iets nieuws is, en downloadt of installeert nooit iets."))
s.append(H2("Verwijderen"))
s.append(P("Startmenu → TaskbarStats → *TaskbarStats verwijderen*, of *Instellingen → Apps*. De uninstaller haalt de "
           "autostart-taak weg en vraagt (Ja/Nee) of je instellingen en verbruikslog in `%AppData%\\TaskbarStats` "
           "ook weg mogen."))
s.append(H2("Portable gebruiken (bijvoorbeeld op een USB-stick)"))
s.append(P("Maak een leeg bestand `portable.txt` naast `TaskbarStats.exe`. Instellingen, thema's, taalbestanden en logs "
           "staan dan in de map `data` ernaast en er wordt niets in `%AppData%` geschreven."))
s.append(H2("Zelf bouwen"))
s.append(P("Heb je de .NET 8 SDK, dan is dubbelklikken op **`build.bat`** genoeg (sluit de draaiende app, bouwt Release en "
           "start opnieuw). Handmatig:"))
s += codeblock("""dotnet build -c Release          # exe: bin\\Release\\net8.0-windows\\TaskbarStats.exe
build-installer.bat              # zelfstandige publish + Inno Setup -> installer\\Output""")

# ---- 3. Bediening
s += H1("3. Bediening")
s.append(P("Het widget is een zwevend venster dat eigendom is van de taakbalk, dus het blijft er netjes bovenop staan. "
           "Alles wat je ermee kunt doen:", lead))
s += table([
    ["Actie", "Wat gebeurt er"],
    ["Slepen (linkermuisknop)", "Verplaatst het widget, ook buiten de taakbalk. De plek wordt onthouden. Met *Positie vergrendelen* zet je hem vast."],
    ["Rechtermuisknop", "Opent het menu (zie hieronder)."],
    ["Muis erboven houden (≥ 2 s, instelbaar)", "Tooltip met alle details: CPU + klokfrequentie, elke GPU met VRAM, RAM in GB, zwaarste programma's, netwerk per adapter met verbruik van vandaag, schijven en vrije ruimte, temperaturen."],
    ["Dubbelklik", "Opent standaard Taakbeheer (instelbaar)."],
    ["Middelste muisknop", "Kopieert alle tooltip-informatie naar het klembord (instelbaar)."],
    ["Ctrl+Alt+W", "Klik-door: het widget blijft zichtbaar, maar je klikken gaan er dwars doorheen."],
], [0.30, 0.70])
s += side_by_side("menu.png", "Het rechtermuisknop-menu: kort, en het blijft open terwijl je kiest.",
                  "widget-compact.png", "Het compacte widget (labels boven, minder breed).")
s.append(H2("Het menu"))
s += B(["**Instellingen…** (vet, bovenaan): het instellingenvenster met tabbladen.",
        "**Thema**: snelkeuze om met één klik een thema toe te passen.",
        "**Bureaublad-dashboard** en **Fullscreen-dashboard**: elk een submenu (tonen, klik-door, vergrendelen, instellingen; "
        "fullscreen: openen, tour, instellingen).",
        "**Vastplakken aan systeemvak**, **Widget klik-door**.",
        "**Verbruik**: ontvangen/verzonden per netwerkadapter (sessie, vandaag, gisteren, 7 dagen, maand) en het dagelijkse log met CSV-export.",
        "**Kopieer info**, **Systeeminformatie kopiëren**, **Credits** (een rustige aftiteling met een paar cijfers uit je eigen verbruik), "
        "**Over** (met knop voor het welkomstscherm) en **Afsluiten**."])
s += box("Het menu blijft open als je een optie aanklikt, zodat je meerdere dingen achter elkaar kunt instellen. Het sluit "
         "als je ernaast klikt, op Esc drukt, of de muis ongeveer een seconde van het menu af is.")
s.append(P("Er draait altijd maar één instantie. Start je de app een tweede keer, dan opent de draaiende app zijn "
           "instellingenvenster."))

# ---- 4. Instellingen
s += H1("4. Het instellingenvenster")
s.append(P("Elke wijziging wordt **direct toegepast en bewaard**; je ziet het resultaat meteen op het widget, het dashboard en "
           "fullscreen. Bedieningselementen die door een andere keuze geen effect hebben, worden grijs, met uitleg waarom. "
           "Instellingen van onderdelen die nu uit staan (bijvoorbeeld de GPU) kun je alvast invullen.", lead))
s += fig("settings-widget.png", "Instellingen → Widget → Onderdelen en indeling (de screenshots tonen de Engelse interface; de app zelf is er ook in het Nederlands en Duits).", width=11.5 * cm)
s += table([
    ["Tabblad", "Wat stel je er in?"],
    ["Widget", "Subtabs: **Onderdelen en indeling** (aan/uit en volgorde met pijlknoppen), **Bronnen** (welke GPU of automatisch, welke netwerkadapter of alle, "
               "schijven, netwerkschijven, ping-doel), **Weergave** (cijfer / meter / balk / grafiek per onderdeel, per core, labels boven, compact, "
               "transparant), **Waarden** (opmaak, processen, FPS), **Uiterlijk** (lettertype, grootte, hoogte), **Geavanceerd** (grafieklengte, "
               "periode, CPU-meting, verversnelheid)."],
    ["Kleuren", "Tekst, achtergrond, meter/balk, waarschuwing, kritiek, rand en drempels (bijvoorbeeld 85% / 95%). Optioneel het Windows-thema volgen."],
    ["Dashboard", "Tonen, voor-/achtergrond, klik-door, vergrendelen, doorzichtigheid, schaal 50-300%, 1-4 kolommen, volgorde en aan/uit per tegel, achtergrondafbeelding."],
    ["Fullscreen", "Welk scherm, volgorde en aan/uit per tegel (met miniatuur van de indeling), tourtijd, netwerktegel per verbindingssoort, achtergrond."],
    ["Thema's", "16 meegeleverde thema's, eigen thema opslaan/laden/importeren/exporteren, JSON-editor, voorbeeld (10 s)."],
    ["Algemeen", "Taal, meestarten, positie vergrendelen/vastplakken, verbergen bij fullscreen-apps, muisacties, tooltip-wachttijd, meldingen, "
                 "maandlimiet, updatecontrole, back-up."],
    ["Plugins", "Alleen zichtbaar als de plugins zijn geïnstalleerd (zie hoofdstuk 8)."],
], [0.16, 0.84])
s.append(H2("Onderdelen van het widget"))
s.append(P("CPU, GPU, geheugen, upload, download, batterij (laptops), schijf lezen/schrijven, CPU- en GPU-temperatuur, ping, "
           "en de extra's: CPU-klokfrequentie, schijf actief (%), schijftemperatuur, hoofdbordtemperatuur, aantal processen en FPS. "
           "De volgorde van links naar rechts stel je in met de pijlknoppen. Schijf- en hoofdbordtemperatuur lezen sensoren "
           "(administrator nodig) en verdwijnen vanzelf als de pc geen waarde geeft."))
s.append(H2("Meldingen en maandlimiet"))
s.append(P("TaskbarStats waarschuwt met een melding als de batterij bijna leeg is (20% en 10%), een schijf bijna vol is, "
           "CPU/GPU/geheugen te lang kritiek blijft, of je 80% en 100% van je maandlimiet voor netwerkdata haalt. "
           "Extra drempels (0 = uit): CPU-, GPU- en schijftemperatuur, geheugengebruik en netwerkverbruik per dag."))

# ---- 5. Weergave
s += H1("5. Weergave: stijlen, grafieken en waarden")
s += fig("widget-stijlen.png", "Vier weergavestijlen: cijfers, meters, balken en grafieken - per onderdeel te kiezen.", width=13.5 * cm)
s.append(H2("Grafieken"))
s.append(P("CPU, GPU, geheugen, temperaturen, netwerk (download en upload als twee lijnen) en ping kunnen als mini-grafiek "
           "in het widget, met de laatste 30, 60 of 120 seconden. Ontbrekende pings onderbreken de lijn met een rode punt. "
           "De **lengte** is *zeer kort*, *kort*, *middel*, *lang* of een eigen breedte; per onderdeel kan die anders "
           "(Instellingen → Widget → Geavanceerd → *Grafieklengte per onderdeel*, 16-160 px)."))
s += fig("widget-grafieklengte-per-onderdeel.png", "Elke grafiek een eigen lengte: netwerk breed, CPU zeer kort, geheugen 100 px.")
s.append(H2("Temperatuur"))
s.append(P("CPU- en GPU-temperatuur zijn te tonen als cijfer, meter of balk, of **klein achter de CPU-/GPU-cel** "
           "(bijvoorbeeld `CPU 33%  55°`) als je ruimte wilt besparen."))
s += fig("widget-temperatuur.png", "Temperatuur als cijfer, meter, balk of samengevoegd met de CPU/GPU-cel.", width=13 * cm)
s.append(H2("Waardeopmaak (Instellingen → Widget → Waarden)"))
s += B(["Netwerksnelheid in bytes (MB/s) of bits (Mb/s), automatische of vaste eenheid.",
        "Korte waarden; eenheid of %-teken weglaten; upload en download omwisselen.",
        "Geheugen als percentage, gebruikt (GB) of beschikbaar (GB).",
        "Schijf lezen/schrijven als `R / W` of als pijlen (↓ lezen, ↑ schrijven) - overal in widget, tooltip, dashboard en fullscreen."])
s.append(H2("Ping"))
s.append(P("Zet *Ping (latency)* aan en het widget toont bijvoorbeeld `PING 12 ms` (oranje vanaf 100 ms, rood vanaf 250 ms of bij "
           "geen antwoord). Het doel (standaard 1.1.1.1) is instelbaar. De tooltip en de netwerkdetails in fullscreen tonen min/gemiddeld/max, "
           "jitter en pakketverlies over de laatste twee minuten."))
s += fig("widget-ping.png", "Widget met ping en temperaturen.")
s.append(H2("Processen"))
s.append(P("Het aantal draaiende processen als `PROC 250`, of alleen de **apps** (processen met een zichtbaar venster, zoals in "
           "Taakbeheer), alleen de **achtergrondprocessen**, of beide (`APPS/BG 5/245`). De tooltip toont ook het aantal threads."))
s += fig("widget-processen.png", "Processen: totaal, apps, achtergrond of beide.", width=11 * cm)
s.append(H2("Achtergrondafbeelding en Windows-thema"))
s.append(P("Per venster (widget, dashboard, fullscreen) kun je een png/jpg/bmp als achtergrond kiezen met modus *Vullen*, *Uitgerekt*, "
           "*Passen*, *Tegelen* of *Gecentreerd* en een dekking van 0-100%. Onder *Kleuren* kan het widget het **Windows-thema volgen**: "
           "donker/licht, eventueel met de accentkleur van Windows voor meters en balken."))
s += fig("widget-achtergrond.png", "Widget met een achtergrondafbeelding.")

# ---- 6. Dashboard & fullscreen
s += H1("6. Dashboard en fullscreen-cockpit")
s.append(H2("Bureaublad-dashboard"))
s.append(P("Een groot, halfdoorzichtig venster met tegels voor CPU (met balkjes per core), GPU per kaart met VRAM, geheugen, "
           "netwerk (minigrafiekje per adapter), schijven, batterij, zwaarste programma's en systeem. Standaard staat het uit: "
           "zet het aan via het menu of *Instellingen → Dashboard*."))
s += B(["Voorgrond of achtergrond, schaal 50-300%, doorzichtigheid, 1-4 kolommen, vergrendelen.",
        "**Klik-door** (**Ctrl+Alt+D** of dubbelklik op het pictogram bij de klok): je klikken gaan erdoorheen, zodat het als "
        "levend bureaubladbehang werkt. Omdat rechtsklikken dan niet meer werkt, zijn dit de manieren om het weer uit te zetten.",
        "Het dashboard herstelt zichzelf als je *Bureaublad weergeven* gebruikt."])
s += side_by_side("dashboard.png", "Het bureaublad-dashboard.", "settings-dashboard.png", "Instellingen → Dashboard: volgorde en aan/uit per tegel.")
s.append(H2("Fullscreen-cockpit (Ctrl+Alt+F)"))
s.append(P("Eén dicht overzicht van alles op een heel scherm (je kiest welk). Klik op een tegel voor de diepte-weergave: "
           "grafiek tot 1 uur, min/gemiddeld/max, per kern, per GPU met VRAM, per adapter, per schijf, top-programma's en systeeminfo."))
s += fig("fullscreen-overview.png", "Het fullscreen-overzicht.", width=14 * cm)
s += side_by_side("fullscreen-cpu.png", "CPU-details.", "fullscreen-gpu.png", "GPU-details met sensoren.")
s += table([
    ["Toets / actie", "Functie"],
    ["Esc", "Eén stap terug; vanuit het overzicht sluit het scherm."],
    ["1 / 2 / 3", "Grafiekperiode: 1 minuut / 5 minuten / 1 uur."],
    ["I (of knop bovenin)", "Specificatiepagina."],
    ["C (op de specificatiepagina)", "Alles kopiëren naar het klembord."],
    ["Spatie, of 3× klikken op een lege plek", "Start de automatische tour; een klik of toets stopt hem."],
    ["Muiswiel", "Scrollen (specificaties, sensoren)."],
], [0.38, 0.62])
s.append(H2("Specificaties en sensoren"))
s.append(P("De specificatiepagina toont computer, hoofdbord en BIOS, Windows, processor (kernen, klokken, cache, instructiesets), "
           "videokaarten (driver, VRAM, uitvoer), geheugenmodules, schijven (type, gezondheid, firmware), beeldschermen, "
           "netwerkadapters, batterij (slijtage, laadcycli), beveiliging (Secure Boot, TPM), geluid, invoerapparaten, Bluetooth en USB. "
           "De detailpagina's tonen sensoren via LibreHardwareMonitor: CPU-vermogen, -temperaturen en -klokken, GPU-temperatuur/-vermogen/-ventilator, "
           "schijftemperatuur en SMART-gezondheid, hoofdbord, ventilatoren, geheugen en batterij. Die sensoren zijn alleen actief zolang het "
           "fullscreen-scherm open is."))
s += side_by_side("fullscreen-specs.png", "De specificatiepagina (toets I).", "fullscreen-disk.png", "Schijfdetails met temperatuur en gezondheid.")
s.append(H2("Automatische tour"))
s.append(P("Klik 3× op een lege plek in het fullscreen-scherm (of druk op de spatiebalk): het scherm loopt alle pagina's af "
           "(overzicht, elk detail, specificaties). De tijd per pagina (standaard 10 s) stel je in via het menu (*Fullscreen-tour*) of "
           "bij *Instellingen → Fullscreen*. Handig op een tweede beeldscherm als permanent infoscherm."))

# ---- 7. Thema's en gamers
s += H1("7. Thema's en gamers")
s.append(H2("Thema's"))
s.append(P("Een thema is een klein JSON-bestand met stijl, kleuren, lettertype, hoogte en de indeling van dashboard en fullscreen "
           "(geen schermposities en geen taal). Er zijn **16 meegeleverd**: Standaard, Donker, Licht, Zwart-wit, Liefde, CGA, Matrix, "
           "Amber, Game Boy, Dracula, Oceaan, Zonsondergang, Bos, Minimaal, Meters en Neon. Sommige hebben een eigen achtergrond die in "
           "code is getekend (vallende Matrix-tekens, Neon-raster, zon, sterren, golven, hartjes, dennenbos met vuurvliegjes, beeldbuislijntjes)."))
s += fig("themes-fps.png", "De meegeleverde thema's, hier met FPS in het widget.")
s += fig("themes-achtergronden.png", "Widgets met de achtergronden van de thema's.", width=13 * cm)
s += B(["**Voorbeeld (10 s)**: het gekozen thema (of de JSON in de editor, ook niet-opgeslagen) wordt 10 seconden op widget, dashboard en "
        "fullscreen getoond met op- en neergaande waarden en wisselende weergavestijlen. Daarna staat alles weer zoals het was.",
        "**JSON-editor**: onder *Thema's* zie en bewerk je de JSON van het gekozen thema; bij een fout krijg je regel en positie. "
        "Ook een meegeleverd thema kun je zo aanpassen (*meegeleverd, aangepast*); **Origineel herstellen** haalt je versie weer weg.",
        "**Delen**: exporteer een thema en geef het bestand door; de ander importeert het. Eigen thema's staan in `%AppData%\\TaskbarStats\\themes\\`.",
        "Op **14 februari** start de app tijdelijk met het Love-thema (alleen in het geheugen; je eigen uiterlijk blijft bewaard). "
        "Uitzetten kan onder *Thema's*."])
s += fig("settings-themes.png", "Instellingen → Thema's.", width=11 * cm)
s.append(H2("Voor gamers"))
s.append(P("Maak van je taakbalk een gratis FPS-teller, of sleep het widget over je game."))
s += fig("gamers-overlay.png", "FPS, GPU-belasting en -temperatuur, CPU en ping zwevend over een game.", width=13 * cm)
s += B(["**FPS** (experimenteel): gemeten zoals PresentMon, via Windows event tracing van het programma op de voorgrond. Werkt met "
        "DirectX-games (DXGI/D3D9), Vulkan (via DxgKrnl) en browsers. Aanzetten bij *Instellingen → Widget → Waarden*. Er wordt alleen een proces-id "
        "en tijdstip geteld, geen inhoud. De tooltip toont ook de **1%-low** en het programma.",
        "**Mini-overlay**: compact, labels boven, alleen FPS, GPU en CPU.",
        "**Klik-door** (Ctrl+Alt+W): het widget blijft zichtbaar, de muisklikken gaan naar de game.",
        "Zet *Verbergen bij volledig scherm* uit als het widget juist zichtbaar moet blijven boven een game."])
s += fig("gamers-mini.png", "Mini-overlay over een game.", width=10 * cm)

# ---- 8. Plugins
s += H1("8. Plugins (optioneel)")
s.append(P("Plugins voegen extra waarden toe aan het widget, het dashboard en fullscreen. Ze staan standaard uit en worden via de "
           "optionele stap *Optionele plugins* in de installer geïnstalleerd (stil: `/TASKS=\"plugins\"`). Daarna zet je ze aan bij "
           "*Instellingen → Plugins*.", lead))
s += table([
    ["Plugin", "Toont", "Gegevens van"],
    ["Claude-verbruik", "% van je 5-uurs- en weeklimiet en de tijd tot die vrijkomt (balk, ring of getal; melding bij de limiet)",
     "De aanmelding van Claude Code op je pc (alleen gelezen), verstuurd naar Anthropic (ongedocumenteerd adres)"],
    ["Weer", "Zon/wolken/regen, temperatuur, luchtvochtigheid, wind, kans op regen", "Open-Meteo (gratis, geen sleutel); alleen coördinaten gaan mee"],
    ["Locatie (GPS)", "Plaatsnaam, coördinaten, nauwkeurigheid, bron, afstand tot huis", "Windows-locatieservice, anders je IP-adres"],
    ["Uptime-monitor", "\"3/3\" actief; rood plus melding bij uitval", "Je eigen lijst websites (HTTP), poorten (TCP) en apparaten (ping)"],
    ["Script / REST", "Elke waarde die je kiest", "Een opdracht, tekstbestand of JSON-webadres: Home Assistant, NAS, koers, ..."],
], [0.19, 0.42, 0.39])
s += fig("plugins-widget.png", "Plugin-voorbeelden in het widget.", width=8.5 * cm)
s.append(H2("Waarom het veilig is"))
s.append(P("TaskbarStats draait als administrator en voert daarom **nooit plugin-code uit**. De plugins draaien in een apart hulpprogramma "
           "(`TaskbarStats.Plugins.exe`) dat via een geplande taak **zonder administratorrechten** start en per plugin een klein "
           "JSON-bestand schrijft naar `%AppData%\\TaskbarStats\\plugins\\`. De app leest die bestanden alleen. Het hulpprogramma stopt vanzelf "
           "als TaskbarStats sluit of er geen plugin aan staat."))
s.append(H2("Instellen"))
s += B(["**Config bewerken…** opent een eenvoudige editor voor het `.ini`-bestand van de plugin (`sleutel = waarde`; de commentaarregels leggen elke optie uit). "
        "Wijzigingen gelden binnen enkele seconden; *Standaard* zet de oorspronkelijke tekst terug.",
        "Onder **Waarden** kies je per waarde: wel/niet in het widget, tekst, balk of ring, en een kleur.",
        "In dashboard en fullscreen is er de tegel **Plugins**; die verschijnt alleen als er iets te tonen is. In fullscreen is het een strook onderaan; "
        "klik erop voor de detailpagina."])
s += side_by_side("plugins-settings.png", "Het tabblad Plugins.", "plugins-config-editor.png", "De configeditor.")
s += box("Voor **Claude-verbruik** moet je aangemeld zijn met de Claude Code-CLI (`claude`, dan `/login`). Het gebruikte adres van Anthropic "
         "is ongedocumenteerd en kan veranderen; de plugin toont dan de reden en laat de laatste waarden vervaagd staan. Je token gaat alleen naar "
         "api.anthropic.com en wordt nooit in een bestand of log gezet.", "note")
s.append(H2("Een eigen plugin, in elke taal"))
s.append(P("Schrijf een bestand `%AppData%\\TaskbarStats\\plugins\\<naam>.json` en schrijf het opnieuw als de waarde verandert. "
           "`ttl` is het aantal seconden voordat een waarde vervaagd (verouderd) wordt getoond, `level` is `ok`, `warn` of `crit`, "
           "en `notify: true` geeft een melding bij `crit`. Het verschijnt vanzelf onder *Plugins → Waarden*."))
s += codeblock("""{
  "plugin": "mytool", "title": "My tool", "updated": "2026-11-07T10:00:00Z", "ttl": 600, "status": "ok",
  "items": [
    { "key": "queue", "label": "Build queue", "short": "Queue", "value": 3, "min": 0, "max": 10,
      "unit": "", "text": "3 jobs", "detail": "last run 10:31", "level": "ok", "widget": true, "notify": false }
  ]
}""")

# ---- 9. Tips en trucs
s += H1("9. Tips en trucs")
s.append(H2("Sneltoetsen en muis"))
s += table([
    ["Sneltoets / actie", "Functie"],
    ["**Ctrl+Alt+D**", "Dashboard klik-door aan/uit"],
    ["**Ctrl+Alt+F**", "Fullscreen-cockpit openen/sluiten"],
    ["**Ctrl+Alt+W**", "Widget klik-door aan/uit (ook via het pictogram bij de klok)"],
    ["Esc (fullscreen)", "Terug / sluiten"],
    ["1 / 2 / 3 (fullscreen)", "Grafiek 1 min / 5 min / 1 uur"],
    ["I / C (fullscreen)", "Specificaties / alles kopiëren"],
    ["Spatie of 3× klikken (fullscreen)", "Automatische tour"],
    ["Dubbelklik / middelklik op het widget", "Taakbeheer / tooltip kopiëren (te wijzigen bij *Algemeen*)"],
], [0.38, 0.62], first_bold=False)
s.append(H2("Handige trucs"))
tips = [
    ("Menu blijft open", "Klik gerust achter elkaar meerdere opties aan; het menu sluit pas als je ernaast klikt, Esc drukt of de muis weg is."),
    ("Widget kwijt of scheef?", "Kies in het menu *Vastplakken aan systeemvak*: het widget gaat naast het systeemvak staan en volgt het als dat verschuift. "
                               "Verslepen zet het weer uit."),
    ("Thema proefrijden", "Gebruik *Voorbeeld (10 s)* in plaats van meteen te kiezen: je ziet het thema in alle stijlen en alles springt daarna vanzelf terug."),
    ("Bureaubladbehang met cijfers", "Zet het dashboard op *achtergrond* + *klik-door* (Ctrl+Alt+D) met een mooi thema en een achtergrondafbeelding."),
    ("Ruimte besparen", "Gebruik *Compact*, zet de temperatuur klein achter de CPU-/GPU-cel en kies korte grafieklengtes per onderdeel."),
    ("Twee GPU's", "Laat de GPU op *Automatisch* staan: de drukste telt, en elke GPU krijgt een eigen kleur zodat je ziet bij welke het getal hoort. "
                   "Wil je er één volgen, kies dan een vaste GPU onder *Bronnen*."),
    ("Netwerk in bits", "Providers rekenen in Mb/s: kies bij *Waarden* voor bits in plaats van bytes."),
    ("Vergelijk met Taakbeheer", "De CPU-meting kun je omzetten naar *Utility*; dat is ongeveer wat sommige tools tonen, maar op een boostende CPU bijna twee keer zo hoog als Taakbeheer."),
    ("Support-hulp", "Op de specificatiepagina kopieert toets **C** alle specificaties als platte tekst - ideaal voor een forumpost. In *Over* kopieert *Kopieer diagnose* versie, Windows en de laatste logregels (zonder gebruikers- of computernaam)."),
    ("Nieuwe pc?", "*Instellingen → Algemeen → Backup*: exporteer al je instellingen naar één JSON-bestand en importeer het op de andere pc. Je kunt ook terugzetten naar de standaard (thema's en verbruikslog blijven)."),
    ("Informatieschermpje", "Fullscreen op een tweede monitor met de automatische tour is een prima permanent infoscherm."),
    ("Thema delen", "Exporteer een thema en mail het bestand; de ander importeert het. Alles wat de app exporteert of kopieert vermeldt het programma en de link."),
    ("Eigen taal", "Zet een taalbestand in `%AppData%\\TaskbarStats\\lang\\` en start de app opnieuw. Met `TASKBARSTATS_PSEUDO=1` en de taal *Pseudo (test)* zie je meteen welke teksten niet vertaald of te lang zijn."),
    ("Netwerkschijven", "Zet *Netwerkschijven meenemen* aan onder *Bronnen*; ze worden op de achtergrond opgevraagd, dus een onbereikbare share vertraagt de app niet."),
    ("Home Assistant, NAS, koers...", "Gebruik de plugin *Script / REST*: een opdracht, tekstbestand of JSON-webadres levert elke waarde die je wilt op het widget."),
]
for t, d in tips:
    s.append(KeepTogether([Paragraph(f"<b>{md(t)}</b> - {md(d)}", bullet, bulletText="•")]))
s += box("Wil je dat de app nooit iets over het internet doet? Laat de plugins en de updatecontrole uit staan: dan gebruikt TaskbarStats het netwerk alleen om het verbruik en de ping te meten.")

# ---- 10. Probleemoplossing
s += H1("10. Problemen oplossen")
s += table([
    ["Probleem", "Oplossing"],
    ["Het menu of de tooltip verschijnt niet", "Klik eerst op het widget en probeer opnieuw."],
    ["Het widget staat buiten beeld", "Startmenu → TaskbarStats starten, dan rechtsklik → *Vastplakken aan systeemvak*."],
    ["Geen temperaturen", "De app moet als administrator draaien (standaard). Heeft je processor geen CPU-sensor, dan gebruikt de app de thermal zone van Windows. "
                          "Sommige sensoren (zoals hoofdbord) bestaan niet op elke pc."],
    ["GPU-waarde lijkt niet te kloppen", "Kies onder *Bronnen* een vaste GPU in plaats van *Automatisch* en vergelijk met de 3D-engine in Taakbeheer."],
    ["FPS blijft leeg", "FPS is experimenteel, staat standaard uit en heeft administratorrechten nodig. Er verschijnt alleen een waarde zolang een programma "
                        "beelden tekent (game, videospeler, browser)."],
    ["Een UAC-melding bij het inloggen", "Gebruik *Met Windows meestarten* (geplande taak met hoogste rechten) in plaats van een eigen snelkoppeling in Opstarten."],
    ["Defender meldt een kwetsbaar stuurprogramma (Winring0)", "Dat gebeurde bij oude versies (1.6.0-1.6.3). Update naar 1.6.4 of nieuwer: die bevat geen driver meer."],
    ["Claude-plugin toont geen cijfers", "Meld je aan met de Claude Code-CLI (`claude`, daarna `/login`). Het connector-bestand van Claude wordt nooit gebruikt."],
    ["Verbruik klopt niet helemaal", "Verkeer terwijl de app niet draait wordt niet meegeteld."],
    ["De app is gecrasht", "Bij de volgende start verschijnt een ballonmelding; klik erop voor het Over-scherm met *Kopieer diagnose*. Fouten staan in `diag.log` in de gegevensmap - alleen op je eigen pc."],
    ["Taakbalk herstart of Verkenner crasht", "Het widget herkent een nieuwe taakbalk en koppelt zichzelf opnieuw; zo niet, sluit de app af en start hem opnieuw."],
], [0.32, 0.68])
s.append(H2("Waar staan mijn gegevens?"))
s += table([
    ["Bestand / map", "Inhoud"],
    ["`%AppData%\\TaskbarStats\\settings.json`", "Je instellingen (overleeft herbouw en herinstallatie)"],
    ["`%AppData%\\TaskbarStats\\usage.json`", "Netwerkverbruik per dag"],
    ["`%AppData%\\TaskbarStats\\themes\\`", "Je eigen thema's"],
    ["`%AppData%\\TaskbarStats\\lang\\`", "Eigen taalbestanden"],
    ["`%AppData%\\TaskbarStats\\plugins\\`", "Uitvoer en configs van plugins"],
    ["`%AppData%\\TaskbarStats\\diag.log`", "Foutlog (blijft op je pc)"],
    ["Portable: map `data` naast de exe", "Alles hierboven, als `portable.txt` naast de exe staat"],
], [0.45, 0.55], first_bold=False)
s += box("TaskbarStats stuurt geen gegevens naar de ontwikkelaar. Alleen de optionele plugins (weer, locatie, Claude-verbruik, uptime, REST) en de "
         "optionele updatecontrole maken verbinding met internet, en alleen als jij ze aanzet.")

# ---- 11. Over
s += H1("11. Licentie, credits en contact")
s.append(P("TaskbarStats is gratis en vrij te gebruiken onder de **MIT-licentie**: gebruiken, aanpassen en delen mag, ook voor eigen projecten. "
           "De enige voorwaarde is dat de copyrightregel en licentietekst behouden blijven - laat dus graag de credits staan."))
s.append(P("**Bedacht, ontworpen en getest door** Eric Bruggema. **Gebouwd met** Claude Code (Anthropic)."))
s += table([
    ["Software van derden", "Licentie", "Gebruikt voor"],
    ["LibreHardwareMonitorLib", "MPL-2.0", "Temperatuur- en andere sensoren"],
    ["HidSharp", "Apache-2.0", "Apparaatcommunicatie"],
    [".NET 8 / Windows Forms", "MIT", "Het programma zelf"],
    ["Inno Setup", "Inno Setup License", "De installer"],
], [0.38, 0.22, 0.40])
s.append(P("Deze componenten vallen onder hun eigen licenties."))
s.append(H2("Links"))
s += table([
    ["Website", "taskbarstats.com"],
    ["Downloads en broncode", "github.com/ericbruggema/TaskbarStats"],
    ["Fouten melden / ideeën", "github.com/ericbruggema/TaskbarStats/issues"],
    ["Demo-video's (Engels)", "github.com/ericbruggema/TaskbarStats/tree/main/docs/tour/promo"],
    ["LinkedIn", "linkedin.com/in/eric-bruggema-28a81a186"],
    ["Facebook", "facebook.com/eric.bruggema"],
], [0.32, 0.68], header=False)
s.append(Spacer(1, 10))
s.append(P(f"Deze handleiding hoort bij TaskbarStats {VERSION}. De screenshots tonen de Engelse interface met verzonnen waarden; "
           "computer- en programmanamen zijn geanonimiseerd.", small))




if __name__ == "__main__":
    build(s, OUT)
