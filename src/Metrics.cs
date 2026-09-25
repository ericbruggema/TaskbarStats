using System.Diagnostics;
using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;

namespace TaskbarStats;

/// <summary>Eén sensormeting uit LibreHardwareMonitor (temperatuur, klok, vermogen, ventilator, ...).</summary>
public sealed record SensorInfo(string Hardware, HardwareType HwType, string Name, SensorType Type, double? Value, double? Min, double? Max)
{
    public static string FormatValue(SensorType t, double v) => t switch
    {
        SensorType.Temperature => $"{v:0.#} °C",
        SensorType.Load or SensorType.Level or SensorType.Control => $"{v:0.#} %",
        SensorType.Clock => v >= 1000 ? $"{v / 1000:0.00} GHz" : $"{v:0} MHz",
        SensorType.Power => $"{v:0.#} W",
        SensorType.Voltage => $"{v:0.000} V",
        SensorType.Current => $"{v:0.###} A",
        SensorType.Fan => $"{v:0} RPM",
        SensorType.Flow => $"{v:0} L/h",
        SensorType.Data => $"{v:0.#} GB",
        SensorType.SmallData => $"{v:0.#} MB",
        SensorType.Throughput => Metrics.FormatRate(v),
        SensorType.Energy => $"{v:0} mWh",
        SensorType.Noise => $"{v:0} dBA",
        _ => $"{v:0.##}",
    };
}

/// <summary>Vrije/totale ruimte van één schijf (station).</summary>
public readonly record struct DriveSpace(string Name, long Total, long Free, bool Network = false)
{
    /// <summary>Naam voor weergave; netwerkschijven krijgen "(net)" erachter.</summary>
    public string Display => Network ? Name + " (net)" : Name;
    public long Used => Total - Free;
    public double UsedPercent => Total <= 0 ? 0 : 100.0 * Used / Total;
}

/// <summary>
/// Leest CPU-, geheugen-, GPU-, netwerk-, schijf- en temperatuurwaarden uit met dezelfde
/// bronnen die Windows Task Manager gebruikt.
///  - CPU  : "Processor Information\% Processor Time" (totaal en per core; dat is wat Taakbeheer toont), of optioneel "% Processor Utility" (telt turbo mee).
///  - MEM  : GlobalMemoryStatusEx.dwMemoryLoad.
///  - GPU  : som van alle "GPU Engine\Utilization Percentage" per fysieke GPU (LUID).
///  - NET  : "Network Interface\Bytes Received/Sent per sec" (per adapter, plus totaal).
///  - DISK : "PhysicalDisk\Disk Read/Write Bytes/sec" (per schijf, plus totaal).
///  - TEMP : LibreHardwareMonitorLib (vereist administrator-rechten).
/// </summary>
public sealed partial class Metrics : IDisposable
{
    private double _rCpu, _rMem, _rGpu, _rDown, _rUp;
    private double[] _rCores = Array.Empty<double>();
    public double CpuPercent { get => Cadence.M(0, _rCpu); private set { _rCpu = value; if (value > PeakCpu) PeakCpu = value; } }
    public double[] CpuCores { get => Cadence.C(_rCores); private set => _rCores = value; }
    public double MemPercent { get => Cadence.M(1, _rMem); private set { _rMem = value; if (value > PeakMem) PeakMem = value; } }
    /// <summary>Hoogste waarden sinds het starten (voor het credits-scherm).</summary>
    public double PeakCpu { get; private set; }
    public double PeakMem { get; private set; }
    public double PeakDown { get; private set; }
    public ulong MemTotalBytes { get; private set; }
    public ulong MemUsedBytes { get; private set; }
    public double GpuPercent { get => Cadence.M(2, _rGpu); private set => _rGpu = value; }
    public double NetDownBytesPerSec { get => Cadence.M(5, _rDown); private set { _rDown = value; if (value > PeakDown) PeakDown = value; } }
    public double NetUpBytesPerSec { get => Cadence.M(6, _rUp); private set => _rUp = value; }
    public double DiskReadBytesPerSec { get; private set; }
    public double DiskWriteBytesPerSec { get; private set; }
    public bool BatteryPresent { get; private set; }
    public double BatteryPercent { get; private set; }
    public bool BatteryCharging { get; private set; }
    public bool BatteryOnAc { get; private set; }
    /// <summary>Geschatte resterende tijd in seconden bij ontladen (-1 = onbekend).</summary>
    public int BatteryRemainingSec { get; private set; } = -1;
    public double? CpuMHz { get; private set; }
    private double? _rCpuT, _rGpuT;
    public double? CpuTempC { get => _rCpuT is double d ? Cadence.M(3, d) : null; private set => _rCpuT = value; }
    public double? GpuTempC { get => _rGpuT is double d ? Cadence.M(4, d) : null; private set => _rGpuT = value; }

