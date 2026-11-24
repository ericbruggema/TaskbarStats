using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace TaskbarStats.Plugins;

/// <summary>Een fout in een recept, met een leesbare uitleg voor de gebruiker (komt in Instellingen > Plugins te staan).</summary>
internal sealed class RecipeException : Exception
{
    public int RetryAfterSec { get; }
    public RecipeException(string message, int retryAfterSec = 0) : base(message) { RetryAfterSec = retryAfterSec; }
}

/// <summary>
/// Een recept: een plugin zonder code. Het configbestand (<c>type = recipe</c>) beschrijft waar de gegevens vandaan komen (een webadres,
/// een bestand of de uitvoer van een opdracht) en per <c>[sectie]</c> hoe één waarde eruitziet, met formules uit <see cref="Expr"/>.
/// De host haalt de gegevens op, rekent de formules uit en schrijft het resultaat; de app leest alleen dat resultaat.
/// Wat een recept mag (adressen, bestanden, opdrachten, geheimen) staat in <see cref="Caps"/> en moet de gebruiker goedkeuren.
/// </summary>
internal sealed class RecipePlugin : IPlugin
{
    public string Id { get; }
    public string Title { get; }
    public int DefaultIntervalSec => 60;

    private const int MaxBody = 2 * 1024 * 1024;

    public RecipePlugin(string id, string title) { Id = id; Title = title; }

    public async Task<PluginResult> RunAsync(PluginContext ctx, CancellationToken ct)
    {
        var cfg = ctx.Config;
        var secretsUsed = new List<string>();
        try
        {
            var sections = cfg.Sections.Where(s => !string.IsNullOrWhiteSpace(s.Name)).ToList();
            if (sections.Count == 0) throw new RecipeException("The recipe has no values yet. Add a [name] section with value = ... (see the handbook).");

            var vars = await LoadSourceAsync(cfg, ctx, secretsUsed, ct).ConfigureAwait(false);
            var res = new PluginResult();
            var errors = new List<string>();
            var keys = new HashSet<string>();
            foreach (var s in sections)
            {
                try
                {
                    var item = BuildItem(s, vars, ctx.Now, keys);
                    if (item is not null) res.Items.Add(item);
                }
                catch (ExprException ex)
                {
                    errors.Add($"[{s.Name}] {ex.Message}");
                    res.Items.Add(new PluginItem
                    {
                        Key = UniqueKey(UptimePlugin.Slug(s.Name), keys), Label = s.Name, Short = s.Get("short"), Text = "?", Level = "warn",
                        Detail = $"{s.Name}: {ex.Message}", Widget = s.Bool("widget", true),
                    });
                }
            }
            if (res.Items.Count == 0) return PluginResult.Fail("Nothing to show: every value is hidden by its 'when =' condition.");
            if (errors.Count > 0) res.Message = Fmt.Scrub(string.Join("; ", errors), secretsUsed);
            return res;
        }
        catch (RecipeException ex)
        {
            string help = cfg.Str("help", "");
            return PluginResult.Fail(Fmt.Scrub(ex.Message, secretsUsed) + (help.Length > 0 ? " " + help : ""), ex.RetryAfterSec);
        }
        catch (ExprException ex) { return PluginResult.Fail(Fmt.Scrub(ex.Message, secretsUsed)); }
        catch (HttpRequestException ex) { return PluginResult.Fail("Network error: " + Fmt.Scrub(ex.Message, secretsUsed)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception or JsonException)
        {
            return PluginResult.Fail(Fmt.Scrub(ex.Message, secretsUsed));
        }
    }

    // ---------- bron ----------
    private async Task<Dictionary<string, object?>> LoadSourceAsync(IniFile cfg, PluginContext ctx, List<string> used, CancellationToken ct)
    {
        string? url = cfg.Get("url"), file = cfg.Get("file"), cmd = cfg.Get("cmd");
        var resolver = new TokenResolver(ctx.DataDir, ctx.Secrets, used);
        string raw;
        int status = 200;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        if (!string.IsNullOrWhiteSpace(url)) (raw, status) = await FetchAsync(url, cfg, ctx, resolver, ct).ConfigureAwait(false);
        else if (!string.IsNullOrWhiteSpace(file)) raw = ScriptPlugin.ReadFile(resolver.Apply(file));
        else if (!string.IsNullOrWhiteSpace(cmd)) raw = await ScriptPlugin.RunCommandAsync(cmd, ct).ConfigureAwait(false);
        else throw new RecipeException("The recipe has no source. Add url = ..., file = ... or cmd = ... above the first [section].");
        sw.Stop();

        var vars = new Dictionary<string, object?>(StringComparer.Ordinal) { ["http_status"] = (double)status, ["elapsed_ms"] = (double)sw.ElapsedMilliseconds };
        string parse = cfg.Str("parse", "auto").ToLowerInvariant();
        string trimmed = raw.TrimStart();
        if (parse == "auto") parse = trimmed.StartsWith('{') || trimmed.StartsWith('[') ? "json" : "text";

        vars["text"] = raw;
        vars["lines"] = raw.Replace("\r", "").Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0).Cast<object?>().ToList();
        if (parse == "json")
        {
            try
            {
                using var doc = JsonDocument.Parse(raw);
                vars["root"] = Expr.FromJson(doc.RootElement);
            }
            catch (JsonException) { throw new RecipeException("The reply is not JSON. Add 'parse = text' (or lines/csv) to the recipe if it is plain text."); }
        }
        else if (parse == "csv") vars["rows"] = ParseCsv(raw, cfg.Bool("csv_header", false), cfg.Str("csv_separator", ",")[0]);
        else if (parse is not ("text" or "lines")) throw new RecipeException($"Unknown parse = {parse} (use json, text, lines or csv).");
        return vars;
    }

