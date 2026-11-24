# Plugins: handboek

[English](plugins.md) · [Deutsch](plugins.de.md) · [Terug naar de README](../README.nl.md)

Plugins voegen extra waarden toe aan het widget, het bureaublad-dashboard en het fullscreen-scherm: je Claude-verbruik, het weer, de status van een website, een koers, een waarde uit je domotica, alles wat je kunt lezen uit een webadres, een bestand of een opdracht.

Dit handboek legt uit hoe je plugins **toevoegt**, **goedkeurt** en **verwijdert**, wat ze **mogen doen**, en hoe je **zelf een recept schrijft** zonder een programma te schrijven.

## 1. Het idee in één minuut

- **Er is niets totdat je het toevoegt.** Plugins die je niet hebt toegevoegd zijn nergens te zien: niet in de instellingen, niet in het widget. Open *Instellingen → Plugins → Plugin toevoegen…*, kies er een uit de galerij en hij is toegevoegd (als een klein `.ini`-bestand in je datamap). *Verwijderen…* haalt hem weer weg.
- **Drie soorten plugin:**
  - **Ingebouwd** (5): Claude-verbruik, Weer, Locatie (GPS), Uptime-monitor, Script / REST.
  - **Recept** (13 kant-en-klaar, en je eigen): een tekstbestand dat zegt waar een waarde vandaan komt en hoe die eruitziet. Geen programmacode. Zie hoofdstuk 6 en 7.
  - **Eigen programma**: je eigen script of programma schrijft een klein JSON-bestand; TaskbarStats toont het. Zie hoofdstuk 11.
- **Jij keurt goed wat een recept mag.** Voordat een recept (of de Script-plugin) draait, zie je een lijst: welke internetadressen hij benadert, welke bestanden hij leest, welke opdrachten hij start, welke geheimen hij gebruikt. Hij draait pas nadat je op *Toestaan* drukt. Verander je hem later zodat hij meer kan, dan word je opnieuw gevraagd.
- **Veilig ontworpen.** TaskbarStats draait als administrator en voert daarom zelf nooit plugin-code uit. Plugins draaien in een apart hulpprogramma (`TaskbarStats.Plugins.exe`) **zonder administratorrechten**. Het hulpprogramma schrijft per plugin een klein resultaatbestand; TaskbarStats leest die bestanden alleen.

## 2. Snel aan de slag

1. *Instellingen → Plugins → **Plugin toevoegen…***
2. Kies een plugin in de lijst. Rechts zie je wat hij doet, wat je nodig hebt en (bij recepten) wat je moet goedkeuren.
3. Druk op **Toevoegen**. Een recept opent in de editor zodat je kunt invullen wat nodig is (een adres, een naam).
4. Druk op **Opslaan en testen**. Heeft het recept toestemming nodig, dan zie je het goedkeuringsvenster; lees het en kies *Toestaan*. Even later staat het resultaat onderin de editor.
5. Vink de plugin in de lijst aan om hem aan te zetten. De waarden verschijnen in het widget (en in de tegel *Plugins* van het dashboard en fullscreen).
6. Onder *Waarden* kies je per waarde: tonen of niet, tekst / balk / ring en een kleur.

Het plugin-hulpprogramma moet geïnstalleerd zijn: vink *Optionele plugins* aan in Setup (stil: `/TASKS="plugins"`).

## 3. Waar alles staat

Alles staat in je datamap, normaal `%AppData%\TaskbarStats` (met `TASKBARSTATS_DATA` of een portable installatie staat die elders).

| Bestand | Wat |
|---------|-----|
| `plugins\<naam>.ini` | De configuratie van een plugin. **Dit bestand is de registratie**: geen bestand, geen plugin. |
| `plugins\<naam>.json` | Het laatste resultaat, geschreven door het hulpprogramma (of door je eigen programma). |
| `plugins\approved.json` | Welke plugins je hebt goedgekeurd, met een controlesom van wat ze mogen. |
| `plugins\secrets.dat` | Je geheimen, versleuteld voor je Windows-account (hoofdstuk 8). |
| `plugins\host.log` | Logboek van het hulpprogramma (`elevated=False` hoort in de eerste regel te staan). |
| `plugin-alerts.json` | Welke meldingen al getoond zijn (hoofdstuk 9). |

