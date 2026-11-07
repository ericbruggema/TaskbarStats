using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace TaskbarStats.Plugins;

/// <summary>
/// Claude-gebruik: hoeveel van de 5-uurslimiet en de weeklimiet van je abonnement is verbruikt, en wanneer die weer vrijkomt.
/// Dit gebruikt de aanmelding van Claude Code zelf (<c>~/.claude/.credentials.json</c>, wordt alleen gelezen; of de omgevingsvariabele <c>CLAUDE_CODE_OAUTH_TOKEN</c>) en vraagt de
/// gebruiksgegevens op bij Anthropic, hetzelfde als het <c>/usage</c>-scherm van Claude Code. Let op: dat is een
/// <b>ongedocumenteerd</b> adres dat Anthropic kan wijzigen; faalt het, dan toont de plugin een duidelijke melding en blijven de
/// laatste waarden (als verouderd) staan. Het token verlaat de pc alleen naar api.anthropic.com en komt nooit in een bestand of log.
/// </summary>
internal sealed class ClaudePlugin : IPlugin
{
    public string Id => "claude";
    public string Title => "Claude usage";
    public int DefaultIntervalSec => 300;

    internal const string UsageUrl = "https://api.anthropic.com/api/oauth/usage";

    public async Task<PluginResult> RunAsync(PluginContext ctx, CancellationToken ct)
    {
        var cfg = ctx.Config;
        string? token = Environment.GetEnvironmentVariable("CLAUDE_CODE_OAUTH_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            string? credPath = FindCredentials(cfg.Str("credentials", "auto"));
            if (credPath is null)
                return PluginResult.Fail("Claude Code sign-in not found. Run 'claude' in a terminal and use /login (or set 'credentials =' to the path of .credentials.json).");
            try { token = ReadToken(credPath); }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                return PluginResult.Fail("Cannot read the Claude Code sign-in file: " + ex.GetType().Name);
            }
            if (token is null)
                return PluginResult.Fail("The Claude Code sign-in file has no Claude subscription sign-in (only connector tokens). Run 'claude' in a terminal and use /login.");
        }

        string url = cfg.Str("usage_url", UsageUrl);   // alleen voor tests / als Anthropic het adres wijzigt
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.TryAddWithoutValidation("anthropic-beta", "oauth-2025-04-20");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage resp;
        try { resp = await ctx.Http.SendAsync(req, ct).ConfigureAwait(false); }
        catch (HttpRequestException ex) { return PluginResult.Fail("Network error: " + ex.Message); }
        using (resp)
        {
            if (resp.StatusCode == HttpStatusCode.Unauthorized || resp.StatusCode == HttpStatusCode.Forbidden)
                return PluginResult.Fail("Claude sign-in expired or not allowed (HTTP " + (int)resp.StatusCode + "). Use Claude Code once to refresh it.");
            if (resp.StatusCode == (HttpStatusCode)429)
            {
                int wait = (int)(resp.Headers.RetryAfter?.Delta?.TotalSeconds ?? 300);
                return PluginResult.Fail("Rate limited by the server; trying again later.", Math.Clamp(wait, 60, 3600));
            }
            if (!resp.IsSuccessStatusCode) return PluginResult.Fail("Usage request failed: HTTP " + (int)resp.StatusCode);
            string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return Parse(body, cfg, ctx.Now());
        }
    }

    // ---------- aanmelding ----------
    internal static string? FindCredentials(string setting)
    {
        if (!string.Equals(setting, "auto", StringComparison.OrdinalIgnoreCase))
            return File.Exists(setting) ? setting : null;
        var dirs = new List<string>();
        var env = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        if (!string.IsNullOrWhiteSpace(env)) dirs.Add(env);
        dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude"));
        foreach (var d in dirs)
        {
            var p = Path.Combine(d, ".credentials.json");
            if (File.Exists(p)) return p;
        }
        return null;
    }

    /// <summary>Het toegangstoken uit het bestand, of null als het bestand geen abonnementsaanmelding bevat.</summary>
    internal static string? ReadToken(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var doc = JsonDocument.Parse(fs);
        if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("claudeAiOauth", out var o)
            && o.ValueKind == JsonValueKind.Object && o.TryGetProperty("accessToken", out var t) && t.GetString() is { Length: > 0 } s)
            return s;
        return null;
    }

    // ---------- antwoord ----------
    private static readonly (string Key, string Label, string Short, bool Main)[] Known =
    {
        ("five_hour", "Claude 5-hour limit", "5h", true),
        ("seven_day", "Claude weekly limit", "7d", true),
        ("seven_day_opus", "Claude weekly limit (Opus)", "7d Opus", false),
        ("seven_day_sonnet", "Claude weekly limit (Sonnet)", "7d Sonnet", false),
    };

    internal static PluginResult Parse(string json, IniFile cfg, DateTimeOffset now)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { return PluginResult.Fail("Unexpected reply from the server (not JSON)."); }
        using (doc)
        {
            bool remaining = string.Equals(cfg.Str("show", "used"), "remaining", StringComparison.OrdinalIgnoreCase);
            bool showReset = cfg.Bool("show_reset", false);
            double warn = cfg.Dbl("warn", 70), crit = cfg.Dbl("crit", 90);
            bool notify = cfg.Bool("notify", true), others = cfg.Bool("other_limits", false);
            var res = new PluginResult();
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return PluginResult.Fail("Unexpected reply from the server.");

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object) continue;
                if (!prop.Value.TryGetProperty("utilization", out var u)) continue;
                double used = u.ValueKind == JsonValueKind.Number ? u.GetDouble() : 0;   // null = nog niet gebruikt
                if (!double.IsFinite(used)) used = 0;
                used = Math.Clamp(used, 0, 100);

                var known = Known.FirstOrDefault(k => k.Key == prop.Name);
                bool isMain = known.Key is not null && known.Main;
                if (known.Key is not null) { if (!isMain && !cfg.Bool(prop.Name, false)) continue; }
                else if (!others) continue;
                if (isMain && !cfg.Bool(prop.Name, true)) continue;

                DateTimeOffset? resets = null;
                if (prop.Value.TryGetProperty("resets_at", out var r) && r.ValueKind == JsonValueKind.String
                    && DateTimeOffset.TryParse(r.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var rt))
                    resets = rt.ToLocalTime();

                double shown = remaining ? 100 - used : used;
                string resetIn = resets is { } rs ? Fmt.Duration(rs - now) : "";
                string text = Fmt.Num(shown) + "%" + (showReset && resetIn != "" ? " · " + resetIn : "");
                string label = known.Key is not null ? known.Label : "Claude " + prop.Name.Replace('_', ' ');
                string detail = $"{Fmt.Num(used)}% used" + (resets is { } r2 ? $", resets in {resetIn} ({r2:ddd HH:mm})" : "");

                res.Items.Add(new PluginItem
                {
                    Key = prop.Name, Label = label, Short = known.Key is not null ? known.Short : prop.Name,
                    Value = shown, Min = 0, Max = 100, Unit = "%", Text = text, Detail = detail,
                    Level = Fmt.Level(used, warn, crit), Widget = isMain, Notify = notify && isMain,
                });
            }
            if (res.Items.Count == 0) return PluginResult.Fail("The server reply contains no usage limits.");
            return res;
        }
    }
}
