using System.Globalization;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TaskbarStats;

/// <summary>Een regel in de specificatiepagina: label plus (eventueel live) waarde.</summary>
public sealed record SpecRow(string Key, Func<string> Value);

/// <summary>Een groepje regels onder een titel.</summary>
public sealed record SpecBlock(string Title, List<SpecRow> Rows);

/// <summary>
/// Verzamelt de hardware- en systeemspecificaties (WMI, registry, Win32) voor de specificatiepagina van het fullscreen-scherm.
/// WMI is traag (soms seconden), dus alles gebeurt eenmalig op een achtergrondthread en het resultaat wordt gecachet;
/// bij het openen van de pagina wordt het hooguit elke 30 s ververst (USB-apparaten komen en gaan).
/// </summary>
public static class HardwareInfo
{
    private static volatile List<SpecBlock> _blocks = new();
    private static volatile bool _loading;
    private static long _loadedAt;

    public static IReadOnlyList<SpecBlock> Blocks => _blocks;
    public static bool Loading => _loading;
    /// <summary>Laatste fout of overgeslagen onderdeel (voor de uitleg op de pagina als er niets te tonen is).</summary>
    public static string LastError { get; private set; } = "";
    private static long _failedAt;

    /// <summary>Start (of ververst) het verzamelen op een achtergrondthread.</summary>
    public static void Refresh(Metrics metrics)
    {
        if (_loading) return;
        if (_blocks.Count > 0 && Environment.TickCount64 - _loadedAt < 30_000) return;
        if (_blocks.Count == 0 && _failedAt != 0 && Environment.TickCount64 - _failedAt < 10_000) return;
        _loading = true;
        var t = new Thread(() =>
        {
            try
            {
                var list = Collect(metrics);
                if (list.Count > 0) { _blocks = list; _failedAt = 0; } else _failedAt = Environment.TickCount64;
                _loadedAt = Environment.TickCount64;
            }
            catch (Exception ex) { LastError = ex.GetType().Name + ": " + ex.Message; _failedAt = Environment.TickCount64; }
            finally { _loading = false; }
        })
        { IsBackground = true, Name = "TaskbarStats specs", Priority = ThreadPriority.BelowNormal };
        t.Start();
    }

    // ---------- WMI-hulpjes ----------
    private const string Cim = @"root\CIMV2";

    private static List<ManagementBaseObject> Q(string ns, string query)
    {
        var res = new List<ManagementBaseObject>();
        try
        {
            using var s = new ManagementObjectSearcher(ns, query);
            s.Options.Timeout = TimeSpan.FromSeconds(10);
            foreach (ManagementBaseObject o in s.Get()) res.Add(o);
        }
        catch (Exception dex) { Diag.Swallow(dex); }
        return res;
    }

    private static string S(ManagementBaseObject o, string prop)
    {
        try { return o[prop]?.ToString()?.Trim() ?? ""; } catch { return ""; }
    }

    private static double D(ManagementBaseObject o, string prop)
    {
        try { return o[prop] is null ? 0 : Convert.ToDouble(o[prop], CultureInfo.InvariantCulture); } catch { return 0; }
    }

    private static SpecRow R(string k, string v) => new(k, () => v);
    private static string Size(double bytes) => Metrics.FormatSize(bytes);

    private static void Add(List<SpecRow> rows, string k, string? v)
    {
        if (!string.IsNullOrWhiteSpace(v)) rows.Add(R(k, v!));
    }

    private static string Date(string? wmi)
    {
        try { return string.IsNullOrEmpty(wmi) ? "" : ManagementDateTimeConverter.ToDateTime(wmi).ToString("d MMM yyyy"); } catch { return ""; }
    }

    private static string Decode(object? o)
    {
        if (o is not ushort[] a) return "";
        return new string(a.TakeWhile(c => c != 0).Select(c => (char)c).ToArray()).Trim();
    }