## 4. De galerij

**Ingebouwd**

| Plugin | Toont | Gegevens van |
|--------|-------|--------------|
| Claude-verbruik | % van je 5-uurs- en weeklimiet, wanneer die vrijkomt | je aanmelding van Claude Code, verstuurd naar Anthropic. Typ eenmalig `claude` en `/login`. |
| Weer | zon/wolken/regen, temperatuur, luchtvochtigheid, wind, kans op regen | Open-Meteo (gratis, zonder sleutel) |
| Locatie (GPS) | plaats, coördinaten, nauwkeurigheid | Windows-locatieservice, anders je IP-adres |
| Uptime-monitor | websites (HTTP), poorten (TCP), apparaten (ping) | je eigen lijst |
| Script / REST | één waarde per `[sectie]` uit een opdracht, bestand of webadres | jijzelf. Vraagt goedkeuring. |

**Recepten** (kant-en-klaar; elk begint uitgeschakeld en is makkelijk aan te passen)

| Recept | Toont | Nodig |
|--------|-------|-------|
| Claude-verbruik (aanpasbaar recept) | dezelfde cijfers als de ingebouwde plugin, als recept geschreven | eenmalig `claude` + `/login` |
| Pi-hole | % geblokkeerde DNS-verzoeken, blokkeren aan/uit | Pi-hole v5 |
| Home Assistant-sensor | de toestand van één entiteit | een langlevend token, bewaard als geheim `homeassistant` |
| GitHub-repository | sterren en open issues | niets |
| Crypto-koers | koers en verandering over 24 uur (CoinGecko) | niets |
| Wisselkoers | EUR naar USD (ECB via frankfurter.dev) | niets |
| Statuspagina van een dienst | OK / kleine / grote storing van GitHub, Claude, Cloudflare, … | niets |
| Luchtkwaliteit | Europese luchtkwaliteitsindex en PM2.5 (Open-Meteo) | je coördinaten |
| Docker-containers actief | aantal draaiende containers | Docker; **start een opdracht** |
| Vrije schijfruimte | GB vrij op een schijf | **start een PowerShell-opdracht** |
| Waarde uit een tekstbestand | een getal uit een bestand dat een ander programma bijhoudt | het pad; **leest een bestand** |
| Leeg recept: webadres (JSON) | beginpunt voor je eigen recept | een adres |
| Eigen script schrijft de waarden | registreert een bron voor je eigen programma (hoofdstuk 11) | je programma |

Je kunt ook een recept uit een `.ini`-bestand **importeren**. Importeer alleen recepten uit een bron die je vertrouwt; het doet niets totdat je hebt gelezen en goedgekeurd wat het mag.

## 5. Wat een plugin mag, en de goedkeuring

Een recept kan alleen wat op zijn lijst staat. De lijst wordt uit het bestand afgeleid:

| Onderdeel | Komt van | Getoond als |
|-----------|----------|-------------|
| Internetadres (host) | elke `url = …` | *Maakt verbinding met het internetadres: api.example.com* |
| Bestand | `file = …` en `{file:…}` | *Leest het bestand: …* |
| Opdracht | `cmd = …` | ***STARTT** deze opdracht op je pc* (rood) |
| Geheim | `{secret:naam}` | *Gebruikt het bewaarde geheim: naam* |
| Omgevingsvariabele | `{env:NAAM}` | *Leest de omgevingsvariabele: NAAM* |

- De controlesom gaat over deze lijst, **niet** over de formules of teksten. Een label of formule wijzigen vraagt nooit opnieuw; een andere host, een bestand, een opdracht of een geheim erbij wel.
- Een webadres mag nooit naar een andere host dan de genoemde: een variabele kan de host niet veranderen, en een omleiding naar een andere host wordt geweigerd (de plugin noemt het nieuwe adres). Geheimen gaan dus nooit ergens heen waar je geen toestemming voor gaf.
- Recepten zijn **geen programma's**. De formuletaal (hoofdstuk 7) kan alleen rekenen met de gegevens die het recept ophaalde. Ze kan geen bestanden lezen, geen netwerk gebruiken en geen opdrachten starten. Alleen `cmd =` en `file =` kunnen dat, en die keur je uitdrukkelijk goed.
- Een recept dat een opdracht start, doet dat als **jijzelf, zonder administratorrechten**, zoals elk programma dat je zelf start. Keur opdrachten alleen goed als je ze begrijpt.
- Geheimen komen nooit in logboeken, foutmeldingen of resultaatbestanden.
- Zolang het niet goedgekeurd is, toont de plugin *Heeft je goedkeuring nodig* en doet hij niets. *Bekijken en goedkeuren…* opent de lijst opnieuw.