    /// <summary>Actuele snelheid per netwerkadapter (down, up) in bytes/s.</summary>
    public IReadOnlyDictionary<string, (double down, double up)> NetPerAdapter => _netRates;
    /// <summary>Actueel gebruik per GPU (sleutel = LUID-string).</summary>
    public IReadOnlyDictionary<string, double> GpuPerLuid => _gpuRates;
    /// <summary>Gebruikt dedicated videogeheugen per GPU (bytes, sleutel = LUID-string).</summary>
    public IReadOnlyDictionary<string, double> VramUsedPerLuid => _vramRates;
    /// <summary>Actuele lees/schrijfsnelheid per fysieke schijf (bytes/s).</summary>
    public IReadOnlyDictionary<string, (double read, double write)> DiskPerDisk => _diskRates;

    private PerformanceCounter? _cpu, _cpuFreq, _cpuPerf;
    private readonly List<PerformanceCounter> _vram = new();
    private Dictionary<string, double> _vramRates = new();
    private readonly List<PerformanceCounter> _cpuCores = new();
    private PdhWildcard? _coreQuery, _netRecvQuery, _netSentQuery;   // één query per teller: veel goedkoper dan een PerformanceCounter per instantie
    private PdhWildcard? _gpuQuery;   // één gezamenlijke query voor alle "GPU Engine"-instanties
    private readonly Dictionary<string, (PerformanceCounter recv, PerformanceCounter sent)> _net = new();
    private readonly Dictionary<string, (PerformanceCounter read, PerformanceCounter write)> _disks = new();
    private Dictionary<string, (double down, double up)> _netRates = new();
    private Dictionary<string, double> _gpuRates = new();
    private Dictionary<string, (double read, double write)> _diskRates = new();

    private string? _gpuLuidFilter;
    private string? _netFilter;
    private Computer? _lhm;
    private readonly object _lhmLock = new();   // alle LibreHardwareMonitor-toegang (UI zet vlaggen, sampler-thread voert uit)
    private bool _wantTemps, _wantSensors, _lhmExtended, _lhmDirty;
    private long _sensorsAt;
    private volatile SensorInfo[] _sensors = Array.Empty<SensorInfo>();

    /// <summary>Alle sensoren (CPU, GPU, hoofdbord, schijven, geheugen, batterij); alleen gevuld zolang <see cref="SetSensorsWanted"/> aan staat.</summary>
    public IReadOnlyList<SensorInfo> Sensors => _sensors;
    private long _rebuiltAt;

    public Metrics()
    {
        SetCpuMode(false);

        try
        {
            // Klokfrequentie = basisfrequentie x "% Processor Performance".
            _cpuFreq = new PerformanceCounter("Processor Information", "Processor Frequency", "_Total", true);
            _cpuPerf = new PerformanceCounter("Processor Information", "% Processor Performance", "_Total", true);
            _cpuFreq.NextValue(); _cpuPerf.NextValue();
        }
        catch { _cpuFreq?.Dispose(); _cpuPerf?.Dispose(); _cpuFreq = _cpuPerf = null; }

        _gpuQuery = PdhWildcard.TryCreate(@"\GPU Engine(*)\Utilization Percentage");
        RebuildVramCounters();
        _netRecvQuery = PdhWildcard.TryCreate(@"\Network Interface(*)\Bytes Received/sec");
        _netSentQuery = PdhWildcard.TryCreate(@"\Network Interface(*)\Bytes Sent/sec");
        if (_netRecvQuery is null || _netSentQuery is null) RebuildNetCounters();   // terugval
        RebuildDiskCounters();
        _rebuiltAt = Environment.TickCount64;
    }

    // Instances heten "0" (Processor) of "groep,core" (Processor Information).
    private static (int g, int c) CoreKey(string n)
    {
        var p = n.Split(',');
        return p.Length == 2 ? (int.Parse(p[0]), int.Parse(p[1])) : (0, int.Parse(n));
    }

    // Standaard "% Processor Time" (wat Taakbeheer op recente Windows-versies toont). "% Processor Utility" schaalt met de klok en
    // is op een boostende CPU bijna 2x zo hoog (bv. 190% van de basisklok); die kun je apart kiezen.
    private readonly object _cpuLock = new();
    private bool? _cpuUtilityMode;

