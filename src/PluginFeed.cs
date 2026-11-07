using System.Text.Json;

namespace TaskbarStats;

/// <summary>
/// Eén waarde van een plugin, zoals de app hem toont (widget, tooltip). Komt uit <c>plugins\&lt;plugin&gt;.json</c>: dat bestand schrijft
/// de plugin-host (zie plugins\TaskbarStats.Plugins), maar elk script dat hetzelfde formaat schrijft werkt ook. De app voert dus
/// zelf nooit plugin-code uit; hij leest alleen deze bestanden.
/// </summary>
public sealed record PluginValue(
    string Plugin, string PluginTitle, string Key, string Label, string? Short, double? Value, double Min, double Max, string Unit,
    string Text, string Detail, string Level, string Color, bool WidgetHint, bool Notify,
    string Status, string Message, DateTime UpdatedUtc, int TtlSec)
{
    /// <summary>Vaste sleutel voor instellingen en meldingen: "plugin/sleutel".</summary>
    public string Id => Plugin + "/" + Key;

    /// <summary>Waarde als percentage van het bereik (0-100); null zonder getal.</summary>
    public double? Percent => Value is double v && Max > Min ? Math.Clamp((v - Min) / (Max - Min) * 100.0, 0, 100) : null;

    /// <summary>Verouderd: de plugin meldt een fout, of de gegevens zijn ouder dan hun geldigheidsduur.</summary>
    public bool IsStale(DateTime nowUtc) => Status == "error" || (nowUtc - UpdatedUtc).TotalSeconds > Math.Max(60, TtlSec);

    public bool Stale => IsStale(DateTime.UtcNow);

    /// <summary>Het label in het widget: "Short" als de plugin dat geeft (leeg = geen label), anders het volledige label.</summary>
    public string WidgetLabel => Short ?? Label;
}

/// <summary>Toestand van één plugin (voor het tabblad Plugins en de tooltip).</summary>
public sealed record PluginStatus(string Plugin, string Title, string Status, string Message, DateTime UpdatedUtc, int TtlSec)
{
    public bool Stale => Status == "error" || (DateTime.UtcNow - UpdatedUtc).TotalSeconds > Math.Max(60, TtlSec);
}

public sealed record PluginData(IReadOnlyList<PluginValue> Values, IReadOnlyList<PluginStatus> Statuses)
{
    public static readonly PluginData Empty = new(Array.Empty<PluginValue>(), Array.Empty<PluginStatus>());
}

/// <summary>Leest de uitvoerbestanden van de plugins (<c>plugins\*.json</c> in de datamap), met een korte cache.</summary>
public static class PluginFeed
{
    private static readonly object L = new();
    private static PluginData _cached = PluginData.Empty;
    private static long _scanAt;
    private static string _signature = "\0";
    private static string _dir = "";

    /// <summary>De laatste bekende gegevens; kijkt hooguit elke 2 s of er bestanden veranderd zijn (goedkoop: een paar kleine bestanden).</summary>
    public static PluginData Current
    {
        get
        {
            lock (L)
            {
                string dir = AppPaths.DataDir;
                if (dir != _dir) { _dir = dir; _signature = "\0"; _scanAt = 0; }
                long now = Environment.TickCount64;
                if (now - _scanAt < 2000) return _cached;
                _scanAt = now;
                try
                {
                    string sig = Signature(dir);
                    if (sig != _signature) { _signature = sig; _cached = Read(dir); }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Diag.Swallow(ex); }
                return _cached;
            }
        }
    }

    /// <summary>Alleen voor tests: opnieuw inlezen bij de eerstvolgende aanroep.</summary>
    internal static void Reset() { lock (L) { _signature = "\0"; _scanAt = 0; _cached = PluginData.Empty; } }

    private static string Signature(string dataDir)
    {
        var dir = Path.Combine(dataDir, "plugins");
        if (!Directory.Exists(dir)) return "";
        var sb = new System.Text.StringBuilder();
        foreach (var f in Directory.EnumerateFiles(dir, "*.json").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var fi = new FileInfo(f);
            sb.Append(fi.Name).Append(':').Append(fi.LastWriteTimeUtc.Ticks).Append(':').Append(fi.Length).Append(';');
        }
        return sb.ToString();
    }

    /// <summary>Leest alle plugin-bestanden in <c>&lt;datamap&gt;\plugins</c>. Ongeldige of half geschreven bestanden worden overgeslagen.</summary>
    internal static PluginData Read(string dataDir)
    {
        var dir = Path.Combine(dataDir, "plugins");
        if (!Directory.Exists(dir)) return PluginData.Empty;
        var values = new List<PluginValue>();
        var statuses = new List<PluginStatus>();
        var files = Directory.EnumerateFiles(dir, "*.json")
            .OrderBy(f => PluginCatalog.OrderOf(Path.GetFileNameWithoutExtension(f)))
            .ThenBy(f => f, StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            try
            {
                string text;
                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var r = new StreamReader(fs)) text = r.ReadToEnd();
                var (st, vals) = Parse(Path.GetFileNameWithoutExtension(file), text, File.GetLastWriteTimeUtc(file));
                if (st is null) continue;
                statuses.Add(st);
                values.AddRange(vals);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { Diag.Swallow(ex); }
        }
        return new PluginData(values, statuses);
    }

    internal static (PluginStatus? status, List<PluginValue> values) Parse(string fileId, string json, DateTime fileTimeUtc)
    {
        var vals = new List<PluginValue>();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return (null, vals);

        string plugin = Str(root, "plugin", fileId);
        if (plugin.Length == 0) plugin = fileId;
        string title = Str(root, "title", PluginCatalog.TitleOf(plugin));
        string status = Str(root, "status", "ok").ToLowerInvariant();
        string message = Str(root, "message", "");
        int ttl = (int)Math.Clamp(Num(root, "ttl", 900), 30, 7 * 86400);
        DateTime updated = fileTimeUtc;
        if (root.TryGetProperty("updated", out var u) && u.ValueKind == JsonValueKind.String
            && DateTime.TryParse(u.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var dt))
            updated = dt;

        if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var it in items.EnumerateArray())
            {
                if (it.ValueKind != JsonValueKind.Object) continue;
                string key = Str(it, "key", "");
                if (key.Length == 0) continue;
                string label = Str(it, "label", key);
                double? value = it.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Number && double.IsFinite(v.GetDouble()) ? v.GetDouble() : null;
                string unit = Str(it, "unit", "");
                string text = Str(it, "text", "");
                if (text.Length == 0) text = value is double nv ? nv.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + unit : "";
                string? shortLabel = it.TryGetProperty("short", out var sh) && sh.ValueKind == JsonValueKind.String ? sh.GetString() : null;
                vals.Add(new PluginValue(plugin, title, key, label, shortLabel, value, Num(it, "min", 0), Num(it, "max", 100), unit, text,
                    Str(it, "detail", ""), Str(it, "level", "").ToLowerInvariant(), Str(it, "color", ""),
                    Bool(it, "widget", true), Bool(it, "notify", false), status, message, updated, ttl));
            }
        }
        return (new PluginStatus(plugin, title, status, message, updated, ttl), vals);
    }

    private static string Str(JsonElement e, string name, string def)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? def : def;

    private static double Num(JsonElement e, string name, double def)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && double.IsFinite(v.GetDouble()) ? v.GetDouble() : def;

    private static bool Bool(JsonElement e, string name, bool def)
        => e.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) ? v.GetBoolean() : def;
}