## 6. Een recept schrijven

Een recept is een tekstbestand `plugins\<naam>.ini` (open het met *Config bewerken…*; commentaar begint met `#`). Boven de eerste `[sectie]` zeg je waar de gegevens vandaan komen; elke `[sectie]` eronder is één waarde.

**In de app:** *Instellingen → Plugins → Hulp en voorbeelden…* (ook een knop in de editor) laat zien hoe het werkt, complete recepten (het antwoord, het recept en wat je ziet) en een lijst formules met wat elke oplevert. Je kunt een voorbeeld kopiëren of direct in de editor zetten.

```ini
type = recipe
title = Pi-hole
enabled = true
interval = 60

url = http://pi.hole/admin/api.php?summaryRaw

[blocked]
label = Pi-hole vandaag geblokkeerd
short = Ads
value = ads_percentage_today
unit = %
format = {value:0.0}%
detail = {ads_blocked_today:#,##0} van {dns_queries_today:#,##0} verzoeken geblokkeerd
warn = 30
```

### Boven de eerste sectie

| Sleutel | Betekenis |
|---------|-----------|
| `type = recipe` | Verplicht. (`external` = je eigen programma schrijft het resultaat, hoofdstuk 11.) |
| `title` | Naam in de instellingen en tooltip. |
| `description` | Eén regel in de instellingen (optioneel). |
| `enabled` | `true` / `false`. Een nieuwe plugin begint met `false`. |
| `interval` | Seconden tussen verversingen (10 tot 86400; standaard 60). Na fouten wacht het hulpprogramma langer, 30 s verdubbelend, nooit meer dan het interval. |
| `timeout` | Seconden voordat hij opgeeft (standaard 25). |
| `url` | Een webadres (`http` of `https`). |
| `method` | `GET` (standaard) of `POST`. |
| `body`, `content_type` | Voor `POST`. Standaard inhoudstype `application/json`. |
| `header` | `Naam: waarde`; herhaal de regel voor meer headers. |
| `file` | Een bestand om te lezen in plaats van een webadres (`%USERPROFILE%` enz. werkt). |
| `cmd` | Een opdracht die gestart wordt in plaats daarvan (de uitvoer zijn de gegevens). |
| `parse` | `auto` (standaard), `json`, `text`, `lines` of `csv`. `auto` = JSON als het met `{` of `[` begint, anders tekst. |
| `csv_header`, `csv_separator` | Voor `csv`: de eerste rij bevat de namen (`rows` worden objecten); scheidingsteken (standaard `,`). |
| `help` | Een zin die aan foutmeldingen wordt toegevoegd ("Log eenmalig in: typ claude en /login"). |

Gebruik één bron per recept (`url`, `file` of `cmd`). Heb je er twee nodig? Maak twee recepten.

In `url`, `header`, `body` en `file` kun je **plaatsvervangers** gebruiken: `{secret:naam}` (een bewaard geheim), `{file:pad#veld}` (een veld uit een JSON-bestand, bijvoorbeeld `{file:~/.claude/.credentials.json#claudeAiOauth.accessToken}`; `~` is je gebruikersmap) en `{env:NAAM}` (een omgevingsvariabele). Elk daarvan staat in de goedkeuringslijst.

### Elke `[sectie]` is één waarde

