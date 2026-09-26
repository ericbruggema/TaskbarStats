using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using Xunit;

namespace TaskbarStats.Tests;

/// <summary>Het widget tekenen naar een gewone Bitmap, zonder venster (WidgetRenderer).</summary>
public class WidgetRendererTests : IDisposable
{
    private readonly TempData _data = new();
    public void Dispose() => _data.Dispose();

    // GdiCache is alleen voor één (UI-)thread: alle tekenwerk van de tests loopt op één vaste thread.
    private static readonly BlockingCollection<Action> Queue = new();
    private static readonly Thread Worker = StartWorker();
    private static Thread StartWorker()
    {
        var t = new Thread(() => { foreach (var a in Queue.GetConsumingEnumerable()) a(); }) { IsBackground = true, Name = "renderer tests" };
        t.Start();
        return t;
    }
    private static T OnWorker<T>(Func<T> f)
    {
        T? res = default; Exception? err = null;
        using var done = new ManualResetEventSlim();
        Queue.Add(() => { try { res = f(); } catch (Exception e) { err = e; } finally { done.Set(); } });
        done.Wait();
        if (err != null) throw new Exception("in tekenthread", err);
        return res!;
    }

    private static AppSettings Cfg(Action<AppSettings>? f = null)
    {
        var s = new AppSettings
        {
            ShowCpu = false, ShowGpu = false, ShowMem = false, ShowNetUp = false, ShowNetDown = false, ShowDisk = false,
            ShowCpuTemp = false, ShowGpuTemp = false, ShowBattery = false, ShowPing = false,
            DiskSpace = DiskSpaceMode.Off, BorderColor = "",
        };
        s.ShowCpu = true;
        f?.Invoke(s);
        return s;
    }

    private static MetricsSnapshot Snap() => new()
    {
        RawCpu = 37, RawGpu = 62, RawMem = 48, RawDown = 2_500_000, RawUp = 400_000,
        RawCores = new double[] { 10, 35, 60, 90 },
        RawCpuTemp = 55, RawGpuTemp = 71,
        MemTotalBytes = 32UL << 30, MemUsedBytes = 15UL << 30,
        DiskReadBytesPerSec = 3e6, DiskWriteBytesPerSec = 1e6,
        BatteryPresent = true, BatteryPercent = 64, BatteryOnAc = true,
    };

    private static MetricHistory History()
    {
        var h = new MetricHistory();
        var r = new Random(7);
        double c = 40, d = 1e6;
        for (int i = 0; i < 120; i++)
        {
            c = Math.Clamp(c + r.Next(-15, 16), 0, 100); d = Math.Max(0, d + r.Next(-400000, 500000));
            h.Cpu.Add(c); h.Gpu.Add(100 - c); h.Mem.Add(50); h.NetDown.Add(d); h.NetUp.Add(d / 4);
        }
        return h;
    }

    private static Ring TempRing()
    {
        var r = new Ring(300);
        for (int i = 0; i < 100; i++) r.Add(40 + i % 30);
        return r;
    }

    private static WidgetRenderContext Ctx(int height = 44) => new()
    {
        Snap = Snap(), History = History(), CpuTempHistory = TempRing(), GpuTempHistory = TempRing(), Height = height,
        Drives = new List<DriveSpace> { new("C:", 500L << 30, 120L << 30), new("D:", 2000L << 30, 1500L << 30) },
        PingStats = () => new PingStats("host", 14, 5, 11, 13, 20, 2, 0, null),
        PingSamples = () => new double[] { 12, 14, 13, 30, 12 },
    };

    private static Bitmap Draw(AppSettings cfg, WidgetRenderContext? ctx = null)
        => OnWorker(() =>
        {
            using var r = new WidgetRenderer(cfg);
            return r.Render(ctx ?? Ctx());
        });

