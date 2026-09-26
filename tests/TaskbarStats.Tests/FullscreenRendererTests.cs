using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Security.Cryptography;
using LibreHardwareMonitor.Hardware;
using Xunit;

namespace TaskbarStats.Tests;

/// <summary>
/// Eén vaste thread voor alle tekenwerk: <c>GdiCache</c> is alleen voor de UI-thread (Debug-controle op de thread-id),
/// en xUnit draait tests niet altijd op dezelfde thread.
/// </summary>
internal static class UiThread
{
    private static readonly BlockingCollection<Action> Queue = new();
    static UiThread()
    {
        var t = new Thread(() => { foreach (var a in Queue.GetConsumingEnumerable()) a(); }) { IsBackground = true, Name = "test-ui" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }

    public static T Run<T>(Func<T> f)
    {
        T? result = default; Exception? error = null;
        using var done = new ManualResetEventSlim();
        Queue.Add(() => { try { result = f(); } catch (Exception e) { error = e; } finally { done.Set(); } });
        done.Wait();
        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        return result!;
    }
    public static void Run(Action a) => Run(() => { a(); return 0; });
}

/// <summary>
/// De tekencode van het fullscreen-scherm (<see cref="FullscreenRenderer"/>) op een gewone bitmap, zonder venster:
/// vaste klok en handgemaakte momentopname, dus de uitvoer is deterministisch.
/// </summary>
public class FullscreenRendererTests : IDisposable
{
    private readonly TempData _data = new();
    private readonly Metrics _metrics = new();
    private static readonly DateTime FixedNow = new(2026, 3, 14, 15, 9, 26);
    private static readonly Size Canvas = new(1920, 1080);
    private static readonly string[] Pages = { "cpu", "gpu", "mem", "net", "disk", "sys", "proc", "spec" };

    public void Dispose() { _metrics.Dispose(); _data.Dispose(); }

    // ---------- opzet ----------
    private static MetricsSnapshot Snap(bool battery = false, bool sensors = false, bool gpu = false) => new()
    {
        RawCpu = 42, RawMem = 61, RawGpu = 30, RawDown = 2.5e6, RawUp = 3e5,
        RawCores = new double[] { 10, 20, 30, 40, 50, 60, 70, 95 }, RawCpuTemp = 55, RawGpuTemp = 62,
        MemTotalBytes = 32UL << 30, MemUsedBytes = 19UL << 30, CpuMHz = 3400,
        DiskReadBytesPerSec = 3e6, DiskWriteBytesPerSec = 1e6,
        BatteryPresent = battery, BatteryPercent = 63, BatteryOnAc = false, BatteryRemainingSec = 5400,
        NetPerAdapter = new Dictionary<string, (double, double)> { ["Ethernet"] = (2.5e6, 4e5), ["Wi-Fi"] = (1.2e5, 3e4) },
        GpuPerLuid = gpu ? new Dictionary<string, double> { [FakeLuid] = 55 } : new Dictionary<string, double>(),
        VramUsedPerLuid = gpu ? new Dictionary<string, double> { [FakeLuid] = 2e9 } : new Dictionary<string, double>(),
        DiskPerDisk = new Dictionary<string, (double, double)> { ["0 C:"] = (3e6, 1e6) },
        Sensors = sensors ? FakeSensors() : Array.Empty<SensorInfo>(),
    };

    private const string FakeLuid = "0x00000000_0x0000F00D";

    private static IReadOnlyList<SensorInfo> FakeSensors()
    {
        SensorInfo S(string hw, HardwareType h, string n, SensorType t, double v) => new(hw, h, n, t, v, v / 2, v * 1.2);
        var l = new List<SensorInfo>
        {
            S("Test CPU", HardwareType.Cpu, "CPU Package", SensorType.Power, 28.4),
            S("Test CPU", HardwareType.Cpu, "Core (Tctl/Tdie)", SensorType.Temperature, 78),
            S("Test GPU One", HardwareType.GpuNvidia, "GPU Core", SensorType.Temperature, 61),
            S("Test SSD", HardwareType.Storage, "Temperature", SensorType.Temperature, 41),
            S("Test Board", HardwareType.SuperIO, "Fan #1", SensorType.Fan, 1200),
        };
        for (int i = 1; i <= 4; i++) l.Add(S("Test CPU", HardwareType.Cpu, "Core #" + i, SensorType.Clock, 2800 + 100 * i));
        return l;
    }

