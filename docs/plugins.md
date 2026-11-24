# Plugins: handbook

[Nederlands](plugins.nl.md) · [Deutsch](plugins.de.md) · [Back to the README](../README.md)

Plugins add extra values to the widget, the desktop dashboard and the full-screen view: your Claude usage, the weather, the status of a website, a price, a value from your home automation, anything you can read from a web address, a file or a command.

This handbook explains how to **add**, **approve** and **remove** plugins, what they are **allowed to do**, and how to **write your own recipe** without writing any program.

## 1. The idea in one minute

- **Nothing is there until you add it.** Plugins you have not added are not shown anywhere: not in the settings, not in the widget. Open *Settings → Plugins → Add plugin…*, pick one from the gallery and it is added (as a small `.ini` file in your data folder). *Remove…* deletes it again.
- **Three kinds of plugin:**
  - **Built in** (5): Claude usage, Weather, Location (GPS), Uptime monitor, Script / REST.
  - **Recipe** (13 ready-made, and your own): a text file that says where to read a value and how to show it. No program code. See sections 6 and 7.
  - **Own program**: your own script or program writes a small JSON file; TaskbarStats shows it. See section 11.
- **You approve what a recipe may do.** Before a recipe (or the Script plugin) runs, you see a list: which internet addresses it contacts, which files it reads, which commands it runs, which secrets it uses. It runs only after you press *Allow*. If you later change it so it can do more, you are asked again.
- **Safe by design.** TaskbarStats runs as administrator, so it never runs plugin code itself. Plugins run in a separate helper (`TaskbarStats.Plugins.exe`) **without administrator rights**. The helper writes one small result file per plugin; TaskbarStats only reads those files.

## 2. Quick start

1. *Settings → Plugins → **Add plugin…***
2. Pick a plugin in the list. On the right you see what it does, what you need, and (for recipes) what you will have to approve.
3. Press **Add**. A recipe opens in the editor so you can fill in what is needed (an address, a name).
4. Press **Save and test**. If the recipe needs permission you see the approval window; read it and choose *Allow*. A moment later the result appears at the bottom of the editor.
5. Tick the plugin in the list to switch it on. Its values show up in the widget (and in the dashboard and full-screen *Plugins* tile).
6. Under *Values* you choose per value: show it or not, text / bar / ring, and a colour.

The plugin helper must be installed: tick *Optional plugins* in Setup (silent: `/TASKS="plugins"`).

## 3. Where everything lives

Everything is in your data folder, normally `%AppData%\TaskbarStats` (with `TASKBARSTATS_DATA` or a portable install it is elsewhere).

| File | What |
|------|------|
| `plugins\<name>.ini` | The configuration of a plugin. **This file is the registration**: no file, no plugin. |
| `plugins\<name>.json` | The latest result, written by the helper (or by your own program). |
| `plugins\approved.json` | Which plugins you approved, with a checksum of what they may do. |
| `plugins\secrets.dat` | Your secrets, encrypted for your Windows account (section 8). |
| `plugins\host.log` | Log of the helper (`elevated=False` must be in the first line). |
| `plugin-alerts.json` | Which notifications were already shown (section 9). |

## 4. The gallery

**Built in**

| Plugin | Shows | Data from |
|--------|-------|-----------|
| Claude usage | % of your 5-hour and weekly limit, when it resets | your Claude Code sign-in, sent to Anthropic. Run `claude` and type `/login` once. |
| Weather | sun/clouds/rain, temperature, humidity, wind, chance of rain | Open-Meteo (free, no key) |
| Location (GPS) | place, coordinates, accuracy | Windows location service, else your IP address |
| Uptime monitor | websites (HTTP), ports (TCP), devices (ping) | your own list |
| Script / REST | one value per `[section]` from a command, file or web address | you. Needs approval. |

**Recipes** (ready-made; each starts switched off and is easy to edit)

