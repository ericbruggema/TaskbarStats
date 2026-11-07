using System.Text;

namespace TaskbarStats.Plugins;

/// <summary>
/// Voert de plugins uit volgens hun eigen configbestand (<c>plugins\&lt;id&gt;.ini</c>) en schrijft het resultaat naar
/// <c>plugins\&lt;id&gt;.json</c>. Een plugin die uit staat (<c>enabled = false</c>) wordt niet uitgevoerd en laat geen bestand achter.
/// Een wijziging van het configbestand werkt direct (de host kijkt naar de tijdstempel).
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
    }

    private readonly IReadOnlyList<IPlugin> _plugins;
    private readonly HttpClient _http;
    private readonly Func<DateTimeOffset> _now;
    private readonly Dictionary<string, State> _state = new();
    private readonly object _lock = new();

    public string DataDir { get; }
    public string PluginsDir { get; }
    public string DefaultsDir { get; }

    public PluginRunner(string dataDir, IReadOnlyList<IPlugin> plugins, HttpClient http, string? defaultsDir = null, Func<DateTimeOffset>? now = null)
    {
        DataDir = dataDir;
        PluginsDir = Path.Combine(dataDir, "plugins");
        DefaultsDir = defaultsDir ?? Path.Combine(AppContext.BaseDirectory, "defaults");
        _plugins = plugins;
        _http = http;
        _now = now ?? (() => DateTimeOffset.Now);
        Directory.CreateDirectory(PluginsDir);
        foreach (var p in plugins) _state[p.Id] = new State();
    }

    public string ConfigPath(string id) => Path.Combine(PluginsDir, id + ".ini");
    public string OutputPath(string id) => Path.Combine(PluginsDir, id + ".json");

    /// <summary>Zet het standaardconfigbestand neer als de gebruiker er nog geen heeft.</summary>
    public void EnsureConfig(IPlugin p)
    {
        var path = ConfigPath(p.Id);
        if (File.Exists(path)) return;
        var def = Path.Combine(DefaultsDir, p.Id + ".ini");
        string text = File.Exists(def) ? File.ReadAllText(def, Encoding.UTF8) : $"# {p.Title}\nenabled = false\n";
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }

    public bool AnyEnabled()
    {
        foreach (var p in _plugins)
        {
            EnsureConfig(p);
            if (IniFile.Load(ConfigPath(p.Id)).Enabled) return true;
        }
        return false;
    }

    /// <summary>Eén rondje: start elke plugin die aan de beurt is (op de achtergrond). Geeft de gestarte taken terug (tests wachten erop).</summary>
    public List<Task> Tick(CancellationToken ct)
    {
        var started = new List<Task>();
        foreach (var p in _plugins)
        {
            State st;
            lock (_lock) st = _state[p.Id];
            EnsureConfig(p);
            var path = ConfigPath(p.Id);
            DateTime stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
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

    /// <summary>Voert één plugin uit en schrijft het resultaat. <paramref name="ignoreEnabled"/>: voor <c>--once</c> en tests.</summary>
    public async Task<PluginResult?> RunPluginAsync(IPlugin p, bool ignoreEnabled, CancellationToken ct)
    {
        EnsureConfig(p);
        var cfg = IniFile.Load(ConfigPath(p.Id));
        State st;
        lock (_lock) st = _state[p.Id];
        int interval = cfg.Int("interval", p.DefaultIntervalSec, 10, 86400);
        var now = _now();

        if (!cfg.Enabled && !ignoreEnabled)
        {
            try { if (File.Exists(OutputPath(p.Id))) File.Delete(OutputPath(p.Id)); } catch (IOException) { }
            lock (_lock) { st.LastGood = new(); st.Failures = 0; st.NextRun = now.AddSeconds(2); }
            return null;
        }

        var ctx = new PluginContext { Config = cfg, Http = _http, DataDir = DataDir, Now = _now };
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
}
