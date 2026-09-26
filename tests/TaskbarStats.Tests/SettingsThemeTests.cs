using System.Globalization;
using Xunit;

namespace TaskbarStats.Tests;

public class SettingsThemeTests : IDisposable
{
    private readonly TempData _data = new();
    public void Dispose() => _data.Dispose();

    [Fact]
    public void Load_zonder_bestand_geeft_standaardwaarden_en_pad_in_datamap()
    {
        var s = AppSettings.Load();
        Assert.Equal(Path.Combine(_data.Dir, "settings.json"), s.FilePath);
        Assert.True(s.ShowCpu);
        Assert.Equal(1000, s.RefreshMs);
        Assert.Equal(Path.Combine(_data.Dir, "lang"), Loc.ExternalDir);
    }

    [Fact]
    public void Save_en_Load_geven_dezelfde_waarden_terug()
    {
        var s = AppSettings.Load();
        s.ShowCpu = false;
        s.FontSize = 12;
        s.PingHost = "9.9.9.9";
        s.NetUnit = RateUnitKind.Bits;
        s.CpuStyle = DisplayStyle.Gauge;
        s.FloatX = 123;
        s.Language = "nl";
        s.Save();

        var t = AppSettings.Load();
        Assert.False(t.ShowCpu);
        Assert.Equal(12, t.FontSize);
        Assert.Equal("9.9.9.9", t.PingHost);
        Assert.Equal(RateUnitKind.Bits, t.NetUnit);
        Assert.Equal(DisplayStyle.Gauge, t.CpuStyle);
        Assert.Equal(123, t.FloatX);
        Assert.Null(t.FloatY);
        Assert.Equal("nl", t.Language);
    }

    [Fact]
    public void Enums_staan_als_tekst_in_het_bestand()
    {
        var s = AppSettings.Load();
        s.NetUnit = RateUnitKind.Bits;
        s.Save();
        Assert.Contains("\"Bits\"", File.ReadAllText(s.FilePath));
    }

    [Fact]
    public void Save_laat_geen_tmp_bestand_achter()
    {
        var s = AppSettings.Load();
        for (int i = 0; i < 5; i++) { s.FontSize = 8 + i; s.Save(); }
        Assert.True(File.Exists(s.FilePath));
        Assert.False(File.Exists(s.FilePath + ".tmp"));
        Assert.Empty(Directory.GetFiles(_data.Dir, "*.tmp"));
    }

    [Fact]
    public void Oud_of_onbekend_json_laadt_met_standaardwaarden_voor_de_rest()
    {
        _data.Write("settings.json", """{ "ShowCpu": false, "BestaatNietMeer": 42, "FontSize": 11 }""");
        var s = AppSettings.Load();
        Assert.False(s.ShowCpu);
        Assert.Equal(11, s.FontSize);
        Assert.True(s.ShowGpu);          // niet in het bestand: default
        Assert.Equal(1000, s.RefreshMs);
    }

    [Fact]
    public void Kapot_json_geeft_standaardinstellingen_zonder_uitzondering()
    {
        _data.Write("settings.json", "{ dit is kapot");
        var s = AppSettings.Load();
        Assert.True(s.ShowCpu);
        Assert.Equal(Path.Combine(_data.Dir, "settings.json"), s.FilePath);
    }

    // ---------- Thema's ----------

    [Fact]
    public void Ingebouwde_themas_hebben_unieke_namen_en_serialiseren_heen_en_terug()
    {
        var all = ThemeStore.BuiltIn();
        Assert.NotEmpty(all);
        Assert.Equal(all.Count, all.Select(t => t.Name).Distinct().Count());

        foreach (var t in all)
        {
            string path = Path.Combine(_data.Dir, "themes", t.Name + ".json");
            ThemeStore.Write(path, t);
            var back = ThemeStore.Read(path);
            Assert.NotNull(back);
            Assert.Equal(t.Name, back!.Name);
            Assert.Equal(t.TextColor, back.TextColor);
            Assert.Equal(t.BackgroundColor, back.BackgroundColor);
            Assert.Equal(t.AccentColor, back.AccentColor);
            Assert.Equal(t.CpuStyle, back.CpuStyle);
            Assert.Equal(t.FontFamily, back.FontFamily);
            Assert.Equal(t.DashOpacity, back.DashOpacity);
            // Hele objecten identiek na een tweede serialisatie
            string p2 = Path.Combine(_data.Dir, "themes", t.Name + "-2.json");
            ThemeStore.Write(p2, back);
            Assert.Equal(File.ReadAllText(path), File.ReadAllText(p2));
        }
    }

    [Fact]
    public void Ingebouwde_themas_hebben_geldige_kleuren_en_drempels()
    {
        foreach (var t in ThemeStore.BuiltIn())
        {
            foreach (var c in new[] { t.TextColor, t.BackgroundColor, t.AccentColor, t.WarnColor, t.CritColor })
                Assert.Matches("^#[0-9A-Fa-f]{6,8}$", c);
            Assert.True(t.WarnThreshold < t.CritThreshold, t.Name);
        }
    }

