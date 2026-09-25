using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace TaskbarStats;

/// <summary>
/// Vertalingen. De bron in de code is Engels: <c>Loc.T("Graph length")</c>; de tekst zelf is de sleutel.
/// Per taal staat er een bestand <c>lang/&lt;code&gt;.json</c> (ingebed in de exe; een bestand met dezelfde naam in
/// <c>%AppData%\TaskbarStats\lang</c> overschrijft of voegt een taal toe) met { "Engelse tekst": "vertaling" }.
/// Ontbreekt een vertaling, dan wordt de Engelse tekst getoond. Dezelfde Engelse tekst met een andere betekenis krijgt
/// een context: <c>"Display@@screen"</c> (alles na @@ wordt nooit getoond). Teksten met {0}, {1}… worden met
/// <c>string.Format</c> ingevuld: <c>Loc.T("New version {0} available.", tag)</c>; letterlijke accolades dan dubbel ({{ }}).
/// </summary>
public static class Loc
{
    private const string Ctx = "@@";
    private static string _lang = "en";
    private static Dictionary<string, string> _table = new();
    private static List<LangInfo>? _available;

    /// <summary>Map met eigen taalbestanden (wordt door AppSettings.Load gezet).</summary>
    public static string? ExternalDir { get; set; }

    /// <summary>Teksten die in de gekozen taal ontbraken (voor tests en het controlescript).</summary>
    public static readonly HashSet<string> Missing = new();

    public sealed record LangInfo(string Code, string Name);

    public static string Lang
    {
        get => _lang;
        set
        {
            var code = string.IsNullOrWhiteSpace(value) ? "en" : value.Trim().ToLowerInvariant();
            _table = code == "en" ? new() : Load(code);
            _lang = code;
        }
    }

    /// <summary>Taal van het besturingssysteem als die beschikbaar is, anders Engels.</summary>
    public static string Detect()
    {
        string two = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
        return Available().Any(l => l.Code == two) ? two : "en";
    }

    /// <summary>Vertaal een Engelse tekst; onbekend of ontbrekend = de Engelse tekst zelf.</summary>
    public static string T(string key)
    {
        if (_lang != "en" && _table.TryGetValue(key, out var v)) return v;
        if (_lang != "en") lock (Missing) Missing.Add(key);
        return Strip(key);
    }

    /// <summary>Vertaal en vul {0}, {1}… in.</summary>
    public static string T(string key, params object?[] args)
    {
        string text = T(key);
        try { return string.Format(CultureInfo.CurrentCulture, text, args); }
        catch (FormatException) { return string.Format(CultureInfo.CurrentCulture, Strip(key), args); }   // foute vertaling: val terug op Engels
    }

    private static string Strip(string key)
    {
        int i = key.IndexOf(Ctx, StringComparison.Ordinal);
        return i < 0 ? key : key[..i];
    }

    /// <summary>Markeert een tekst als vertaalbaar zonder hem nu te vertalen (voor arrays; vertaal later met Loc.T(variabele)).</summary>
    public static string N(string key) => key;

    /// <summary>Alleen nog voor teksten die bewust buiten de taalbestanden blijven (Nederlands/Engels).</summary>
    public static string Pick(string nl, string en) => _lang == "nl" ? nl : en;

    // ---------- talen en bestanden ----------
    /// <summary>Beschikbare talen: Engels, alle ingebedde en alle eigen taalbestanden (naam uit "_name").</summary>
    public static IReadOnlyList<LangInfo> Available()
    {
        if (_available is not null) return _available;
        var codes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var asm = Assembly.GetExecutingAssembly();
        foreach (var n in asm.GetManifestResourceNames())
            if (n.StartsWith("lang.", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                codes.Add(n[5..^5]);
        try
        {
            if (ExternalDir is not null && Directory.Exists(ExternalDir))
                foreach (var f in Directory.GetFiles(ExternalDir, "*.json")) codes.Add(Path.GetFileNameWithoutExtension(f));
        }
        catch { }
        var list = new List<LangInfo> { new("en", "English") };
        foreach (var c in codes)
        {
            if (c.Equals("en", StringComparison.OrdinalIgnoreCase)) continue;
            var t = Read(c.ToLowerInvariant());
            list.Add(new LangInfo(c.ToLowerInvariant(), t.TryGetValue("_name", out var nm) && nm.Length > 0 ? nm : c.ToLowerInvariant()));
        }
        return _available = list;
    }

    private static Dictionary<string, string> Load(string code)
    {
        var t = Read(code);
        t.Remove("_name");
        return t;
    }

    private static Dictionary<string, string> Read(string code)
    {
        var res = new Dictionary<string, string>(StringComparer.Ordinal);
        void Merge(string json)
        {
            try
            {
                var d = JsonSerializer.Deserialize<Dictionary<string, string>>(json, new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                if (d is not null) foreach (var kv in d) res[kv.Key] = kv.Value;
            }
            catch { /* kapot taalbestand: negeren, Engels blijft werken */ }
        }
        try
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("lang." + code + ".json");
            if (s is not null) { using var r = new StreamReader(s); Merge(r.ReadToEnd()); }
        }
        catch { }
        try
        {
            string? f = ExternalDir is null ? null : Path.Combine(ExternalDir, code + ".json");
            if (f is not null && File.Exists(f)) Merge(File.ReadAllText(f));
        }
        catch { }
        return res;
    }
}