| Recipe | Shows | Needs |
|--------|-------|-------|
| Claude usage (editable recipe) | the same numbers as the built-in plugin, written as a recipe | `claude` + `/login` once |
| Pi-hole | % of DNS queries blocked, blocking on/off | Pi-hole v5 |
| Home Assistant sensor | the state of one entity | a long-lived token saved as secret `homeassistant` |
| GitHub repository | stars and open issues | nothing |
| Crypto price | price and 24 h change (CoinGecko) | nothing |
| Exchange rate | EUR to USD (ECB via frankfurter.dev) | nothing |
| Service status page | OK / minor / major outage of GitHub, Claude, Cloudflare, … | nothing |
| Air quality | European air quality index and PM2.5 (Open-Meteo) | your coordinates |
| Docker containers running | number of running containers | Docker; **runs a command** |
| Free disk space | GB free on a drive | **runs a PowerShell command** |
| Value from a text file | a number from a file another program keeps up to date | the path; **reads a file** |
| Blank recipe: web address (JSON) | starting point for your own recipe | an address |
| Own script writes the values | registers a source for your own program (section 11) | your program |

You can also **import** a recipe from a `.ini` file. Only import recipes from a source you trust; it does nothing until you read and approve what it may do.

## 5. What a plugin may do, and the approval

A recipe can only do what is on its list. The list is worked out from the file:

| Entry | Comes from | Shown as |
|-------|-----------|----------|
| Internet address (host) | every `url = …` | *Connects to the internet address: api.example.com* |
| File | `file = …` and `{file:…}` | *Reads the file: …* |
| Command | `cmd = …` | ***RUNS** this command on your pc* (in red) |
| Secret | `{secret:name}` | *Uses the saved secret: name* |
| Environment variable | `{env:NAME}` | *Reads the environment variable: NAME* |

- The checksum covers this list, **not** the formulas or texts. Changing a label or a formula never asks again; changing the host, adding a file, a command or a secret does.
- A web address may never go to a different host than the ones listed: a variable cannot change the host, and a redirect to another host is refused (the plugin tells you the new address). Secrets therefore never travel anywhere you did not approve.
- Recipes are **not programs**. The formula language (section 7) can only calculate with the data the recipe fetched. It cannot read files, use the network or run commands. The only things that can are `cmd =` and `file =`, and you approve those explicitly.
- A recipe that runs a command runs it as **you, without administrator rights**, like any program you start yourself. Approve commands only if you understand them.
- Secrets never appear in logs, error messages or result files.
- Until approved, the plugin shows *Needs your approval* and does nothing. *Review and approve…* opens the list again.

## 6. Writing a recipe

A recipe is a text file `plugins\<name>.ini` (open it with *Edit config…*; comments start with `#`). Above the first `[section]` you say where the data comes from; each `[section]` below is one value.

**In the app:** *Settings → Plugins → Help and examples…* (also a button in the editor) shows how it works, complete recipes (the reply, the recipe and what you see) and a list of formulas with what each one gives. You can copy an example or put it straight into the editor.

```ini
type = recipe
title = Pi-hole
enabled = true
interval = 60

url = http://pi.hole/admin/api.php?summaryRaw

[blocked]
label = Pi-hole blocked today
short = Ads
value = ads_percentage_today
unit = %
format = {value:0.0}%
detail = {ads_blocked_today:#,##0} of {dns_queries_today:#,##0} queries blocked
warn = 30
```

### Above the first section

