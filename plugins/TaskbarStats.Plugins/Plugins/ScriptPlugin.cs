using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TaskbarStats.Plugins;

/// <summary>
/// Eigen waarden in het widget: een opdracht of script (draait onder JOUW account, nooit als administrator), een tekstbestand of een
/// JSON-adres (REST) per sectie van de config. De uitvoer mag een getal zijn ("42"), "getal|tekst" ("42|3 builds"), een regel tekst
/// of JSON met value/text/detail/level/color. Zo is bijna alles te koppelen: Home Assistant, Pi-hole, een NAS, een eigen script.
/// </summary>
internal sealed class ScriptPlugin : IPlugin
{
    public string Id => "script";
    public string Title => "Script / REST";
    public int DefaultIntervalSec => 60;

    private const int MaxOutput = 64 * 1024;

    public async Task<PluginResult> RunAsync(PluginContext ctx, CancellationToken ct)
    {
        var sections = ctx.Config.Sections.Where(s => !string.IsNullOrWhiteSpace(s.Name)).ToList();
        if (sections.Count == 0) return PluginResult.Fail("No items configured. Add a [Name] section with cmd =, file = or url =.");
        var res = new PluginResult();
        var errors = new List<string>();
        foreach (var s in sections)
        {
            try { res.Items.Add(await RunItemAsync(s, ctx, ct).ConfigureAwait(false)); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { errors.Add($"{s.Name}: timeout"); res.Items.Add(Failed(s, "timeout")); }
            catch (Exception ex) when (ex is IOException or HttpRequestException or InvalidOperationException or System.ComponentModel.Win32Exception or JsonException or UnauthorizedAccessException)
            {
                string m = Fmt.Scrub(ex.Message);
                errors.Add($"{s.Name}: {m}");
                res.Items.Add(Failed(s, m));
            }
        }
        if (errors.Count > 0) res.Message = string.Join("; ", errors);
        return res;
    }

    private static PluginItem Failed(IniFile.Section s, string why) => new()
    {
        Key = UptimePlugin.Slug(s.Name), Label = s.Name, Short = s.Str("short", s.Name), Text = "?", Detail = $"{s.Name}: {why}", Level = "warn",
        Widget = s.Bool("widget", true),
    };

    private async Task<PluginItem> RunItemAsync(IniFile.Section s, PluginContext ctx, CancellationToken ct)
    {
        string raw;
        string? cmd = s.Get("cmd"), file = s.Get("file"), url = s.Get("url");
        if (!string.IsNullOrWhiteSpace(cmd)) raw = await RunCommandAsync(cmd, ct).ConfigureAwait(false);
        else if (!string.IsNullOrWhiteSpace(file)) raw = ReadFile(file);
        else if (!string.IsNullOrWhiteSpace(url)) raw = await FetchAsync(url, s, ctx.Http, ct).ConfigureAwait(false);
        else throw new InvalidOperationException("needs cmd =, file = or url =");
        return Interpret(s, raw);
    }

    // ---------- bronnen ----------
    internal static async Task<string> RunCommandAsync(string cmd, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("cmd.exe", "/d /s /c \"" + cmd + "\"")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("could not start the command");
        var errTask = p.StandardError.ReadToEndAsync(ct);
        try
        {
            var outTask = ReadLimitedAsync(p.StandardOutput, ct);
            await p.WaitForExitAsync(ct).ConfigureAwait(false);
            string o = await outTask.ConfigureAwait(false);
            if (p.ExitCode != 0 && string.IsNullOrWhiteSpace(o))
            {
                string e = (await errTask.ConfigureAwait(false)).Trim();
                throw new InvalidOperationException($"exit code {p.ExitCode}" + (e.Length > 0 ? ": " + e.Split('\n')[0].Trim() : ""));
            }
            return o;
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(true); } catch (InvalidOperationException) { }
            throw;
        }
    }

    private static async Task<string> ReadLimitedAsync(StreamReader r, CancellationToken ct)
    {
        var buf = new char[4096];
        var sb = new StringBuilder();
        int n;
        while ((n = await r.ReadAsync(buf.AsMemory(), ct).ConfigureAwait(false)) > 0)
        {
            if (sb.Length < MaxOutput) sb.Append(buf, 0, n);
        }
        return sb.ToString();
    }

    private static string ReadFile(string path)
    {
        using var fs = new FileStream(Environment.ExpandEnvironmentVariables(path), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var r = new StreamReader(fs, Encoding.UTF8);
        var buf = new char[MaxOutput];
        int n = r.Read(buf, 0, buf.Length);
        return new string(buf, 0, n);
    }

    private static async Task<string> FetchAsync(string url, IniFile.Section s, HttpClient http, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        foreach (var h in s.All("header"))
        {
            int c = h.IndexOf(':');
            if (c > 0) req.Headers.TryAddWithoutValidation(h[..c].Trim(), h[(c + 1)..].Trim());
        }
        using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) throw new HttpRequestException("HTTP " + (int)resp.StatusCode);
        string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        string? path = s.Get("path");
        if (string.IsNullOrWhiteSpace(path)) return body.Length > MaxOutput ? body[..MaxOutput] : body;
        using var doc = JsonDocument.Parse(body);
        var el = SelectPath(doc.RootElement, path) ?? throw new InvalidOperationException($"'{path}' not found in the reply");
        return el.ValueKind == JsonValueKind.String ? el.GetString() ?? "" : el.GetRawText();
    }