    [Fact]
    public void Capture_en_ApplyTo_zijn_elkaars_spiegel()
    {
        var a = AppSettings.Load();
        a.ShowCpu = false;
        a.CpuStyle = DisplayStyle.Bar;
        a.AccentColor = "#123456";
        a.FontSize = 11;
        a.DashColumns = 3;
        a.ShowCpuFreq = true;

        var theme = ThemeData.Capture(a, "Mijn thema");
        Assert.Equal("Mijn thema", theme.Name);

        var b = new AppSettings();
        theme.ApplyTo(b);
        Assert.False(b.ShowCpu);
        Assert.Equal(DisplayStyle.Bar, b.CpuStyle);
        Assert.Equal("#123456", b.AccentColor);
        Assert.Equal(11, b.FontSize);
        Assert.Equal(3, b.DashColumns);
        Assert.True(b.ShowCpuFreq);
    }

    [Fact]
    public void ApplyTo_begrenst_waarden()
    {
        var t = new ThemeData { FontSize = 999, WidgetHeight = 1, DashScale = 5000, DashColumns = 0, DashOpacity = 0, GraphWidthPx = 1, FontFamily = "  " };
        var c = new AppSettings();
        t.ApplyTo(c);
        Assert.Equal(16, c.FontSize);
        Assert.Equal(24, c.WidgetHeight);
        Assert.Equal(400, c.DashScale);
        Assert.Equal(1, c.DashColumns);
        Assert.Equal(10, c.DashOpacity);
        Assert.Equal(16, c.GraphWidthPx);
        Assert.Equal("Segoe UI", c.FontFamily);
    }

    [Fact]
    public void User_themas_worden_uit_de_themamap_gelezen()
    {
        var s = AppSettings.Load();
        ThemeStore.Write(ThemeStore.FileFor(s, "Eigen"), new ThemeData { Name = "Eigen", AccentColor = "#ABCDEF" });
        _data.Write(Path.Combine("themes", "zonder-naam.json"), """{ "AccentColor": "#111111" }""");
        _data.Write(Path.Combine("themes", "kapot.json"), "{ kapot");

        var users = ThemeStore.User(s);
        Assert.Equal(2, users.Count);
        Assert.Contains(users, t => t.Name == "Eigen" && t.AccentColor == "#ABCDEF");
        Assert.Contains(users, t => t.Name == "zonder-naam");   // bestandsnaam vult een lege naam aan
    }

    [Fact]
    public void FileFor_maakt_een_veilige_bestandsnaam()
    {
        var s = AppSettings.Load();
        string p = ThemeStore.FileFor(s, "a/b:c");
        Assert.Equal(Path.Combine(_data.Dir, "themes"), Path.GetDirectoryName(p));
        Assert.DoesNotContain('/', Path.GetFileName(p));
        Assert.DoesNotContain(':', Path.GetFileName(p));
        Assert.EndsWith("theme.json", ThemeStore.FileFor(s, "  "));
    }

    // ---------- Tiles en losse hulpfuncties ----------

    [Fact]
    public void Tiles_Order_vult_aan_en_verwijdert_onbekende_en_dubbele_ids()
    {
        var o = Tiles.Order(new() { "net", "bestaatniet", "net", "cpu" });
        Assert.Equal(new[] { "net", "cpu" }, o.Take(2));
        Assert.Equal(Tiles.All.Length, o.Count);
        Assert.Equal(Tiles.All.OrderBy(x => x), o.OrderBy(x => x));
        Assert.Equal(Tiles.All, Tiles.Order(null));
    }

    [Fact]
    public void Tiles_WidgetOrder_bevat_alle_widget_ids_precies_een_keer()
    {
        var o = Tiles.WidgetOrder(new() { "mem", "mem", "x" });
        Assert.Equal("mem", o[0]);
        Assert.Equal(Tiles.WidgetAll.Length, o.Count);
        Assert.Equal(o.Count, o.Distinct().Count());
    }

    [Fact]
    public void Tiles_zichtbaarheid_dashboard_en_fullscreen()
    {
        var c = new AppSettings();
        foreach (var id in Tiles.All) Tiles.SetDashOn(c, id, false);
        Assert.All(Tiles.All, id => Assert.False(Tiles.DashOn(c, id)));
        Tiles.SetDashOn(c, "cpu", true);
        Assert.True(Tiles.DashOn(c, "cpu"));
        Assert.True(Tiles.FullOn(c, "cpu"));
        c.FullHidden = new() { "cpu" };
        Assert.False(Tiles.FullOn(c, "cpu"));
    }

    [Fact]
    public void FormatSize_kiest_eenheid()
    {
        var old = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            const double GB = 1024.0 * 1024 * 1024;
            Assert.Equal("1.5 GB", Metrics.FormatSize(1.5 * GB));
            Assert.Equal("16 GB", Metrics.FormatSize(16 * GB));
            Assert.Equal("1.0 TB", Metrics.FormatSize(1024 * GB));
            Assert.Equal("0.0 GB", Metrics.FormatSize(0));
        }
        finally { CultureInfo.CurrentCulture = old; }
    }
}