| Key | Meaning |
|-----|---------|
| `type = recipe` | Required. (`external` = your own program writes the result, section 11.) |
| `title` | Name in the settings and tooltip. |
| `description` | One line shown in the settings (optional). |
| `enabled` | `true` / `false`. A new plugin starts `false`. |
| `interval` | Seconds between updates (10 to 86400; default 60). After errors the helper waits longer, 30 s doubling, never more than the interval. |
| `timeout` | Seconds before giving up (default 25). |
| `url` | A web address (`http` or `https`). |
| `method` | `GET` (default) or `POST`. |
| `body`, `content_type` | For `POST`. Default content type `application/json`. |
| `header` | `Name: value`; repeat the line for more headers. |
| `file` | A file to read instead of a web address (`%USERPROFILE%` etc. work). |
| `cmd` | A command to run instead (its output is the data). |
| `parse` | `auto` (default), `json`, `text`, `lines` or `csv`. `auto` = JSON if it starts with `{` or `[`, else text. |
| `csv_header`, `csv_separator` | For `csv`: first row holds the names (`rows` become objects); separator (default `,`). |
| `help` | A sentence added to error messages ("Sign in once: run claude and type /login"). |

Use one source per recipe (`url`, `file` or `cmd`). Need two? Make two recipes.

In `url`, `header`, `body` and `file` you can use **placeholders**: `{secret:name}` (a saved secret), `{file:path#field}` (a field from a JSON file, for example `{file:~/.claude/.credentials.json#claudeAiOauth.accessToken}`; `~` is your user folder) and `{env:NAME}` (an environment variable). Each one appears in the approval list.

### Each `[section]` is one value

| Key | Meaning |
|-----|---------|
| `label` | Full name (tooltip, settings). May contain `{…}` formulas. Default: the section name. |
| `short` | Short name in the widget; empty = no label. |
| `value` | A formula (section 7). A number gives a bar/ring/threshold; text is shown as text. |
| `format` / `text` | The text in the widget, with `{formula:format}` parts. Default: the value (with the unit). |
| `detail` | Tooltip line(s), same notation. Default: `label: text`. |
| `unit`, `min`, `max` | Unit, and the range for bars and rings (default 0 to 100). |
| `warn`, `crit`, `invert` | Colour thresholds on the number. `invert = true` means that *low* is bad (free space). |
| `level` | Your own formula giving `'ok'`, `'warn'` or `'crit'`. Replaces `warn`/`crit`. |
| `color` | A colour hint `#RRGGBB`. Your own choice in the settings wins. |
| `when` | A formula; the value is hidden when it is false. |
| `widget` | `false` = only in the tooltip, not in the widget (you can still choose in the settings). |
| `notify` | `true` = notification when the level becomes `crit` (section 9). |
| `reset` | A formula giving a time, for example `five_hour.resets_at`: the moment the value resets by itself (section 9). |

### The data you can use in formulas

- If the reply is a JSON object, its **keys are names**: `ads_percentage_today`, `rates.USD`, `items[0].price`, `a["key with spaces"]`. `root` is the whole reply (use `root[0].name` when the reply is a list).
- `text` the raw text, `lines` the non-empty lines, `rows` the CSV rows, `http_status` the HTTP status, `elapsed_ms` the request time.
- `now` the current time as Unix seconds. In `format`, `detail`, `level`, `color`: `value` (this value) and `text` (its text).
- Missing data is `null` and never an error: `a.b.c` is `null` when `b` is missing. Calculating with `null` gives `null`; the widget then shows `?`.

## 7. The formula language

Formulas are small expressions. They can only calculate; they cannot touch files, the network or commands.

**Syntax:** numbers `12.5`, text `'ok'` or `"ok"`, `true false null`, operators `+ - * / %`, comparison `== != < <= > >=`, logic `&& || !`, choice `a ? b : c`, brackets, names `price`, paths `a.b`, `a[0]`, `a['key']`, function calls `round(x, 1)`.

- Text compares **without caring about capitals**: `'OK' == 'ok'`. Text that is a number compares as a number: `'10' > '9'`.
- `+` joins text when one side is text: `'n=' + 5`.
- Backslashes in text stay as they are, except `\\`, `\'`, `\"`, `\n`, `\t`. So patterns work: `regex(text, '(\d+)')`.
- Limits: 2000 characters, 600 parts and 20 000 steps per formula; patterns time out after 0.25 s.