| Sleutel | Betekenis |
|---------|-----------|
| `label` | Volledige naam (tooltip, instellingen). Mag `{…}`-formules bevatten. Standaard: de sectienaam. |
| `short` | Korte naam in het widget; leeg = geen label. |
| `value` | Een formule (hoofdstuk 7). Een getal geeft balk/ring/drempels; tekst wordt als tekst getoond. |
| `format` / `text` | De tekst in het widget, met `{formule:opmaak}`-stukjes. Standaard: de waarde (met eenheid). |
| `detail` | Tooltipregel(s), zelfde notatie. Standaard: `label: tekst`. |
| `unit`, `min`, `max` | Eenheid, en het bereik voor balken en ringen (standaard 0 tot 100). |
| `warn`, `crit`, `invert` | Kleurdrempels op het getal. `invert = true` betekent dat *laag* slecht is (vrije ruimte). |
| `level` | Je eigen formule die `'ok'`, `'warn'` of `'crit'` geeft. Vervangt `warn`/`crit`. |
| `color` | Een kleurtip `#RRGGBB`. Je eigen keuze in de instellingen gaat voor. |
| `when` | Een formule; de waarde is verborgen als die onwaar is. |
| `widget` | `false` = alleen in de tooltip, niet in het widget (in de instellingen kun je dat nog kiezen). |
| `notify` | `true` = melding als het niveau `crit` wordt (hoofdstuk 9). |
| `reset` | Een formule die een tijdstip geeft, bijvoorbeeld `five_hour.resets_at`: het moment waarop de waarde vanzelf terugkeert (hoofdstuk 9). |

### De gegevens die je in formules kunt gebruiken

- Is het antwoord een JSON-object, dan zijn de **sleutels namen**: `ads_percentage_today`, `rates.USD`, `items[0].price`, `a["sleutel met spaties"]`. `root` is het hele antwoord (gebruik `root[0].name` als het antwoord een lijst is).
- `text` de ruwe tekst, `lines` de niet-lege regels, `rows` de CSV-rijen, `http_status` de HTTP-status, `elapsed_ms` de duur van het verzoek.
- `now` de huidige tijd in Unix-seconden. In `format`, `detail`, `level`, `color`: `value` (deze waarde) en `text` (de tekst ervan).
- Ontbrekende gegevens zijn `null` en nooit een fout: `a.b.c` is `null` als `b` ontbreekt. Rekenen met `null` geeft `null`; het widget toont dan `?`.

## 7. De formuletaal

Formules zijn kleine uitdrukkingen. Ze kunnen alleen rekenen; ze raken geen bestanden, netwerk of opdrachten aan.

**Syntaxis:** getallen `12.5`, tekst `'ok'` of `"ok"`, `true false null`, bewerkingen `+ - * / %`, vergelijken `== != < <= > >=`, logica `&& || !`, keuze `a ? b : c`, haakjes, namen `price`, paden `a.b`, `a[0]`, `a['sleutel']`, functieaanroepen `round(x, 1)`.

- Tekst vergelijkt **zonder op hoofdletters te letten**: `'OK' == 'ok'`. Tekst die een getal is vergelijkt als getal: `'10' > '9'`.
- `+` plakt tekst aan elkaar als één kant tekst is: `'n=' + 5`.
- Backslashes in tekst blijven staan, behalve `\\`, `\'`, `\"`, `\n`, `\t`. Zo werken patronen: `regex(text, '(\d+)')`.
- Grenzen: 2000 tekens, 600 onderdelen en 20 000 stappen per formule; patronen stoppen na 0,25 s.

**Functies**

| Groep | Functies |
|-------|----------|
| Getallen | `abs(x)` `round(x, decimalen)` `floor(x)` `ceil(x)` `sqrt(x)` `pow(x, y)` `clamp(x, laag, hoog)` `pct(deel, geheel)` `min(...)` `max(...)` `sum(lijst)` `avg(lijst)` `num(x)` (tekst naar getal, verdraagt `12,5%`) |
| Logica | `if(voorwaarde, a, b)` `coalesce(a, b, ...)` (de eerste die niet null of leeg is) `isnull(x)` |
| Tekst | `str(x)` `len(x)` `upper` `lower` `trim` `contains(s, deel)` `startswith` `endswith` `replace(s, a, b)` `substr(s, begin, lengte)` `split(s, scheiding)` `join(lijst, scheiding)` `matches(s, patroon)` `regex(s, patroon, groep)` (groep 1 als het patroon er een heeft, anders 0) |
| Lijsten | `count(lijst)` `first(lijst)` `last(lijst)` `pluck(lijst, 'sleutel')` `where(lijst, 'sleutel', waarde)` `json(tekst)` (JSON lezen die als tekst staat) |
| Tijd | `time(x)` (ISO-tekst of seconden/milliseconden naar seconden) `until(t)` `ago(t)` `duration(seconden)` (`2h 10m`) `clock(t, 'ddd HH:mm')` (lokale tijd) |
| Grootte | `bytes(n)` (`1.5 GB`) |

