namespace TaskbarStats;

/// <summary>Ringbuffer met de laatste N metingen (nieuwste rechts) voor de grafiekjes.</summary>
public sealed class Ring
{
    private readonly double[] _d;
    private int _n, _head;
    public Ring(int capacity = 3600) => _d = new double[capacity];
    public int Capacity => _d.Length;
    public int Count => _n;
    public void Add(double v) { _d[_head] = v; _head = (_head + 1) % _d.Length; if (_n < _d.Length) _n++; }
    /// <summary>i = 0 is de oudste meting.</summary>
    public double this[int i] => _d[((_head - _n + i) % _d.Length + _d.Length) % _d.Length];
    public double Max() => Max(_n);
    /// <summary>Hoogste waarde in de laatste <paramref name="last"/> metingen.</summary>
    public double Max(int last) { double m = 0; for (int i = Math.Max(0, _n - last); i < _n; i++) m = Math.Max(m, this[i]); return m; }
}

/// <summary>Geschiedenis (1 meting per seconde, ~5 minuten) van de belangrijkste metingen.</summary>
public sealed class MetricHistory
{
    public readonly Ring Cpu = new(), Gpu = new(), Mem = new(), NetDown = new(), NetUp = new(), DiskRead = new(), DiskWrite = new();
    public readonly Dictionary<string, Ring> GpuPer = new(), VramPer = new();
    public readonly List<Ring> Cores = new();
    /// <summary>Per netwerkadapter de geschiedenis van download en upload (bytes/s).</summary>
    public readonly Dictionary<string, (Ring down, Ring up)> NetPer = new();
    private long _at;

    public void Sample(MetricsSnapshot m)
    {
        long now = Environment.TickCount64;
        if (now - _at < 1000 || Cadence.Active) return;
        _at = now;
        Cpu.Add(m.CpuPercent); Gpu.Add(m.GpuPercent); Mem.Add(m.MemPercent);
        NetDown.Add(m.NetDownBytesPerSec); NetUp.Add(m.NetUpBytesPerSec);
        DiskRead.Add(m.DiskReadBytesPerSec); DiskWrite.Add(m.DiskWriteBytesPerSec);
        foreach (var (luid, v) in m.GpuPerLuid)
        {
            if (luid == "") continue;
            if (!GpuPer.TryGetValue(luid, out var r)) GpuPer[luid] = r = new Ring();
            r.Add(v);
        }
        foreach (var (luid, v) in m.VramUsedPerLuid)
        {
            if (!VramPer.TryGetValue(luid, out var r)) VramPer[luid] = r = new Ring();
            r.Add(v);
        }
        foreach (var (name, r) in m.NetPerAdapter)
        {
            if (!NetPer.TryGetValue(name, out var pr)) NetPer[name] = pr = (new Ring(), new Ring());
            pr.down.Add(r.down); pr.up.Add(r.up);
        }
        while (Cores.Count < m.CpuCores.Length) Cores.Add(new Ring());
        for (int i = 0; i < m.CpuCores.Length; i++) Cores[i].Add(m.CpuCores[i]);
    }
}

/// <summary>Alles wat het dashboard van het widget nodig heeft.</summary>
public sealed class DashContext
{
    public required Metrics Metrics { get; init; }
    public required AppSettings Cfg { get; init; }
    public required UsageTracker Usage { get; init; }
    public required MetricHistory History { get; init; }
    public required Func<List<DriveSpace>> Drives { get; init; }
    public required Action<Point> ShowMenu { get; init; }
    public Action<int>? Nudge { get; init; }
}
