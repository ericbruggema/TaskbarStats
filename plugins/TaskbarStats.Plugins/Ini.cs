using System.Globalization;
using System.Text;

namespace TaskbarStats.Plugins;

/// <summary>
/// Een eenvoudig configbestand: <c>sleutel = waarde</c>, regels met <c>#</c> of <c>;</c> zijn commentaar, <c>[naam]</c> begint een
/// sectie (voor plugins met meerdere doelen, zoals uptime en script). Sleutels zijn niet hoofdlettergevoelig; een waarde mag
/// <c>=</c> bevatten (URL's). Geen JSON, zodat gebruikers het in Kladblok kunnen bewerken en commentaar kunnen houden.
/// </summary>
internal sealed class IniFile
{
    internal sealed class Section
    {
        public string Name { get; }
        public List<(string Key, string Value)> Entries { get; } = new();
        public Section(string name) { Name = name; }

        public string? Get(string key)
        {
            foreach (var (k, v) in Entries) if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) return v;
            return null;
        }

        public IEnumerable<string> All(string key)
            => Entries.Where(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase)).Select(e => e.Value);

        public string Str(string key, string def)
        {
            var v = Get(key);
            return string.IsNullOrWhiteSpace(v) ? def : v.Trim();
        }

        public int Int(string key, int def, int min = int.MinValue, int max = int.MaxValue)
        {
            var v = Get(key);
            int r = int.TryParse(v?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : def;
            return Math.Clamp(r, min, max);
        }

        public double Dbl(string key, double def)
        {
            var v = Get(key)?.Trim().Replace(',', '.');
            return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) ? n : def;
        }

        public bool Bool(string key, bool def)
        {
            var v = Get(key)?.Trim().ToLowerInvariant();
            return v switch
            {
                "true" or "yes" or "on" or "1" or "ja" or "an" => true,
                "false" or "no" or "off" or "0" or "nee" or "aus" => false,
                _ => def,
            };
        }
    }

    public Section Root { get; } = new("");
    public List<Section> Sections { get; } = new();

    // Handige doorverwijzingen naar de hoofdsectie (de sleutels vóór de eerste [sectie]).
    public string? Get(string key) => Root.Get(key);
    public IEnumerable<string> All(string key) => Root.All(key);
    public string Str(string key, string def) => Root.Str(key, def);
    public int Int(string key, int def, int min = int.MinValue, int max = int.MaxValue) => Root.Int(key, def, min, max);
    public double Dbl(string key, double def) => Root.Dbl(key, def);
    public bool Bool(string key, bool def) => Root.Bool(key, def);
    public bool Enabled => Root.Bool("enabled", false);

    public static IniFile Parse(string? text)
    {
        var ini = new IniFile();
        var cur = ini.Root;
        foreach (var raw in (text ?? "").Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim().TrimStart('﻿');
            if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
            if (line[0] == '[' && line[^1] == ']')
            {
                cur = new Section(line[1..^1].Trim());
                ini.Sections.Add(cur);
                continue;
            }
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            cur.Entries.Add((line[..eq].Trim(), line[(eq + 1)..].Trim()));
        }
        return ini;
    }

    public static IniFile Load(string path)
    {
        try { return Parse(File.ReadAllText(path, Encoding.UTF8)); }
        catch (Exception) { return new IniFile(); }
    }

    /// <summary>Controle voor de editor in de app: een regel die geen commentaar, sectie of <c>sleutel = waarde</c> is, is een fout.</summary>
    public static string? Validate(string? text)
    {
        int n = 0;
        foreach (var raw in (text ?? "").Replace("\r\n", "\n").Split('\n'))
        {
            n++;
            var line = raw.Trim().TrimStart('﻿');
            if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
            if (line[0] == '[') { if (line[^1] != ']') return $"line {n}: missing ]"; continue; }
            if (line.IndexOf('=') <= 0) return $"line {n}: expected 'key = value'";
        }
        return null;
    }
}
