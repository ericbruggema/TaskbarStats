using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Xunit;

namespace TaskbarStats.Tests;

/// <summary>DashboardRenderer tekent naar een gewone Bitmap, zonder venster; plus de Ring/MetricHistory-modellen.</summary>
public class DashboardRendererTests
{
    private static readonly string[] AllTiles = { "cpu", "gpu", "mem", "net", "disk", "batt", "proc", "sys" };

    private static MetricsSnapshot Snap(double cpu = 42, bool battery = false) => new()
    {
        RawCpu = cpu, RawMem = 55, RawGpu = 10, RawDown = 2e6, RawUp = 3e5,
        RawCores = new double[] { 10, 35, 60, 90 },
        MemTotalBytes = 32UL << 30, MemUsedBytes = 19UL << 30,
        DiskReadBytesPerSec = 1e6, DiskWriteBytesPerSec = 5e5,
        BatteryPresent = battery, BatteryPercent = 64, BatteryOnAc = false, BatteryRemainingSec = 5400,
        NetPerAdapter = new Dictionary<string, (double down, double up)> { ["Ethernet"] = (2e6, 3e5) },
    };

    private static List<DriveSpace> Drives(int n)
    {
        var l = new List<DriveSpace>();
        for (int i = 0; i < n; i++) l.Add(new DriveSpace(((char)('C' + i)) + ":", 500L << 30, 120L << 30));
        return l;
    }

    private static MetricHistory History()
    {
        var h = new MetricHistory();
        var r = new Random(7);
        for (int i = 0; i < 300; i++)
        {
            h.Cpu.Add(r.Next(0, 100)); h.Gpu.Add(r.Next(0, 100)); h.Mem.Add(50 + r.Next(0, 10));
            h.NetDown.Add(r.Next(0, 2000000)); h.NetUp.Add(r.Next(0, 300000));
            h.DiskRead.Add(r.Next(0, 1000000)); h.DiskWrite.Add(r.Next(0, 500000));
        }
        return h;
    }

    /// <summary>Instellingen met precies de gevraagde tegels aan (in een tijdelijke datamap).</summary>
    private static AppSettings Cfg(params string[] on)
    {
        var s = AppSettings.Load();
        foreach (var id in AllTiles) Tiles.SetDashOn(s, id, on.Contains(id));
        s.DashColumns = 1; s.DashScale = 100; s.MonthlyLimitGb = 0; s.DashBgImage = "";
        return s;
    }

    private static DashboardRenderer Make(AppSettings cfg, string dir, ProcessSampler? procs = null)
        => new(cfg, History(), new UsageTracker(dir), procs ?? new ProcessSampler { TopCount = 5 });

