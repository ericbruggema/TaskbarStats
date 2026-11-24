# Plugins: Handbuch

[English](plugins.md) · [Nederlands](plugins.nl.md) · [Zurück zur README](../README.de.md)

Plugins fügen dem Widget, dem Desktop-Dashboard und der Vollbildansicht zusätzliche Werte hinzu: deine Claude-Nutzung, das Wetter, den Status einer Webseite, einen Kurs, einen Wert aus deiner Hausautomation, alles, was du aus einer Webadresse, einer Datei oder einem Befehl lesen kannst.

Dieses Handbuch erklärt, wie du Plugins **hinzufügst**, **genehmigst** und **entfernst**, was sie **tun dürfen** und wie du **selbst ein Rezept schreibst**, ohne ein Programm zu schreiben.

## 1. Die Idee in einer Minute

- **Es ist nichts da, bevor du es hinzufügst.** Plugins, die du nicht hinzugefügt hast, sind nirgends zu sehen: nicht in den Einstellungen, nicht im Widget. Öffne *Einstellungen → Plugins → Plugin hinzufügen…*, wähle eins aus der Galerie, und es ist hinzugefügt (als kleine `.ini`-Datei in deinem Datenordner). *Entfernen…* löscht es wieder.
- **Drei Arten von Plugins:**
  - **Eingebaut** (5): Claude-Nutzung, Wetter, Standort (GPS), Uptime-Monitor, Script / REST.
  - **Rezept** (13 fertige, und deine eigenen): eine Textdatei, die sagt, woher ein Wert kommt und wie er aussieht. Kein Programmcode. Siehe Kapitel 6 und 7.
  - **Eigenes Programm**: dein eigenes Skript oder Programm schreibt eine kleine JSON-Datei; TaskbarStats zeigt sie an. Siehe Kapitel 11.
- **Du genehmigst, was ein Rezept darf.** Bevor ein Rezept (oder das Script-Plugin) läuft, siehst du eine Liste: welche Internetadressen es kontaktiert, welche Dateien es liest, welche Befehle es startet, welche Geheimnisse es nutzt. Es läuft erst, nachdem du auf *Erlauben* drückst. Änderst du es später so, dass es mehr kann, wirst du erneut gefragt.
- **Sicher entworfen.** TaskbarStats läuft als Administrator und führt deshalb nie selbst Plugin-Code aus. Plugins laufen in einem separaten Hilfsprogramm (`TaskbarStats.Plugins.exe`) **ohne Administratorrechte**. Das Hilfsprogramm schreibt pro Plugin eine kleine Ergebnisdatei; TaskbarStats liest diese Dateien nur.

## 2. Schnellstart

1. *Einstellungen → Plugins → **Plugin hinzufügen…***
2. Wähle ein Plugin in der Liste. Rechts siehst du, was es tut, was du brauchst und (bei Rezepten), was du genehmigen musst.
3. Drücke **Hinzufügen**. Ein Rezept öffnet sich im Editor, damit du einträgst, was nötig ist (eine Adresse, einen Namen).
4. Drücke **Speichern und testen**. Braucht das Rezept eine Erlaubnis, siehst du das Genehmigungsfenster; lies es und wähle *Erlauben*. Kurz darauf erscheint das Ergebnis unten im Editor.
5. Hake das Plugin in der Liste an, um es einzuschalten. Seine Werte erscheinen im Widget (und in der Kachel *Plugins* von Dashboard und Vollbild).
6. Unter *Werte* wählst du pro Wert: anzeigen oder nicht, Text / Balken / Ring und eine Farbe.

Das Plugin-Hilfsprogramm muss installiert sein: Hake im Setup *Optionale Plugins* an (still: `/TASKS="plugins"`).

## 3. Wo alles liegt

Alles liegt in deinem Datenordner, normalerweise `%AppData%\TaskbarStats` (mit `TASKBARSTATS_DATA` oder einer portablen Installation liegt er woanders).