**Teksten met formules** (`format`, `detail`, `label`, …): `{formule}` of `{formule:opmaak}`; `{{` en `}}` zijn losse accolades.

| Opmaak | Geeft voor 62,5 |
|--------|-----------------|
| `{value}` | `62.5` |
| `{value:0}` | `63` (.5 naar boven) |
| `{value:0.0}` | `62.5` |
| `{value:+0.0}` | `+62.5` |
| `{value:#,##0}` | `63` (en `1,234,568` voor 1234567,8) |
| `{value:0%}` | `6250%` (een `%` in de opmaak vermenigvuldigt met 100; schrijf `{value:0}%` om alleen een teken toe te voegen) |

Keuzes werken binnen teksten: `{status == 'enabled' ? 'on' : 'OFF'}`.

## 8. Geheimen

Een API-sleutel of token hoort nooit in een recept (recepten worden gedeeld). Bewaar het één keer: *Instellingen → Plugins → **Geheimen…** → Toevoegen of vervangen…* (een naam zoals `homeassistant` en de waarde). In het recept schrijf je `{secret:homeassistant}`:

```ini
header = Authorization: Bearer {secret:homeassistant}
```

- Geheimen staan in `plugins\secrets.dat`, versleuteld met de Windows-gegevensbescherming voor jouw account op deze pc. Het bestand is nutteloos op een andere pc of voor een andere gebruiker.
- De instellingen tonen een bewaarde waarde nooit meer; je kunt hem vervangen of verwijderen.
- Een plugin verwijderen laat zijn geheimen staan (een ander recept kan ze gebruiken).
- Een ontbrekend geheim geeft een duidelijke melding: *Het geheim 'x' is niet ingesteld. Voeg het toe onder Instellingen > Plugins > Geheimen.*

## 9. Meldingen

- **Eén melding per probleem.** Als een waarde met `notify = true` *kritiek* wordt, krijg je één melding. Zolang de toestand gelijk blijft komt er geen tweede, ook niet na een herstart of een update (dit wordt onthouden). Een nieuw probleem (een tweede site valt uit) of een herstel gevolgd door een nieuw probleem geeft wel een nieuwe melding.
- **Een ongewijzigde waarschuwing herhalen** (*Instellingen → Plugins → Pluginmeldingen*): *Alleen als er iets verandert* (standaard), of na 30 min, 1 u of 4 u.
- **Meld het kort voordat een limiet wordt teruggezet** (standaard 5 minuten, of *Uit*, 2, 10, 15): bij een waarde met een `reset`-tijd die op *waarschuwing* of hoger staat, krijg je één melding kort voordat die terugkeert ("Claude 5-uurslimiet: bijna teruggezet, over ongeveer 4 min weer vrij"). Eén per terugzetmoment. De ingebouwde Claude-plugin en het Claude-recept geven de terugzettijd van elke limiet.
- Meldingen moeten aan staan (*Instellingen → Algemeen*), en er wordt niets getoond tijdens een themavoorbeeld.

## 10. Uitgewerkte voorbeelden

**Claude-verbruik als recept** (dezelfde cijfers als de ingebouwde plugin; sjabloon *Claude-verbruik (aanpasbaar recept)*):

```ini
type = recipe
title = Claude usage
interval = 300
help = Log eenmalig in: typ in een terminal 'claude' en /login.
url = https://api.anthropic.com/api/oauth/usage
header = Authorization: Bearer {file:~/.claude/.credentials.json#claudeAiOauth.accessToken}
header = anthropic-beta: oauth-2025-04-20

[five_hour]
label = Claude 5-uurslimiet
short = 5h
when = five_hour != null
value = clamp(coalesce(five_hour.utilization, 0), 0, 100)
unit = %
format = {value:0}%
detail = {value:0}% gebruikt{five_hour.resets_at ? ', weer vrij over ' + until(five_hour.resets_at) : ''}
warn = 70
crit = 90
notify = true
reset = five_hour.resets_at
```

