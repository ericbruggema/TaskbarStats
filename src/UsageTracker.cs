using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskbarStats;

/// <summary>Ontvangen/verzonden bytes.</summary>
public sealed class AdapterUsage
{
    public long Down { get; set; }
    public long Up { get; set; }
    [JsonIgnore] public long Total => Down + Up;
}

/// <summary>
/// Houdt per netwerkadapter bij hoeveel er is ontvangen/verzonden: sinds het starten (sessie)
/// en per dag (bewaard in usage.json). Telt de verschillen van de cumulatieve tellers van Windows
/// op, dus nauwkeurig en bestand tegen een reboot (teller terug naar 0).
/// Verkeer terwijl de app niet draait wordt niet geteld.
/// </summary>
public sealed class UsageTracker
{
    private readonly string _path;
    // "yyyy-MM-dd" -> adapternaam -> gebruik
    private Dictionary<string, Dictionary<string, AdapterUsage>> _days = new();
    private readonly Dictionary<string, (long rx, long tx)> _last = new();
    private readonly Dictionary<string, AdapterUsage> _session = new();
    private long _lastSample, _lastSave;
    private bool _dirty;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public UsageTracker(string directory)
    {
        _path = Path.Combine(directory, "usage.json");
        try
        {
            if (File.Exists(_path))
                _days = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, AdapterUsage>>>(File.ReadAllText(_path)) ?? new();
        }
        catch { _days = new(); }

        // Oude dagen opruimen (max. ~14 maanden bewaren).
        string cutoff = Key(DateTime.Now.AddDays(-430));
        foreach (var k in _days.Keys.Where(k => string.CompareOrdinal(k, cutoff) < 0).ToList()) _days.Remove(k);
    }

    private static string Key(DateTime d) => d.ToString("yyyy-MM-dd");

    /// <summary>Zelfde naam als de Performance-Counter-instance (haakjes/#/slashes vervangen).</summary>
    public static string PerfName(string description)
        => description.Replace('(', '[').Replace(')', ']').Replace('#', '_').Replace('\\', '_').Replace('/', '_');

    /// <summary>Maximaal 1x per seconde de tellers uitlezen.</summary>
    public void Sample()
    {
        long now = Environment.TickCount64;
        if (now - _lastSample < 1000) return;
        _lastSample = now;
        string day = Key(DateTime.Now);
        var seen = new Dictionary<string, int>();

        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                long rx, tx;
                try { var st = ni.GetIPv4Statistics(); rx = st.BytesReceived; tx = st.BytesSent; }
                catch { continue; }

                string key = PerfName(ni.Description);
                seen[key] = seen.TryGetValue(key, out int n) ? n + 1 : 1;
                if (seen[key] > 1) key += "_" + seen[key];

                if (_last.TryGetValue(key, out var l))
                {
                    long dr = rx >= l.rx ? rx - l.rx : rx;     // teller gereset (reboot/adapter opnieuw)
                    long dt = tx >= l.tx ? tx - l.tx : tx;
                    if (dr > 0 || dt > 0) Add(day, key, dr, dt);
                }
                _last[key] = (rx, tx);
            }
        }
        catch { }

        if (_dirty && now - _lastSave > 60_000) Save();
    }

    private void Add(string day, string adapter, long down, long up)
    {
        if (!_days.TryGetValue(day, out var d)) _days[day] = d = new();
        if (!d.TryGetValue(adapter, out var u)) d[adapter] = u = new();
        u.Down += down; u.Up += up;
        if (!_session.TryGetValue(adapter, out var s)) _session[adapter] = s = new();
        s.Down += down; s.Up += up;
        _dirty = true;
    }

    public void Save()
    {
        _lastSave = Environment.TickCount64;
        _dirty = false;
        try { File.WriteAllText(_path, JsonSerializer.Serialize(_days, Json)); }
        catch { }
    }

    public void Clear()
    {
        _days.Clear();
        _session.Clear();
        Save();
    }

    // ---------- Opvragen ----------

    /// <summary>Totaal over een periode (inclusief begin en einde), voor één adapter of alle (null).</summary>
    public AdapterUsage Sum(DateTime from, DateTime to, string? adapter = null)
    {
        var r = new AdapterUsage();
        string a = Key(from), b = Key(to);
        foreach (var (day, list) in _days)
        {
            if (string.CompareOrdinal(day, a) < 0 || string.CompareOrdinal(day, b) > 0) continue;
            foreach (var (name, u) in list)
                if (adapter is null || adapter == name) { r.Down += u.Down; r.Up += u.Up; }
        }
        return r;
    }

    public AdapterUsage Session(string? adapter = null)
    {
        var r = new AdapterUsage();
        foreach (var (name, u) in _session)
            if (adapter is null || adapter == name) { r.Down += u.Down; r.Up += u.Up; }
        return r;
    }

    public AdapterUsage Today(string? adapter = null) => Sum(DateTime.Now, DateTime.Now, adapter);
    public AdapterUsage Yesterday(string? adapter = null) => Sum(DateTime.Now.AddDays(-1), DateTime.Now.AddDays(-1), adapter);
    public AdapterUsage Week(string? adapter = null) => Sum(DateTime.Now.AddDays(-6), DateTime.Now, adapter);
    public AdapterUsage Month(string? adapter = null)
    {
        var n = DateTime.Now;
        return Sum(new DateTime(n.Year, n.Month, 1), n, adapter);
    }

    /// <summary>Adapters met verkeer in deze maand of deze sessie.</summary>
    public List<string> KnownAdapters()
    {
        var n = DateTime.Now;
        string from = Key(new DateTime(n.Year, n.Month, 1));
        var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (day, list) in _days)
            if (string.CompareOrdinal(day, from) >= 0) foreach (var name in list.Keys) set.Add(name);
        foreach (var name in _session.Keys) set.Add(name);
        return set.ToList();
    }

    /// <summary>Alle dagen (nieuwste eerst) met per adapter het gebruik, voor het logvenster.</summary>
    public List<(DateTime day, Dictionary<string, AdapterUsage> perAdapter)> Days(int lastDays)
    {
        string from = Key(DateTime.Now.AddDays(-(lastDays - 1)));
        return _days.Where(kv => string.CompareOrdinal(kv.Key, from) >= 0)
                    .OrderByDescending(kv => kv.Key)
                    .Select(kv => (DateTime.ParseExact(kv.Key, "yyyy-MM-dd", null), kv.Value))
                    .ToList();
    }
}