| Datei | Was |
|-------|-----|
| `plugins\<name>.ini` | Die Konfiguration eines Plugins. **Diese Datei ist die Registrierung**: keine Datei, kein Plugin. |
| `plugins\<name>.json` | Das letzte Ergebnis, geschrieben vom Hilfsprogramm (oder von deinem eigenen Programm). |
| `plugins\approved.json` | Welche Plugins du genehmigt hast, mit einer Prüfsumme dessen, was sie dürfen. |
| `plugins\secrets.dat` | Deine Geheimnisse, verschlüsselt für dein Windows-Konto (Kapitel 8). |
| `plugins\host.log` | Protokoll des Hilfsprogramms (`elevated=False` muss in der ersten Zeile stehen). |
| `plugin-alerts.json` | Welche Benachrichtigungen schon gezeigt wurden (Kapitel 9). |

## 4. Die Galerie

**Eingebaut**

| Plugin | Zeigt | Daten von |
|--------|-------|-----------|
| Claude-Nutzung | % deines 5-Stunden- und Wochenlimits, wann es zurückgesetzt wird | deiner Claude-Code-Anmeldung, an Anthropic gesendet. Gib einmalig `claude` und `/login` ein. |
| Wetter | Sonne/Wolken/Regen, Temperatur, Luftfeuchtigkeit, Wind, Regenwahrscheinlichkeit | Open-Meteo (kostenlos, ohne Schlüssel) |
| Standort (GPS) | Ort, Koordinaten, Genauigkeit | Windows-Standortdienst, sonst deine IP-Adresse |
| Uptime-Monitor | Webseiten (HTTP), Ports (TCP), Geräte (Ping) | deine eigene Liste |
| Script / REST | ein Wert pro `[Abschnitt]` aus einem Befehl, einer Datei oder Webadresse | du selbst. Braucht Genehmigung. |

**Rezepte** (fertig; jedes beginnt ausgeschaltet und ist leicht anzupassen)

| Rezept | Zeigt | Nötig |
|--------|-------|-------|
| Claude-Nutzung (anpassbares Rezept) | dieselben Zahlen wie das eingebaute Plugin, als Rezept geschrieben | einmalig `claude` + `/login` |
| Pi-hole | % blockierter DNS-Anfragen, Blockierung an/aus | Pi-hole v5 |
| Home-Assistant-Sensor | der Zustand einer Entität | ein langlebiges Token, gespeichert als Geheimnis `homeassistant` |
| GitHub-Repository | Sterne und offene Issues | nichts |
| Krypto-Kurs | Kurs und Änderung in 24 Std. (CoinGecko) | nichts |
| Wechselkurs | EUR in USD (EZB über frankfurter.dev) | nichts |
| Statusseite eines Dienstes | OK / kleine / große Störung von GitHub, Claude, Cloudflare, … | nichts |
| Luftqualität | Europäischer Luftqualitätsindex und PM2.5 (Open-Meteo) | deine Koordinaten |
| Docker-Container aktiv | Anzahl laufender Container | Docker; **startet einen Befehl** |
| Freier Speicherplatz | GB frei auf einem Laufwerk | **startet einen PowerShell-Befehl** |
| Wert aus einer Textdatei | eine Zahl aus einer Datei, die ein anderes Programm pflegt | der Pfad; **liest eine Datei** |
| Leeres Rezept: Webadresse (JSON) | Ausgangspunkt für dein eigenes Rezept | eine Adresse |
| Eigenes Skript schreibt die Werte | registriert eine Quelle für dein eigenes Programm (Kapitel 11) | dein Programm |

Du kannst ein Rezept auch aus einer `.ini`-Datei **importieren**. Importiere nur Rezepte aus einer Quelle, der du vertraust; es tut nichts, bis du gelesen und genehmigt hast, was es darf.

## 5. Was ein Plugin darf, und die Genehmigung

Ein Rezept kann nur, was auf seiner Liste steht. Die Liste wird aus der Datei abgeleitet:

| Eintrag | Stammt von | Angezeigt als |
|---------|-----------|---------------|
| Internetadresse (Host) | jede `url = …` | *Verbindet sich mit der Internetadresse: api.example.com* |
| Datei | `file = …` und `{file:…}` | *Liest die Datei: …* |
| Befehl | `cmd = …` | ***STARTET** diesen Befehl auf deinem PC* (rot) |
| Geheimnis | `{secret:name}` | *Verwendet das gespeicherte Geheimnis: name* |
| Umgebungsvariable | `{env:NAME}` | *Liest die Umgebungsvariable: NAME* |

- Die Prüfsumme gilt für diese Liste, **nicht** für Formeln oder Texte. Ein Label oder eine Formel zu ändern fragt nie erneut; ein anderer Host, eine Datei, ein Befehl oder ein Geheimnis zusätzlich schon.
- Eine Webadresse darf nie zu einem anderen als den genannten Hosts gehen: Eine Variable kann den Host nicht ändern, und eine Weiterleitung zu einem anderen Host wird abgelehnt (das Plugin nennt die neue Adresse). Geheimnisse gehen also nie irgendwohin, wofür du keine Erlaubnis gegeben hast.
- Rezepte sind **keine Programme**. Die Formelsprache (Kapitel 7) kann nur mit den Daten rechnen, die das Rezept abgerufen hat. Sie kann keine Dateien lesen, kein Netzwerk nutzen und keine Befehle starten. Das können nur `cmd =` und `file =`, und diese genehmigst du ausdrücklich.
- Ein Rezept, das einen Befehl startet, tut das als **du selbst, ohne Administratorrechte**, wie jedes Programm, das du selbst startest. Genehmige Befehle nur, wenn du sie verstehst.
- Geheimnisse erscheinen nie in Protokollen, Fehlermeldungen oder Ergebnisdateien.
- Solange nicht genehmigt, zeigt das Plugin *Braucht deine Genehmigung* und tut nichts. *Prüfen und genehmigen…* öffnet die Liste erneut.

## 6. Ein Rezept schreiben

Ein Rezept ist eine Textdatei `plugins\<name>.ini` (öffne sie mit *Konfiguration bearbeiten…*; Kommentare beginnen mit `#`). Über dem ersten `[Abschnitt]` sagst du, woher die Daten kommen; jeder `[Abschnitt]` darunter ist ein Wert.

**In der App:** *Einstellungen → Plugins → Hilfe und Beispiele…* (auch eine Schaltfläche im Editor) zeigt, wie es funktioniert, vollständige Rezepte (die Antwort, das Rezept und was du siehst) und eine Liste von Formeln mit ihrem Ergebnis. Du kannst ein Beispiel kopieren oder direkt in den Editor einfügen.

```ini
type = recipe
title = Pi-hole
enabled = true
interval = 60

url = http://pi.hole/admin/api.php?summaryRaw

[blocked]
label = Pi-hole heute blockiert
short = Ads
value = ads_percentage_today
unit = %
format = {value:0.0}%
detail = {ads_blocked_today:#,##0} von {dns_queries_today:#,##0} Anfragen blockiert
warn = 30
```

### Über dem ersten Abschnitt

| Schlüssel | Bedeutung |
|-----------|-----------|
| `type = recipe` | Pflicht. (`external` = dein eigenes Programm schreibt das Ergebnis, Kapitel 11.) |
| `title` | Name in den Einstellungen und im Tooltip. |
| `description` | Eine Zeile in den Einstellungen (optional). |
| `enabled` | `true` / `false`. Ein neues Plugin beginnt mit `false`. |
| `interval` | Sekunden zwischen Aktualisierungen (10 bis 86400; Standard 60). Nach Fehlern wartet das Hilfsprogramm länger, 30 s verdoppelnd, nie mehr als das Intervall. |
| `timeout` | Sekunden, bevor es aufgibt (Standard 25). |
| `url` | Eine Webadresse (`http` oder `https`). |
| `method` | `GET` (Standard) oder `POST`. |
| `body`, `content_type` | Für `POST`. Standard-Inhaltstyp `application/json`. |
| `header` | `Name: Wert`; wiederhole die Zeile für weitere Header. |
| `file` | Eine Datei, die statt einer Webadresse gelesen wird (`%USERPROFILE%` usw. funktioniert). |
| `cmd` | Ein Befehl, der stattdessen gestartet wird (seine Ausgabe sind die Daten). |
| `parse` | `auto` (Standard), `json`, `text`, `lines` oder `csv`. `auto` = JSON, wenn es mit `{` oder `[` beginnt, sonst Text. |
| `csv_header`, `csv_separator` | Für `csv`: Die erste Zeile enthält die Namen (`rows` werden Objekte); Trennzeichen (Standard `,`). |
| `help` | Ein Satz, der Fehlermeldungen angehängt wird ("Einmalig anmelden: claude und /login eingeben"). |