    private static byte[] Pixels(Bitmap b)
    {
        var d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var buf = new byte[Math.Abs(d.Stride) * b.Height];
            System.Runtime.InteropServices.Marshal.Copy(d.Scan0, buf, 0, buf.Length);
            return buf;
        }
        finally { b.UnlockBits(d); }
    }

    // Aantal pixels in een gebied dat afwijkt van de achtergrondkleur linksboven.
    private static int Ink(Bitmap b, Rectangle area)
    {
        var bg = b.GetPixel(0, 0);
        int n = 0;
        for (int y = area.Top; y < Math.Min(area.Bottom, b.Height); y++)
            for (int x = area.Left; x < Math.Min(area.Right, b.Width); x++)
                if (b.GetPixel(x, y) != bg) n++;
        return n;
    }

    [Fact]
    public void Tekent_zonder_venster_of_handle()
    {
        using var bmp = Draw(Cfg());
        Assert.Equal(44, bmp.Height);
        Assert.True(bmp.Width >= 60);
        Assert.True(Ink(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height)) > 50);   // er staat echt iets
    }

    [Fact]
    public void Breedte_groeit_als_een_onderdeel_aangaat()
    {
        using var een = Draw(Cfg());
        using var twee = Draw(Cfg(s => s.ShowGpu = true));
        using var drie = Draw(Cfg(s => { s.ShowGpu = true; s.ShowMem = true; }));
        Assert.True(twee.Width > een.Width);
        Assert.True(drie.Width > twee.Width);
    }

    [Fact]
    public void Onderdeel_zonder_waarde_krijgt_geen_cel()
    {
        // batterij zonder accu en temperatuur zonder meting: niets erbij
        var ctx = Ctx();
        ctx.Snap = new MetricsSnapshot { RawCpu = 10 };
        using var basis = Draw(Cfg(), ctx);
        using var met = Draw(Cfg(s => { s.ShowBattery = true; s.ShowCpuTemp = true; s.ShowGpuTemp = true; }), ctx);
        Assert.Equal(basis.Width, met.Width);
    }

    [Fact]
    public void Twee_keer_tekenen_geeft_dezelfde_pixels()
    {
        AppSettings Cfg2() => Cfg(s =>
        {
            s.ShowGpu = s.ShowMem = s.ShowDisk = s.ShowNetDown = s.ShowNetUp = s.ShowCpuTemp = s.ShowGpuTemp = s.ShowBattery = s.ShowPing = true;
            s.CpuStyle = DisplayStyle.Gauge; s.GpuStyle = DisplayStyle.Bar; s.MemStyle = DisplayStyle.Graph; s.NetStyle = TextGraphStyle.Graph;
            s.DiskSpace = DiskSpaceMode.Each;
        });
        using var a = Draw(Cfg2());
        using var b = Draw(Cfg2());
        Assert.Equal(a.Size, b.Size);
        Assert.Equal(Pixels(a), Pixels(b));
    }

    [Fact]
    public void Dezelfde_renderer_geeft_bij_herhaling_hetzelfde_beeld()
    {
        var cfg = Cfg(s => { s.ShowGpu = true; s.GpuStyle = DisplayStyle.Graph; s.CpuStyle = DisplayStyle.Graph; });
        var (p1, p2) = OnWorker(() =>
        {
            using var r = new WidgetRenderer(cfg);
            var ctx = Ctx();
            using var a = r.Render(ctx); using var b = r.Render(ctx);
            return (Pixels(a), Pixels(b));
        });
        Assert.Equal(p1, p2);
    }

    [Fact]
    public void Grafiekbreedte_volgt_GraphWidthFor()
    {
        AppSettings G(GraphLen len, int px = 46) => Cfg(s =>
        {
            s.CpuStyle = DisplayStyle.Graph;
            s.GraphSizes["cpu"] = new GraphSize { Len = len, Px = px };
        });
        using var tiny = Draw(G(GraphLen.Tiny));
        using var lang = Draw(G(GraphLen.Long));
        using var eigen = Draw(G(GraphLen.Custom, 120));
        double scale = Math.Clamp(44 / 40.0, 0.85, 1.4);
        int Gw(int px) => (int)Math.Round(px * scale);
        Assert.Equal(Gw(60) - Gw(24), lang.Width - tiny.Width);
        Assert.Equal(Gw(120) - Gw(60), eigen.Width - lang.Width);
    }

    [Fact]
    public void Grafiekbreedte_van_een_ander_onderdeel_doet_niet_mee()
    {
        var a = Cfg(s => { s.CpuStyle = DisplayStyle.Graph; s.GraphSizes["cpu"] = new GraphSize { Len = GraphLen.Short }; });
        var b = Cfg(s => { s.CpuStyle = DisplayStyle.Graph; s.GraphSizes["cpu"] = new GraphSize { Len = GraphLen.Short }; s.GraphSizes["gpu"] = new GraphSize { Len = GraphLen.Long }; });
        using var x = Draw(a);
        using var y = Draw(b);
        Assert.Equal(x.Width, y.Width);   // GPU staat uit
    }

    [Fact]
    public void Labels_boven_zet_het_label_op_de_bovenste_rijen()
    {
        using var naast = Draw(Cfg());
        using var boven = Draw(Cfg(s => s.LabelsAbove = true));
        var strip = new Rectangle(0, 0, 200, 8);
        Assert.Equal(0, Ink(naast, strip));
        Assert.True(Ink(boven, strip) > 0);
    }

    [Fact]
    public void Labels_boven_maakt_een_grafiekcel_smaller()
    {
        AppSettings G(bool above) => Cfg(s => { s.CpuStyle = DisplayStyle.Graph; s.LabelsAbove = above; });
        using var naast = Draw(G(false));
        using var boven = Draw(G(true));
        Assert.True(boven.Width < naast.Width);   // label ernaast telt mee in de breedte, label erboven niet
    }

    [Fact]
    public void Hoogte_komt_uit_de_context()
    {
        using var laag = Draw(Cfg(), Ctx(30));
        using var hoog = Draw(Cfg(), Ctx(70));
        Assert.Equal(30, laag.Height);
        Assert.Equal(70, hoog.Height);
        Assert.True(hoog.Width > laag.Width);   // lettertype groeit mee
    }

    [Fact]
    public void Schijfruimte_maakt_een_cel_per_schijf()
    {
        using var uit = Draw(Cfg());
        using var totaal = Draw(Cfg(s => s.DiskSpace = DiskSpaceMode.Total));
        using var elk = Draw(Cfg(s => s.DiskSpace = DiskSpaceMode.Each));
        using var een = Draw(Cfg(s => { s.DiskSpace = DiskSpaceMode.Single; s.DiskSpaceDrive = "D:"; }));
        Assert.True(totaal.Width > uit.Width);
        Assert.True(elk.Width > totaal.Width);
        Assert.True(een.Width > uit.Width);
        using var geen = Draw(Cfg(s => { s.DiskSpace = DiskSpaceMode.Single; s.DiskSpaceDrive = "Q:"; }));
        Assert.Equal(uit.Width, geen.Width);
    }

    [Fact]
    public void Opmaakopties_veranderen_de_breedte()
    {
        AppSettings N(Action<AppSettings> f) => Cfg(s => { s.ShowNetDown = true; s.ShowNetUp = true; f(s); });
        using var normaal = Draw(N(_ => { }));
        using var kort = Draw(N(s => { s.HideUnit = true; s.ShortValues = true; }));
        using var compact = Draw(N(s => s.Compact = true));
        Assert.True(kort.Width < normaal.Width);
        Assert.True(compact.Width < normaal.Width);
    }

    [Fact]
    public void Temperatuurgeschiedenis_bepaalt_de_grafieklijn()
    {
        var cfg = Cfg(s => { s.ShowCpuTemp = true; s.CpuTempStyle = DisplayStyle.Graph; s.CpuStyle = DisplayStyle.Digital; });
        var leeg = Ctx(); leeg.CpuTempHistory = new Ring(300);
        using var zonder = Draw(cfg, leeg);
        using var met = Draw(cfg, Ctx());
        Assert.Equal(zonder.Size, met.Size);
        Assert.NotEqual(Pixels(zonder), Pixels(met));
    }

    [Fact]
    public void Achtergrond_transparant_of_effen()
    {
        using var effen = Draw(Cfg());
        using var doorzichtig = Draw(Cfg(s => s.TransparentBackground = true));
        Assert.Equal(255, effen.GetPixel(0, 0).A);
        Assert.Equal(1, doorzichtig.GetPixel(0, 0).A);   // alpha 1: onzichtbaar maar klikbaar
    }

    [Fact]
    public void Rand_wordt_om_het_widget_getekend()
    {
        using var bmp = Draw(Cfg(s => s.BorderColor = "#FF0000"));
        Assert.Equal(Color.FromArgb(255, 255, 0, 0), bmp.GetPixel(0, 0));
        Assert.Equal(Color.FromArgb(255, 255, 0, 0), bmp.GetPixel(bmp.Width - 1, bmp.Height - 1));
    }

    [Fact]
    public void Windows_thema_bepaalt_de_effectieve_kleuren()
    {
        var (own, dark, light) = OnWorker(() =>
        {
            var s = Cfg(x => { x.BackgroundColor = "#204060"; });
            using var r = new WidgetRenderer(s);
            var own = r.EffectiveBackground();
            s.WidgetFollowWindows = WinThemeMode.DarkLight;
            r.WinLight = false; var dark = (r.EffectiveBackground(), r.EffectiveText());
            r.WinLight = true; var light = (r.EffectiveBackground(), r.EffectiveText());
            return (own, dark, light);
        });
        Assert.Equal(Color.FromArgb(0x20, 0x40, 0x60), own);
        Assert.Equal(Color.FromArgb(0x1F, 0x1F, 0x1F), dark.Item1);
        Assert.Equal(Color.White, dark.Item2);
        Assert.Equal(Color.FromArgb(0xF3, 0xF3, 0xF3), light.Item1);
        Assert.Equal(Color.Black, light.Item2);
    }

    [Fact]
    public void Windows_thema_licht_geeft_een_lichte_bitmap()
    {
        var cfg = Cfg(s => s.WidgetFollowWindows = WinThemeMode.DarkLight);
        var bmp = OnWorker(() =>
        {
            using var r = new WidgetRenderer(cfg) { WinLight = true };
            return r.Render(Ctx());
        });
        using (bmp) Assert.Equal(Color.FromArgb(255, 0xF3, 0xF3, 0xF3), bmp.GetPixel(0, 0));
    }

    [Fact]
    public void Meten_en_tekenen_geven_dezelfde_breedte()
    {
        var cfg = Cfg(s => { s.ShowGpu = true; s.ShowMem = true; s.ShowNetDown = true; });
        var (w, bw) = OnWorker(() =>
        {
            using var r = new WidgetRenderer(cfg);
            var ctx = Ctx();
            int w = r.MeasureWidth(ctx, 96f);
            using var b = r.Render(ctx, w, 96f);
            return (w, b.Width);
        });
        Assert.Equal(w, bw);
    }

    [Fact]
    public void Achtergrondlaag_wordt_onder_de_cellen_getekend()
    {
        var cfg = Cfg();
        using var bmp = OnWorker(() =>
        {
            using var r = new WidgetRenderer(cfg);
            var ctx = Ctx();
            return r.Render(ctx, r.MeasureWidth(ctx, 96f), 96f, g => g.FillRectangle(Brushes.Green, 0, 0, 5000, 5000));
        });
        Assert.Equal(Color.FromArgb(255, 0, 128, 0), bmp.GetPixel(1, 1));
    }

    [Fact]
    public void Batterijsymbolen_blijven_via_WidgetForm_bereikbaar()
    {
        // dashboard en fullscreen roepen deze statics op WidgetForm aan
        using var bmp = new Bitmap(40, 40);
        OnWorker(() =>
        {
            using var g = Graphics.FromImage(bmp);
            WidgetForm.DrawBolt(g, 20, 20, 20);
            WidgetForm.DrawPlug(g, 20, 20, 20);
            using var path = WidgetForm.RoundedRect(new RectangleF(1, 1, 30, 30), 4);
            return 0;
        });
        Assert.True(Ink(bmp, new Rectangle(0, 0, 40, 40)) > 0 || bmp.GetPixel(20, 20).A > 0);
    }
}
