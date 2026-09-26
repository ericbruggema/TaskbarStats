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
/// Een lege vertaling ("") telt als "nog niet vertaald" en toont Engels. Meervoud: <c>Loc.P("{0} day|{0} days", n)</c>; het taalbestand
/// geeft dezelfde vormen gescheiden door '|' in de volgorde van zijn <c>_plural</c>-regel. Meta-sleutels in een taalbestand:
/// <c>_name</c> (naam in die taal), <c>_culture</c> (bijv. "de-DE": getal-/datumopmaak in vertaalde teksten), <c>_plural</c>.
/// Regiobestanden (<c>pt-br.json</c>) vullen het bestand van de hoofdtaal (<c>pt.json</c>) aan. Controle en beheer: <c>tools\LangTool</c>.
/// </summary>
public static class Loc
{
    private const string Ctx = "@@";
    private static string _lang = "en";
    private static Dictionary<string, string> _table = new();
    private static List<LangInfo>? _available;
    private static string _plural = "one-other";
    private static CultureInfo _culture = CultureInfo.CurrentCulture;

    /// <summary>Testtaal "qps": elke tekst wordt met accenten, haken en +30% lengte getoond. Zo zie je ongelokaliseerde tekst en afgeknipte vertalingen (TASKBARSTATS_PSEUDO=1 toont de taal in de lijst).</summary>
    public const string Pseudo = "qps";

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
            _plural = "one-other";
            _culture = CultureInfo.CurrentCulture;
            var table = code is "en" or Pseudo ? new() : Load(code);
            if (table.Remove("_plural", out var pl)) _plural = pl;
            if (table.Remove("_culture", out var cu) && cu.Length > 0) { try { _culture = CultureInfo.GetCultureInfo(cu); } catch (CultureNotFoundException) { } }
            _table = table;
            _lang = code;
        }
    }

    /// <summary>Taal van het besturingssysteem als die beschikbaar is, anders Engels.</summary>
    public static string Detect()
    {
        string full = CultureInfo.CurrentUICulture.Name.ToLowerInvariant(), two = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
        var have = Available();
        if (have.Any(l => l.Code == full)) return full;                 // bijv. pt-br
        return have.Any(l => l.Code == two) ? two : "en";
    }

    /// <summary>Vertaal een Engelse tekst; onbekend of ontbrekend = de Engelse tekst zelf.</summary>
    public static string T(string key)
    {
        if (_lang == "en") return Strip(key);
        if (_lang == Pseudo) return Fake(Strip(key));
        if (_table.TryGetValue(key, out var v)) return v;
        lock (Missing) Missing.Add(key);
        return Strip(key);
    }

    /// <summary>Vertaal en vul {0}, {1}… in.</summary>
    public static string T(string key, params object?[] args)
    {
        string text = T(key);
        try { return string.Format(_culture, text, args); }
        catch (FormatException) { return string.Format(_culture, Strip(key), args); }   // foute vertaling: val terug op Engels
    }

    /// <summary>
    /// Meervoud: <c>Loc.P("{0} day|{0} days", n)</c>. De sleutel heeft de twee Engelse vormen; n is {0}, eventuele extra waarden zijn {1}, {2}…
    /// </summary>
    public static string P(string key, long n, params object?[] args)
    {
        string text = key; int form;
        if (_lang != "en" && _lang != Pseudo && _table.TryGetValue(key, out var v)) { text = v; form = PluralForm(_plural, n); }
        else
        {
            if (_lang != "en" && _lang != Pseudo) lock (Missing) Missing.Add(key);
            form = n == 1 ? 0 : 1;
        }
        var forms = text.Split('|');
        string f = forms[Math.Min(form, forms.Length - 1)];
        if (_lang == Pseudo) f = Fake(f);
        var all = new object?[args.Length + 1];
        all[0] = n; Array.Copy(args, 0, all, 1, args.Length);
        try { return string.Format(_culture, f, all); }
        catch (FormatException) { return string.Format(_culture, key.Split('|')[n == 1 ? 0 : 1], all); }
    }

    /// <summary>Welke vorm bij n hoort volgens de <c>_plural</c>-regel van de taal.</summary>
    internal static int PluralForm(string rule, long n)
    {
        n = Math.Abs(n);
        switch (rule)
        {
            case "other": return 0;
            case "zero-or-one-other": return n <= 1 ? 0 : 1;                                  // Frans, Portugees (Brazilië)
            case "one-few-many-other":                                                          // Pools, Russisch, Oekraïens (vereenvoudigd: 3 vormen)
                long m10 = n % 10, m100 = n % 100;
                if (m10 == 1 && m100 != 11) return 0;
                if (m10 is >= 2 and <= 4 && (m100 < 12 || m100 > 14)) return 1;
                return 2;
            default: return n == 1 ? 0 : 1;                                                     // one-other: Engels, Nederlands, Duits, Spaans, Italiaans…
        }
    }

    private static string Fake(string s)
    {
        const string from = "AEIOUaeiouCcNnYySs", to = "ÂÊÎÔÛâêîôûÇçÑñÝýŠš";
        var sb = new System.Text.StringBuilder("[");
        bool inBrace = false;
        foreach (char c in s)
        {
            if (c == '{') inBrace = true;
            int i = inBrace ? -1 : from.IndexOf(c);
            sb.Append(i >= 0 ? to[i] : c);
            if (c == '}') inBrace = false;
        }
        sb.Append(' ', Math.Max(1, s.Length * 3 / 10)).Append('!').Append(']');
        return sb.ToString();
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
        catch (Exception dex) { Diag.Swallow(dex); }
        var list = new List<LangInfo> { new("en", "English") };
        if (Environment.GetEnvironmentVariable("TASKBARSTATS_PSEUDO") == "1") list.Add(new LangInfo(Pseudo, "Pseudo (test)"));
        foreach (var c in codes)
        {
            if (c.Equals("en", StringComparison.OrdinalIgnoreCase) || c.Equals(Pseudo, StringComparison.OrdinalIgnoreCase)) continue;
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

    /// <summary>Leest een taal: bij een regiotaal (pt-br) eerst de hoofdtaal (pt), dan het regiobestand erbovenop.</summary>
    private static Dictionary<string, string> Read(string code)
    {
        int dash = code.IndexOf('-');
        var res = dash > 0 ? Read(code[..dash]) : new Dictionary<string, string>(StringComparer.Ordinal);
        Merge(res, code);
        return res;
    }

    private static void Merge(Dictionary<string, string> res, string code)
    {
        void MergeJson(string json)
        {
            try
            {
                var d = JsonSerializer.Deserialize<Dictionary<string, string>>(json, new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                if (d is not null) foreach (var kv in d) if (kv.Value is { Length: > 0 }) res[kv.Key] = kv.Value;   // leeg = nog niet vertaald: niets overschrijven
            }
            catch (Exception ex) { Diag.Warn("lang", "Kapot taalbestand " + code + ": " + ex.Message); }   // Engels blijft werken
        }
        try
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("lang." + code + ".json");
            if (s is not null) { using var r = new StreamReader(s); MergeJson(r.ReadToEnd()); }
        }
        catch (Exception ex) { Diag.Warn("lang", "Ingebed taalbestand " + code + " niet te lezen: " + ex.Message); }
        try
        {
            string? f = ExternalDir is null ? null : Path.Combine(ExternalDir, code + ".json");
            if (f is not null && File.Exists(f)) MergeJson(File.ReadAllText(f));
        }
        catch (Exception ex) { Diag.Warn("lang", "Taalbestand " + code + " niet te lezen: " + ex.Message); }
    }
}