**Functions**

| Group | Functions |
|-------|-----------|
| Numbers | `abs(x)` `round(x, decimals)` `floor(x)` `ceil(x)` `sqrt(x)` `pow(x, y)` `clamp(x, lo, hi)` `pct(part, whole)` `min(...)` `max(...)` `sum(list)` `avg(list)` `num(x)` (text to number, tolerates `12,5%`) |
| Logic | `if(cond, a, b)` `coalesce(a, b, ...)` (first that is not null or empty) `isnull(x)` |
| Text | `str(x)` `len(x)` `upper` `lower` `trim` `contains(s, part)` `startswith` `endswith` `replace(s, a, b)` `substr(s, start, length)` `split(s, sep)` `join(list, sep)` `matches(s, pattern)` `regex(s, pattern, group)` (group 1 if the pattern has one, else 0) |
| Lists | `count(list)` `first(list)` `last(list)` `pluck(list, 'key')` `where(list, 'key', value)` `json(text)` (read JSON stored as text) |
| Time | `time(x)` (ISO text or seconds/milliseconds to seconds) `until(t)` `ago(t)` `duration(seconds)` (`2h 10m`) `clock(t, 'ddd HH:mm')` (local time) |
| Size | `bytes(n)` (`1.5 GB`) |

**Texts with formulas** (`format`, `detail`, `label`, …): `{formula}` or `{formula:format}`; `{{` and `}}` are literal braces.

| Format | Gives for 62.5 |
|--------|----------------|
| `{value}` | `62.5` |
| `{value:0}` | `63` (rounds .5 up) |
| `{value:0.0}` | `62.5` |
| `{value:+0.0}` | `+62.5` |
| `{value:#,##0}` | `63` (and `1,234,568` for 1234567.8) |
| `{value:0%}` | `6250%` (a `%` in the format multiplies by 100; write `{value:0}%` to just add a sign) |

Choices work inside texts: `{status == 'enabled' ? 'on' : 'OFF'}`.

## 8. Secrets

An API key or token never belongs in a recipe (recipes get shared). Save it once: *Settings → Plugins → **Secrets…** → Add or replace…* (a name such as `homeassistant` and the value). In the recipe write `{secret:homeassistant}`:

```ini
header = Authorization: Bearer {secret:homeassistant}
```

- Secrets are stored in `plugins\secrets.dat`, encrypted with Windows data protection for your account on this pc. The file is useless on another pc or for another user.
- The settings never show a saved value again; you can replace or remove it.
- Removing a plugin keeps its secrets (another recipe may use them).
- A missing secret gives a clear message: *The secret 'x' is not set. Add it under Settings > Plugins > Secrets.*

## 9. Notifications

- **One notification per problem.** When a value with `notify = true` turns *critical*, you get one notification. As long as the state stays the same you get no second one, also not after a restart or an update (this is remembered). A new problem (a second site goes down) or a recovery followed by a new problem gives a new notification.
- **Repeat an unchanged warning** (*Settings → Plugins → Plugin notifications*): *Only when it changes* (default), or after 30 min, 1 h or 4 h.
- **Tell me before a limit is reset** (default 5 minutes, or *Off*, 2, 10, 15): for a value with a `reset` time that is at *warning* or higher, you get one notification shortly before it resets ("Claude 5-hour limit: almost reset, resets in about 4 min"). One per reset moment. The built-in Claude plugin and the Claude recipe provide the reset time of each limit.
- Notifications must be on (*Settings → General*), and nothing is shown while a theme preview runs.

## 10. Worked examples

**Claude usage as a recipe** (the same numbers as the built-in plugin; template *Claude usage (editable recipe)*):

