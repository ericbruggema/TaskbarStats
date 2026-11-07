using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TaskbarStats.Plugins;

/// <summary>
/// Eén waarde die de app kan tonen (widget, tooltip). Dit is het hele "contract" met de app: een plugin vult een lijst
/// <see cref="PluginItem"/>s, de host schrijft ze naar <c>plugins\&lt;id&gt;.json</c>. Iedereen die dat bestandsformaat schrijft
/// (een eigen script, een taak) kan dus een waarde in het widget krijgen.
/// </summary>
internal sealed class PluginItem
{
    /// <summary>Vaste sleutel binnen de plugin (de app onthoudt hierop de keuzes van de gebruiker).</summary>
    public string Key { get; set; } = "";
    /// <summary>Volledige naam (tooltip, instellingen).</summary>
    public string Label { get; set; } = "";
    /// <summary>Korte naam in het widget; leeg = geen label, null = gelijk aan <see cref="Label"/>.</summary>
    public string? Short { get; set; }
    /// <summary>Getal (voor balk/meter en drempels); null = alleen tekst.</summary>
    public double? Value { get; set; }
    public double Min { get; set; }
    public double Max { get; set; } = 100;
    public string Unit { get; set; } = "";
    /// <summary>Wat het widget toont; leeg = waarde + eenheid.</summary>
    public string Text { get; set; } = "";
    /// <summary>Extra regel in de tooltip.</summary>
    public string Detail { get; set; } = "";
    /// <summary>"ok", "warn" of "crit" (kleur en melding); leeg = de app kiest zelf.</summary>
    public string Level { get; set; } = "";
    /// <summary>Kleurtip (#RRGGBB); de keuze van de gebruiker in de app gaat voor.</summary>
    public string Color { get; set; } = "";
    /// <summary>Tip: standaard in het widget tonen (de gebruiker kan het zelf aan/uit zetten).</summary>
    public bool Widget { get; set; } = true;
    /// <summary>Laat de app een melding tonen als dit onderdeel op "crit" springt.</summary>
    public bool Notify { get; set; }
}

internal sealed class PluginResult
{
    public List<PluginItem> Items { get; } = new();
    /// <summary>Toelichting of foutmelding (leeg = niets te melden).</summary>
    public string Message { get; set; } = "";
    /// <summary>true = mislukt; de vorige goede waarden blijven dan (verouderd) staan.</summary>
    public bool Error { get; set; }
    /// <summary>Bij een fout: wacht minstens zoveel seconden (bijvoorbeeld bij "te veel verzoeken").</summary>
    public int RetryAfterSec { get; set; }

    public static PluginResult Fail(string message, int retryAfterSec = 0)
        => new() { Error = true, Message = message, RetryAfterSec = retryAfterSec };
}

internal sealed class PluginContext
{
    public IniFile Config { get; init; } = new();
    public HttpClient Http { get; init; } = null!;
    public string DataDir { get; init; } = "";
    /// <summary>Klok (injecteerbaar voor tests).</summary>
    public Func<DateTimeOffset> Now { get; init; } = () => DateTimeOffset.Now;
    /// <summary>Voor tests: de plek waar de plugin zijn eigen tijdelijke gegevens bewaart.</summary>
    public string CacheDir => Path.Combine(DataDir, "plugins", "cache");
}

internal interface IPlugin
{
    string Id { get; }
    string Title { get; }
    /// <summary>Standaard verversingstijd in seconden (de gebruiker overschrijft dat met <c>interval =</c>).</summary>
    int DefaultIntervalSec { get; }
    Task<PluginResult> RunAsync(PluginContext ctx, CancellationToken ct);
}

/// <summary>Schrijft het resultaat als JSON. Met de hand geschreven (geen reflectie): dat blijft werken als de exe wordt ingekort.</summary>
internal static class OutputFile
{
    public static string Render(string pluginId, string title, PluginResult r, string status, int intervalSec, DateTimeOffset now)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteNumber("version", 1);
            w.WriteString("plugin", pluginId);
            w.WriteString("title", title);
            w.WriteString("updated", now.UtcDateTime.ToString("o", CultureInfo.InvariantCulture));
            w.WriteNumber("interval", intervalSec);
            w.WriteNumber("ttl", intervalSec * 3 + 60);
            w.WriteString("status", status);
            w.WriteString("message", r.Message ?? "");
            w.WriteStartArray("items");
            foreach (var it in r.Items)
            {
                w.WriteStartObject();
                w.WriteString("key", it.Key);
                w.WriteString("label", it.Label);
                if (it.Short is not null) w.WriteString("short", it.Short);
                if (it.Value is double v && double.IsFinite(v)) w.WriteNumber("value", Math.Round(v, 3));
                w.WriteNumber("min", it.Min);
                w.WriteNumber("max", it.Max);
                w.WriteString("unit", it.Unit);
                w.WriteString("text", it.Text);
                w.WriteString("detail", it.Detail);
                w.WriteString("level", it.Level);
                w.WriteString("color", it.Color);
                w.WriteBoolean("widget", it.Widget);
                w.WriteBoolean("notify", it.Notify);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>Schrijft atomair (tijdelijk bestand + verplaatsen), zodat de app nooit een half bestand leest.</summary>
    public static void WriteAtomic(string path, string content)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content, new UTF8Encoding(false));
        File.Move(tmp, path, true);
    }
}

/// <summary>Kleine hulpjes die meerdere plugins gebruiken.</summary>
internal static class Fmt
{
    public static string Duration(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        if (t.TotalDays >= 1) return $"{(int)t.TotalDays}d {t.Hours}h";
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes:00}m";
        return $"{Math.Max(1, (int)Math.Ceiling(t.TotalMinutes))}m";
    }

    public static string Num(double v, int decimals = 0)
    {
        // 62,5 -> 63 (niet naar even afronden: dat gaf "62%" bij 62,5)
        var s = Math.Round(v, decimals, MidpointRounding.AwayFromZero).ToString("F" + decimals, CultureInfo.InvariantCulture);
        return s.StartsWith("-") && s.Trim('-', '0', '.') == "" ? s[1..] : s;   // "-0" -> "0"
    }

    /// <summary>Niveau uit een percentage "gebruikt": drempels uit de config.</summary>
    public static string Level(double pct, double warn, double crit) => pct >= crit ? "crit" : pct >= warn ? "warn" : "ok";

    /// <summary>Haalt geheimen uit een foutmelding (token in een URL of kop) voordat die in een bestand of log komt.</summary>
    public static string Scrub(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        s = System.Text.RegularExpressions.Regex.Replace(s, @"(?i)(bearer\s+)[A-Za-z0-9\-\._~\+/]+=*", "$1***");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"(?i)((?:token|key|secret|password|sk-ant-)[A-Za-z0-9_\-]*[=:]\s*)[^\s&""']+", "$1***");
        return s;
    }
}