    // ---------- Verzamelen ----------
    private static List<SpecBlock> Collect(Metrics m)
    {
        var res = new List<SpecBlock>();
        bool first = _blocks.Count == 0;   // eerste keer: blokken tonen zodra ze klaar zijn (WMI kan op trage computers lang duren)
        void Show() { if (first) _blocks = new List<SpecBlock>(res); }
        // Elk onderdeel apart: als er één faalt (WMI uit, ontbrekende API, beperkte rechten) blijft de rest gewoon zichtbaar.
        T Safe<T>(string what, Func<T> f, T fallback)
        {
            try { return f(); }
            catch (Exception ex) { LastError = what + ": " + ex.GetType().Name + " " + ex.Message; return fallback; }
        }
        void Put(string title, string what, Func<List<SpecRow>> rows)
        {
            var r = Safe(what, rows, new List<SpecRow>());
            if (r.Count > 0) { res.Add(new SpecBlock(title, r)); Show(); }
        }
        void Many(string what, Func<IEnumerable<SpecBlock>> blocks)
        {
            var r = Safe(what, () => blocks().ToList(), new List<SpecBlock>());
            if (r.Count > 0) { res.AddRange(r); Show(); }
        }

        Put(Loc.T("Computer"), "computer", Computer);
        Put(Loc.T("Operating system"), "os", Os);
        Put(Loc.T("Processor"), "cpu", () => Cpu(m));
        Put(Loc.T("Processor — instruction sets"), "features", Features);
        Many("gpu", Gpus);
        Put(Loc.T("Memory@@ram"), "memory", () => Memory(m));
        Many("dimms", Dimms);
        Many("storage", Storage);
        Put(Loc.T("Volumes"), "volumes", Volumes);
        Many("displays", Displays);
        Many("network", Network);
        Put(Loc.T("Battery"), "battery", Battery);
        Put(Loc.T("Security"), "security", Security);
        Put(Loc.T("Audio"), "audio", Audio);
        Put(Loc.T("Input devices"), "input", Input);
        Put("Bluetooth", "bluetooth", Bluetooth);
        Put(Loc.T("USB devices"), "usb", Usb);
        return res;
    }

    private static List<SpecRow> Computer()
    {
        var rows = new List<SpecRow>();
        Add(rows, Loc.T("Name"), Environment.MachineName);
        var cs = Q(Cim, "SELECT Manufacturer, Model, SystemType, Domain, PartOfDomain FROM Win32_ComputerSystem").FirstOrDefault();
        if (cs is not null)
        {
            Add(rows, Loc.T("Manufacturer"), S(cs, "Manufacturer"));
            Add(rows, "Model", S(cs, "Model"));
            Add(rows, Loc.T("System type"), S(cs, "SystemType"));
        }
        var bb = Q(Cim, "SELECT Manufacturer, Product, Version FROM Win32_BaseBoard").FirstOrDefault();
        if (bb is not null) Add(rows, Loc.T("Motherboard"), $"{S(bb, "Manufacturer")} {S(bb, "Product")}".Trim());
        var bios = Q(Cim, "SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS").FirstOrDefault();
        if (bios is not null)
        {
            Add(rows, "BIOS", $"{S(bios, "Manufacturer")} {S(bios, "SMBIOSBIOSVersion")}".Trim());
            Add(rows, Loc.T("BIOS date"), Date(S(bios, "ReleaseDate")));
        }
        return rows;
    }