(Het webadres is niet gedocumenteerd door Anthropic en kan veranderen. Een token van `claude setup-token` werkt **niet**: de server weigert het omdat het recht `user:profile` ontbreekt. Meld je aan met `claude` en `/login`.)

**Home Assistant** (token bewaard als geheim `homeassistant`):

```ini
type = recipe
title = Home Assistant
url = http://homeassistant.local:8123/api/states/sensor.woonkamer_temperatuur
header = Authorization: Bearer {secret:homeassistant}

[sensor]
label = {attributes.friendly_name}
value = state
unit = °C
format = {value:0.0}°
detail = veranderd {ago(last_changed)} geleden
```

**Aantal Docker-containers** (start een opdracht; jij keurt dat goed):

```ini
type = recipe
title = Docker
cmd = docker ps -q

[running]
label = Docker-containers
value = count(lines)
format = {value}
```

**Vrije schijfruimte** met kleuren (laag is slecht):

```ini
type = recipe
title = Schijfruimte
interval = 300
cmd = powershell -NoProfile -Command "[math]::Round((Get-PSDrive C).Free / 1GB, 1)"

[free]
label = Vrije ruimte op C:
value = num(text)
format = {value:0} GB
invert = true
warn = 50
crit = 20
notify = true
```

## 11. Je eigen programma schrijft de waarden

Gebruik het sjabloon *Eigen script schrijft de waarden (JSON-bestand)*. Het registreert een bron met de naam van het `.ini`-bestand; TaskbarStats start niets. Je script (PowerShell, Python, een geplande taak, alles) schrijft `plugins\<naam>.json`:

```json
{
  "plugin": "<naam>", "title": "Mijn script", "updated": "2026-01-01T12:00:00Z", "ttl": 300, "status": "ok",
  "message": "",
  "items": [
    { "key": "temp", "label": "Kamertemperatuur", "short": "Kamer", "value": 21.5, "min": 0, "max": 40,
      "unit": "°C", "text": "21.5°", "detail": "Woonkamer", "level": "ok", "color": "#34C759",
      "widget": true, "notify": false, "reset": "2026-01-01T18:00:00Z" }
  ]
}
```

Alleen `items[].key` is verplicht. `ttl` is hoeveel seconden de waarden vers blijven; daarna worden ze vervaagd getoond met een `?`. `status: "error"` plus een `message` toont het probleem in de instellingen. Schrijf het bestand in één keer (schrijf een tijdelijk bestand en hernoem het). Een JSON-bestand zonder zijn `.ini`-registratie wordt genegeerd.

## 12. Problemen oplossen

| Je ziet | Doe |
|---------|-----|
| *Heeft je goedkeuring nodig…* | *Bekijken en goedkeuren…* (of *Opslaan en testen* in de editor). |
| *Het geheim 'x' is niet ingesteld* | Instellingen → Plugins → Geheimen → Toevoegen of vervangen. |
| *Het antwoord is geen JSON* | Zet `parse = text` (of `lines` / `csv`) in het recept. |
| *De server verwijst door naar een andere host* | Zet het genoemde adres in `url =` (en keur opnieuw goed). |
| *HTTP 401 / 403* | Verkeerde of ontbrekende sleutel. Voor Claude: `claude`, dan `/login`. |
| *Rate limited* | De dienst vraagt je te vertragen; de plugin wacht vanzelf. Gebruik een langer `interval`. |
| Een waarde toont `?` | Zijn formule gaf niets (ontbrekende gegevens, of een fout: houd de muis erboven of lees de melding in de instellingen). |
| *Wacht op het plugin-hulpprogramma* | Het hulpprogramma start vanzelf; gebruik *Helper starten*, of lees *Logboek openen*. Niet geïnstalleerd? Draai Setup en vink *Optionele plugins* aan. |
| Helemaal niets | Staat de plugin aangevinkt, en staat *Plugin-waarden in het widget tonen* aan? |

## 13. Grenzen

Recepten lezen één bron, rekenen formules uit en tonen waarden. Ze kunnen niet eerst inloggen en dan ophalen (een login die een sleutel teruggeeft die daarna meegestuurd moet worden), niet over een lijst lopen om per element een waarde te maken, en geen toestand tussen verversingen bewaren. Schrijf daarvoor een eigen programma (hoofdstuk 11) of gebruik de plugin *Script / REST*.