```ini
type = recipe
title = Claude usage
interval = 300
help = Sign in once: run 'claude' in a terminal and type /login.
url = https://api.anthropic.com/api/oauth/usage
header = Authorization: Bearer {file:~/.claude/.credentials.json#claudeAiOauth.accessToken}
header = anthropic-beta: oauth-2025-04-20

[five_hour]
label = Claude 5-hour limit
short = 5h
when = five_hour != null
value = clamp(coalesce(five_hour.utilization, 0), 0, 100)
unit = %
format = {value:0}%
detail = {value:0}% used{five_hour.resets_at ? ', free again in ' + until(five_hour.resets_at) : ''}
warn = 70
crit = 90
notify = true
reset = five_hour.resets_at
```

(The web address is not documented by Anthropic and may change. A token from `claude setup-token` does **not** work: the server refuses it because it lacks the `user:profile` permission. Sign in with `claude` and `/login`.)

**Home Assistant** (token saved as secret `homeassistant`):

```ini
type = recipe
title = Home Assistant
url = http://homeassistant.local:8123/api/states/sensor.living_room_temperature
header = Authorization: Bearer {secret:homeassistant}

[sensor]
label = {attributes.friendly_name}
value = state
unit = °C
format = {value:0.0}°
detail = changed {ago(last_changed)} ago
```

**Number of Docker containers** (runs a command; you approve it):

```ini
type = recipe
title = Docker
cmd = docker ps -q

[running]
label = Docker containers
value = count(lines)
format = {value}
```

**Free disk space** with colours (low is bad):

```ini
type = recipe
title = Disk space
interval = 300
cmd = powershell -NoProfile -Command "[math]::Round((Get-PSDrive C).Free / 1GB, 1)"

[free]
label = Free space on C:
value = num(text)
format = {value:0} GB
invert = true
warn = 50
crit = 20
notify = true
```

## 11. Your own program writes the values

Use the template *Own script writes the values (JSON file)*. It registers a source named after the `.ini` file; TaskbarStats starts nothing. Your script (PowerShell, Python, a scheduled task, anything) writes `plugins\<name>.json`:

```json
{
  "plugin": "<name>", "title": "My script", "updated": "2026-01-01T12:00:00Z", "ttl": 300, "status": "ok",
  "message": "",
  "items": [
    { "key": "temp", "label": "Room temperature", "short": "Room", "value": 21.5, "min": 0, "max": 40,
      "unit": "°C", "text": "21.5°", "detail": "Living room", "level": "ok", "color": "#34C759",
      "widget": true, "notify": false, "reset": "2026-01-01T18:00:00Z" }
  ]
}
```

Only `items[].key` is required. `ttl` is how many seconds the values stay fresh; after that they are greyed out with a `?`. `status: "error"` plus a `message` shows the problem in the settings. Write the file in one go (write a temporary file, then rename it). A JSON file without its `.ini` registration is ignored.

## 12. Troubleshooting

| You see | Do |
|---------|----|
| *Needs your approval…* | *Review and approve…* (or *Save and test* in the editor). |
| *The secret 'x' is not set* | Settings → Plugins → Secrets → Add or replace. |
| *The reply is not JSON* | Add `parse = text` (or `lines` / `csv`) to the recipe. |
| *The server redirects to another host* | Put the address it names in `url =` (and approve again). |
| *HTTP 401 / 403* | Wrong or missing key. For Claude: `claude`, then `/login`. |
| *Rate limited* | The service asks you to slow down; the plugin waits by itself. Use a longer `interval`. |
| A value shows `?` | Its formula gave nothing (missing data, or an error: hover the value or read the message in the settings). |
| *Waiting for the plugin helper* | The helper starts by itself; use *Start helper*, or read *Open log*. Not installed? Run Setup and tick *Optional plugins*. |
| Nothing at all | Is the plugin ticked on, and is *Show plugin values in the widget* on? |

## 13. Limits

Recipes read one source, evaluate formulas and show values. They cannot log in first and then fetch (a login that returns a key that must be sent next), loop over a list to make one value per item, or keep state between updates. For that, write your own program (section 11) or use the *Script / REST* plugin.