    public void SetCpuMode(bool utility)
    {
        lock (_cpuLock)
        {
            if (_cpuUtilityMode == utility) return;
            _cpuUtilityMode = utility;
            _cpu?.Dispose(); _cpu = null;
            _coreQuery?.Dispose(); _coreQuery = null;
            foreach (var c in _cpuCores) c.Dispose();
            _cpuCores.Clear();
            if (utility) InitCpu("Processor Information", "% Processor Utility");
            if (_cpu is null) InitCpu("Processor Information", "% Processor Time");
            if (_cpu is null) InitCpu("Processor", "% Processor Time");
        }
    }

    private double[] ReadCores()
    {
        if (_coreQuery is null) return _cpuCores.Select(c => { try { return Math.Min(100.0, c.NextValue()); } catch { return 0.0; } }).ToArray();
        return _coreQuery.Read()
            .Where(r => !r.instance.Contains("_Total") && r.instance.Split(',').All(x => int.TryParse(x, out _)))
            .OrderBy(r => CoreKey(r.instance))
            .Select(r => Math.Min(100.0, Math.Max(0, r.value)))
            .ToArray();
    }

    private void InitCpu(string category, string counter)
    {
        try
        {
            _cpu = new PerformanceCounter(category, counter, "_Total", true);
            _cpu.NextValue();
        }
        catch { _cpu?.Dispose(); _cpu = null; return; }

        try
        {
            // Instances heten "0" (Processor) of "groep,core" (Processor Information).
            static (int g, int c) Key(string n) => CoreKey(n);
            _coreQuery = PdhWildcard.TryCreate($@"\{category}(*)\{counter}");
            if (_coreQuery is not null) return;
            var cat = new PerformanceCounterCategory(category);
            foreach (var n in cat.GetInstanceNames()
                                 .Where(n => !n.Contains("_Total") &&
                                             n.Split(',').All(x => int.TryParse(x, out _)))
                                 .OrderBy(Key))
            {
                var c = new PerformanceCounter(category, counter, n, true);
                c.NextValue();
                _cpuCores.Add(c);
            }
        }
        catch { }
    }

    // ---------- Temperatuur ----------

    /// <summary>Temperaturen in het widget (CPU/GPU). Het openen van LibreHardwareMonitor gebeurt op de sampler-thread.</summary>
    public void EnableTemperatures(bool on)
    {
        lock (_lhmLock) { _wantTemps = on; _lhmDirty = true; }
    }

    /// <summary>Volledige sensorlijst (fullscreen-scherm): zet ook hoofdbord, schijven, geheugen en batterij aan.</summary>
    public void SetSensorsWanted(bool on)
    {
        lock (_lhmLock) { _wantSensors = on; _lhmDirty = true; if (!on) _sensors = Array.Empty<SensorInfo>(); }
    }

    // Op de sampler-thread: LibreHardwareMonitor openen/sluiten of uitbreiden als de wensen zijn veranderd.
    private void SyncLhm()
    {
        lock (_lhmLock)
        {
            if (!_lhmDirty) return;
            _lhmDirty = false;
            try
            {
                if (!(_wantTemps || _wantSensors)) { CloseLhm(); return; }
                if (_lhm is not null && _lhmExtended == _wantSensors) return;
                CloseLhm();
                _lhm = new Computer
                {
                    IsCpuEnabled = true, IsGpuEnabled = true,
                    IsMemoryEnabled = _wantSensors, IsMotherboardEnabled = _wantSensors, IsStorageEnabled = _wantSensors,
                    IsBatteryEnabled = _wantSensors, IsControllerEnabled = _wantSensors,
                };
                _lhm.Open();
                _lhmExtended = _wantSensors;
            }
            catch { _lhm = null; }
        }
    }

    private void CloseLhm()
    {
        try { _lhm?.Close(); } catch { }
        _lhm = null;
        CpuTempC = null;
        GpuTempC = null;
        _sensors = Array.Empty<SensorInfo>();
    }