    private static byte[] Pixels(Bitmap b)
    {
        var d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[d.Stride * b.Height];
            Marshal.Copy(d.Scan0, bytes, 0, bytes.Length);
            return bytes;
        }
        finally { b.UnlockBits(d); }
    }

    [Fact]
    public void Render_werkt_zonder_venster_en_geeft_een_gevulde_bitmap()
    {
        using var td = new TempData();
        using var r = Make(Cfg("cpu"), td.Dir);
        using var bmp = r.Render(Snap(), Drives(1), 96);
        Assert.Equal(PixelFormat.Format32bppArgb, bmp.PixelFormat);
        Assert.Contains(Pixels(bmp), b => b != 0);   // er is iets getekend (achtergrond, tegel)
    }

    [Fact]
    public void Twee_keer_renderen_geeft_identieke_pixels()
    {
        using var td = new TempData();
        using var r = Make(Cfg("cpu", "mem", "net", "disk", "batt"), td.Dir);   // zonder "sys": die toont de klok
        var snap = Snap(battery: true);
        var drives = Drives(2);
        using var a = r.Render(snap, drives, 96);
        using var b = r.Render(snap, drives, 96);
        Assert.Equal(a.Size, b.Size);
        Assert.True(Pixels(a).AsSpan().SequenceEqual(Pixels(b)));
    }

    [Fact]
    public void Andere_meting_geeft_ander_beeld()
    {
        using var td = new TempData();
        using var r = Make(Cfg("cpu"), td.Dir);
        using var a = r.Render(Snap(cpu: 10), Drives(1), 96);
        using var b = r.Render(Snap(cpu: 95), Drives(1), 96);
        Assert.Equal(a.Size, b.Size);
        Assert.False(Pixels(a).AsSpan().SequenceEqual(Pixels(b)));
    }

    [Fact]
    public void Measure_komt_overeen_met_de_bitmap()
    {
        using var td = new TempData();
        var cfg = Cfg("cpu", "mem", "disk");
        cfg.DashColumns = 2;
        using var r = Make(cfg, td.Dir);
        var m = r.Measure(Snap(), Drives(2), 96);
        using var bmp = r.Render(Snap(), Drives(2), 96);
        Assert.Equal(bmp.Size, m);
    }

    [Fact]
    public void Formaat_volgt_kolommen_en_tegels()
    {
        using var td = new TempData();
        var cfg = Cfg("cpu");
        using var r = Make(cfg, td.Dir);
        // 1 kolom, 1 tegel (190 hoog): breedte = 2*16 + 300, hoogte = 2*16 + 190
        Assert.Equal(new Size(332, 222), r.Measure(Snap(), Drives(1), 96));
        for (int cols = 1; cols <= 4; cols++)
        {
            cfg.DashColumns = cols;
            Assert.Equal(32 + cols * 300 + (cols - 1) * 12, r.Measure(Snap(), Drives(1), 96).Width);
        }
        cfg.DashColumns = 9;   // wordt begrensd op 4
        Assert.Equal(32 + 4 * 300 + 3 * 12, r.Measure(Snap(), Drives(1), 96).Width);

        // twee tegels onder elkaar (cpu 190 + mem 160) in één kolom, naast elkaar in twee kolommen
        Tiles.SetDashOn(cfg, "mem", true);
        cfg.DashColumns = 1;
        Assert.Equal(32 + 190 + 12 + 160, r.Measure(Snap(), Drives(1), 96).Height);
        cfg.DashColumns = 2;
        Assert.Equal(32 + 190, r.Measure(Snap(), Drives(1), 96).Height);
    }

    [Fact]
    public void Schijftegel_wordt_hoger_met_meer_schijven()
    {
        using var td = new TempData();
        using var r = Make(Cfg("disk"), td.Dir);
        int h1 = r.Measure(Snap(), Drives(1), 96).Height, h3 = r.Measure(Snap(), Drives(3), 96).Height;
        Assert.Equal(2 * 34, h3 - h1);
    }

    [Fact]
    public void Batterijtegel_alleen_met_batterij()
    {
        using var td = new TempData();
        using var r = Make(Cfg("batt"), td.Dir);
        Assert.Equal(32 + 112, r.Measure(Snap(battery: true), Drives(1), 96).Height);
        Assert.Equal(80, r.Measure(Snap(battery: false), Drives(1), 96).Height);   // geen tegels = melding van 80 hoog
    }

    [Fact]
    public void Geen_tegels_geeft_een_lage_melding_en_lukt_te_tekenen()
    {
        using var td = new TempData();
        using var r = Make(Cfg(), td.Dir);
        using var bmp = r.Render(Snap(), Drives(1), 96);
        Assert.Equal(new Size(332, 80), bmp.Size);
    }

    [Fact]
    public void Schaal_en_dpi_schalen_het_formaat()
    {
        using var td = new TempData();
        var cfg = Cfg("cpu");
        using var r = Make(cfg, td.Dir);
        var basis = r.Measure(Snap(), Drives(1), 96);
        Assert.Equal(new Size(basis.Width * 2, basis.Height * 2), r.Measure(Snap(), Drives(1), 192));
        cfg.DashScale = 200;
        Assert.Equal(new Size(basis.Width * 2, basis.Height * 2), r.Measure(Snap(), Drives(1), 96));
    }

    [Fact]
    public void Volgorde_van_tegels_verandert_de_indeling_niet_de_grootte_bij_een_kolom()
    {
        using var td = new TempData();
        var cfg = Cfg("cpu", "mem");
        using var r = Make(cfg, td.Dir);
        var a = r.Measure(Snap(), Drives(1), 96);
        cfg.DashOrder = new List<string> { "mem", "cpu" };
        Assert.Equal(a, r.Measure(Snap(), Drives(1), 96));
    }

    [Fact]
    public void Programmategel_tekent_met_gevulde_sampler()
    {
        using var td = new TempData();
        var procs = new ProcessSampler { TopCount = 5 };
        var pt = typeof(ProcessSampler);
        pt.GetProperty("TopCpu")!.GetSetMethod(true)!.Invoke(procs, new object[] { new List<(string, double)> { ("alpha", 12.5), ("beta", 8.1) } });
        pt.GetProperty("TopMem")!.GetSetMethod(true)!.Invoke(procs, new object[] { new List<(string, long)> { ("alpha", 3L << 30), ("beta", 2L << 30) } });
        pt.GetProperty("HasCpu")!.GetSetMethod(true)!.Invoke(procs, new object[] { true });
        var cfg = Cfg("proc");
        using var r = Make(cfg, td.Dir, procs);
        using var withData = r.Render(Snap(), Drives(1), 96);
        using var empty = Make(cfg, td.Dir).Render(Snap(), Drives(1), 96);
        Assert.Equal(new Size(332, 32 + 34 + 5 * 18 + 22), withData.Size);
        Assert.False(Pixels(withData).AsSpan().SequenceEqual(Pixels(empty)));   // de namen staan erop
    }

    [Fact]
    public void Ring_telt_en_wikkelt_om()
    {
        var r = new Ring(4);
        Assert.Equal(0, r.Count);
        Assert.Equal(0, r.Max());
        for (int i = 1; i <= 3; i++) r.Add(i);
        Assert.Equal(3, r.Count);
        Assert.Equal(1, r[0]); Assert.Equal(3, r[2]);
        for (int i = 4; i <= 6; i++) r.Add(i);   // 6 waarden in 4 plaatsen: 3,4,5,6
        Assert.Equal(4, r.Count); Assert.Equal(4, r.Capacity);
        Assert.Equal(3, r[0]); Assert.Equal(6, r[3]);
    }

    [Fact]
    public void Ring_max_kijkt_naar_de_laatste_n_metingen()
    {
        var r = new Ring(10);
        foreach (var v in new double[] { 50, 3, 4, 7 }) r.Add(v);
        Assert.Equal(50, r.Max());
        Assert.Equal(7, r.Max(3));      // 3, 4, 7
        Assert.Equal(7, r.Max(1));
        Assert.Equal(50, r.Max(100));   // meer dan aanwezig: alles
    }

    [Fact]
    public void MetricHistory_sample_vult_de_ringen_en_beperkt_tot_een_per_seconde()
    {
        var h = new MetricHistory();
        var snap = Snap();
        h.Sample(snap);
        Assert.Equal(1, h.Cpu.Count);
        Assert.Equal(1, h.NetDown.Count);
        Assert.Equal(snap.CpuCores.Length, h.Cores.Count);
        h.Sample(snap);   // binnen een seconde: genegeerd
        Assert.Equal(1, h.Cpu.Count);
    }
}
