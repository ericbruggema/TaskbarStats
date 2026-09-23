using System.Diagnostics;

namespace TaskbarStats;

/// <summary>
/// Bepaalt de zwaarste processen (CPU en geheugen), gegroepeerd per programmanaam.
/// Wordt alleen bemonsterd terwijl het nodig is (muis boven het widget, dashboard of fullscreen open).
/// CPU% is het aandeel van de totale CPU (zoals Taakbeheer). Het bemonsteren (alle processen doorlopen) kost ~10 ms,
/// daarom <see cref="SampleAsync"/>: dat draait op een achtergrond-thread zodat de UI niet hapert.
/// </summary>
public sealed class ProcessSampler
{
    private readonly object _sync = new();
    private int _busy;
    private Dictionary<int, (TimeSpan cpu, long stamp)> _prev = new();

    /// <summary>Aantal programma's in de toplijsten (tooltip: 3, dashboard/fullscreen: 5–12).</summary>
    public int TopCount { get; set; } = 3;
    public bool HasCpu { get; private set; }
    public List<(string name, double cpu)> TopCpu { get; private set; } = new();
    public List<(string name, long mem)> TopMem { get; private set; } = new();

    public void Reset()
    {
        lock (_sync)
        {
            _prev = new();
            HasCpu = false;
            TopCpu = new();
            TopMem = new();
        }
    }

    /// <summary>Bemonstert op een achtergrond-thread; een lopende bemonstering wordt niet nogmaals gestart.</summary>
    public void SampleAsync()
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
        Task.Run(() =>
        {
            try { Sample(); }
            catch { }
            finally { Volatile.Write(ref _busy, 0); }
        });
    }

    public void Sample()
    {
        lock (_sync)
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
            var topCpu = cpu.OrderByDescending(k => k.Value).Take(TopCount).Select(k => (k.Key, k.Value)).ToList();
            var topMem = mem.OrderByDescending(k => k.Value).Take(TopCount).Select(k => (k.Key, k.Value)).ToList();
            TopCpu = topCpu;
            TopMem = topMem;
            HasCpu = haveDelta;
        }
    }
}