    private static async Task<(string body, int status)> FetchAsync(string urlTemplate, IniFile cfg, PluginContext ctx, TokenResolver resolver, CancellationToken ct)
    {
        string url = resolver.Apply(urlTemplate);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new RecipeException("The url is not a valid http(s) address.");
        // Het adres na het invullen mag alleen naar een host gaan die in het recept zelf staat (dus ook door de gebruiker goedgekeurd is).
        var allowed = Caps.Compute(cfg).Hosts;
        if (!allowed.Contains(uri.Host.ToLowerInvariant())) throw new RecipeException($"The address goes to '{uri.Host}', which the recipe does not list. A variable cannot change the host.");

        var method = string.Equals(cfg.Str("method", "GET"), "POST", StringComparison.OrdinalIgnoreCase) ? HttpMethod.Post : HttpMethod.Get;
        string? body = method == HttpMethod.Post ? resolver.Apply(cfg.Str("body", "")) : null;
        var headers = new List<(string, string)>();
        foreach (var h in cfg.All("header"))
        {
            int c = h.IndexOf(':');
            if (c > 0) headers.Add((h[..c].Trim(), resolver.Apply(h[(c + 1)..].Trim())));
        }

        for (int hop = 0; hop <= 3; hop++)
        {
            using var req = new HttpRequestMessage(method, uri);
            foreach (var (n, v) in headers) req.Headers.TryAddWithoutValidation(n, v);
            if (!req.Headers.Contains("Accept")) req.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
            if (body is not null) req.Content = new StringContent(body, Encoding.UTF8, cfg.Str("content_type", "application/json"));

            HttpResponseMessage resp;
            try { resp = await ctx.HttpStrict.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false); }
            catch (HttpRequestException ex) { throw new RecipeException("Network error: " + ex.Message); }
            using (resp)
            {
                int code = (int)resp.StatusCode;
                if (code is 301 or 302 or 303 or 307 or 308)
                {
                    var loc = resp.Headers.Location;
                    if (loc is null) throw new RecipeException($"HTTP {code} without a new address.");
                    var next = loc.IsAbsoluteUri ? loc : new Uri(uri, loc);
                    if (!string.Equals(next.Host, uri.Host, StringComparison.OrdinalIgnoreCase))
                        throw new RecipeException($"The server redirects to another host ({next.Host}). Update the url in the recipe to {next.GetLeftPart(UriPartial.Path)}.");
                    uri = next;
                    if (code is 301 or 302 or 303 && method == HttpMethod.Post) { method = HttpMethod.Get; body = null; }
                    continue;
                }
                if (code == 429)
                {
                    int wait = (int)(resp.Headers.RetryAfter?.Delta?.TotalSeconds ?? 300);
                    throw new RecipeException("Rate limited by the server; trying again later.", Math.Clamp(wait, 60, 3600));
                }
                string text = await ReadLimitedAsync(resp, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) throw new RecipeException($"HTTP {code}" + ServerReason(text));
                return (text, code);
            }
        }
        throw new RecipeException("Too many redirects.");
    }

    private static async Task<string> ReadLimitedAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        await using var s = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var ms = new MemoryStream();
        var buf = new byte[16 * 1024];
        int n;
        while ((n = await s.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
        {
            ms.Write(buf, 0, n);
            if (ms.Length > MaxBody) throw new RecipeException("The reply is too large (over 2 MB).");
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>De reden die de server geeft (error.message, message, detail), beknopt; leeg als er geen is.</summary>
    internal static string ServerReason(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var r = doc.RootElement;
            if (r.ValueKind == JsonValueKind.Object)
            {
                string? s = null;
                if (r.TryGetProperty("error", out var e))
                {
                    if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String) s = m.GetString();
                    else if (e.ValueKind == JsonValueKind.String) s = e.GetString();
                }
                if (s is null && r.TryGetProperty("message", out var m2) && m2.ValueKind == JsonValueKind.String) s = m2.GetString();
                if (s is null && r.TryGetProperty("detail", out var d) && d.ValueKind == JsonValueKind.String) s = d.GetString();
                if (!string.IsNullOrWhiteSpace(s)) return ": " + (s.Length > 140 ? s[..140] + "…" : s);
            }
        }
        catch (JsonException) { }
        return "";
    }

    private static List<object?> ParseCsv(string raw, bool header, char sep)
    {
        var rows = new List<List<string>>();
        foreach (var line in raw.Replace("\r", "").Split('\n'))
        {
            if (line.Length == 0) continue;
            var cells = new List<string>();
            var sb = new StringBuilder();
            bool q = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (q) { if (c == '"') { if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else q = false; } else sb.Append(c); }
                else if (c == '"') q = true;
                else if (c == sep) { cells.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
            cells.Add(sb.ToString());
            rows.Add(cells);
            if (rows.Count > 5000) break;
        }
        var res = new List<object?>();
        if (header && rows.Count > 0)
        {
            var names = rows[0];
            foreach (var r in rows.Skip(1))
            {
                var d = new Dictionary<string, object?>(StringComparer.Ordinal);
                for (int i = 0; i < names.Count && i < r.Count; i++) d[names[i].Trim()] = r[i];
                res.Add(d);
            }
        }
        else foreach (var r in rows) res.Add(r.Cast<object?>().ToList());
        return res;
    }

    // ---------- één waarde ----------
    private static string UniqueKey(string key, HashSet<string> keys)
    {
        string k = key; int n = 2;
        while (!keys.Add(k)) k = key + "_" + n++;
        return k;
    }

    private static PluginItem? BuildItem(IniFile.Section s, Dictionary<string, object?> vars, Func<DateTimeOffset> now, HashSet<string> keys)
    {
        var env = new ExprEnv { Vars = vars, Now = now };
        string Tpl(string key, string def, IReadOnlyDictionary<string, object?>? extra = null)
        {
            string src = s.Get(key) ?? def;
            if (src.Length == 0) return "";
            Guard(src);
            var e = extra is null ? env : new ExprEnv { Vars = vars, Locals = extra, Now = now };
            return Template.Parse(src).Render(e);
        }

        string? when = s.Get("when");
        if (!string.IsNullOrWhiteSpace(when))
        {
            Guard(when);
            if (!Expr.Truthy(Expr.Parse(when).Eval(env))) return null;
        }

        object? value = null;
        string? vsrc = s.Get("value");
        if (!string.IsNullOrWhiteSpace(vsrc))
        {
            Guard(vsrc);
            value = Expr.Parse(vsrc).Eval(env);
        }
        double? num = Expr.ToNum(value);
        var extra2 = new Dictionary<string, object?> { ["value"] = value };

        string unit = s.Str("unit", "");
        string label = Tpl("label", s.Name, extra2);
        string text = Tpl("format", "", extra2);
        if (text.Length == 0) text = Tpl("text", "", extra2);
        if (text.Length == 0)
        {
            if (num is double nv && value is not string) text = ScriptPlugin.Compact(nv) + (unit.Length > 0 ? (unit.Length <= 3 ? unit : " " + unit) : "");
            else if (value is null) text = "?";
            else text = Expr.ToText(value) + (num is not null && unit.Length > 0 ? (unit.Length <= 3 ? unit : " " + unit) : "");
        }
        extra2["text"] = text;
        string detail = Tpl("detail", "", extra2);
        if (detail.Length == 0) detail = $"{label}: {text}";

        // niveau: eigen formule, anders drempels
        string level = "";
        string? lsrc = s.Get("level");
        if (!string.IsNullOrWhiteSpace(lsrc))
        {
            Guard(lsrc);
            var lv = Expr.Parse(lsrc).Eval(new ExprEnv { Vars = vars, Locals = extra2, Now = now });
            string l = Expr.ToText(lv).Trim().ToLowerInvariant();
            level = l is "ok" or "warn" or "crit" ? l : "";
        }
        else if (num is double val && (s.Get("warn") is not null || s.Get("crit") is not null))
        {
            double warn = s.Dbl("warn", double.NaN), crit = s.Dbl("crit", double.NaN);
            bool inv = s.Bool("invert", false);
            bool Hit(double lim) => !double.IsNaN(lim) && (inv ? val <= lim : val >= lim);
            level = Hit(crit) ? "crit" : Hit(warn) ? "warn" : "ok";
        }

        string color = Tpl("color", "", extra2);
        // 'reset' = formule die een tijdstip geeft (bijvoorbeeld five_hour.resets_at): de app kan dan melden dat het bijna zover is
        DateTimeOffset? reset = null;
        string? rsrc = s.Get("reset");
        if (!string.IsNullOrWhiteSpace(rsrc))
        {
            Guard(rsrc);
            if (Funcs.Time(Expr.Parse(rsrc).Eval(new ExprEnv { Vars = vars, Locals = extra2, Now = now })) is double rt)
            {
                try { reset = DateTimeOffset.FromUnixTimeSeconds((long)rt); }
                catch (ArgumentOutOfRangeException) { reset = null; }
            }
        }
        return new PluginItem
        {
            Key = UniqueKey(UptimePlugin.Slug(s.Name), keys), Label = label, Short = s.Get("short") is { } sh ? Tpl("short", sh, extra2) : null,
            Value = num, Min = s.Dbl("min", 0), Max = s.Dbl("max", 100), Unit = unit,
            Text = text, Detail = detail, Level = level, Color = color, Widget = s.Bool("widget", true), Notify = s.Bool("notify", false), Reset = reset,
        };
    }

    private static void Guard(string src) { if (src.Length > Expr.MaxLength * 2) throw new ExprException("the text is too long"); }
}

/// <summary>Vult <c>{secret:naam}</c>, <c>{file:pad#veld}</c> en <c>{env:NAAM}</c> in en onthoudt de ingevulde waarden (om ze uit meldingen te halen).</summary>
internal sealed class TokenResolver
{
    private readonly string _dataDir;
    private readonly IReadOnlyDictionary<string, string> _secrets;
    private readonly List<string> _used;

    public TokenResolver(string dataDir, IReadOnlyDictionary<string, string> secrets, List<string> used) { _dataDir = dataDir; _secrets = secrets; _used = used; }

    public string Apply(string s) => Caps.TokenRe.Replace(s, m =>
    {
        string kind = m.Groups[1].Value.ToLowerInvariant(), arg = m.Groups[2].Value.Trim();
        string v = kind switch
        {
            "secret" => _secrets.TryGetValue(arg, out var sv) ? sv
                : throw new RecipeException($"The secret '{arg}' is not set. Add it under Settings > Plugins > Secrets."),
            "env" => Environment.GetEnvironmentVariable(arg) is { Length: > 0 } ev ? ev
                : throw new RecipeException($"The environment variable '{arg}' is not set."),
            _ => FromFile(arg),
        };
        if (v.Length >= 4) _used.Add(v);
        return v;
    });

    private static string FromFile(string spec)
    {
        int hash = spec.IndexOf('#');
        string path = (hash >= 0 ? spec[..hash] : spec).Trim();
        string field = hash >= 0 ? spec[(hash + 1)..].Trim() : "";
        if (path.StartsWith("~")) path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + path[1..];
        path = Environment.ExpandEnvironmentVariables(path);
        if (!File.Exists(path)) throw new RecipeException($"The file '{path}' was not found.");
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (fs.Length > 1_000_000) throw new RecipeException($"The file '{Path.GetFileName(path)}' is too large.");
        using var r = new StreamReader(fs, Encoding.UTF8);
        string text = r.ReadToEnd();
        if (field.Length == 0) return text.Trim();
        try
        {
            using var doc = JsonDocument.Parse(text);
            var el = ScriptPlugin.SelectPath(doc.RootElement, field);
            string? v = el is { ValueKind: JsonValueKind.String } s ? s.GetString() : el is { ValueKind: JsonValueKind.Number } n ? n.GetRawText() : null;
            return !string.IsNullOrEmpty(v) ? v : throw new RecipeException($"'{field}' was not found in {Path.GetFileName(path)}. Is the program signed in?");
        }
        catch (JsonException) { throw new RecipeException($"{Path.GetFileName(path)} is not a JSON file."); }
    }
}