    private void UpdateTemperatures()
    {
        lock (_lhmLock)
        {
            if (!(_wantTemps || _wantSensors)) return;
            double? cpu = null, gpuCore = null, gpuAlt = null;
            if (_lhm is not null)
                try
                {
                    foreach (var hw in _lhm.Hardware)
                    {
                        bool gpu = hw.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;
                        if (hw.HardwareType != HardwareType.Cpu && !gpu) continue;   // schijven/hoofdbord alleen in de sensor-snapshot
                        hw.Update();
                        foreach (var s in hw.Sensors)
                        {
                            if (s.SensorType != SensorType.Temperature || s.Value is not float v || v < 1) continue;
                            if (hw.HardwareType == HardwareType.Cpu &&
                                (s.Name.Contains("Package") || s.Name.Contains("Core (Tctl", StringComparison.OrdinalIgnoreCase) ||
                                 s.Name.Contains("Tdie", StringComparison.OrdinalIgnoreCase) || s.Name.StartsWith("Core")))
                                cpu = cpu is null ? v : Math.Max(cpu.Value, v);
                            if (gpu && s.Name.Contains("Core")) gpuCore = gpuCore is null ? v : Math.Max(gpuCore.Value, v);
                            else if (gpu && (s.Name.Contains("Hot Spot") || s.Name.Contains("SoC"))) gpuAlt = gpuAlt is null ? v : Math.Max(gpuAlt.Value, v);
                        }
                    }
                }
                catch { }
            // Sommige processors (bv. nieuwe Ryzen AI) hebben geen bruikbare sensor in LibreHardwareMonitor: dan de ACPI-thermal zone van Windows.
            CpuTempC = cpu ?? ReadThermalZone();
            GpuTempC = gpuCore ?? gpuAlt;
        }
    }

    // ACPI-thermal zone(s) van Windows ("Thermal Zone Information"): geeft op veel laptops een bruikbare (systeem/CPU-)temperatuur
    // ook als de sensor-driver de processor niet kent. Hoogste zone, hooguit elke 2 s.
    private readonly List<PerformanceCounter> _tz = new();
    private bool _tzInit;
    private long _tzAt;
    private double? _tzTemp;

    /// <summary>Temperatuur van de ACPI-thermal zone (°C), of null als Windows die niet geeft.</summary>
    public double? ReadThermalZone()
    {
        long now = Environment.TickCount64;
        if (now - _tzAt < 2000) return _tzTemp;
        _tzAt = now;
        try
        {
            if (!_tzInit)
            {
                _tzInit = true;
                foreach (var inst in new PerformanceCounterCategory("Thermal Zone Information").GetInstanceNames())
                    try { _tz.Add(new PerformanceCounter("Thermal Zone Information", "High Precision Temperature", inst, true)); } catch { }
            }
            double? best = null;
            foreach (var c in _tz)
            {
                double k = c.NextValue() / 10.0 - 273.15;    // tienden Kelvin
                if (k is > 5 and < 125) best = best is null ? k : Math.Max(best.Value, k);
            }
            _tzTemp = best;
        }
        catch { _tzTemp = null; }
        return _tzTemp;
    }

    // Elke 2 s alle sensoren uitlezen (zwaarder dan de temperaturen: schijven/hoofdbord), alleen als het fullscreen-scherm open is.
    private void SnapshotSensors()
    {
        if (!_wantSensors) return;
        long now = Environment.TickCount64;
        if (now - _sensorsAt < 2000) return;
        _sensorsAt = now;
        lock (_lhmLock)
        {
            if (_lhm is null || !_lhmExtended) return;
            var list = new List<SensorInfo>();
            try { foreach (var hw in _lhm.Hardware) VisitHardware(hw, list); } catch { }
            if (ReadThermalZone() is double tz)
                list.Add(new SensorInfo("ACPI", HardwareType.Cpu, Loc.Pick("Thermal zone (ACPI, systeem)", "Thermal zone (ACPI, system)"), SensorType.Temperature, tz, null, null));
            _sensors = list.ToArray();
        }
    }

    private static void VisitHardware(IHardware hw, List<SensorInfo> list)
    {
        try { hw.Update(); } catch { }
        foreach (var s in hw.Sensors)
            list.Add(new SensorInfo(hw.Name, hw.HardwareType, s.Name, s.SensorType,
                                    s.Value is float v ? v : null, s.Min is float mn ? mn : null, s.Max is float mx ? mx : null));
        foreach (var sub in hw.SubHardware) VisitHardware(sub, list);
    }

    // ---------- Netwerk ----------