Nutze eine Quelle pro Rezept (`url`, `file` oder `cmd`). Brauchst du zwei? Mach zwei Rezepte.

In `url`, `header`, `body` und `file` kannst du **Platzhalter** verwenden: `{secret:name}` (ein gespeichertes Geheimnis), `{file:Pfad#Feld}` (ein Feld aus einer JSON-Datei, zum Beispiel `{file:~/.claude/.credentials.json#claudeAiOauth.accessToken}`; `~` ist dein Benutzerordner) und `{env:NAME}` (eine Umgebungsvariable). Jeder steht in der Genehmigungsliste.

### Jeder `[Abschnitt]` ist ein Wert

| Schlüssel | Bedeutung |
|-----------|-----------|
| `label` | Vollständiger Name (Tooltip, Einstellungen). Darf `{…}`-Formeln enthalten. Standard: der Abschnittsname. |
| `short` | Kurzname im Widget; leer = kein Label. |
| `value` | Eine Formel (Kapitel 7). Eine Zahl ergibt Balken/Ring/Schwellen; Text wird als Text angezeigt. |
| `format` / `text` | Der Text im Widget, mit `{Formel:Format}`-Teilen. Standard: der Wert (mit Einheit). |
| `detail` | Tooltip-Zeile(n), gleiche Schreibweise. Standard: `label: Text`. |
| `unit`, `min`, `max` | Einheit und der Bereich für Balken und Ringe (Standard 0 bis 100). |
| `warn`, `crit`, `invert` | Farbschwellen für die Zahl. `invert = true` heißt, dass *niedrig* schlecht ist (freier Platz). |
| `level` | Deine eigene Formel, die `'ok'`, `'warn'` oder `'crit'` ergibt. Ersetzt `warn`/`crit`. |
| `color` | Ein Farbhinweis `#RRGGBB`. Deine eigene Wahl in den Einstellungen hat Vorrang. |
| `when` | Eine Formel; der Wert wird ausgeblendet, wenn sie falsch ist. |
| `widget` | `false` = nur im Tooltip, nicht im Widget (in den Einstellungen kannst du es trotzdem wählen). |
| `notify` | `true` = Benachrichtigung, wenn die Stufe `crit` wird (Kapitel 9). |
| `reset` | Eine Formel, die eine Zeit ergibt, zum Beispiel `five_hour.resets_at`: der Moment, in dem der Wert von selbst zurückgesetzt wird (Kapitel 9). |

### Die Daten, die du in Formeln verwenden kannst

- Ist die Antwort ein JSON-Objekt, sind seine **Schlüssel Namen**: `ads_percentage_today`, `rates.USD`, `items[0].price`, `a["Schlüssel mit Leerzeichen"]`. `root` ist die ganze Antwort (nutze `root[0].name`, wenn die Antwort eine Liste ist).
- `text` der Rohtext, `lines` die nicht leeren Zeilen, `rows` die CSV-Zeilen, `http_status` der HTTP-Status, `elapsed_ms` die Dauer der Anfrage.
- `now` die aktuelle Zeit in Unix-Sekunden. In `format`, `detail`, `level`, `color`: `value` (dieser Wert) und `text` (sein Text).
- Fehlende Daten sind `null` und nie ein Fehler: `a.b.c` ist `null`, wenn `b` fehlt. Rechnen mit `null` ergibt `null`; das Widget zeigt dann `?`.