    /// <summary>Eenvoudig pad: <c>data.items[0].price</c> (met of zonder <c>$.</c> ervoor).</summary>
    internal static JsonElement? SelectPath(JsonElement root, string path)
    {
        var cur = root;
        path = path.Trim();
        if (path.StartsWith("$")) path = path[1..].TrimStart('.');
        foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            string name = part;
            var indexes = new List<int>();
            int br = part.IndexOf('[');
            if (br >= 0)
            {
                name = part[..br];
                foreach (var seg in part[br..].Split(new[] { '[', ']' }, StringSplitOptions.RemoveEmptyEntries))
                    if (int.TryParse(seg, out var idx)) indexes.Add(idx);
            }
            if (name.Length > 0)
            {
                if (cur.ValueKind != JsonValueKind.Object || !cur.TryGetProperty(name, out cur)) return null;
            }
            foreach (var i in indexes)
            {
                if (cur.ValueKind != JsonValueKind.Array || i < 0 || i >= cur.GetArrayLength()) return null;
                cur = cur[i];
            }
        }
        return cur;
    }

    // ---------- uitvoer begrijpen ----------
    internal static PluginItem Interpret(IniFile.Section s, string raw)
    {
        var item = new PluginItem
        {
            Key = UptimePlugin.Slug(s.Name), Label = s.Name, Short = s.Get("short"), Unit = s.Str("unit", ""),
            Min = s.Dbl("min", 0), Max = s.Dbl("max", 100), Widget = s.Bool("widget", true), Notify = s.Bool("notify", false),
            Color = s.Str("color", ""),
        };
        string t = raw.Trim();
        double? value = null;
        string text = "", detail = "", level = "", color = "";

        if (t.StartsWith("{"))
        {
            using var doc = JsonDocument.Parse(t);
            var r = doc.RootElement;
            if (r.TryGetProperty("value", out var v)) value = AsNumber(v);
            if (r.TryGetProperty("text", out var tx) && tx.ValueKind == JsonValueKind.String) text = tx.GetString() ?? "";
            if (r.TryGetProperty("detail", out var d) && d.ValueKind == JsonValueKind.String) detail = d.GetString() ?? "";
            if (r.TryGetProperty("level", out var l) && l.ValueKind == JsonValueKind.String) level = l.GetString() ?? "";
            if (r.TryGetProperty("color", out var c) && c.ValueKind == JsonValueKind.String) color = c.GetString() ?? "";
            if (r.TryGetProperty("unit", out var u) && u.ValueKind == JsonValueKind.String) item.Unit = u.GetString() ?? item.Unit;
        }
        else
        {
            string first = t.Replace("\r", "").Split('\n')[0].Trim();
            int bar = first.IndexOf('|');
            string head = bar >= 0 ? first[..bar] : first;
            value = ParseNumber(head);
            if (value is not null) text = bar >= 0 ? first[(bar + 1)..].Trim() : "";
            else text = first;
        }

        item.Value = value;
        if (text == "")
            text = value is double nv ? Compact(nv) + (item.Unit.Length > 0 && item.Unit.Length <= 3 ? item.Unit : item.Unit.Length > 0 ? " " + item.Unit : "") : "?";
        item.Text = text;
        item.Detail = detail != "" ? detail : $"{s.Name}: {text}";
        item.Color = color != "" ? color : item.Color;

        // drempels uit de config ("warn"/"crit"); "invert = true" = lage waarden zijn slecht (bijvoorbeeld vrije ruimte)
        if (level == "" && value is double val && (s.Get("warn") is not null || s.Get("crit") is not null))
        {
            double warn = s.Dbl("warn", double.NaN), crit = s.Dbl("crit", double.NaN);
            bool inv = s.Bool("invert", false);
            bool Hit(double lim) => !double.IsNaN(lim) && (inv ? val <= lim : val >= lim);
            level = Hit(crit) ? "crit" : Hit(warn) ? "warn" : "ok";
        }
        item.Level = level;
        return item;
    }

    /// <summary>Getal zoals het script het gaf, zonder overbodige nullen: 7,25 / 12,5 / 130.</summary>
    internal static string Compact(double v)
        => (Math.Abs(v) < 10 ? v.ToString("0.##", CultureInfo.InvariantCulture) : Math.Abs(v) < 100 ? v.ToString("0.#", CultureInfo.InvariantCulture) : v.ToString("0", CultureInfo.InvariantCulture));

    private static double? AsNumber(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.Number when double.IsFinite(v.GetDouble()) => v.GetDouble(),
        JsonValueKind.String => ParseNumber(v.GetString() ?? ""),
        JsonValueKind.True => 1,
        JsonValueKind.False => 0,
        _ => null,
    };

    internal static double? ParseNumber(string s)
    {
        s = s.Trim().TrimEnd('%').Trim().Replace(',', '.');
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) ? n : null;
    }
}