    public static string[] GetNetworkAdapters()
    {
        try { return new PerformanceCounterCategory("Network Interface").GetInstanceNames().OrderBy(x => x).ToArray(); }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>null = alle adapters samengeteld, anders alleen deze adapter in het totaal.</summary>
    public void SetNetworkAdapter(string? adapter) => _netFilter = adapter;

    private void RebuildNetCounters()
    {
        foreach (var (r, s) in _net.Values) { r.Dispose(); s.Dispose(); }
        _net.Clear();
        foreach (var n in GetNetworkAdapters())
        {
            try
            {
                var r = new PerformanceCounter("Network Interface", "Bytes Received/sec", n, true);
                var s = new PerformanceCounter("Network Interface", "Bytes Sent/sec", n, true);
                r.NextValue(); s.NextValue();
                _net[n] = (r, s);
            }
            catch { }
        }
    }

    // ---------- Schijven ----------

    private static string[] GetDiskInstances()
    {
        try
        {
            return new PerformanceCounterCategory("PhysicalDisk").GetInstanceNames()
                       .Where(n => n != "_Total").OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch { return Array.Empty<string>(); }
    }

    private void RebuildDiskCounters()
    {
        foreach (var (r, w) in _disks.Values) { r.Dispose(); w.Dispose(); }
        _disks.Clear();
        foreach (var n in GetDiskInstances())
        {
            try
            {
                var r = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", n, true);
                var w = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", n, true);
                r.NextValue(); w.NextValue();
                _disks[n] = (r, w);
            }
            catch { }
        }
    }

    /// <summary>Ruimte per station van het gevraagde type (standaard vaste schijven; Network = gekoppelde netwerkschijven).</summary>
    public static List<DriveSpace> GetDriveSpaces(DriveType type = DriveType.Fixed)
    {
        var list = new List<DriveSpace>();
        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (d.DriveType != type || !d.IsReady) continue;
                    list.Add(new DriveSpace(d.Name.TrimEnd('\\'), d.TotalSize, d.AvailableFreeSpace, type == DriveType.Network));
                }
                catch { }
            }
        }
        catch { }