## 7. Die Formelsprache

Formeln sind kleine Ausdrücke. Sie können nur rechnen; sie berühren keine Dateien, kein Netzwerk und keine Befehle.

**Syntax:** Zahlen `12.5`, Text `'ok'` oder `"ok"`, `true false null`, Operatoren `+ - * / %`, Vergleich `== != < <= > >=`, Logik `&& || !`, Auswahl `a ? b : c`, Klammern, Namen `price`, Pfade `a.b`, `a[0]`, `a['Schlüssel']`, Funktionsaufrufe `round(x, 1)`.

- Text wird **ohne Beachtung der Groß-/Kleinschreibung** verglichen: `'OK' == 'ok'`. Text, der eine Zahl ist, wird als Zahl verglichen: `'10' > '9'`.
- `+` verbindet Text, wenn eine Seite Text ist: `'n=' + 5`.
- Backslashes in Text bleiben, außer `\\`, `\'`, `\"`, `\n`, `\t`. So funktionieren Muster: `regex(text, '(\d+)')`.
- Grenzen: 2000 Zeichen, 600 Teile und 20 000 Schritte pro Formel; Muster brechen nach 0,25 s ab.

**Funktionen**

| Gruppe | Funktionen |
|--------|-----------|
| Zahlen | `abs(x)` `round(x, Nachkommastellen)` `floor(x)` `ceil(x)` `sqrt(x)` `pow(x, y)` `clamp(x, unten, oben)` `pct(Teil, Ganzes)` `min(...)` `max(...)` `sum(Liste)` `avg(Liste)` `num(x)` (Text zu Zahl, verträgt `12,5%`) |
| Logik | `if(Bedingung, a, b)` `coalesce(a, b, ...)` (der erste, der nicht null oder leer ist) `isnull(x)` |
| Text | `str(x)` `len(x)` `upper` `lower` `trim` `contains(s, Teil)` `startswith` `endswith` `replace(s, a, b)` `substr(s, Start, Länge)` `split(s, Trenner)` `join(Liste, Trenner)` `matches(s, Muster)` `regex(s, Muster, Gruppe)` (Gruppe 1, wenn das Muster eine hat, sonst 0) |
| Listen | `count(Liste)` `first(Liste)` `last(Liste)` `pluck(Liste, 'Schlüssel')` `where(Liste, 'Schlüssel', Wert)` `json(Text)` (als Text gespeichertes JSON lesen) |
| Zeit | `time(x)` (ISO-Text oder Sekunden/Millisekunden zu Sekunden) `until(t)` `ago(t)` `duration(Sekunden)` (`2h 10m`) `clock(t, 'ddd HH:mm')` (Ortszeit) |
| Größe | `bytes(n)` (`1.5 GB`) |

**Texte mit Formeln** (`format`, `detail`, `label`, …): `{Formel}` oder `{Formel:Format}`; `{{` und `}}` sind einzelne Klammern.

| Format | Ergibt für 62,5 |
|--------|-----------------|
| `{value}` | `62.5` |
| `{value:0}` | `63` (.5 wird aufgerundet) |
| `{value:0.0}` | `62.5` |
| `{value:+0.0}` | `+62.5` |
| `{value:#,##0}` | `63` (und `1,234,568` für 1234567,8) |
| `{value:0%}` | `6250%` (ein `%` im Format multipliziert mit 100; schreibe `{value:0}%`, um nur ein Zeichen anzuhängen) |

Auswahl funktioniert in Texten: `{status == 'enabled' ? 'on' : 'OFF'}`.

## 8. Geheimnisse

Ein API-Schlüssel oder Token gehört nie in ein Rezept (Rezepte werden weitergegeben). Speichere es einmal: *Einstellungen → Plugins → **Geheimnisse…** → Hinzufügen oder ersetzen…* (ein Name wie `homeassistant` und der Wert). Im Rezept schreibst du `{secret:homeassistant}`:

