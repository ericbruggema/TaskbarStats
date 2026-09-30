namespace TaskbarStats;

/// <summary>
/// Een onveranderlijke momentopname van alle meetwaarden. De sampler-thread maakt er na elke meting één
/// (<see cref="Metrics.Current"/>, atomair vervangen); de vensters (widget, dashboard, fullscreen) lezen die ene verwijzing per teken- of
/// tooltiprondje. Zo zien ze nooit een half bijgewerkte meting, en hoeft er nergens meer te worden vergrendeld.
/// De ruwe waarden staan er in; <c>Cadence</c> past bij het lezen dezelfde bewerking toe als vroeger <see cref="Metrics"/> zelf deed
/// (die is tijdsafhankelijk en hoort bij het tekenmoment).
/// </summary>
public sealed class MetricsSnapshot
{
    public static readonly MetricsSnapshot Empty = new();

    // ruwe waarden (zie Metrics: dezelfde bewerking bij het lezen)
    internal double RawCpu { get; init; }
    internal double RawMem { get; init; }
    internal double RawGpu { get; init; }
    internal double RawDown { get; init; }
    internal double RawUp { get; init; }
    internal double[] RawCores { get; init; } = Array.Empty<double>();
    internal double? RawCpuTemp { get; init; }
    internal double? RawGpuTemp { get; init; }

    public double CpuPercent => Cadence.M(0, RawCpu);
    public double[] CpuCores => Cadence.C(RawCores);
    public double MemPercent => Cadence.M(1, RawMem);
    public double GpuPercent => Cadence.M(2, RawGpu);
    public double? CpuTempC => RawCpuTemp is double d ? Cadence.M(3, d) : null;
    public double? GpuTempC => RawGpuTemp is double d ? Cadence.M(4, d) : null;
    public double NetDownBytesPerSec => Cadence.M(5, RawDown);
    public double NetUpBytesPerSec => Cadence.M(6, RawUp);

    public ulong MemTotalBytes { get; init; }
    public ulong MemUsedBytes { get; init; }
    public double DiskReadBytesPerSec { get; init; }
    public double DiskWriteBytesPerSec { get; init; }
    public bool BatteryPresent { get; init; }
    public double BatteryPercent { get; init; }
    public bool BatteryCharging { get; init; }
    public bool BatteryOnAc { get; init; }
    /// <summary>Geschatte resterende tijd in seconden bij ontladen (-1 = onbekend).</summary>
    public int BatteryRemainingSec { get; init; } = -1;
    public double? CpuMHz { get; init; }
    public double? DiskBusyPercent { get; init; }
    public double? DiskTempC { get; init; }
    public double? MoboTempC { get; init; }
    /// <summary>Hoogste waarden sinds het starten (voor het credits-scherm).</summary>
    public double PeakCpu { get; init; }
    public double PeakMem { get; init; }
    public double PeakDown { get; init; }

    /// <summary>Aantal processen: alle, apps (met venster) en threads.</summary>
    public int ProcessTotal { get; init; }
    public int ProcessApps { get; init; }
    public int ThreadTotal { get; init; }
    /// <summary>Achtergrondprocessen = alle processen min de apps.</summary>
    public int ProcessBackground => Math.Max(0, ProcessTotal - ProcessApps);
    /// <summary>Framerate van het voorgrondprogramma (experimenteel; null zolang uit of zonder frames).</summary>
    public FpsReading? Fps { get; init; }

    /// <summary>Snelheid per netwerkadapter (down, up) in bytes/s.</summary>
    public IReadOnlyDictionary<string, (double down, double up)> NetPerAdapter { get; init; } = new Dictionary<string, (double, double)>();
    /// <summary>Gebruik per GPU (sleutel = LUID-string).</summary>
    public IReadOnlyDictionary<string, double> GpuPerLuid { get; init; } = new Dictionary<string, double>();
    /// <summary>Drukste engine-soort per GPU (3D/VideoDecode/Copy/...; sleutel = LUID-string, zie <see cref="Metrics.EngineName"/>).</summary>
    public IReadOnlyDictionary<string, string> GpuEnginePerLuid { get; init; } = new Dictionary<string, string>();
    /// <summary>Gebruikt dedicated videogeheugen per GPU (bytes, sleutel = LUID-string).</summary>
    public IReadOnlyDictionary<string, double> VramUsedPerLuid { get; init; } = new Dictionary<string, double>();
    /// <summary>Lees/schrijfsnelheid per fysieke schijf (bytes/s).</summary>
    public IReadOnlyDictionary<string, (double read, double write)> DiskPerDisk { get; init; } = new Dictionary<string, (double, double)>();
    /// <summary>Alle sensoren; alleen gevuld zolang het fullscreen-scherm ze vraagt.</summary>
    public IReadOnlyList<SensorInfo> Sensors { get; init; } = Array.Empty<SensorInfo>();
}

