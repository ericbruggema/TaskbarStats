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
        _ => id,
    };

    public static string DescriptionOf(string id) => id switch
    {
        "claude" => Loc.T("Shows how much of your Claude 5-hour and weekly limit is used and when it resets. Needs a Claude Code sign-in (run 'claude' in a terminal and use /login). Uses an undocumented Anthropic address that may change."),
        "weather" => Loc.T("Current weather (sun, clouds, rain), temperature, humidity, wind and chance of rain for your GPS/Wi-Fi location or a place you choose."),
        "location" => Loc.T("Where you are: place name, coordinates and accuracy from the Windows location service (GPS or Wi-Fi) or your IP address."),
        "uptime" => Loc.T("Checks websites, ports and devices every minute and turns red (with a notification) when something is down."),
        "script" => Loc.T("Shows any value from a script, a text file or a web address (JSON), for example Home Assistant, a NAS or your own tool."),
        _ => "",
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

    public static bool AnyEnabled() => PluginCatalog.Ids.Any(IsEnabled);

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