```ini
header = Authorization: Bearer {secret:homeassistant}
```

- Geheimnisse liegen in `plugins\secrets.dat`, verschlüsselt mit dem Windows-Datenschutz für dein Konto auf diesem PC. Die Datei ist auf einem anderen PC oder für einen anderen Benutzer nutzlos.
- Die Einstellungen zeigen einen gespeicherten Wert nie wieder; du kannst ihn ersetzen oder entfernen.
- Ein Plugin zu entfernen lässt seine Geheimnisse bestehen (ein anderes Rezept kann sie nutzen).
- Ein fehlendes Geheimnis gibt eine klare Meldung: *Das Geheimnis 'x' ist nicht gesetzt. Füge es unter Einstellungen > Plugins > Geheimnisse hinzu.*

## 9. Benachrichtigungen

- **Eine Benachrichtigung pro Problem.** Wird ein Wert mit `notify = true` *kritisch*, erhältst du eine Benachrichtigung. Solange der Zustand gleich bleibt, kommt keine zweite, auch nicht nach einem Neustart oder Update (das wird gemerkt). Ein neues Problem (eine zweite Seite fällt aus) oder eine Erholung gefolgt von einem neuen Problem ergibt eine neue Benachrichtigung.
- **Unveränderte Warnung wiederholen** (*Einstellungen → Plugins → Plugin-Benachrichtigungen*): *Nur bei Änderung* (Standard), oder nach 30 Min., 1 Std. oder 4 Std.
- **Kurz vor dem Zurücksetzen eines Limits melden** (Standard 5 Minuten, oder *Aus*, 2, 10, 15): Bei einem Wert mit einer `reset`-Zeit, der auf *Warnung* oder höher steht, erhältst du kurz davor eine Benachrichtigung ("Claude-5-Stunden-Limit: fast zurückgesetzt, in etwa 4 Min. wieder frei"). Eine pro Rücksetzzeitpunkt. Das eingebaute Claude-Plugin und das Claude-Rezept liefern die Rücksetzzeit jedes Limits.
- Benachrichtigungen müssen aktiv sein (*Einstellungen → Allgemein*), und während einer Themenvorschau wird nichts gezeigt.

## 10. Ausgearbeitete Beispiele

**Claude-Nutzung als Rezept** (dieselben Zahlen wie das eingebaute Plugin; Vorlage *Claude-Nutzung (anpassbares Rezept)*):

```ini
type = recipe
title = Claude usage
interval = 300
help = Einmalig anmelden: In einem Terminal 'claude' und /login eingeben.
url = https://api.anthropic.com/api/oauth/usage
header = Authorization: Bearer {file:~/.claude/.credentials.json#claudeAiOauth.accessToken}
header = anthropic-beta: oauth-2025-04-20

[five_hour]
label = Claude 5-Stunden-Limit
short = 5h
when = five_hour != null
value = clamp(coalesce(five_hour.utilization, 0), 0, 100)
unit = %
format = {value:0}%
detail = {value:0}% verbraucht{five_hour.resets_at ? ', wieder frei in ' + until(five_hour.resets_at) : ''}
warn = 70
crit = 90
notify = true
reset = five_hour.resets_at
```

(Die Webadresse ist von Anthropic nicht dokumentiert und kann sich ändern. Ein Token von `claude setup-token` funktioniert **nicht**: Der Server lehnt es ab, weil ihm die Berechtigung `user:profile` fehlt. Melde dich mit `claude` und `/login` an.)

**Home Assistant** (Token als Geheimnis `homeassistant` gespeichert):

```ini
type = recipe
title = Home Assistant
url = http://homeassistant.local:8123/api/states/sensor.wohnzimmer_temperatur
header = Authorization: Bearer {secret:homeassistant}

[sensor]
label = {attributes.friendly_name}
value = state
unit = °C
format = {value:0.0}°
detail = geändert vor {ago(last_changed)}
```