    private static MetricHistory History()
    {
        var h = new MetricHistory();
        var r = new Random(7);
        double c = 40, g = 30, m = 55, d = 1e6, u = 2e5;
        for (int i = 0; i < 120; i++)
        {
            c = Math.Clamp(c + r.Next(-15, 16), 0, 100); g = Math.Clamp(g + r.Next(-15, 16), 0, 100); m = Math.Clamp(m + r.Next(-4, 5), 0, 100);
            d = Math.Max(0, d + r.Next(-500000, 600000)); u = Math.Max(0, u + r.Next(-100000, 120000));
            h.Cpu.Add(c); h.Gpu.Add(g); h.Mem.Add(m); h.NetDown.Add(d); h.NetUp.Add(u); h.DiskRead.Add(d / 2); h.DiskWrite.Add(u);
        }
        return h;
    }

    private sealed class Rig : IDisposable
    {
        public FullscreenRenderer R = null!;
        public FullscreenView V = new();
        public AppSettings Cfg = null!;
        public Rig() { }
        public void Dispose() => R.Dispose();
    }

    private Rig MakeRig(MetricHistory? history = null, IEnumerable<SpecBlock>? specs = null, List<DriveSpace>? drives = null, bool loadingSpecs = false)
    {
        var cfg = AppSettings.Load();
        cfg.ShowPing = false;   // geen echte pingmeting in tests
        var ctx = new DashContext
        {
            Metrics = _metrics, Cfg = cfg, Usage = new UsageTracker(_data.Dir), History = history ?? History(),
            Drives = () => drives ?? new List<DriveSpace> { new("C:", 500L << 30, 120L << 30), new("D:", 2000L << 30, 1500L << 30) },
            ShowMenu = _ => { },
        };
        var rig = new Rig { Cfg = cfg };
        rig.R = new FullscreenRenderer(ctx, new ProcessSampler { TopCount = 12 })
        {
            Now = () => FixedNow,
            Ticks = () => 1_000_000_000,
            Specs = () => new SpecData(specs?.ToList() ?? new List<SpecBlock>(), loadingSpecs, ""),
            RefreshSpecs = () => { },   // geen echte WMI-metingen
        };
        rig.V.OpenedAt = 0;
        return rig;
    }

    private static List<SpecBlock> SpecBlocks(int n)
    {
        var l = new List<SpecBlock>();
        for (int i = 0; i < n; i++)
        {
            int k = i;
            l.Add(new SpecBlock("Block " + k, Enumerable.Range(0, 5 + k % 6).Select(r => new SpecRow("Key " + k + "." + r, () => "Value " + k + " " + new string('x', 5 + (k + r) * 3 % 40))).ToList()));
        }
        return l;
    }

    private static string Render(Rig rig, MetricsSnapshot snap, Size? size = null)
    {
        var s = size ?? Canvas;
        using var bmp = new Bitmap(s.Width, s.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp)) rig.R.Paint(g, s, rig.V, snap);
        return Hash(bmp);
    }

