using System.Diagnostics;
using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;

namespace TaskbarStats;

/// <summary>Vrije/totale ruimte van één schijf (station).</summary>
public readonly record struct DriveSpace(string Name, long Total, long Free)
{
    public long Used => Total - Free;
    public double UsedPercent => Total <= 0 ? 0 : 100.0 * Used / Total;
}

/// <summary>
/// Leest CPU-, geheugen-, GPU-, netwerk-, schijf- en temperatuurwaarden uit met dezelfde
/// bronnen die Windows Task Manager gebruikt.
///  - CPU  : "Processor Information\% Processor Utility" (totaal en per core, zoals Task Manager).
///  - MEM  : GlobalMemoryStatusEx.dwMemoryLoad.
///  - GPU  : som van alle "GPU Engine\Utilization Percentage" per fysieke GPU (LUID).
///  - NET  : "Network Interface\Bytes Received/Sent per sec" (per adapter, plus totaal).
///  - DISK : "PhysicalDisk\Disk Read/Write Bytes/sec" (per schijf, plus totaal).
///  - TEMP : LibreHardwareMonitorLib (vereist administrator-rechten).
/// </summary>
public sealed class Metrics : IDisposable
{
    public double CpuPercent { get; private set; }
    public double[] CpuCores { get; private set; } = Array.Empty<double>();
    public double MemPercent { get; private set; }
    public ulong MemTotalBytes { get; private set; }
    public ulong MemUsedBytes { get; private set; }
    public double GpuPercent { get; private set; }
    public double NetDownBytesPerSec { get; private set; }
    public double NetUpBytesPerSec { get; private set; }
    public double DiskReadBytesPerSec { get; private set; }
    public double DiskWriteBytesPerSec { get; private set; }
    public bool BatteryPresent { get; private set; }
    public double BatteryPercent { get; private set; }
    public bool BatteryCharging { get; private set; }
    public bool BatteryOnAc { get; private set; }
    /// <summary>Geschatte resterende tijd in seconden bij ontladen (-1 = onbekend).</summary>
    public int BatteryRemainingSec { get; private set; } = -1;
    public double? CpuMHz { get; private set; }
    public double? CpuTempC { get; private set; }
    public double? GpuTempC { get; private set; }

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
    private readonly List<PerformanceCounter> _gpuCounters = new();
    private readonly Dictionary<string, (PerformanceCounter recv, PerformanceCounter sent)> _net = new();
    private readonly Dictionary<string, (PerformanceCounter read, PerformanceCounter write)> _disks = new();
    private Dictionary<string, (double down, double up)> _netRates = new();
    private Dictionary<string, double> _gpuRates = new();
    private Dictionary<string, (double read, double write)> _diskRates = new();

    private string? _gpuLuidFilter;
    private string? _netFilter;
    private Computer? _lhm;
    private long _rebuiltAt;