**Anzahl der Docker-Container** (startet einen Befehl; du genehmigst das):

```ini
type = recipe
title = Docker
cmd = docker ps -q

[running]
label = Docker-Container
value = count(lines)
format = {value}
```

**Freier Speicherplatz** mit Farben (niedrig ist schlecht):

```ini
type = recipe
title = Speicherplatz
interval = 300
cmd = powershell -NoProfile -Command "[math]::Round((Get-PSDrive C).Free / 1GB, 1)"

[free]
label = Freier Platz auf C:
value = num(text)
format = {value:0} GB
invert = true
warn = 50
crit = 20
notify = true
```

## 11. Dein eigenes Programm schreibt die Werte

Nutze die Vorlage *Eigenes Skript schreibt die Werte (JSON-Datei)*. Sie registriert eine Quelle mit dem Namen der `.ini`-Datei; TaskbarStats startet nichts. Dein Skript (PowerShell, Python, eine geplante Aufgabe, irgendwas) schreibt `plugins\<name>.json`:

```json
{
  "plugin": "<name>", "title": "Mein Skript", "updated": "2026-01-01T12:00:00Z", "ttl": 300, "status": "ok",
  "message": "",
  "items": [
    { "key": "temp", "label": "Raumtemperatur", "short": "Raum", "value": 21.5, "min": 0, "max": 40,
      "unit": "°C", "text": "21.5°", "detail": "Wohnzimmer", "level": "ok", "color": "#34C759",
      "widget": true, "notify": false, "reset": "2026-01-01T18:00:00Z" }
  ]
}
```

Nur `items[].key` ist Pflicht. `ttl` ist, wie viele Sekunden die Werte frisch bleiben; danach werden sie ausgegraut mit einem `?` angezeigt. `status: "error"` plus eine `message` zeigt das Problem in den Einstellungen. Schreibe die Datei in einem Zug (eine temporäre Datei schreiben, dann umbenennen). Eine JSON-Datei ohne ihre `.ini`-Registrierung wird ignoriert.

## 12. Fehlersuche

| Du siehst | Tu |
|-----------|----|
| *Braucht deine Genehmigung…* | *Prüfen und genehmigen…* (oder *Speichern und testen* im Editor). |
| *Das Geheimnis 'x' ist nicht gesetzt* | Einstellungen → Plugins → Geheimnisse → Hinzufügen oder ersetzen. |
| *Die Antwort ist kein JSON* | Setze `parse = text` (oder `lines` / `csv`) im Rezept. |
| *Der Server leitet zu einem anderen Host weiter* | Trage die genannte Adresse in `url =` ein (und genehmige erneut). |
| *HTTP 401 / 403* | Falscher oder fehlender Schlüssel. Für Claude: `claude`, dann `/login`. |
| *Rate limited* | Der Dienst bittet dich zu bremsen; das Plugin wartet von selbst. Nutze ein längeres `interval`. |
| Ein Wert zeigt `?` | Seine Formel hat nichts ergeben (fehlende Daten oder ein Fehler: fahre mit der Maus darüber oder lies die Meldung in den Einstellungen). |
| *Warte auf das Plugin-Hilfsprogramm* | Das Hilfsprogramm startet von selbst; nutze *Helfer starten* oder lies *Protokoll öffnen*. Nicht installiert? Führe das Setup aus und hake *Optionale Plugins* an. |
| Gar nichts | Ist das Plugin angehakt, und ist *Plugin-Werte im Widget anzeigen* an? |

## 13. Grenzen

Rezepte lesen eine Quelle, werten Formeln aus und zeigen Werte an. Sie können sich nicht zuerst anmelden und dann abrufen (eine Anmeldung, die einen Schlüssel zurückgibt, der danach mitgeschickt werden muss), nicht über eine Liste laufen, um pro Element einen Wert zu bilden, und keinen Zustand zwischen Aktualisierungen behalten. Schreibe dafür ein eigenes Programm (Kapitel 11) oder nutze das Plugin *Script / REST*.
