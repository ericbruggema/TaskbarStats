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

    /// <summary>Snelheid per netwerkadapter (down, up) in bytes/s.</summary>
    public IReadOnlyDictionary<string, (double down, double up)> NetPerAdapter { get; init; } = new Dictionary<string, (double, double)>();
    /// <summary>Gebruik per GPU (sleutel = LUID-string).</summary>
    public IReadOnlyDictionary<string, double> GpuPerLuid { get; init; } = new Dictionary<string, double>();
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
    public MetricsSnapshot Current => _current;

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
            NetPerAdapter = _netRates, GpuPerLuid = _gpuRates, VramUsedPerLuid = _vramRates, DiskPerDisk = _diskRates,
            Sensors = _sensors,
        };
    }
}