    public Metrics()
    {
        // "% Processor Utility" is de meting van Task Manager (houdt rekening met turbo).
        // Op systemen waar die ontbreekt vallen we terug op "% Processor Time".
        InitCpu("Processor Information", "% Processor Utility");
        if (_cpu is null) InitCpu("Processor", "% Processor Time");

        try
        {
            // Klokfrequentie = basisfrequentie x "% Processor Performance".
            _cpuFreq = new PerformanceCounter("Processor Information", "Processor Frequency", "_Total", true);
            _cpuPerf = new PerformanceCounter("Processor Information", "% Processor Performance", "_Total", true);
            _cpuFreq.NextValue(); _cpuPerf.NextValue();
        }
        catch { _cpuFreq?.Dispose(); _cpuPerf?.Dispose(); _cpuFreq = _cpuPerf = null; }

        RebuildGpuCounters();
        RebuildNetCounters();
        RebuildDiskCounters();
        _rebuiltAt = Environment.TickCount64;
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
            static (int g, int c) Key(string n)
            {
                var p = n.Split(',');
                return p.Length == 2 ? (int.Parse(p[0]), int.Parse(p[1])) : (0, int.Parse(n));
            }
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

    public void EnableTemperatures(bool on)
    {
        try
        {
            if (on && _lhm is null)
            {
                _lhm = new Computer { IsCpuEnabled = true, IsGpuEnabled = true };
                _lhm.Open();
            }
            else if (!on && _lhm is not null)
            {
                _lhm.Close();
                _lhm = null;
                CpuTempC = null;
                GpuTempC = null;
            }
        }
        catch { _lhm = null; }
    }

    private void UpdateTemperatures()
    {
        if (_lhm is null) return;
        try
        {
            foreach (var hw in _lhm.Hardware)
            {
                hw.Update();
                foreach (var s in hw.Sensors)
                {
                    if (s.SensorType != SensorType.Temperature || s.Value is null) continue;
                    if (hw.HardwareType == HardwareType.Cpu &&
                        (s.Name.Contains("Package") || s.Name.Contains("Core (Tctl", StringComparison.OrdinalIgnoreCase) ||
                         s.Name.Contains("Tdie", StringComparison.OrdinalIgnoreCase) || s.Name.StartsWith("Core")))
                        CpuTempC = s.Value;
                    if ((hw.HardwareType == HardwareType.GpuNvidia || hw.HardwareType == HardwareType.GpuAmd ||
                         hw.HardwareType == HardwareType.GpuIntel) && s.Name.Contains("Core"))
                        GpuTempC = s.Value;
                }
            }
        }
        catch { }
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

    /// <summary>Ruimte per vast station (C:, D:, ...).</summary>
    public static List<DriveSpace> GetDriveSpaces()
    {
        var list = new List<DriveSpace>();
        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                    list.Add(new DriveSpace(d.Name.TrimEnd('\\'), d.TotalSize, d.AvailableFreeSpace));
                }
                catch { }
            }
        }
        catch { }
        return list;
    }

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

    /// <summary>Dedicated videogeheugen van een GPU in bytes (0 = onbekend).</summary>
    public static long GpuDedicatedBytes(string luid)
    {
        _gpuNames ??= DxgiNames.Read();
        return DxgiNames.Dedicated.GetValueOrDefault(luid);
    }

    private void RebuildGpuCounters()
    {
        foreach (var c in _gpuCounters) c.Dispose();
        _gpuCounters.Clear();
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
        try
        {
            var cat = new PerformanceCounterCategory("GPU Engine");
            foreach (var inst in cat.GetInstanceNames())
            {
                if (!inst.Contains("engtype")) continue;
                foreach (var c in cat.GetCounters(inst))
                {
                    if (c.CounterName == "Utilization Percentage")
                    {
                        try { c.NextValue(); _gpuCounters.Add(c); } catch { c.Dispose(); }
                    }
                    else c.Dispose();
                }
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
        try { CpuPercent = _cpu is null ? 0 : Math.Min(100, _cpu.NextValue()); } catch { CpuPercent = 0; }

        try { CpuCores = _cpuCores.Select(c => { try { return Math.Min(100.0, c.NextValue()); } catch { return 0.0; } }).ToArray(); }
        catch { }

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
            foreach (var c in _gpuCounters)
            {
                var luid = ExtractLuid(c.InstanceName) ?? "";
                double v; try { v = c.NextValue(); } catch { continue; }
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

        UpdateTemperatures();

        // Instances (GPU-engines, adapters, schijven) komen en gaan; af en toe opnieuw opbouwen.
        if (Environment.TickCount64 - _rebuiltAt > 5000)
        {
            _rebuiltAt = Environment.TickCount64;
            RebuildGpuCounters();
            if (!_net.Keys.OrderBy(x => x).SequenceEqual(GetNetworkAdapters())) RebuildNetCounters();
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

    public void Dispose()
    {
        _cpu?.Dispose();
        _cpuFreq?.Dispose();
        _cpuPerf?.Dispose();
        foreach (var c in _vram) c.Dispose();
        foreach (var c in _cpuCores) c.Dispose();
        foreach (var c in _gpuCounters) c.Dispose();
        foreach (var (r, s) in _net.Values) { r.Dispose(); s.Dispose(); }
        foreach (var (r, w) in _disks.Values) { r.Dispose(); w.Dispose(); }
        try { _lhm?.Close(); } catch { }
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
