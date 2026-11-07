using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Xunit;

namespace TaskbarStats.Tests;

/// <summary>
/// Maakt de plugin-screenshots voor README/docs (docs\screenshots\plugins-*.png). Draait alleen als TASKBARSTATS_SHOTS naar een map wijst:
///   $env:TASKBARSTATS_SHOTS = "docs\screenshots"; dotnet test tests\TaskbarStats.Tests --filter PluginShots
/// Alles is getekend met VERZONNEN waarden (Amsterdam, Claude 62%): nooit echte locatie, aanmelding of gegevens in een publieke afbeelding.
/// </summary>
public class PluginShots : IDisposable
{
    private readonly TempData _data = new();
    public void Dispose() { PluginFeed.Reset(); _data.Dispose(); }

    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    private static string? OutDir => Environment.GetEnvironmentVariable("TASKBARSTATS_SHOTS") is { Length: > 0 } d ? Path.GetFullPath(d) : null;

    // ---------- verzonnen plugin-uitvoer ----------
    private static string Item(string key, string label, string? shortLabel, double? value, string unit, string text, string detail = "",
        string level = "", double min = 0, double max = 100, bool widget = true)
        => "{\"key\":\"" + key + "\",\"label\":\"" + label + "\"" + (shortLabel is null ? "" : ",\"short\":\"" + shortLabel + "\"")
           + (value is null ? "" : ",\"value\":" + value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))
           + ",\"min\":" + min.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"max\":" + max.ToString(System.Globalization.CultureInfo.InvariantCulture)
           + ",\"unit\":\"" + unit + "\",\"text\":\"" + text + "\",\"detail\":\"" + detail.Replace("\n", "\\n") + "\",\"level\":\"" + level + "\",\"widget\":" + (widget ? "true" : "false") + "}";

    private static string File_(string plugin, string title, string status, int ageMin, string message, params string[] items)
        => "{\"version\":1,\"plugin\":\"" + plugin + "\",\"title\":\"" + title + "\",\"updated\":\"" + DateTime.UtcNow.AddMinutes(-ageMin).ToString("o")
           + "\",\"ttl\":900,\"status\":\"" + status + "\",\"message\":\"" + message + "\",\"items\":[" + string.Join(",", items) + "]}";

    private static string Claude(double h5, double d7, string status = "ok", string message = "")
        => File_("claude", "Claude-verbruik", status, 1, message,
            Item("five_hour", "Claude 5-uurslimiet", "5h", h5, "%", $"{h5:0}%", $"{h5:0}% gebruikt, vrij over 2h 10m (za 14:30)", h5 >= 90 ? "crit" : h5 >= 70 ? "warn" : "ok"),
            Item("seven_day", "Claude weeklimiet", "7d", d7, "%", $"{d7:0}%", $"{d7:0}% gebruikt, vrij over 3d 4h (di 09:00)", d7 >= 90 ? "crit" : d7 >= 70 ? "warn" : "ok"));

    private static string Weather()
        => File_("weather", "Weer", "ok", 2, "",
            Item("temp", "Weer", "", 14, "°C", "⛅ 14°", "Amsterdam, NL - Half bewolkt, voelt als 12°, 9°..16°", min: -20, max: 40),
            Item("humidity", "Luchtvochtigheid", "Vocht", 72, "%", "72%", "Luchtvochtigheid 72%", widget: false),
            Item("rain", "Kans op regen", "Regen", 30, "%", "30%", "Kans op regen 30%, 0.2 mm", widget: false),
            Item("wind", "Wind", "Wind", 18, "km/h", "18 km/h ZW", "Wind 18 km/h ZW", max: 100, widget: false));

    private static string Location()
        => File_("location", "Locatie (GPS)", "ok", 2, "",
            Item("place", "Locatie", "", null, "", "Amsterdam", "Amsterdam, NL  ·  Wi-Fi  ·  ±30 m"),
            Item("coords", "Coördinaten", "GPS", null, "", "52.370, 4.895", "52.370, 4.895  ·  Wi-Fi  ·  ±30 m", widget: false));

    private static string Uptime(bool down)
        => File_("uptime", "Uptime-monitor", "ok", 1, "",
            Item("summary", "Uptime", "UP", down ? 66.7 : 100, "", down ? "2/3" : "3/3", down ? "✓ Website  120 ms\\n✓ DNS  12 ms\\n✕ NAS  timeout" : "✓ Website  120 ms\\n✓ DNS  12 ms\\n✓ NAS  3 ms", down ? "crit" : "ok"));

    private static string Script()
        => File_("script", "Script / REST", "ok", 1, "",
            Item("btc", "Bitcoin", "BTC", 61234, "€", "€61.2k", "Bitcoin: €61,234", max: 100000),
            Item("free_space_c", "Vrije ruimte C:", "C vrij", 212, "GB", "212 GB", "Vrije ruimte C: 212 GB", "ok", max: 500));

    private static PluginData Read(params string[] files)
    {
        var vals = new List<PluginValue>();
        var sts = new List<PluginStatus>();
        foreach (var f in files)
        {
            var (st, v) = PluginFeed.Parse("x", f, DateTime.UtcNow);
            if (st is not null) { sts.Add(st); vals.AddRange(v); }
        }
        return new PluginData(vals, sts);
    }

    // ---------- widget-rijen ----------
    private static MetricsSnapshot Snap(PluginData p) => new()
    {
        RawCpu = 23, RawGpu = 9, RawMem = 46, RawCores = new double[] { 12, 30, 8, 41, 22, 15, 35, 19 }, RawDown = 1_800_000, RawUp = 300_000,
        MemTotalBytes = 32UL << 30, MemUsedBytes = 15UL << 30, Plugins = p,
    };

    private static Bitmap Strip(AppSettings cfg, PluginData p)
    {
        using var r = new WidgetRenderer(cfg);
        return r.Render(new WidgetRenderContext { Snap = Snap(p), Height = 60 }, 96f);
    }

    private static AppSettings Cfg(Action<AppSettings>? f = null)
    {
        var s = new AppSettings { ShowCpu = true, ShowGpu = true, ShowMem = true, ShowNetUp = true, ShowNetDown = true, ShowBattery = false, ShowPing = false, DiskSpace = DiskSpaceMode.Off, BorderColor = "" };
        f?.Invoke(s);
        return s;
    }

    private static void Row(List<(string caption, Bitmap bmp)> rows, string caption, AppSettings cfg, PluginData p) => rows.Add((caption, Strip(cfg, p)));

    private static Bitmap Compose(List<(string caption, Bitmap bmp)> rows)
    {
        int w = rows.Max(r => r.bmp.Width) + 40;
        int h = rows.Sum(r => r.bmp.Height + 38) + 14;
        var canvas = new Bitmap(w, h);
        using var g = Graphics.FromImage(canvas);
        g.Clear(Color.FromArgb(32, 32, 34));
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        using var font = new Font("Segoe UI", 10f);
        using var br = new SolidBrush(Color.FromArgb(190, 190, 195));
        int y = 10;
        foreach (var (caption, bmp) in rows)
        {
            g.DrawString(caption, font, br, 20, y);
            g.DrawImageUnscaled(bmp, 20, y + 24);
            y += bmp.Height + 38;
            bmp.Dispose();
        }
        return canvas;
    }

    private static T Sta<T>(Func<T> f)
    {
        T? res = default; Exception? err = null;
        var t = new Thread(() => { try { res = f(); } catch (Exception e) { err = e; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (err != null) throw new Exception("in STA-thread", err);
        return res!;
    }

    private static Bitmap Shot(Form f)
    {
        var bmp = new Bitmap(f.Width, f.Height);
        using var g = Graphics.FromImage(bmp);
        var hdc = g.GetHdc();
        PrintWindow(f.Handle, hdc, 2);   // PW_RENDERFULLCONTENT
        g.ReleaseHdc(hdc);
        return bmp;
    }

    private static void ShowOffscreen(Form f)
    {
        f.StartPosition = FormStartPosition.Manual;
        f.Location = new Point(-5000, 40);
        f.ShowInTaskbar = false;
        f.Show();
        for (int i = 0; i < 8; i++) { Application.DoEvents(); Thread.Sleep(60); }
    }

    private static TabControl? FindTabs(Control root)
    {
        foreach (Control c in root.Controls)
        {
            if (c is TabControl t) return t;
            var inner = FindTabs(c);
            if (inner is not null) return inner;
        }
        return null;
    }

    /// <summary>Het scrollende paneel van het GESELECTEERDE tabblad (de andere tabbladen hebben er ook een).</summary>
    private static FlowLayoutPanel? ScrollPanel(Control root)
        => FindTabs(root)?.SelectedTab?.Controls.OfType<FlowLayoutPanel>().FirstOrDefault();

    [Fact]
    public void MakeScreenshots()
    {
        var dir = OutDir;
        if (dir is null) return;   // gewone testrun: niets doen
        Directory.CreateDirectory(dir);
        string oldLang = Loc.Lang;
        Loc.Lang = "nl";
        try
        {
            // ---- widget ----
            var rows = new List<(string, Bitmap)>();
            Row(rows, "Claude-verbruik als balken (standaard): 5 uur en week", Cfg(), Read(Claude(62, 41)));
            Row(rows, "…of als ringen", Cfg(c => { c.PluginCfg("claude/five_hour").Style = DisplayStyle.Gauge; c.PluginCfg("claude/seven_day").Style = DisplayStyle.Gauge; }), Read(Claude(62, 41)));
            Row(rows, "Kleur verandert vanzelf: oranje vanaf 70%, rood vanaf 90% (met een melding)", Cfg(), Read(Claude(93, 76)));
            Row(rows, "Weer, locatie en uptime-monitor", Cfg(c => c.ShowNetUp = c.ShowNetDown = false), Read(Weather(), Location(), Uptime(false)));
            Row(rows, "Er valt iets uit: de uptime-monitor wordt rood", Cfg(c => c.ShowNetUp = c.ShowNetDown = false), Read(Weather(), Uptime(true)));
            Row(rows, "Script / REST: elke waarde die je zelf kiest", Cfg(c => c.ShowNetUp = c.ShowNetDown = false), Read(Script()));
            Row(rows, "Meldt een plugin een fout, dan blijven de laatste waarden vervaagd staan", Cfg(c => c.ShowNetUp = c.ShowNetDown = false), Read(Claude(62, 41, "error", "Rate limited")));
            using (var wide = Compose(rows)) wide.Save(Path.Combine(dir, "plugins-widget.png"));

            // ---- dashboard en fullscreen (renderers zonder venster; geen computernaam/programma's in beeld) ----
            var plug = Read(Claude(62, 41), Weather(), Location(), Uptime(false), Script());
            var hist = new MetricHistory();
            var rnd = new Random(7);
            double hc = 30, hm = 50;
            for (int i = 0; i < 300; i++)
            {
                hc = Math.Clamp(hc + rnd.Next(-9, 10), 3, 90); hm = Math.Clamp(hm + rnd.Next(-2, 3), 40, 60);
                hist.Cpu.Add(hc); hist.Mem.Add(hm); hist.Gpu.Add(8); hist.NetDown.Add(rnd.Next(0, 2_000_000)); hist.NetUp.Add(rnd.Next(0, 300_000));
                hist.DiskRead.Add(0); hist.DiskWrite.Add(0);
            }
            var dcfg = AppSettings.Load();
            foreach (var id in Tiles.All) Tiles.SetDashOn(dcfg, id, id is "cpu" or "mem" or "plugins");
            dcfg.DashColumns = 2; dcfg.DashScale = 100; dcfg.DashBgImage = ""; dcfg.PluginItems = null;
            using (var dr = new DashboardRenderer(dcfg, hist, new UsageTracker(_data.Dir), new ProcessSampler { TopCount = 5 }))
            using (var db = dr.Render(Snap(plug), new List<DriveSpace>(), 96))
            using (var onBlack = new Bitmap(db.Width, db.Height))
            {
                using (var gg = Graphics.FromImage(onBlack)) { gg.Clear(Color.FromArgb(32, 32, 34)); gg.DrawImageUnscaled(db, 0, 0); }
                onBlack.Save(Path.Combine(dir, "plugins-dashboard.png"));
            }

            Sta(() =>
            {
                var fcfg = AppSettings.Load();
                fcfg.ShowPing = false;
                fcfg.FullHidden = new List<string> { "gpu", "disk", "batt", "sys", "proc" };
                using var metrics = new Metrics();
                var ctx = new DashContext { Metrics = metrics, Cfg = fcfg, Usage = new UsageTracker(_data.Dir), History = hist, Drives = () => new List<DriveSpace>(), ShowMenu = _ => { } };
                using var fr = new FullscreenRenderer(ctx, new ProcessSampler { TopCount = 5 })
                {
                    Now = () => new DateTime(2026, 3, 14, 15, 9, 26),
                    Ticks = () => 1_000_000_000,
                    Specs = () => new SpecData(new List<SpecBlock>(), false, ""),
                    RefreshSpecs = () => { },
                };
                var view = new FullscreenView { OpenedAt = 0 };
                var snap = Snap(plug);
                foreach (var (name, detail) in new[] { ("plugins-fullscreen.png", (string?)null), ("plugins-fullscreen-detail.png", "plugins") })
                {
                    view.Detail = detail;
                    using var fb = new Bitmap(1920, 1080);
                    using (var gg = Graphics.FromImage(fb)) { gg.Clear(Color.FromArgb(14, 16, 22)); fr.Paint(gg, new Size(1920, 1080), view, snap);
                        if (detail is null)   // echte computernaam nooit in een publieke afbeelding
                        {
                            gg.FillRectangle(new SolidBrush(Color.FromArgb(14, 16, 22)), 148, 14, 190, 32);
                            using var nf = new Font("Segoe UI", 15f, FontStyle.Regular, GraphicsUnit.Pixel);
                            gg.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                            gg.DrawString("· DEMO-PC", nf, new SolidBrush(Color.FromArgb(150, 160, 175)), 154, 21);
                        }
                    }
                    fb.Save(Path.Combine(dir, name));
                }
                return 0;
            });

            // ---- instellingen en editor (echte vensters, buiten beeld getekend) ----
            string host = PluginHost.HostExe;
            Directory.CreateDirectory(Path.GetDirectoryName(host)!);
            bool madeHost = !File.Exists(host);
            Process? dummy = null;
            try
            {
                if (madeHost) File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), host);   // doet alsof de host draait (alleen de procesnaam telt)
                dummy = Process.Start(new ProcessStartInfo(host, "-n 40 127.0.0.1") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true });

                foreach (var id in PluginCatalog.Ids)
                {
                    string ini = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "plugins", "TaskbarStats.Plugins", "defaults", id + ".ini"));
                    PluginConfig.Write(id, PluginConfig.WithEnabled(ini, id is "claude" or "weather" or "location"));
                }
                _data.Write("plugins/claude.json", Claude(62, 41));
                _data.Write("plugins/weather.json", Weather());
                _data.Write("plugins/location.json", Location());
                PluginFeed.Reset();

                var cfg = Cfg();
                cfg.PluginCfg("weather/humidity").Show = false;
                var top = Sta(() =>
                {
                    var f = new SettingsForm(cfg, new SettingsHost { Apply = () => { }, ResetDash = () => { }, ResetWidget = () => { }, ShowLog = () => { }, Drives = () => new() })
                    { Size = new Size(780, 1020) };
                    ShowOffscreen(f);
                    f.SelectTab(6);
                    for (int i = 0; i < 8; i++) { Application.DoEvents(); Thread.Sleep(60); }
                    var a = Shot(f);
                    var sp = ScrollPanel(f);
                    if (sp is not null) { sp.AutoScrollPosition = new Point(0, 100000); for (int i = 0; i < 6; i++) { Application.DoEvents(); Thread.Sleep(60); } }
                    var b = Shot(f);
                    f.Close(); f.Dispose();
                    return (a, b);
                });
                top.a.Save(Path.Combine(dir, "plugins-settings.png"));
                top.b.Save(Path.Combine(dir, "plugins-settings-values.png"));
                top.a.Dispose(); top.b.Dispose();

                var ed = Sta(() =>
                {
                    var f = new PluginConfigForm("claude") { Size = new Size(820, 640) };
                    ShowOffscreen(f);
                    var s = Shot(f);
                    f.Close(); f.Dispose();
                    return s;
                });
                ed.Save(Path.Combine(dir, "plugins-config-editor.png"));
                ed.Dispose();
            }
            finally
            {
                try { dummy?.Kill(); dummy?.Dispose(); } catch (InvalidOperationException) { }
                Thread.Sleep(300);
                if (madeHost) { try { File.Delete(host); Directory.Delete(Path.GetDirectoryName(host)!); } catch (IOException) { } }
            }
        }
        finally { Loc.Lang = oldLang; }
    }
}
