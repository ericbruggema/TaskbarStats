using System.Text;

namespace TaskbarStats;

/// <summary>De meegeleverde plugins (de host in <c>plugins\TaskbarStats.Plugins</c> voert ze uit; de app kent alleen hun id's en teksten).</summary>
public static class PluginCatalog
{
    public static readonly string[] Ids = { "claude", "weather", "location", "uptime", "script" };

    public static string TitleOf(string id) => id switch
    {
        "claude" => Loc.T("Claude usage"),
        "weather" => Loc.T("Weather"),
        "location" => Loc.T("Location (GPS)"),
        "uptime" => Loc.T("Uptime monitor"),
        "script" => Loc.T("Script / REST"),
        _ => PluginRegistry.IniTitle(id) ?? id,
    };

    public static string DescriptionOf(string id) => id switch
    {
        "claude" => Loc.T("Shows how much of your Claude 5-hour and weekly limit is used and when it resets. Needs a Claude Code sign-in (run 'claude' in a terminal and use /login). Uses an undocumented Anthropic address that may change."),
        "weather" => Loc.T("Current weather (sun, clouds, rain), temperature, humidity, wind and chance of rain for your GPS/Wi-Fi location or a place you choose."),
        "location" => Loc.T("Where you are: place name, coordinates and accuracy from the Windows location service (GPS or Wi-Fi) or your IP address."),
        "uptime" => Loc.T("Checks websites, ports and devices every minute and turns red (with a notification) when something is down."),
        "script" => Loc.T("Shows any value from a script, a text file or a web address (JSON), for example Home Assistant, a NAS or your own tool."),
        _ => "",   // recepten: zie PluginRegistry.DescriptionOf
    };

    public static bool IsBuiltIn(string id) => Array.IndexOf(Ids, id) >= 0;

    /// <summary>Sorteervolgorde: eerst de meegeleverde plugins in vaste volgorde, daarna eigen bestanden.</summary>
    public static int OrderOf(string id)
    {
        int i = Array.IndexOf(Ids, id);
        return i >= 0 ? i : 100;
    }
}

/// <summary>
/// De configbestanden van de plugins (<c>plugins\&lt;id&gt;.ini</c> in de datamap): aan/uit zetten, lezen, schrijven, controleren en de
/// standaardtekst terughalen. De plugin-host kijkt zelf naar de tijdstempel en past een wijziging binnen enkele seconden toe.
/// </summary>
public static class PluginConfig
{
    public static string Dir => Path.Combine(AppPaths.DataDir, "plugins");
    public static string PathOf(string id) => Path.Combine(Dir, id + ".ini");

    private static string DefaultsPath(string id)
    {
        var exe = AppPaths.ExeDir ?? AppContext.BaseDirectory;
        return Path.Combine(exe, "plugins", "defaults", id + ".ini");
    }

    /// <summary>De standaardtekst van het configbestand (naast de host meegeleverd); een minimale tekst als die ontbreekt.</summary>
    public static string DefaultText(string id)
    {
        try { if (File.Exists(DefaultsPath(id))) return File.ReadAllText(DefaultsPath(id), Encoding.UTF8); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Diag.Swallow(ex); }
        return $"# {PluginCatalog.TitleOf(id)}\r\nenabled = false\r\n";
    }

    /// <summary>De tekst van het configbestand; bestaat het nog niet, dan de standaardtekst.</summary>
    public static string Read(string id)
    {
        try { if (File.Exists(PathOf(id))) return File.ReadAllText(PathOf(id), Encoding.UTF8); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Diag.Swallow(ex); }
        return DefaultText(id);
    }

    // ---------- geheimen in de editor ----------
    // 'token =' (Claude-plugin) is een geheim: de editor toont het als stippen en bewaart het echte token zolang de stippen blijven staan.
    /// <summary>Wat de editor toont in plaats van het token.</summary>
    public const string Masked = "•••••••• (hidden, saved - type over it to replace, delete it to remove)";

    private static string[] SplitLines(string text) => text.Replace("\r\n", "\n").Split('\n');

    private static bool IsTokenLine(string line, out int valueStart)
    {
        valueStart = 0;
        var t = line.TrimStart();
        if (t.Length == 0 || t[0] is '#' or ';' or '[') return false;
        int eq = t.IndexOf('=');
        if (eq <= 0 || !t[..eq].Trim().Equals("token", StringComparison.OrdinalIgnoreCase)) return false;
        valueStart = line.Length - t.Length + eq + 1;
        return true;
    }

    /// <summary>De tekst voor de editor: een ingevuld token wordt <see cref="Masked"/>.</summary>
    public static string MaskSecrets(string text)
    {
        var lines = SplitLines(text);
        for (int i = 0; i < lines.Length; i++)
            if (IsTokenLine(lines[i], out int v) && lines[i][v..].Trim().Length > 0)
                lines[i] = lines[i][..v] + " " + Masked;
        return string.Join("\r\n", lines);
    }

