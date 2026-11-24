using System.Text;
using System.Text.RegularExpressions;

namespace TaskbarStats.Plugins;

/// <summary>
/// Voert de GEREGISTREERDE plugins uit. Een plugin bestaat pas als zijn configbestand <c>plugins\&lt;id&gt;.ini</c> bestaat: de gebruiker
/// voegt hem toe in de app (Instellingen &gt; Plugins &gt; Plugin toevoegen) en kan hem daar weer verwijderen. Zonder configbestand
/// draait er niets en is er niets te zien. De meegeleverde plugins hebben hun eigen code (id = bestandsnaam); andere bestanden zijn
/// recepten (<c>type = recipe</c>) of eigen bronnen (<c>type = external</c>, de host voert niets uit: een eigen script schrijft het JSON-bestand).
/// Een plugin die uit staat (<c>enabled = false</c>) wordt niet uitgevoerd en laat geen uitvoerbestand achter. Recepten en het script-plugin
/// draaien pas na goedkeuring van wat ze mogen (<see cref="Caps"/>). Een wijziging van het configbestand werkt direct (tijdstempel).
/// </summary>
internal sealed class PluginRunner
{
    private sealed class State
    {
        public DateTime ConfigStamp;
        public DateTimeOffset NextRun = DateTimeOffset.MinValue;
        public bool Running;
        public List<PluginItem> LastGood = new();
        public int Failures;
        public bool Unapproved;
    }

