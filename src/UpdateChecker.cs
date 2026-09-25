using System.Net.Http;
using System.Text.Json;

namespace TaskbarStats;

public enum UpdateStatus { Skipped, UpToDate, Newer, Failed }

public sealed record UpdateInfo(string Tag, string Url);

public sealed record UpdateResult(UpdateStatus Status, UpdateInfo? Info = null, string? Error = null);

/// <summary>
/// Controle op een nieuwere versie via de GitHub-releases (alleen als de gebruiker dat aanzet, hoogstens 1× per 24 uur).
/// Het netwerkverkeer draait nooit op de UI-thread (via <see cref="Task.Run(Action)"/>). Er wordt niets gedownload
/// of geïnstalleerd; de gebruiker opent zelf de release-pagina.
/// </summary>
public static class UpdateChecker
{
    public const string DefaultUrl = "https://api.github.com/repos/ericbruggema/TaskbarStats/releases/latest";
    private const string FallbackPage = "https://github.com/ericbruggema/TaskbarStats/releases/latest";

    /// <summary>Nieuwere versie waarvan we weten (of null). Wordt bijgewerkt door <see cref="RunAsync"/> en <see cref="Restore"/>.</summary>
    public static UpdateInfo? Available { get; private set; }

    /// <summary>Wordt afgevuurd (op de aanroepende thread van <see cref="RunAsync"/>) als er een nieuwere versie is gevonden.</summary>
    public static event Action? Found;

    private static long _lastTry;   // tickcount van de laatste poging (na een mislukte poging minstens 1 uur wachten)

    private static readonly HttpClient Http = MakeClient();

    private static HttpClient MakeClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("TaskbarStats-UpdateCheck");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    // ---------- Vergelijken ----------

    /// <summary>"v1.3.0", "1.3.0-beta1", "1.10" → versie (ontbrekende delen = 0) en of het een prerelease is.</summary>
    public static bool TryParseTag(string? tag, out Version version, out bool prerelease)
    {
        version = new Version(0, 0, 0, 0);
        prerelease = false;
        if (string.IsNullOrWhiteSpace(tag)) return false;
        string s = tag.Trim().TrimStart('v', 'V');
        int plus = s.IndexOf('+');
        if (plus >= 0) s = s[..plus];
        int dash = s.IndexOf('-');
        if (dash >= 0) { prerelease = true; s = s[..dash]; }
        var parts = s.Split('.');
        if (parts.Length is < 1 or > 4) return false;
        var n = new int[4];
        for (int i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out n[i])) return false;
        version = new Version(n[0], n[1], n[2], n[3]);
        return true;
    }

    /// <summary>Is <paramref name="tag"/> een nieuwere, stabiele versie dan <paramref name="current"/>?</summary>
    public static bool IsNewer(string? tag, string? current)
    {
        if (!TryParseTag(tag, out var latest, out bool latestPre) || latestPre) return false;
        if (!TryParseTag(current, out var cur, out bool curPre)) return false;
        int c = latest.CompareTo(cur);
        return c > 0 || (c == 0 && curPre);   // 1.4.0-beta -> 1.4.0 telt ook als nieuwer
    }

    /// <summary>Beoordeelt het JSON-antwoord van GitHub (/releases/latest).</summary>
    public static UpdateResult Evaluate(string json, string current)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return new(UpdateStatus.Failed, null, "onverwacht antwoord");
            string? tag = root.TryGetProperty("tag_name", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            if (!TryParseTag(tag, out _, out _)) return new(UpdateStatus.Failed, null, "onbekende versietag");
            bool draft = root.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True;
            bool pre = root.TryGetProperty("prerelease", out var p) && p.ValueKind == JsonValueKind.True;
            if (draft || pre || !IsNewer(tag, current)) return new(UpdateStatus.UpToDate);
            // Alleen een https-adres op github.com openen we later; iets anders uit het antwoord vervangen we door de vaste pagina.
            string url = FallbackPage;
            if (root.TryGetProperty("html_url", out var u) && u.ValueKind == JsonValueKind.String
                && Uri.TryCreate(u.GetString(), UriKind.Absolute, out var uri)
                && uri.Scheme == Uri.UriSchemeHttps && (uri.Host == "github.com" || uri.Host.EndsWith(".github.com")))
                url = uri.AbsoluteUri;
            return new(UpdateStatus.Newer, new UpdateInfo(tag!.Trim(), url));
        }
        catch (Exception ex) { return new(UpdateStatus.Failed, null, ex.Message); }
    }

    // ---------- Ophalen ----------

    /// <summary>Haalt de release-JSON op. TASKBARSTATS_UPDATE_URL mag een http(s)-adres, file://-adres of bestandspad zijn (voor tests).</summary>
    public static async Task<string> FetchAsync()
    {
        string url = Environment.GetEnvironmentVariable("TASKBARSTATS_UPDATE_URL") is { Length: > 0 } o ? o : DefaultUrl;
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return await Http.GetStringAsync(url).ConfigureAwait(false);
        if (url.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) url = new Uri(url).LocalPath;
        return await File.ReadAllTextAsync(url).ConfigureAwait(false);
    }

    /// <summary>Controleert (asynchroon, buiten de UI-thread). Aanroepen vanaf de UI-thread: het vervolg (opslaan) landt daar ook weer.</summary>
    public static async Task<UpdateResult> RunAsync(AppSettings cfg, string current, bool force)
    {
        if (!force)
        {
            if (!cfg.CheckUpdates) return new(UpdateStatus.Skipped);
            if (cfg.LastUpdateCheck is DateTime last && DateTime.UtcNow - last < TimeSpan.FromHours(24)) return new(UpdateStatus.Skipped);
            if (_lastTry != 0 && Environment.TickCount64 - _lastTry < 3_600_000) return new(UpdateStatus.Skipped);
        }
        _lastTry = Environment.TickCount64;

        UpdateResult r;
        try
        {
            string json = await Task.Run(FetchAsync);
            r = Evaluate(json, current);
        }
        catch (Exception ex) { r = new(UpdateStatus.Failed, null, ex is TaskCanceledException ? "time-out" : ex.Message); }

        if (r.Status is UpdateStatus.UpToDate or UpdateStatus.Newer)
        {
            cfg.LastUpdateCheck = DateTime.UtcNow;
            cfg.UpdateLatestTag = r.Info?.Tag;
            cfg.UpdateLatestUrl = r.Info?.Url;
            cfg.Save();
            Available = r.Info;
            if (r.Info is not null) Found?.Invoke();
        }
        return r;
    }

    /// <summary>Bij het opstarten: onthoud een eerder gevonden nieuwere versie (of wis die als je inmiddels bijgewerkt bent).</summary>
    public static void Restore(AppSettings cfg, string current)
    {
        Available = cfg.CheckUpdates && cfg.UpdateLatestTag is { Length: > 0 } tag && IsNewer(tag, current)
            ? new UpdateInfo(tag, cfg.UpdateLatestUrl is { Length: > 0 } u ? u : FallbackPage)
            : null;
    }
}