    private static List<SpecRow> Os()
    {
        var rows = new List<SpecRow>();
        var os = Q(Cim, "SELECT Caption, Version, BuildNumber, OSArchitecture, InstallDate, LastBootUpTime, RegisteredUser FROM Win32_OperatingSystem").FirstOrDefault();
        if (os is not null)
        {
            Add(rows, "Windows", S(os, "Caption").Replace("Microsoft ", ""));
            string ubr = "";
            try { using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"); ubr = k?.GetValue("UBR")?.ToString() ?? ""; } catch (Exception dex) { Diag.Swallow(dex); }
            Add(rows, Loc.T("Version"), $"{S(os, "Version")}{(ubr != "" ? "." + ubr : "")} (build {S(os, "BuildNumber")})");
            try
            {
                using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                Add(rows, Loc.T("Release"), k?.GetValue("DisplayVersion")?.ToString());
            }
            catch (Exception dex) { Diag.Swallow(dex); }
            Add(rows, Loc.T("Architecture"), S(os, "OSArchitecture"));
            Add(rows, Loc.T("Installed"), Date(S(os, "InstallDate")));
            Add(rows, Loc.T("Last boot"), Date(S(os, "LastBootUpTime")));
        }
        Add(rows, ".NET", RuntimeInformation.FrameworkDescription);
        Add(rows, Loc.T("Language / region"), $"{CultureInfo.CurrentUICulture.DisplayName} / {CultureInfo.CurrentCulture.Name}");
        Add(rows, Loc.T("Time zone"), TimeZoneInfo.Local.DisplayName);
        return rows;
    }

    private static List<SpecRow> Cpu(Metrics m)
    {
        var rows = new List<SpecRow>();
        var p = Q(Cim, "SELECT Name, Manufacturer, Description, Architecture, SocketDesignation, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, ExtClock, L2CacheSize, L3CacheSize, VirtualizationFirmwareEnabled, SecondLevelAddressTranslationExtensions, AddressWidth FROM Win32_Processor").ToList();
        if (p.Count == 0) return rows;
        var c = p[0];
        Add(rows, Loc.T("Model"), S(c, "Name"));
        Add(rows, Loc.T("Manufacturer"), S(c, "Manufacturer"));
        Add(rows, Loc.T("Family"), S(c, "Description"));
        Add(rows, Loc.T("Architecture"), D(c, "Architecture") switch { 0 => "x86", 5 => "ARM", 9 => "x64", 12 => "ARM64", _ => S(c, "Architecture") });
        Add(rows, "Socket", S(c, "SocketDesignation"));
        if (p.Count > 1) Add(rows, Loc.T("Processors"), p.Count.ToString());
        int cores = (int)p.Sum(x => D(x, "NumberOfCores")), threads = (int)p.Sum(x => D(x, "NumberOfLogicalProcessors"));
        Add(rows, Loc.T("Cores / threads"), $"{cores} / {threads}");
        double mhz = D(c, "MaxClockSpeed");
        if (mhz > 0) Add(rows, Loc.T("Base clock"), $"{mhz / 1000:0.00} GHz");
        rows.Add(new SpecRow(Loc.T("Current clock"), () => m.CpuMHz is double f ? $"{f / 1000:0.00} GHz" : "—"));
        rows.Add(new SpecRow(Loc.T("Load"), () => $"{m.CpuPercent:0}%"));
        double l2 = D(c, "L2CacheSize"), l3 = D(c, "L3CacheSize");
        if (l2 > 0) Add(rows, "L2-cache", l2 >= 1024 ? $"{l2 / 1024:0.#} MB" : $"{l2:0} KB");
        if (l3 > 0) Add(rows, "L3-cache", l3 >= 1024 ? $"{l3 / 1024:0.#} MB" : $"{l3:0} KB");
        Add(rows, Loc.T("Virtualization"), YesNo(c, "VirtualizationFirmwareEnabled"));
        Add(rows, Loc.T("Address width"), D(c, "AddressWidth") > 0 ? $"{D(c, "AddressWidth"):0}-bit" : "");
        return rows;
    }

    private static string YesNo(ManagementBaseObject o, string prop)
    {
        try { return o[prop] is bool b ? (b ? Loc.T("on") : Loc.T("off")) : ""; } catch { return ""; }
    }

    [DllImport("kernel32.dll")] private static extern bool IsProcessorFeaturePresent(int feature);

    private static List<SpecRow> Features()
    {
        var rows = new List<SpecRow>();
        var set = new (int id, string name)[]
        {
            (6, "SSE"), (10, "SSE2"), (13, "SSE3"), (36, "SSSE3"), (37, "SSE4.1"), (38, "SSE4.2"),
            (39, "AVX"), (40, "AVX2"), (41, "AVX-512"), (12, "NX"), (17, "PAE"), (7, "3DNow!"), (28, "RDTSC"),
            (23, "CMPXCHG16B"), (27, "MONITOR/MWAIT"),
        };
        var on = new List<string>(); var off = new List<string>();
        foreach (var (id, name) in set)
        {
            bool has; try { has = IsProcessorFeaturePresent(id); } catch { continue; }
            (has ? on : off).Add(name);
        }
        for (int i = 0; i < on.Count; i += 8)
            rows.Add(R(i == 0 ? Loc.T("Supported") : "", string.Join("  ", on.Skip(i).Take(8))));
        if (off.Count > 0) rows.Add(R(Loc.T("Not present"), string.Join("  ", off)));
        return rows;
    }

    private static List<SpecBlock> Gpus()
    {
        var res = new List<SpecBlock>();
        int n = 0;
        foreach (var v in Q(Cim, "SELECT Name, DriverVersion, DriverDate, VideoProcessor, AdapterRAM, CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate, CurrentBitsPerPixel, Status, AdapterCompatibility, VideoModeDescription FROM Win32_VideoController"))
        {
            var rows = new List<SpecRow>();
            string name = S(v, "Name");
            Add(rows, Loc.T("Model"), name);
            Add(rows, Loc.T("Manufacturer"), S(v, "AdapterCompatibility"));
            Add(rows, "GPU", S(v, "VideoProcessor"));
            long ded = 0;
            try { foreach (var luid in Metrics.GetGpuLuids()) if (Metrics.GpuName(luid).Equals(name, StringComparison.OrdinalIgnoreCase)) ded = Metrics.GpuDedicatedBytes(luid); } catch (Exception dex) { Diag.Swallow(dex); }
            if (ded <= 0) ded = (long)D(v, "AdapterRAM");
            if (ded > 0) Add(rows, Loc.T("Video memory"), Size(ded));
            Add(rows, Loc.T("Driver"), S(v, "DriverVersion"));
            Add(rows, Loc.T("Driver date"), Date(S(v, "DriverDate")));
            double w = D(v, "CurrentHorizontalResolution"), h = D(v, "CurrentVerticalResolution");
            if (w > 0) Add(rows, Loc.T("Output"), $"{w:0} × {h:0} @ {D(v, "CurrentRefreshRate"):0} Hz");
            Add(rows, "Status", S(v, "Status"));
            if (rows.Count > 0) res.Add(new SpecBlock(n++ == 0 ? Loc.T("Graphics") : Loc.T("Graphics") + " " + (n), rows));
        }
        return res;
    }

    private static List<SpecRow> Memory(Metrics m)
    {
        var rows = new List<SpecRow>();
        rows.Add(new SpecRow(Loc.T("Total"), () => Size(m.MemTotalBytes)));
        rows.Add(new SpecRow(Loc.T("In use"), () => $"{Size(m.MemUsedBytes)} ({m.MemPercent:0}%)"));
        var arr = Q(Cim, "SELECT MemoryDevices, MaxCapacity, MaxCapacityEx FROM Win32_PhysicalMemoryArray").FirstOrDefault();
        var dimms = Q(Cim, "SELECT Capacity FROM Win32_PhysicalMemory").Count;
        if (arr is not null)
        {
            Add(rows, Loc.T("Slots"), $"{dimms} {Loc.T("used of")} {D(arr, "MemoryDevices"):0}");
            double max = D(arr, "MaxCapacityEx") > 0 ? D(arr, "MaxCapacityEx") * 1024 : D(arr, "MaxCapacity") * 1024;
            if (max > 0) Add(rows, Loc.T("Maximum"), Size(max));
        }
        return rows;
    }

    private static string MemType(double code) => code switch
    {
        20 => "DDR", 21 => "DDR2", 24 => "DDR3", 26 => "DDR4", 27 => "LPDDR", 28 => "LPDDR2", 29 => "LPDDR3", 30 => "LPDDR4",
        34 => "DDR5", 35 => "LPDDR5", _ => "",
    };

    private static List<SpecBlock> Dimms()
    {
        var rows = new List<SpecRow>();
        foreach (var d in Q(Cim, "SELECT DeviceLocator, BankLabel, Capacity, Speed, ConfiguredClockSpeed, SMBIOSMemoryType, MemoryType, Manufacturer, PartNumber, FormFactor FROM Win32_PhysicalMemory"))
        {
            string type = MemType(D(d, "SMBIOSMemoryType"));
            double speed = D(d, "ConfiguredClockSpeed") > 0 ? D(d, "ConfiguredClockSpeed") : D(d, "Speed");
            var parts = new List<string> { Size(D(d, "Capacity")) };
            if (type != "") parts.Add(type);
            if (speed > 0) parts.Add($"{speed:0} MT/s");
            string man = S(d, "Manufacturer"), part = S(d, "PartNumber");
            if (man != "" && !man.StartsWith("0000") && man != "Unknown") parts.Add(man);
            if (part != "") parts.Add(part);
            string slot = S(d, "DeviceLocator");
            rows.Add(R(slot == "" ? S(d, "BankLabel") : slot, string.Join(" · ", parts)));
        }
        return rows.Count == 0 ? new() : new() { new SpecBlock(Loc.T("Memory modules"), rows) };
    }

    private static string Bus(double code) => code switch
    {
        1 => "SCSI", 2 => "ATAPI", 3 => "ATA", 4 => "1394", 5 => "SSA", 6 => "Fibre", 7 => "USB", 8 => "RAID", 9 => "iSCSI", 10 => "SAS",
        11 => "SATA", 12 => "SD", 13 => "MMC", 15 => "File-backed virtual", 16 => "Storage Spaces", 17 => "NVMe", _ => "",
    };

    private static List<SpecBlock> Storage()
    {
        var res = new List<SpecBlock>();
        var phys = Q(@"root\Microsoft\Windows\Storage", "SELECT FriendlyName, MediaType, BusType, Size, HealthStatus, OperationalStatus, FirmwareVersion, SpindleSpeed, Model FROM MSFT_PhysicalDisk");
        if (phys.Count > 0)
        {
            int i = 0;
            foreach (var d in phys)
            {
                i++;
                var rows = new List<SpecRow>();
                Add(rows, "Model", S(d, "FriendlyName"));
                string media = D(d, "MediaType") switch { 3 => "HDD", 4 => "SSD", 5 => "SCM", _ => "" };
                string bus = Bus(D(d, "BusType"));
                Add(rows, Loc.T("Type"), $"{media} {bus}".Trim());
                Add(rows, Loc.T("Size"), Size(D(d, "Size")));
                Add(rows, Loc.T("Health"), D(d, "HealthStatus") switch { 0 => Loc.T("healthy"), 1 => Loc.T("warning"), 2 => Loc.T("unhealthy"), _ => "" });
                Add(rows, "Firmware", S(d, "FirmwareVersion"));
                if (D(d, "SpindleSpeed") is > 0 and < 100000) Add(rows, Loc.T("Spindle speed"), $"{D(d, "SpindleSpeed"):0} rpm");
                res.Add(new SpecBlock(Loc.T("Drive") + $" {i - 1}", rows));
            }
            return res;
        }
        int n = 0;
        foreach (var d in Q(Cim, "SELECT Model, InterfaceType, Size, MediaType, Status, FirmwareRevision FROM Win32_DiskDrive"))
        {
            var rows = new List<SpecRow>();
            Add(rows, "Model", S(d, "Model"));
            Add(rows, Loc.T("Interface"), S(d, "InterfaceType"));
            Add(rows, Loc.T("Size"), Size(D(d, "Size")));
            Add(rows, "Status", S(d, "Status"));
            Add(rows, "Firmware", S(d, "FirmwareRevision"));
            res.Add(new SpecBlock(Loc.T("Drive") + $" {n++}", rows));
        }
        return res;
    }

    private static List<SpecRow> Volumes()
    {
        var rows = new List<SpecRow>();
        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady) continue;
                    string label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? "" : d.VolumeLabel + " · ";
                    string kind = d.DriveType switch { DriveType.Fixed => "", DriveType.Removable => Loc.T("removable") + " · ", DriveType.Network => Loc.T("network") + " · ", DriveType.CDRom => "cd/dvd · ", _ => "" };
                    string name = d.Name.TrimEnd('\\');
                    rows.Add(R(name, $"{kind}{label}{d.DriveFormat} · {Size(d.TotalFreeSpace)} {Loc.T("free of")} {Size(d.TotalSize)}"));
                }
                catch (Exception dex) { Diag.Swallow(dex); }
            }
        }
        catch (Exception dex) { Diag.Swallow(dex); }
        return rows;
    }

    // ---------- Schermen ----------
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public uint dmFields;
        public int dmPositionX, dmPositionY;
        public uint dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public uint dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplaySettings(string? device, int mode, ref DEVMODE dm);

    private static List<SpecBlock> Displays()
    {
        var res = new List<SpecBlock>();
        var ids = Q(@"root\wmi", "SELECT ManufacturerName, UserFriendlyName, YearOfManufacture, WeekOfManufacture FROM WmiMonitorID");
        var sizes = Q(@"root\wmi", "SELECT MaxHorizontalImageSize, MaxVerticalImageSize FROM WmiMonitorBasicDisplayParams");
        int i = 0;
        foreach (var sc in Screen.AllScreens)
        {
            var rows = new List<SpecRow>();
            if (i < ids.Count)
            {
                string man = Decode(ids[i]["ManufacturerName"]), model = Decode(ids[i]["UserFriendlyName"]);
                Add(rows, "Monitor", $"{man} {model}".Trim());
                double year = D(ids[i], "YearOfManufacture");
                if (year > 1990) Add(rows, Loc.T("Made"), $"{year:0}");
            }
            if (i < sizes.Count)
            {
                double w = D(sizes[i], "MaxHorizontalImageSize"), h = D(sizes[i], "MaxVerticalImageSize");
                if (w > 0 && h > 0) Add(rows, Loc.T("Diagonal"), $"{Math.Sqrt(w * w + h * h) / 2.54:0.0}\" ({w:0} × {h:0} cm)");
            }
            Add(rows, Loc.T("Resolution"), $"{sc.Bounds.Width} × {sc.Bounds.Height}");
            var dm = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
            try { if (EnumDisplaySettings(sc.DeviceName, -1, ref dm)) Add(rows, Loc.T("Refresh rate"), $"{dm.dmDisplayFrequency} Hz"); } catch (Exception dex) { Diag.Swallow(dex); }
            Add(rows, Loc.T("Colour depth"), $"{sc.BitsPerPixel}-bit");
            Add(rows, Loc.T("Position"), $"{sc.Bounds.X}, {sc.Bounds.Y}");
            Add(rows, Loc.T("Work area"), $"{sc.WorkingArea.Width} × {sc.WorkingArea.Height}");
            Add(rows, Loc.T("Primary"), sc.Primary ? Loc.T("yes") : Loc.T("no"));
            i++;
            res.Add(new SpecBlock(Loc.T("Display") + $" {i}", rows));
        }
        return res;
    }

    // ---------- Netwerk ----------
    private static List<SpecBlock> Network()
    {
        var res = new List<SpecBlock>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                var rows = new List<SpecRow>();
                Add(rows, Loc.T("Adapter"), ni.Description);
                Add(rows, Loc.T("Type"), ni.NetworkInterfaceType switch
                {
                    NetworkInterfaceType.Wireless80211 => "Wi-Fi",
                    NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet => "Ethernet",
                    var t => t.ToString(),
                });
                if (ni.Speed > 0) Add(rows, Loc.T("Link speed"), ni.Speed >= 1_000_000_000 ? $"{ni.Speed / 1e9:0.#} Gbps" : $"{ni.Speed / 1e6:0} Mbps");
                var mac = ni.GetPhysicalAddress().ToString();
                if (mac.Length == 12) Add(rows, "MAC", string.Join(":", Enumerable.Range(0, 6).Select(i => mac.Substring(i * 2, 2))));
                var ip = ni.GetIPProperties();
                var v4 = ip.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                Add(rows, "IPv4", v4?.Address.ToString());
                var gw = ip.GatewayAddresses.FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                Add(rows, "Gateway", gw?.Address.ToString());
                var dns = ip.DnsAddresses.Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).Take(2).Select(a => a.ToString());
                Add(rows, "DNS", string.Join(", ", dns));
                res.Add(new SpecBlock(Loc.T("Network") + " — " + ni.Name, rows));
                if (res.Count >= 4) break;
            }
        }
        catch (Exception dex) { Diag.Swallow(dex); }
        return res;
    }

    // ---------- Batterij ----------
    private static List<SpecRow> Battery()
    {
        var rows = new List<SpecRow>();
        var b = Q(Cim, "SELECT Name, Chemistry, DesignVoltage, EstimatedChargeRemaining, BatteryStatus FROM Win32_Battery").FirstOrDefault();
        if (b is null) return rows;
        Add(rows, Loc.T("Name"), S(b, "Name"));
        Add(rows, Loc.T("Chemistry"), D(b, "Chemistry") switch { 3 => "Lead acid", 4 => "NiCd", 5 => "NiMH", 6 => "Li-ion", 7 => "Zinc air", 8 => "Li-polymer", _ => "" });
        if (D(b, "DesignVoltage") > 0) Add(rows, Loc.T("Design voltage"), $"{D(b, "DesignVoltage") / 1000:0.0} V");
        var st = Q(@"root\wmi", "SELECT DesignedCapacity FROM BatteryStaticData").FirstOrDefault();
        var full = Q(@"root\wmi", "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity").FirstOrDefault();
        var cyc = Q(@"root\wmi", "SELECT CycleCount FROM BatteryCycleCount").FirstOrDefault();
        double design = st is null ? 0 : D(st, "DesignedCapacity"), fc = full is null ? 0 : D(full, "FullChargedCapacity");
        if (design > 0) Add(rows, Loc.T("Design capacity"), $"{design / 1000:0.0} Wh");
        if (fc > 0) Add(rows, Loc.T("Full charge now"), $"{fc / 1000:0.0} Wh");
        if (design > 0 && fc > 0) Add(rows, Loc.T("Wear"), $"{Math.Max(0, 100 - 100 * fc / design):0}%  ({100 * fc / design:0}% {Loc.T("health")})");
        if (cyc is not null && D(cyc, "CycleCount") > 0) Add(rows, Loc.T("Charge cycles"), $"{D(cyc, "CycleCount"):0}");
        return rows;
    }

    // ---------- Beveiliging ----------
    private static List<SpecRow> Security()
    {
        var rows = new List<SpecRow>();
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
            if (k is not null)
            {
                Add(rows, Loc.T("Boot mode"), "UEFI");
                Add(rows, "Secure Boot", k.GetValue("UEFISecureBootEnabled") is int i && i == 1 ? Loc.T("on") : Loc.T("off"));
            }
            else Add(rows, Loc.T("Boot mode"), "Legacy BIOS");
        }
        catch (Exception dex) { Diag.Swallow(dex); }
        var tpm = Q(@"root\CIMV2\Security\MicrosoftTpm", "SELECT IsEnabled_InitialValue, SpecVersion, ManufacturerVersion FROM Win32_Tpm").FirstOrDefault();
        if (tpm is not null)
        {
            Add(rows, "TPM", $"{S(tpm, "SpecVersion").Split(',')[0]} · {(tpm["IsEnabled_InitialValue"] is true ? Loc.T("on") : Loc.T("off"))}");
        }
        else Add(rows, "TPM", Loc.T("not found"));
        return rows;
    }

    // ---------- Randapparatuur ----------
    private static List<SpecRow> Audio()
    {
        var rows = new List<SpecRow>();
        foreach (var d in Q(Cim, "SELECT Name, Status, Manufacturer FROM Win32_SoundDevice"))
            rows.Add(R(S(d, "Status") == "OK" || S(d, "Status") == "" ? Loc.T("Device") : S(d, "Status"), S(d, "Name")));
        return rows;
    }

    private static List<SpecRow> Input()
    {
        var rows = new List<SpecRow>();
        var seen = new HashSet<string>();
        foreach (var d in Q(Cim, "SELECT Name FROM Win32_Keyboard"))
            if (seen.Add("k" + S(d, "Name"))) rows.Add(R(Loc.T("Keyboard"), S(d, "Name")));
        foreach (var d in Q(Cim, "SELECT Name, NumberOfButtons FROM Win32_PointingDevice"))
            if (seen.Add("p" + S(d, "Name"))) rows.Add(R(Loc.T("Mouse / touchpad"), S(d, "Name")));
        foreach (var d in Q(Cim, "SELECT Name FROM Win32_PnPEntity WHERE PNPClass = 'Camera' AND Present = TRUE"))
            if (seen.Add("c" + S(d, "Name"))) rows.Add(R(Loc.T("Camera"), S(d, "Name")));
        return rows;
    }

    private static readonly string[] BtSkip = { "Enumerator", "RFCOMM", "Protocol", "Service", "Transport", "Gateway", "Identification", "Radio Management", "Avrcp", "Audio/Video", "Generic Attribute", "Generic Access", "Device Information", "Battery", "Human Interface", "Object Push", "Personal Area" };

    private static List<SpecRow> Bluetooth()
    {
        var rows = new List<SpecRow>();
        var seen = new HashSet<string>();
        foreach (var d in Q(Cim, "SELECT Name FROM Win32_PnPEntity WHERE PNPClass = 'Bluetooth' AND Present = TRUE"))
        {
            string n = S(d, "Name");
            if (n == "" || BtSkip.Any(s => n.Contains(s, StringComparison.OrdinalIgnoreCase))) continue;
            if (seen.Add(n)) rows.Add(R(rows.Count == 0 ? Loc.T("Adapter / devices") : "", n));
            if (rows.Count >= 12) break;
        }
        return rows;
    }

    private static readonly string[] UsbSkip = { "Root Hub", "Generic USB Hub", "USB Composite Device", "Host Controller", "Generic SuperSpeed USB Hub" };

    private static List<SpecRow> Usb()
    {
        var rows = new List<SpecRow>();
        foreach (var c in Q(Cim, "SELECT Name FROM Win32_USBController"))
            rows.Add(R(Loc.T("Controller"), S(c, "Name")));
        var counts = new Dictionary<string, (string cls, int n)>();
        foreach (var d in Q(Cim, @"SELECT Name, PNPClass, Manufacturer FROM Win32_PnPEntity WHERE DeviceID LIKE 'USB\\%' AND Present = TRUE"))
        {
            string n = S(d, "Name");
            if (n == "" || UsbSkip.Any(s => n.Contains(s, StringComparison.OrdinalIgnoreCase))) continue;
            string man = S(d, "Manufacturer");
            string cls = S(d, "PNPClass");
            counts[n] = counts.TryGetValue(n, out var v) ? (v.cls, v.n + 1) : (cls, 1);
        }
        foreach (var (name, (cls, n)) in counts.OrderBy(kv => kv.Value.cls).ThenBy(kv => kv.Key).Take(40))
            rows.Add(R(cls == "" ? "USB" : cls, n > 1 ? $"{name} (×{n})" : name));
        return rows;
    }
}