public sealed partial class Metrics
{
    private volatile MetricsSnapshot _current = MetricsSnapshot.Empty;

    /// <summary>De laatste complete meting. Lees dit één keer per teken- of tooltiprondje en gebruik die verwijzing verder.</summary>
    public MetricsSnapshot Current => _demo ?? _current;

    // Voorbeeldwaarden (themavoorbeeld in de instellingen): zolang die er zijn, tonen alle vensters die in plaats van de echte meting.
    private static volatile MetricsSnapshot? _demo;

    /// <summary>Zet verzonnen, bewegende waarden (t in seconden) zodat een themavoorbeeld laag, midden, waarschuwing en kritiek laat zien.</summary>
    internal static void SetDemo(double t)
    {
        static double Wave(double t, double speed, double phase, double lo, double hi) => lo + (hi - lo) * (0.5 + 0.5 * Math.Sin(t * speed + phase));
        double cpu = Wave(t, 1.1, 0, 4, 100), gpu = Wave(t, 0.8, 1.7, 2, 100), mem = Wave(t, 0.5, 3.1, 30, 97);
        var cores = new double[8];
        for (int i = 0; i < cores.Length; i++) cores[i] = Wave(t, 0.9 + i * 0.23, i * 0.9, 0, 100);
        const ulong total = 32UL << 30;
        _demo = new MetricsSnapshot
        {
            RawCpu = cpu, RawGpu = gpu, RawMem = mem, RawCores = cores,
            RawCpuTemp = Wave(t, 0.7, 0.4, 38, 96), RawGpuTemp = Wave(t, 0.6, 2.2, 36, 94),
            RawDown = Wave(t, 1.3, 0.5, 0, 45e6), RawUp = Wave(t, 1.7, 2.5, 0, 6e6),
            MemTotalBytes = total, MemUsedBytes = (ulong)(total * (mem / 100)),
            DiskReadBytesPerSec = Wave(t, 1.0, 1, 0, 300e6), DiskWriteBytesPerSec = Wave(t, 1.4, 4, 0, 120e6),
            PeakCpu = 100, PeakMem = 97, PeakDown = 45e6,
            ProcessTotal = 312, ProcessApps = 18, ThreadTotal = 4380,
            Fps = new FpsReading(0, "game", Wave(t, 0.9, 0.3, 48, 144), Wave(t, 0.9, 0.3, 30, 96)),
        };
    }

    /// <summary>Zet de echte meting weer terug.</summary>
    internal static void ClearDemo() => _demo = null;

    internal static bool DemoActive => _demo is not null;

    /// <summary>Maakt van de huidige waarden een nieuwe momentopname (sampler-thread, aan het einde van <see cref="Update"/>).</summary>
    internal void PublishSnapshot()
    {
        _current = new MetricsSnapshot
        {
            RawCpu = _rCpu, RawMem = _rMem, RawGpu = _rGpu, RawDown = _rDown, RawUp = _rUp, RawCores = _rCores, RawCpuTemp = _rCpuT, RawGpuTemp = _rGpuT,
            MemTotalBytes = MemTotalBytes, MemUsedBytes = MemUsedBytes,
            DiskReadBytesPerSec = DiskReadBytesPerSec, DiskWriteBytesPerSec = DiskWriteBytesPerSec,
            BatteryPresent = BatteryPresent, BatteryPercent = BatteryPercent, BatteryCharging = BatteryCharging, BatteryOnAc = BatteryOnAc,
            BatteryRemainingSec = BatteryRemainingSec, CpuMHz = CpuMHz,
            DiskBusyPercent = DiskBusyPercent, DiskTempC = DiskTempC, MoboTempC = MoboTempC,
            PeakCpu = PeakCpu, PeakMem = PeakMem, PeakDown = PeakDown,
            ProcessTotal = ProcessTotal, ProcessApps = ProcessApps, ThreadTotal = ThreadTotal,
            Fps = Fps.Enabled ? Fps.Read() : null,
            NetPerAdapter = _netRates, GpuPerLuid = _gpuRates, GpuEnginePerLuid = _gpuEngines, VramUsedPerLuid = _vramRates, DiskPerDisk = _diskRates,
            Sensors = _sensors,
        };
    }
}