        // Een als administrator draaiend programma ziet de netwerkstations van de gewone sessie niet in DriveInfo
        // (UAC: elke sessie heeft eigen stationsletters). De koppelingen staan wel in HKCU\Network; de share zelf is
        // bereikbaar via het UNC-pad.
        if (type == DriveType.Network)
        {
            try
            {
                using var root = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Network");
                foreach (var letter in root?.GetSubKeyNames() ?? Array.Empty<string>())
                {
                    string name = letter.ToUpperInvariant() + ":";
                    if (list.Any(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
                    using var key = root!.OpenSubKey(letter);
                    if (key?.GetValue("RemotePath") is not string remote || remote.Length < 3) continue;
                    if (GetDiskFreeSpaceEx(remote.TrimEnd('\\') + "\\", out ulong avail, out ulong total, out _) && total > 0)
                        list.Add(new DriveSpace(name, (long)total, (long)avail, true));
                }
                list.Sort((x, y) => string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase));
            }
            catch { }
        }
        return list;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetDiskFreeSpaceEx(string directory, out ulong freeAvailable, out ulong total, out ulong totalFree);

    // ---------- GPU ----------

    /// <summary>null = automatisch de drukste GPU, anders een vaste LUID-string.</summary>
    public void SetGpuLuid(string? luid) => _gpuLuidFilter = luid;

    public static string[] GetGpuLuids()
    {
        try
        {
            return new PerformanceCounterCategory("GPU Engine").GetInstanceNames()
                      .Select(ExtractLuid).Where(l => l is not null).Distinct().OrderBy(x => x).ToArray()!;
        }
        catch { return Array.Empty<string>(); }
    }

    private static Dictionary<string, string>? _gpuNames;

    /// <summary>Leesbare GPU-naam bij een LUID-string (valt terug op de LUID zelf).</summary>
    public static string GpuName(string luid)
    {
        _gpuNames ??= DxgiNames.Read();
        return _gpuNames.TryGetValue(luid, out var n) ? n : luid;
    }

    /// <summary>True voor een echte videokaart: naam bekend via DXGI en niet de "Microsoft Basic Render Driver".</summary>
    public static bool IsRealGpu(string luid)
    {
        _gpuNames ??= DxgiNames.Read();
        return _gpuNames.TryGetValue(luid, out var n) && !n.StartsWith("Microsoft Basic", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Dedicated videogeheugen van een GPU in bytes (0 = onbekend).</summary>
    public static long GpuDedicatedBytes(string luid)
    {
        _gpuNames ??= DxgiNames.Read();
        return DxgiNames.Dedicated.GetValueOrDefault(luid);
    }

    // Videogeheugen (paar instanties): apart per teller; de zware "GPU Engine"-tellers gaan via _gpuQuery.
    private void RebuildVramCounters()
    {
        foreach (var c in _vram) c.Dispose();
        _vram.Clear();
        try
        {
            var mem = new PerformanceCounterCategory("GPU Adapter Memory");
            foreach (var inst in mem.GetInstanceNames())
            {
                try
                {
                    var c = new PerformanceCounter("GPU Adapter Memory", "Dedicated Usage", inst, true);
                    c.NextValue();
                    _vram.Add(c);
                }
                catch { }
            }
        }
        catch { }
    }

    private static string? ExtractLuid(string instance)
    {
        int i = instance.IndexOf("luid_", StringComparison.Ordinal);
        if (i < 0) return null;
        int j = instance.IndexOf("_phys", i, StringComparison.Ordinal);
        return j < 0 ? null : instance.Substring(i, j - i);
    }

    // ---------- Verversen ----------

    public void Update()
    {
        lock (_cpuLock)
        {
            try { CpuPercent = _cpu is null ? 0 : Math.Min(100, _cpu.NextValue()); } catch { CpuPercent = 0; }

            try { CpuCores = ReadCores(); }
            catch { }
        }

        try
        {
            var ms = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref ms))
            {
                MemPercent = ms.dwMemoryLoad;
                MemTotalBytes = ms.ullTotalPhys;
                MemUsedBytes = ms.ullTotalPhys - ms.ullAvailPhys;
            }
        }
        catch { }

        try
        {
            var perLuid = new Dictionary<string, double>();
            if (_gpuQuery is not null)
                foreach (var (inst, v) in _gpuQuery.Read())
                {
                    if (!inst.Contains("engtype")) continue;
                    var luid = ExtractLuid(inst) ?? "";
                    perLuid[luid] = perLuid.TryGetValue(luid, out var cur) ? cur + v : v;
                }
            foreach (var k in perLuid.Keys.ToList()) perLuid[k] = Math.Min(100, perLuid[k]);
            _gpuRates = perLuid;
            GpuPercent = _gpuLuidFilter is not null
                ? perLuid.GetValueOrDefault(_gpuLuidFilter)
                : perLuid.Count == 0 ? 0 : perLuid.Values.Max();
        }
        catch { GpuPercent = 0; }

        try { CpuMHz = _cpuFreq is null || _cpuPerf is null ? null : _cpuFreq.NextValue() * _cpuPerf.NextValue() / 100.0; }
        catch { CpuMHz = null; }

        try
        {
            var vr = new Dictionary<string, double>();
            foreach (var c in _vram)
            {
                var luid = ExtractLuid(c.InstanceName);
                if (luid is null) continue;
                try { vr[luid] = vr.GetValueOrDefault(luid) + c.NextValue(); } catch { }
            }
            _vramRates = vr;
        }
        catch { }

        try
        {
            var rates = new Dictionary<string, (double, double)>();
            double down = 0, up = 0;
            if (_netRecvQuery is not null && _netSentQuery is not null)
            {
                var sent = _netSentQuery.Read().ToDictionary(x => x.instance, x => x.value);
                foreach (var (name, d) in _netRecvQuery.Read())
                {
                    double u = sent.GetValueOrDefault(name);
                    rates[name] = (d, u);
                    if (_netFilter is null || _netFilter == name) { down += d; up += u; }
                }
            }
            else
                foreach (var (name, (r, s)) in _net)
                {
                    double d, u;
                    try { d = r.NextValue(); u = s.NextValue(); } catch { continue; }
                    rates[name] = (d, u);
                    if (_netFilter is null || _netFilter == name) { down += d; up += u; }
                }
            _netRates = rates;
            NetDownBytesPerSec = down; NetUpBytesPerSec = up;
        }
        catch { }

        try
        {
            var rates = new Dictionary<string, (double, double)>();
            double rd = 0, wr = 0;
            foreach (var (name, (r, w)) in _disks)
            {
                double a, b;
                try { a = r.NextValue(); b = w.NextValue(); } catch { continue; }
                rates[name] = (a, b);
                rd += a; wr += b;
            }
            _diskRates = rates;
            DiskReadBytesPerSec = rd; DiskWriteBytesPerSec = wr;
        }
        catch { }

        try
        {
            var ps = System.Windows.Forms.SystemInformation.PowerStatus;
            BatteryPresent = !ps.BatteryChargeStatus.HasFlag(BatteryChargeStatus.NoSystemBattery);
            BatteryPercent = Math.Clamp(ps.BatteryLifePercent * 100.0, 0, 100);
            BatteryCharging = ps.BatteryChargeStatus.HasFlag(BatteryChargeStatus.Charging);
            BatteryOnAc = ps.PowerLineStatus == PowerLineStatus.Online;
            BatteryRemainingSec = ps.BatteryLifeRemaining;
        }
        catch { BatteryPresent = false; }

        SyncLhm();
        UpdateTemperatures();
        SnapshotSensors();

        // Instances (adapters, schijven, VRAM) komen en gaan; af en toe opnieuw opbouwen.
        if (Environment.TickCount64 - _rebuiltAt > 5000)
        {
            _rebuiltAt = Environment.TickCount64;
            RebuildVramCounters();
            if (_netRecvQuery is null && !_net.Keys.OrderBy(x => x).SequenceEqual(GetNetworkAdapters())) RebuildNetCounters();
            if (!_disks.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                            .SequenceEqual(GetDiskInstances())) RebuildDiskCounters();
        }
    }

    public static string FormatRate(double bytesPerSec, bool compact = false)
    {
        if (bytesPerSec >= 1024 * 1024) return $"{bytesPerSec / (1024 * 1024):0.0} MB" + (compact ? "" : "/s");
        if (bytesPerSec >= 1024)        return compact ? $"{bytesPerSec / 1024:0} KB" : $"{bytesPerSec / 1024:0.0} KB/s";
        return $"{bytesPerSec:0} B" + (compact ? "" : "/s");
    }

    /// <summary>Bytes leesbaar (B / KB / MB / GB / TB), bv. voor verbruikstotalen.</summary>
    public static string FormatBytes(double b)
    {
        if (b >= 1099511627776.0) return $"{b / 1099511627776.0:0.00} TB";
        if (b >= 1073741824.0) return $"{b / 1073741824.0:0.00} GB";
        if (b >= 1048576.0) return $"{b / 1048576.0:0.0} MB";
        if (b >= 1024.0) return $"{b / 1024.0:0} KB";
        return $"{b:0} B";
    }

    public static string FormatSize(double bytes)
    {
        double gb = bytes / (1024.0 * 1024 * 1024);
        return gb >= 1000 ? $"{gb / 1024:0.0} TB" : gb >= 10 ? $"{gb:0} GB" : $"{gb:0.0} GB";
    }

    /// <summary>Ping naar een instelbaar doel (meet alleen als het is ingeschakeld).</summary>
    public PingMonitor Ping { get; } = new();

    public void Dispose()
    {
        Ping.Dispose();
        _cpu?.Dispose();
        _cpuFreq?.Dispose();
        _cpuPerf?.Dispose();
        foreach (var c in _vram) c.Dispose();
        foreach (var c in _cpuCores) c.Dispose();
        _gpuQuery?.Dispose();
        _coreQuery?.Dispose(); _netRecvQuery?.Dispose(); _netSentQuery?.Dispose();
        foreach (var (r, s) in _net.Values) { r.Dispose(); s.Dispose(); }
        foreach (var (r, w) in _disks.Values) { r.Dispose(); w.Dispose(); }
        lock (_lhmLock) CloseLhm();
    }

    // ---------- P/Invoke ----------

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys;
        public ulong ullTotalPageFile, ullAvailPageFile;
        public ulong ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
}

/// <summary>Leest GPU-namen + LUID via DXGI, zodat we "NVIDIA GeForce RTX ..." kunnen tonen i.p.v. een LUID.</summary>
internal static class DxgiNames
{
    /// <summary>Dedicated videogeheugen (bytes) per LUID-string, gevuld door Read().</summary>
    public static Dictionary<string, long> Dedicated { get; } = new();

