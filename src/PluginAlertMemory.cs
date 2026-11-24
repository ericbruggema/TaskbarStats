using System.Text.Json;

namespace TaskbarStats;

/// <summary>
/// Onthoudt welke plugin-meldingen al getoond zijn, ook na een herstart van de app (<c>plugin-alerts.json</c> in de datamap).
/// Zonder dit kwam elke herstart of herinstallatie dezelfde "limiet bijna op"-melding opnieuw. Een melding komt alleen als de toestand
/// (de <i>handtekening</i>) anders is dan de laatste keer, of - als de gebruiker dat wil - na een instelbaar aantal minuten.
/// </summary>
internal sealed class PluginAlertMemory
{
    private sealed class Entry { public string Sig { get; set; } = ""; public DateTime AtUtc { get; set; } }

    private readonly string _path;
    private readonly Dictionary<string, Entry> _items = new();
    private bool _dirty;

    public PluginAlertMemory(string? dataDir = null)
    {
        _path = Path.Combine(dataDir ?? AppPaths.DataDir, "plugin-alerts.json");
        try
        {
            if (File.Exists(_path))
                foreach (var kv in JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(_path)) ?? new())
                    if (kv.Value is not null) _items[kv.Key] = kv.Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { Diag.Swallow(ex); }
    }

    /// <summary>Moet er nu gemeld worden? Ja als er nog niets gemeld is, de toestand anders is, of (bij <paramref name="repeatMin"/> &gt; 0) de laatste melding lang genoeg geleden is.</summary>
    public bool ShouldNotify(string key, string sig, int repeatMin, DateTime nowUtc)
    {
        if (!_items.TryGetValue(key, out var e)) return true;
        if (e.Sig != sig) return true;
        return repeatMin > 0 && (nowUtc - e.AtUtc).TotalMinutes >= repeatMin;
    }

    public void Mark(string key, string sig, DateTime nowUtc)
    {
        _items[key] = new Entry { Sig = sig, AtUtc = nowUtc };
        _dirty = true;
        Save();
    }

    /// <summary>De toestand is weer normaal: de volgende keer dat het misgaat komt er opnieuw een melding.</summary>
    public void Forget(string key)
    {
        if (_items.Remove(key)) { _dirty = true; Save(); }
    }

    private void Save()
    {
        if (!_dirty) return;
        _dirty = false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_items), new System.Text.UTF8Encoding(false));
            File.Move(tmp, _path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Diag.Swallow(ex); }
    }
}

/// <summary>Een melding die de regels hebben besloten te tonen.</summary>
internal sealed record PluginAlert(string Title, string Text, bool Warning);

/// <summary>
/// De regels voor plugin-meldingen (los van het venster, dus te testen):
/// 1. Een waarde die op "kritiek" springt en waarvan de plugin een melding wil, geeft één melding per toestand. Blijft ze kritiek, dan komt
///    er geen tweede, tenzij de gebruiker herhalen heeft aangezet (<see cref="AppSettings.PluginAlertRepeatMin"/>); verandert er iets
///    (bijvoorbeeld een tweede site valt uit) dan komt er wel een nieuwe. Is de waarde weer normaal, dan telt een volgende keer als nieuw.
/// 2. Een waarde die op "waarschuwing" of hoger staat en een terugzettijd heeft (een limiet die vrijkomt), geeft kort van tevoren één melding
///    ("bijna weer vrij", <see cref="AppSettings.PluginResetNotifyMin"/> minuten), één keer per terugzetmoment.
/// </summary>
internal static class PluginAlertRules
{
    public static List<PluginAlert> Evaluate(IReadOnlyList<PluginValue> values, AppSettings cfg, PluginAlertMemory mem, DateTime nowUtc)
    {
        var res = new List<PluginAlert>();
        foreach (var v in values)
        {
            if (!cfg.PluginShown(v)) continue;
            string key = "plugin:" + v.Id, rkey = "reset:" + v.Id;

            bool crit = v.Notify && v.Level == "crit" && !v.Stale;
            if (!crit) mem.Forget(key);
            else
            {
                string sig = Signature(v);
                if (mem.ShouldNotify(key, sig, cfg.PluginAlertRepeatMin, nowUtc))
                {
                    res.Add(new PluginAlert(v.Label, AlertText(v), true));
                    mem.Mark(key, sig, nowUtc);
                }
            }

            int min = cfg.PluginResetNotifyMin;
            if (min > 0 && v.Notify && !v.Stale && v.ResetUtc is DateTime reset && (v.Level == "warn" || v.Level == "crit"))
            {
                var left = reset - nowUtc;
                if (left > TimeSpan.Zero && left <= TimeSpan.FromMinutes(min))
                {
                    string sig = reset.ToString("o");
                    if (mem.ShouldNotify(rkey, sig, 0, nowUtc))
                    {
                        int mins = Math.Max(1, (int)Math.Ceiling(left.TotalMinutes));
                        res.Add(new PluginAlert(v.Label, Loc.T("Almost reset: now {0}, resets in about {1} min.", v.Text, mins), false));
                        mem.Mark(rkey, sig, nowUtc);
                    }
                }
            }
        }
        return res;
    }

    /// <summary>Wat de toestand uniek maakt: bij een uptime-monitor welke doelen het zijn die uitvallen; anders niets (het niveau is al "kritiek").</summary>
    internal static string Signature(PluginValue v)
        => string.Join("\n", v.Detail.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(l => l.StartsWith("✕")));

    internal static string AlertText(PluginValue v)
    {
        var lines = v.Detail.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var bad = lines.Where(l => l.StartsWith("✕")).ToList();
        if (bad.Count > 0) return string.Join("\n", bad);
        return lines.Length > 0 ? lines[0] : v.Text;
    }
}