    private static string Hash(Bitmap bmp)
    {
        var d = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[Math.Abs(d.Stride) * d.Height];
            System.Runtime.InteropServices.Marshal.Copy(d.Scan0, bytes, 0, bytes.Length);
            return Convert.ToHexString(SHA256.HashData(bytes));
        }
        finally { bmp.UnlockBits(d); }
    }

    private static int DistinctColors(Bitmap bmp)
    {
        var set = new HashSet<int>();
        for (int y = 0; y < bmp.Height; y += 7) for (int x = 0; x < bmp.Width; x += 7) set.Add(bmp.GetPixel(x, y).ToArgb());
        return set.Count;
    }

    // ---------- tests ----------
    [Fact]
    public void Overzicht_geeft_twee_keer_precies_hetzelfde_beeld()
        => UiThread.Run(() =>
        {
            using var rig = MakeRig();
            var snap = Snap(battery: true);
            Assert.Equal(Render(rig, snap), Render(rig, snap));
        });

    [Fact]
    public void Overzicht_is_niet_leeg()
        => UiThread.Run(() =>
        {
            using var rig = MakeRig();
            using var bmp = new Bitmap(1920, 1080, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp)) rig.R.Paint(g, Canvas, rig.V, Snap());
            Assert.True(DistinctColors(bmp) > 20);
            Assert.Equal(FullscreenRenderer.Bg.ToArgb(), bmp.GetPixel(2, 1078).ToArgb());   // hoek buiten de tegels: achtergrondkleur
        });

    [Fact]
    public void Elke_detailpagina_is_deterministisch_en_verschilt_van_de_andere_pagina_s()
        => UiThread.Run(() =>
        {
            using var rig = MakeRig(specs: SpecBlocks(6));
            var snap = Snap(battery: true, sensors: true);
            var seen = new Dictionary<string, string> { ["overzicht"] = Render(rig, snap) };
            foreach (var page in Pages)
            {
                rig.V.Detail = page;
                string a = Render(rig, snap), b = Render(rig, snap);
                Assert.True(a == b, "pagina " + page + " is niet deterministisch");
                foreach (var (name, h) in seen) Assert.True(h != a, $"pagina {page} is gelijk aan {name}");
                seen[page] = a;
            }
        });

    [Fact]
    public void Lege_gegevens_geven_geen_fout_op_geen_enkele_pagina()
        => UiThread.Run(() =>
        {
            using var rig = MakeRig(history: new MetricHistory(), drives: new List<DriveSpace>());
            var snap = MetricsSnapshot.Empty;   // geen GPU, geen batterij, geen sensoren, geen adapters
            Render(rig, snap);
            foreach (var page in Pages) { rig.V.Detail = page; Render(rig, snap); }
        });

    [Fact]
    public void Alle_onderdelen_uit_toont_een_melding_en_geen_tegels()
        => UiThread.Run(() =>
        {
            using var rig = MakeRig();
            rig.Cfg.FullHidden = new List<string> { "cpu", "gpu", "mem", "net", "disk", "batt", "sys", "proc" };
            string none = Render(rig, Snap());
            Assert.DoesNotContain(rig.R.Hits, h => h.key is "cpu" or "gpu" or "mem" or "net" or "disk" or "bat" or "sys" or "proc");
            rig.Cfg.FullHidden = null;
            Assert.NotEqual(none, Render(rig, Snap()));
        });

    [Fact]
    public void Met_GPU_batterij_en_sensoren_verschilt_het_van_zonder()
        => UiThread.Run(() =>
        {
            var names = (Dictionary<string, string>?)typeof(Metrics).GetField("_gpuNames", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
            var field = typeof(Metrics).GetField("_gpuNames", BindingFlags.NonPublic | BindingFlags.Static)!;
            field.SetValue(null, new Dictionary<string, string> { [FakeLuid] = "Test GPU One" });
            try
            {
                using var rig = MakeRig();
                foreach (var page in new string?[] { null, "gpu", "sys", "cpu", "disk" })
                {
                    rig.V.Detail = page;
                    Assert.NotEqual(Render(rig, Snap(gpu: false)), Render(rig, Snap(gpu: true, battery: true, sensors: true)));
                }
                rig.V.Detail = "gpu";
                var withGpu = Snap(gpu: true, sensors: true);
                Assert.Equal(Render(rig, withGpu), Render(rig, withGpu));
            }
            finally { field.SetValue(null, names); }
        });

    [Fact]
    public void Grafiekvenster_verandert_de_grafieken()
        => UiThread.Run(() =>
        {
            using var rig = MakeRig();
            var snap = Snap();
            foreach (var page in new[] { "cpu", "net" })
            {
                rig.V.Detail = page;
                var hs = new HashSet<string>();
                foreach (int win in new[] { 60, 300, 3600 }) { rig.V.Win = win; hs.Add(Render(rig, snap)); }
                Assert.Equal(3, hs.Count);
            }
        });

    [Fact]
    public void Hover_en_tour_veranderen_het_beeld_en_de_kop()
        => UiThread.Run(() =>
        {
            using var rig = MakeRig();
            var snap = Snap();
            string plain = Render(rig, snap);
            rig.V.Hover = "cpu";
            string hot = Render(rig, snap);
            Assert.NotEqual(plain, hot);
            rig.V.Hover = null;
            rig.V.Tour = true; rig.V.TourIdx = 1; rig.V.TourCount = 5; rig.V.TourAt = 1_000_000_000 - 2000;
            string tour = Render(rig, snap);
            Assert.NotEqual(plain, tour);
            rig.V.TourIdx = 2;
            Assert.NotEqual(tour, Render(rig, snap));   // "Tour 3/5" i.p.v. "Tour 2/5"
        });

    [Fact]
    public void Specificatiepagina_scrolt_en_geeft_de_maximale_scrollstand_terug()
        => UiThread.Run(() =>
        {
            using var rig = MakeRig(specs: SpecBlocks(20));
            rig.V.Detail = "spec";
            var snap = Snap();
            string top = Render(rig, snap);
            Assert.True(rig.V.SpecMax > 0);
            rig.V.SpecScroll = 1e6f;
            string end = Render(rig, snap);
            Assert.Equal(rig.V.SpecMax, rig.V.SpecScroll);   // de renderer begrenst de scrollstand op het einde van de lijst
            Assert.NotEqual(top, end);
            rig.V.SpecScroll = rig.V.SpecMax / 2;
            Assert.NotEqual(top, Render(rig, snap));

            using var few = MakeRig(specs: SpecBlocks(1));
            few.V.Detail = "spec"; few.V.SpecScroll = 500;
            Render(few, snap);
            Assert.Equal(0, few.V.SpecMax);
            Assert.Equal(0, few.V.SpecScroll);
        });

    [Fact]
    public void Specificatiepagina_zonder_gegevens_vraagt_ze_opnieuw_aan()
        => UiThread.Run(() =>
        {
            using var rig = MakeRig();
            int asked = 0;
            rig.R.RefreshSpecs = () => asked++;
            rig.V.Detail = "spec";
            string empty = Render(rig, Snap());
            Assert.Equal(1, asked);
            using var loading = MakeRig(loadingSpecs: true);
            loading.V.Detail = "spec";
            Assert.NotEqual(empty, Render(loading, Snap()));
        });

    [Fact]
    public void Klikbare_vlakken_van_het_overzicht_en_van_een_detailpagina()
        => UiThread.Run(() =>
        {
            using var rig = MakeRig();
            Render(rig, Snap());
            var keys = rig.R.Hits.Select(h => h.key).ToList();
            foreach (var k in new[] { "cpu", "gpu", "mem", "net", "disk", "bat", "sys", "proc", "spec", "exit", "w60", "w300", "w3600" }) Assert.Contains(k, keys);
            Assert.DoesNotContain("back", keys);

            var cpu = rig.R.Hits.First(h => h.key == "cpu").r;
            Assert.Equal("cpu", rig.R.HitAt(new Point((int)(cpu.X + cpu.Width / 2), (int)(cpu.Y + cpu.Height / 2))));
            Assert.Null(rig.R.HitAt(new Point(1, 1)));

            rig.V.Detail = "cpu";
            Render(rig, Snap());
            Assert.Contains(rig.R.Hits, h => h.key == "back");
            Assert.DoesNotContain(rig.R.Hits, h => h.key == "gpu");
        });

    [Fact]
    public void Schaal_en_klikpunten_volgen_de_venstergrootte()
        => UiThread.Run(() =>
        {
            using var rig = MakeRig();
            Render(rig, Snap());
            Assert.Equal(1f, rig.R.Scale);
            var half = new Size(960, 540);
            Render(rig, Snap(), half);
            Assert.Equal(0.5f, rig.R.Scale);
            var cpu = rig.R.Hits.First(h => h.key == "cpu").r;   // canvascoördinaten blijven gelijk
            Assert.Equal("cpu", rig.R.HitAt(new Point((int)((cpu.X + 20) / 2), (int)((cpu.Y + 20) / 2))));

            // ander beeldverhouding: het canvas wordt gecentreerd (zwarte randen), niet uitgerekt
            var wide = new Size(2560, 1080);
            Render(rig, Snap(), wide);
            Assert.Equal(1f, rig.R.Scale);
            Assert.Equal("cpu", rig.R.HitAt(new Point((int)(cpu.X + 20 + 320), (int)(cpu.Y + 20))));   // (2560-1920)/2 = 320 naar rechts
        });

    [Fact]
    public void Achtergrond_wordt_na_het_wissen_en_voor_de_tegels_getekend()
        => UiThread.Run(() =>
        {
            using var rig = MakeRig();
            using var bmp = new Bitmap(1920, 1080, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            bool called = false;
            rig.R.Paint(g, Canvas, rig.V, Snap(), gg => { called = true; gg.FillRectangle(Brushes.Red, 0, 1070, 1920, 10); });
            Assert.True(called);
            Assert.Equal(Color.Red.ToArgb(), bmp.GetPixel(900, 1075).ToArgb());   // onder de tegels: de achtergrond blijft zichtbaar
        });

    [Theory]
    [InlineData(1, 3000)]
    [InlineData(10, 10000)]
    [InlineData(999, 300000)]
    public void Tourduur_is_begrensd_op_3_tot_300_seconden(int seconds, int expectedMs)
    {
        var cfg = new AppSettings { TourSeconds = seconds };
        Assert.Equal(expectedMs, FullscreenView.TourMillis(cfg));
    }
}