    internal static readonly Regex IdRe = new("^[A-Za-z0-9_-]{1,40}$", RegexOptions.CultureInvariant);
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase) { "approved", "secrets", "host", "cache", "templates", "defaults" };

    private readonly IReadOnlyList<IPlugin> _builtIns;
    private readonly HttpClient _http;
    private readonly HttpClient? _httpStrict;
    private readonly Func<DateTimeOffset> _now;
    private readonly Dictionary<string, State> _state = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public string DataDir { get; }
    public string PluginsDir { get; }
    /// <summary>Tests en <c>--once</c> zetten dit uit; de gewone host controleert altijd.</summary>
    public bool RequireApproval { get; set; } = true;

    public PluginRunner(string dataDir, IReadOnlyList<IPlugin> builtIns, HttpClient http, Func<DateTimeOffset>? now = null, HttpClient? httpStrict = null)
    {
        DataDir = dataDir;
        PluginsDir = Path.Combine(dataDir, "plugins");
        _builtIns = builtIns;
        _http = http;
        _httpStrict = httpStrict;
        _now = now ?? (() => DateTimeOffset.Now);
        Directory.CreateDirectory(PluginsDir);
    }

    public string ConfigPath(string id) => Path.Combine(PluginsDir, id + ".ini");
    public string OutputPath(string id) => Path.Combine(PluginsDir, id + ".json");

    /// <summary>Wat we van één configbestand weten; hergebruikt zolang het bestand (tijdstip en grootte) niet verandert.</summary>
    private sealed record Scanned(DateTime Stamp, long Length, IPlugin? Plugin, bool Enabled);

    private readonly Dictionary<string, Scanned> _scanCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// De geregistreerde plugins die de host moet uitvoeren (meegeleverde met een configbestand, en recepten), met het tijdstip van hun configbestand
    /// en of ze aan staan. Een configbestand wordt alleen opnieuw ingelezen als het veranderd is: de host kijkt elke 1,5 s, en dat hoeft niet
    /// elke keer alle bestanden te parsen.
    /// </summary>
    private List<(IPlugin Plugin, DateTime Stamp, bool Enabled)> Scan()
    {
        var res = new List<(IPlugin, DateTime, bool)>();
        FileInfo[] files;
        try { files = new DirectoryInfo(PluginsDir).EnumerateFiles("*.ini").OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray(); }
        catch (IOException) { return res; }
        foreach (var fi in files)
        {
            string id = Path.GetFileNameWithoutExtension(fi.Name);
            if (!IdRe.IsMatch(id) || Reserved.Contains(id)) continue;
            Scanned? sc;
            lock (_lock) _scanCache.TryGetValue(id, out sc);
            if (sc is null || sc.Stamp != fi.LastWriteTimeUtc || sc.Length != fi.Length)
            {
                IPlugin? plugin = _builtIns.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
                bool enabled;
                try
                {
                    var cfg = IniFile.Load(fi.FullName);
                    enabled = cfg.Enabled;
                    if (plugin is null && string.Equals(cfg.Str("type", ""), "recipe", StringComparison.OrdinalIgnoreCase))
                        plugin = new RecipePlugin(id, cfg.Str("title", id));
                }
                catch (IOException) { continue; }   // net half geschreven of vergrendeld: de volgende ronde opnieuw
                sc = new Scanned(fi.LastWriteTimeUtc, fi.Length, plugin, enabled);
                lock (_lock) _scanCache[id] = sc;
            }
            if (sc.Plugin is not null) res.Add((sc.Plugin, sc.Stamp, sc.Enabled));
        }
        return res;
    }

    /// <summary>De geregistreerde plugins die de host moet uitvoeren.</summary>
    public List<IPlugin> Discover() => Scan().Select(s => s.Plugin).ToList();

    /// <summary>Zoekt één plugin op id (voor <c>--once</c>): meegeleverd, of een recept als het configbestand er is.</summary>
    public IPlugin? Resolve(string id)
    {
        var built = _builtIns.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        if (built is not null) return built;
        if (!IdRe.IsMatch(id) || !File.Exists(ConfigPath(id))) return null;
        var cfg = IniFile.Load(ConfigPath(id));
        return string.Equals(cfg.Str("type", ""), "recipe", StringComparison.OrdinalIgnoreCase) ? new RecipePlugin(id, cfg.Str("title", id)) : null;
    }

    public bool AnyEnabled() => Scan().Any(s => s.Enabled);

    private State StateOf(string id)
    {
        lock (_lock)
        {
            if (!_state.TryGetValue(id, out var st)) _state[id] = st = new State();
            return st;
        }
    }

    /// <summary>Eén rondje: start elke plugin die aan de beurt is (op de achtergrond). Geeft de gestarte taken terug (tests wachten erop).</summary>
    public List<Task> Tick(CancellationToken ct)
    {
        var started = new List<Task>();
        var scan = Scan();
        CleanUpRemoved(scan.Select(s => s.Plugin).ToList());
        foreach (var (p, stamp, _) in scan)
        {
            var st = StateOf(p.Id);
            lock (_lock)
            {
                if (st.Running) continue;
                if (stamp != st.ConfigStamp) { st.ConfigStamp = stamp; st.NextRun = DateTimeOffset.MinValue; }
                if (_now() < st.NextRun) continue;
                st.Running = true;
            }
            started.Add(Task.Run(async () =>
            {
                try { await RunPluginAsync(p, false, ct).ConfigureAwait(false); }
                catch (Exception ex) { HostLog.Write("ERROR", p.Id + ": " + Fmt.Scrub(ex.Message)); }
                finally { lock (_lock) st.Running = false; }
            }, ct));
        }
        return started;
    }

    /// <summary>Een plugin waarvan het configbestand weg is (verwijderd in de app of met de hand): uitvoerbestand weg, status vergeten.</summary>
    private void CleanUpRemoved(List<IPlugin> found)
    {
        List<string> gone;
        lock (_lock) gone = _state.Keys.Where(id => !found.Any(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))).ToList();
        foreach (var id in gone)
        {
            try { if (File.Exists(OutputPath(id))) File.Delete(OutputPath(id)); }
            catch (IOException) { }
            lock (_lock) _state.Remove(id);
            HostLog.Write("INFO", $"{id}: removed");
        }
    }

    /// <summary>Voert één plugin uit en schrijft het resultaat. <paramref name="ignoreEnabled"/>: voor <c>--once</c> en tests (geen aan/uit en geen goedkeuring).</summary>
    public async Task<PluginResult?> RunPluginAsync(IPlugin p, bool ignoreEnabled, CancellationToken ct)
    {
        var cfg = IniFile.Load(ConfigPath(p.Id));
        var st = StateOf(p.Id);
        int interval = cfg.Int("interval", p.DefaultIntervalSec, 10, 86400);
        var now = _now();

        if (!cfg.Enabled && !ignoreEnabled)
        {
            try { if (File.Exists(OutputPath(p.Id))) File.Delete(OutputPath(p.Id)); } catch (IOException) { }
            lock (_lock) { st.LastGood = new(); st.Failures = 0; st.NextRun = now.AddSeconds(2); st.Unapproved = false; }
            return null;
        }

        // Recepten en het script-plugin: alleen uitvoeren als de gebruiker precies deze lijst (adressen, bestanden, opdrachten) heeft goedgekeurd.
        if (RequireApproval && !ignoreEnabled && Caps.NeedsApproval(p.Id, cfg))
        {
            string hash = Caps.Compute(cfg).Hash();
            if (!Approvals.IsApproved(DataDir, p.Id, hash))
            {
                var denied = PluginResult.Fail("Needs your approval. Open TaskbarStats > Settings > Plugins and choose 'Review and approve'.");
                bool first;
                lock (_lock) { first = !st.Unapproved; st.Unapproved = true; st.NextRun = _now().AddSeconds(30); }
                if (first) HostLog.Write("WARN", $"{p.Id}: waiting for approval");
                OutputFile.WriteAtomic(OutputPath(p.Id), OutputFile.Render(p.Id, p.Title, denied, "error", interval, _now()));
                return denied;
            }
            lock (_lock) st.Unapproved = false;
        }

        var ctx = new PluginContext
        {
            Config = cfg, Http = _http, HttpStrictClient = _httpStrict, DataDir = DataDir, Now = _now,
            // een recept krijgt alleen de geheimen die het zelf noemt ({secret:naam}, dezelfde lijst die de gebruiker heeft goedgekeurd)
            Secrets = p is RecipePlugin ? Secrets.LoadAll(DataDir, Caps.Compute(cfg).Secrets) : new Dictionary<string, string>(),
        };
        PluginResult result;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(cfg.Int("timeout", 25, 3, 300)));
        try { result = await p.RunAsync(ctx, cts.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { result = PluginResult.Fail("timed out"); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { result = PluginResult.Fail(Fmt.Scrub(ex.Message)); }

        string status;
        int next;
        lock (_lock)
        {
            if (result.Error)
            {
                st.Failures++;
                if (result.Items.Count == 0) result.Items.AddRange(st.LastGood);   // oude waarden blijven (als verouderd) zichtbaar
                status = "error";
                // terugvallen: 30 s, 60 s, 120 s ... maar nooit langer dan het normale interval (of wat de server vraagt)
                next = Math.Max(result.RetryAfterSec, Math.Min(interval, 30 << Math.Min(st.Failures - 1, 6)));
                HostLog.Write("WARN", $"{p.Id}: {result.Message}");
            }
            else
            {
                st.Failures = 0;
                st.LastGood = new List<PluginItem>(result.Items);
                status = "ok";
                next = interval;
            }
            st.NextRun = _now().AddSeconds(next);
        }
        OutputFile.WriteAtomic(OutputPath(p.Id), OutputFile.Render(p.Id, p.Title, result, status, interval, _now()));
        return result;
    }
}

/// <summary>Klein logboek naast de uitvoerbestanden (<c>plugins\host.log</c>, maximaal ~200 KB, daarna opnieuw beginnen).</summary>
internal static class HostLog
{
    public static string? Path { get; set; }
    private static readonly object L = new();

    public static void Write(string level, string msg)
    {
        if (Path is null) return;
        try
        {
            lock (L)
            {
                var fi = new FileInfo(Path);
                if (fi.Exists && fi.Length > 200_000) File.Move(Path, Path + ".old", true);
                File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level,-5} {msg}{Environment.NewLine}");
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Map met de crashrapporten: dezelfde als die van de app (<c>&lt;datamap&gt;\crashes</c>), zodat het logvenster van de app ze toont.</summary>
    public static string? CrashDir => Path is null ? null : System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(Path)!)!, "crashes");

    /// <summary>
    /// Bewaart een fout van de host als crashrapport (<c>host-crash-*.txt</c>, met de volledige stack) en zet hem ook in host.log.
    /// Mag nooit zelf een fout geven; tokens worden eruit gehaald.
    /// </summary>
    public static void WriteCrash(string kind, Exception? ex, string? raw = null)
    {
        try
        {
            string detail = Fmt.Scrub(ex is AggregateException ag ? ag.Flatten().ToString() : ex?.ToString() ?? raw ?? "(no details)");
            Write("ERROR", kind + ": " + detail);
            string? dir = CrashDir;
            if (dir is null) return;
            Directory.CreateDirectory(dir);
            string file = System.IO.Path.Combine(dir, $"host-crash-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            for (int i = 2; File.Exists(file); i++) file = System.IO.Path.Combine(dir, $"host-crash-{DateTime.Now:yyyyMMdd-HHmmss}-{i}.txt");
            var sb = new StringBuilder();
            sb.AppendLine("TaskbarStats plugin host crash report");
            sb.AppendLine("Kind: " + kind);
            sb.AppendLine("Time: " + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"));
            sb.AppendLine("Version: " + Program.Version + ", pid " + Environment.ProcessId);
            sb.AppendLine("Windows: " + Environment.OSVersion.Version + ", " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
            sb.AppendLine();
            sb.AppendLine("--- error ---");
            sb.AppendLine(detail);
            File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
            foreach (var old in new DirectoryInfo(dir).EnumerateFiles("host-crash-*.txt").OrderByDescending(f => f.LastWriteTimeUtc).Skip(15))
                try { old.Delete(); } catch (IOException) { }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