    public static Dictionary<string, string> Read()
    {
        var map = new Dictionary<string, string>();
        IntPtr pFactory = IntPtr.Zero;
        try
        {
            var iid = typeof(IDXGIFactory1).GUID;
            if (CreateDXGIFactory1(ref iid, out pFactory) != 0 || pFactory == IntPtr.Zero) return map;
            var factory = (IDXGIFactory1)Marshal.GetObjectForIUnknown(pFactory);
            try
            {
                for (uint i = 0; factory.EnumAdapters1(i, out var adapter) == 0; i++)
                {
                    try
                    {
                        if (adapter.GetDesc1(out var d) == 0)
                        {
                            string key = $"luid_0x{d.LuidHigh:X8}_0x{d.LuidLow:X8}";
                            map[key] = d.Description;
                            Dedicated[key] = (long)d.DedicatedVideoMemory.ToUInt64();
                        }
                    }
                    finally { Marshal.ReleaseComObject(adapter); }
                }
            }
            finally { Marshal.ReleaseComObject(factory); }
        }
        catch { }
        finally { if (pFactory != IntPtr.Zero) Marshal.Release(pFactory); }
        return map;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_ADAPTER_DESC1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId, DeviceId, SubSysId, Revision;
        public UIntPtr DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint LuidLow;
        public int LuidHigh;
        public uint Flags;
    }