    /// <summary>Zet bij het opslaan het echte token terug waar de editor nog de stippen toont (uit het huidige bestand).</summary>
    public static string RestoreSecrets(string edited, string original)
    {
        string orig = "";
        foreach (var l in SplitLines(original))
            if (IsTokenLine(l, out int ov) && l[ov..].Trim() is { Length: > 0 } val && val != Masked) { orig = val; break; }
        var lines = SplitLines(edited);
        for (int i = 0; i < lines.Length; i++)
            if (IsTokenLine(lines[i], out int v) && lines[i][v..].Trim() == Masked)
                lines[i] = lines[i][..v] + (orig.Length > 0 ? " " + orig : "");
        return string.Join("\r\n", lines);
    }

    // Zelfde tekst als onderaan plugins\...\defaults\claude.ini (hier vast, zodat het ook werkt als de defaults-map ontbreekt).
    private const string TokenSection =
        "# Alternative 1: paste an access token here. It needs the 'user:profile' permission, which only the normal\n" +
        "# Claude Code sign-in has (run 'claude', then /login). A token from 'claude setup-token' is REFUSED by the server\n" +
        "# (HTTP 403, 'scope requirement user:profile'), so for most people the sign-in file is the right choice.\n" +
        "# It is only ever sent to api.anthropic.com. It is kept in THIS file (in your own user folder), so do not share\n" +
        "# this file or a screenshot of it. When you open this editor again the token is hidden (shown as dots) and kept\n" +
        "# as long as you leave the dots in place; clear the line to remove the token. Leave empty to use the sign-in file.\n" +
        "# Order used: token here, then the CLAUDE_CODE_OAUTH_TOKEN environment variable, then the sign-in file above.\n" +
        "token =\n";

    /// <summary>Een bestaand claude.ini van vóór de 'token ='-optie krijgt het kopje en de lege regel erbij, zodat je het token kunt invullen.</summary>
    public static string WithTokenSection(string id, string text)
    {
        if (id != "claude") return text;
        foreach (var l in SplitLines(text)) if (IsTokenLine(l, out _)) return text;
        return text.TrimEnd('\r', '\n') + "\r\n\r\n" + TokenSection.Replace("\n", "\r\n");
    }

    public static void Write(string id, string text)
    {
        Directory.CreateDirectory(Dir);
        var tmp = PathOf(id) + ".tmp";
        File.WriteAllText(tmp, text, new UTF8Encoding(false));
        File.Move(tmp, PathOf(id), true);
    }

    /// <summary>null = in orde; anders een melding met het regelnummer (een regel is commentaar, <c>[sectie]</c> of <c>sleutel = waarde</c>).</summary>
    public static string? Validate(string? text)
    {
        int n = 0;
        foreach (var raw in (text ?? "").Replace("\r\n", "\n").Split('\n'))
        {
            n++;
            var line = raw.Trim().TrimStart('﻿');
            if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
            if (line[0] == '[') { if (line[^1] != ']') return Loc.T("Line {0}: a section must end with ]", n); continue; }
            if (line.IndexOf('=') <= 0) return Loc.T("Line {0}: expected 'key = value' (or start the line with # for a comment)", n);
        }
        return null;
    }

    /// <summary>Staat <c>enabled = true</c> in de config? (alleen de regels vóór de eerste [sectie] tellen)</summary>
    public static bool IsEnabled(string id) => IsEnabledIn(Read(id));

    internal static bool IsEnabledIn(string text)
    {
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim().TrimStart('﻿');
            if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
            if (line[0] == '[') return false;
            int eq = line.IndexOf('=');
            if (eq <= 0 || !line[..eq].Trim().Equals("enabled", StringComparison.OrdinalIgnoreCase)) continue;
            return line[(eq + 1)..].Trim().ToLowerInvariant() is "true" or "yes" or "on" or "1" or "ja" or "an";
        }
        return false;
    }

    /// <summary>Staat er een toegevoegde plugin aan die de host moet uitvoeren? (eigen bronnen, <c>type = external</c>, voert de host niet uit)</summary>
    public static bool AnyEnabled() => PluginRegistry.Entries().Any(e => PluginRegistry.TypeOfText(e.Id, e.Text) != "external" && IsEnabledIn(e.Text));

    public static void SetEnabled(string id, bool on) => Write(id, WithEnabled(Read(id), on));

    /// <summary>Zet de regel <c>enabled = …</c> (de eerste vóór een sectie) om; ontbreekt ze, dan komt ze bovenaan.</summary>
    internal static string WithEnabled(string text, bool on)
    {
        string value = on ? "true" : "false";
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim().TrimStart('﻿');
            if (line.Length > 0 && line[0] == '[') break;
            if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
            int eq = line.IndexOf('=');
            if (eq > 0 && line[..eq].Trim().Equals("enabled", StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = "enabled = " + value;
                return string.Join("\r\n", lines);
            }
        }
        lines.Insert(0, "enabled = " + value);
        return string.Join("\r\n", lines);
    }
}
