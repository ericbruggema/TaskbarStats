using System.Diagnostics;

namespace TaskbarStats;

/// <summary>
/// Bepaalt de zwaarste processen (CPU en geheugen), gegroepeerd per programmanaam.
/// Wordt alleen bemonsterd terwijl de muis boven het widget hangt, zodat het licht blijft.
/// CPU% is het aandeel van de totale CPU (zoals Taakbeheer).
/// </summary>
public sealed class ProcessSampler
{
    private Dictionary<int, (TimeSpan cpu, long stamp)> _prev = new();

    public bool HasCpu { get; private set; }
    public List<(string name, double cpu)> TopCpu { get; private set; } = new();
    public List<(string name, long mem)> TopMem { get; private set; } = new();

    public void Reset()
    {
        _prev = new();
        HasCpu = false;
        TopCpu = new();
        TopMem = new();
    }

    public void Sample()
    {
        long now = Stopwatch.GetTimestamp();
        var cur = new Dictionary<int, (TimeSpan, long)>();
        var cpu = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var mem = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        bool haveDelta = _prev.Count > 0;

        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                if (p.Id == 0) continue;   // "Idle"
                string name;
                try { name = p.ProcessName; } catch { continue; }

                try { mem[name] = mem.GetValueOrDefault(name) + p.WorkingSet64; } catch { }

                try
                {
                    var t = p.TotalProcessorTime;
                    cur[p.Id] = (t, now);
                    if (_prev.TryGetValue(p.Id, out var pr))
                    {
                        double dt = (now - pr.stamp) / (double)Stopwatch.Frequency;
                        if (dt > 0)
                            cpu[name] = cpu.GetValueOrDefault(name) +
                                        (t - pr.cpu).TotalSeconds / dt / Environment.ProcessorCount * 100.0;
                    }
                }
                catch { /* geen toegang tot dit proces */ }
            }
        }

        _prev = cur;
        HasCpu = haveDelta;
        TopCpu = cpu.OrderByDescending(k => k.Value).Take(3).Select(k => (k.Key, k.Value)).ToList();
        TopMem = mem.OrderByDescending(k => k.Value).Take(3).Select(k => (k.Key, k.Value)).ToList();
    }
}