    // Alleen de volgorde van de vtable telt; ongebruikte methodes zijn placeholders.
    [ComImport, Guid("770aae78-f26f-4dba-a829-253c83d1b387"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIFactory1
    {
        void P1(); void P2(); void P3(); void P4();      // IDXGIObject
        void P5(); void P6(); void P7(); void P8(); void P9(); // IDXGIFactory
        [PreserveSig] int EnumAdapters1(uint index, out IDXGIAdapter1 adapter);
    }

    [ComImport, Guid("29038f61-3839-4626-91fd-086879011a05"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIAdapter1
    {
        void P1(); void P2(); void P3(); void P4();      // IDXGIObject
        void P5(); void P6(); void P7();                 // IDXGIAdapter
        [PreserveSig] int GetDesc1(out DXGI_ADAPTER_DESC1 desc);
    }

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);
}

/// <summary>
/// Eén PDH-query met een jokerteken (bv. "\GPU Engine(*)\Utilization Percentage"). Leest alle instanties in één keer
/// (zoals Taakbeheer) in plaats van honderden losse PerformanceCounter-objecten per tik uit te lezen.
/// </summary>
internal sealed class PdhWildcard : IDisposable
{
    private IntPtr _query, _counter, _buffer;
    private uint _bufferSize;

    private PdhWildcard() { }

    public static PdhWildcard? TryCreate(string path)
    {
        var w = new PdhWildcard();
        try
        {
            if (PdhOpenQuery(null, IntPtr.Zero, out w._query) != 0) return null;
            if (PdhAddEnglishCounter(w._query, path, IntPtr.Zero, out w._counter) != 0) { w.Dispose(); return null; }
            PdhCollectQueryData(w._query);   // eerste meting: begintelling
            return w;
        }
        catch { w.Dispose(); return null; }
    }

    /// <summary>Verzamelt een nieuwe meting en geeft per instantie de waarde (alleen geldige waarden).</summary>
    public List<(string instance, double value)> Read()
    {
        var result = new List<(string, double)>();
        if (_query == IntPtr.Zero) return result;
        if (PdhCollectQueryData(_query) != 0) return result;

        uint size = _bufferSize, count = 0;
        uint rc = PdhGetFormattedCounterArray(_counter, 0x200 /* PDH_FMT_DOUBLE */, ref size, ref count, _buffer);
        if (rc == 0x800007D2 /* PDH_MORE_DATA */)
        {
            if (_buffer != IntPtr.Zero) Marshal.FreeHGlobal(_buffer);
            _bufferSize = size;
            _buffer = Marshal.AllocHGlobal((int)size);
            rc = PdhGetFormattedCounterArray(_counter, 0x200, ref size, ref count, _buffer);
        }
        if (rc != 0 || _buffer == IntPtr.Zero) return result;

        // PDH_FMT_COUNTERVALUE_ITEM: { LPWSTR name; DWORD status; (padding) double value }
        int itemSize = IntPtr.Size == 8 ? 24 : 16;
        int valueOffset = IntPtr.Size == 8 ? 16 : 8;
        for (int i = 0; i < count; i++)
        {
            IntPtr item = _buffer + i * itemSize;
            uint status = (uint)Marshal.ReadInt32(item, IntPtr.Size);
            if (status != 0 && status != 1) continue;   // PDH_CSTATUS_VALID_DATA / NEW_DATA
            string? name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(item));
            if (name is null) continue;
            double v = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item, valueOffset));
            result.Add((name, v));
        }
        return result;
    }

    public void Dispose()
    {
        if (_query != IntPtr.Zero) { PdhCloseQuery(_query); _query = IntPtr.Zero; }
        if (_buffer != IntPtr.Zero) { Marshal.FreeHGlobal(_buffer); _buffer = IntPtr.Zero; }
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQuery(string? source, IntPtr userData, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr userData, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint bufferSize, ref uint itemCount, IntPtr buffer);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);
}
