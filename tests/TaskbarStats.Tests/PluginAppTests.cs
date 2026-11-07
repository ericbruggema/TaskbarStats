using System.Drawing;
using Xunit;

namespace TaskbarStats.Tests;

/// <summary>De app-kant van de plugins: configbestanden, het inlezen van plugins\*.json, instellingen en het tekenen in het widget.</summary>
public class PluginAppTests : IDisposable
{
    private readonly TempData _data = new();
    public void Dispose() { PluginFeed.Reset(); _data.Dispose(); }

    private const string Config = "# Weather\r\nenabled = false\r\ninterval = 900\r\n[Section]\r\nenabled = true\r\n";

    // ---------- configbestand ----------
    [Fact]
    public void Config_EnabledFlag_IsReadAndRewritten_OnlyBeforeTheFirstSection()
    {
        Assert.False(PluginConfig.IsEnabledIn(Config));
        string on = PluginConfig.WithEnabled(Config, true);
        Assert.True(PluginConfig.IsEnabledIn(on));
        Assert.Contains("enabled = true\r\ninterval = 900", on);
        Assert.Contains("[Section]\r\nenabled = true", on);   // de regel in de sectie blijft zoals hij was
        Assert.Equal(Config.Replace("\r\n", "\n"), PluginConfig.WithEnabled(on, false).Replace("\r\n", "\n"));
    }

    [Fact]
    public void Config_MissingEnabledLine_IsAddedAtTheTop()
    {
        string text = PluginConfig.WithEnabled("# only a comment\r\ninterval = 5\r\n", true);
        Assert.StartsWith("enabled = true", text);
        Assert.True(PluginConfig.IsEnabledIn(text));
    }

    [Fact]
    public void Config_ReadWrite_RoundTrip_AndDefaultsToOff()
    {
        Assert.False(PluginConfig.IsEnabled("claude"));           // geen bestand: standaard uit
        Assert.False(PluginConfig.AnyEnabled());
        PluginConfig.SetEnabled("claude", true);
        Assert.True(PluginConfig.IsEnabled("claude"));
        Assert.True(PluginConfig.AnyEnabled());
        Assert.True(File.Exists(PluginConfig.PathOf("claude")));
        PluginConfig.SetEnabled("claude", false);
        Assert.False(PluginConfig.AnyEnabled());
    }

    [Fact]
    public void Config_Validate()
    {
        Assert.Null(PluginConfig.Validate("# x\r\nenabled = true\r\n[a]\r\nk = v"));
        Assert.NotNull(PluginConfig.Validate("enabled true"));
        Assert.NotNull(PluginConfig.Validate("[open"));
        Assert.Null(PluginConfig.Validate(""));
    }

    [Fact]
    public void Catalog_HasTextsForEveryPlugin()
    {
        foreach (var id in PluginCatalog.Ids)
        {
            Assert.NotEqual(id, PluginCatalog.TitleOf(id));
            Assert.NotEmpty(PluginCatalog.DescriptionOf(id));
            Assert.True(PluginCatalog.IsBuiltIn(id));
        }
        Assert.False(PluginCatalog.IsBuiltIn("mine"));
        Assert.True(PluginCatalog.OrderOf("claude") < PluginCatalog.OrderOf("weather"));
        Assert.True(PluginCatalog.OrderOf("script") < PluginCatalog.OrderOf("mine"));
    }

    // ---------- plugins\*.json lezen ----------
    private static string Json(string plugin, string status, string items, string updated = "", string message = "", int ttl = 960)
        => "{\"version\":1,\"plugin\":\"" + plugin + "\",\"title\":\"" + plugin + " title\",\"updated\":\""
           + (updated == "" ? DateTime.UtcNow.ToString("o") : updated) + "\",\"ttl\":" + ttl + ",\"status\":\"" + status
           + "\",\"message\":\"" + message + "\",\"items\":[" + items + "]}";

    [Fact]
    public void Feed_ReadsFiles_InCatalogOrder_AndSkipsBrokenOnes()
    {
        _data.Write("plugins/weather.json", Json("weather", "ok", "{\"key\":\"temp\",\"label\":\"Weather\",\"value\":4,\"text\":\"4°\"}"));
        _data.Write("plugins/claude.json", Json("claude", "ok", "{\"key\":\"five_hour\",\"label\":\"5h\",\"value\":40,\"unit\":\"%\"},{\"key\":\"seven_day\",\"label\":\"7d\",\"value\":10,\"unit\":\"%\"}"));
        _data.Write("plugins/zzz-mine.json", Json("zzz-mine", "ok", "{\"key\":\"x\",\"label\":\"X\",\"text\":\"hello\"}"));
        _data.Write("plugins/half.json", "{\"version\":1,\"plugin\":\"half\",\"items\":[{");   // half geschreven
        _data.Write("plugins/host.log", "not json at all");

        var d = PluginFeed.Read(_data.Dir);
        Assert.Equal(new[] { "claude", "weather", "zzz-mine" }, d.Statuses.Select(s => s.Plugin));
        Assert.Equal(new[] { "claude/five_hour", "claude/seven_day", "weather/temp", "zzz-mine/x" }, d.Values.Select(v => v.Id));
        Assert.Equal("40%", d.Values[0].Text);        // tekst ontbreekt: waarde + eenheid
        Assert.Equal("hello", d.Values[3].Text);
    }

    [Fact]
    public void Feed_UsesTheFileNameWhenTheFileHasNoPluginField_AndSkipsItemsWithoutKey()
    {
        _data.Write("plugins/custom.json", "{\"items\":[{\"label\":\"no key\"},{\"key\":\"a\",\"label\":\"A\",\"text\":\"1\"}]}");
        var d = PluginFeed.Read(_data.Dir);
        Assert.Equal("custom", d.Statuses.Single().Plugin);
        Assert.Equal("custom/a", d.Values.Single().Id);
        Assert.Equal("ok", d.Values.Single().Status);   // zonder status: ok
    }

    [Fact]
    public void Feed_Staleness()
    {
        string old = DateTime.UtcNow.AddHours(-2).ToString("o");
        var fresh = PluginFeed.Parse("p", Json("p", "ok", "{\"key\":\"a\",\"text\":\"1\"}"), DateTime.UtcNow).values.Single();
        var aged = PluginFeed.Parse("p", Json("p", "ok", "{\"key\":\"a\",\"text\":\"1\"}", old), DateTime.UtcNow).values.Single();
        var failed = PluginFeed.Parse("p", Json("p", "error", "{\"key\":\"a\",\"text\":\"1\"}", message: "boom"), DateTime.UtcNow).values.Single();
        Assert.False(fresh.Stale);
        Assert.True(aged.Stale);      // ouder dan de geldigheidsduur
        Assert.True(failed.Stale);    // de plugin meldt een fout: laatste waarden vervaagd
        Assert.Equal("boom", failed.Message);
    }

    [Fact]
    public void Feed_Current_NoticesChangedFiles()
    {
        PluginFeed.Reset();
        Assert.Empty(PluginFeed.Current.Values);
        _data.Write("plugins/claude.json", Json("claude", "ok", "{\"key\":\"five_hour\",\"label\":\"5h\",\"value\":40,\"unit\":\"%\"}"));
        PluginFeed.Reset();
        Assert.Single(PluginFeed.Current.Values);
    }

    [Fact]
    public void Value_Percent_UsesTheRange()
    {
        PluginValue V(double? v, double min, double max) => new("p", "P", "k", "K", null, v, min, max, "", "", "", "", "", true, false, "ok", "", DateTime.UtcNow, 900);
        Assert.Equal(50, V(0, -20, 20).Percent);
        Assert.Equal(100, V(500, 0, 100).Percent);   // begrensd
        Assert.Equal(0, V(-5, 0, 100).Percent);
        Assert.Null(V(null, 0, 100).Percent);
        Assert.Null(V(5, 10, 10).Percent);            // geen bereik
    }

    // ---------- instellingen ----------
    private static PluginValue Val(string unit = "%", double? value = 40, bool hint = true, string level = "", string text = "40%", string label = "5h")
        => new("claude", "Claude usage", "five_hour", "Claude 5-hour limit", label, value, 0, 100, unit, text, "detail", level, "", hint, false, "ok", "", DateTime.UtcNow, 900);

    [Fact]
    public void Settings_ShowDefaultsToThePluginsHint_AndTheUserCanOverrideIt()
    {
        var c = new AppSettings();
        var v = Val(hint: false);
        Assert.False(c.PluginShown(v));
        c.PluginCfg(v.Id).Show = true;
        Assert.True(c.PluginShown(v));
        Assert.True(new AppSettings().PluginShown(Val(hint: true)));
    }

    [Fact]
    public void Settings_StyleDefaultsToBarForPercentages_TextOtherwise()
    {
        var c = new AppSettings();
        Assert.Equal(DisplayStyle.Bar, c.PluginStyle(Val()));
        Assert.Equal(DisplayStyle.Digital, c.PluginStyle(Val(unit: "°C")));
        Assert.Equal(DisplayStyle.Digital, c.PluginStyle(Val(value: null)));
        c.PluginCfg("claude/five_hour").Style = DisplayStyle.Gauge;
        Assert.Equal(DisplayStyle.Gauge, c.PluginStyle(Val()));
    }

    [Fact]
    public void Settings_AreSavedAndRestored()
    {
        var c = new AppSettings();
        c.PluginCfg("claude/five_hour").Show = true;
        c.PluginCfg("claude/five_hour").Style = DisplayStyle.Gauge;
        c.PluginCfg("claude/five_hour").Color = "#112233";
        c.ShowPlugins = false;
        var json = System.Text.Json.JsonSerializer.Serialize(c);
        var back = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)!;
        Assert.False(back.ShowPlugins);
        Assert.Equal("#112233", back.PluginItems!["claude/five_hour"].Color);
        Assert.Equal(DisplayStyle.Gauge, back.PluginItems["claude/five_hour"].Style);
        Assert.True(new AppSettings().ShowPlugins);   // oude settings.json zonder deze velden: aan, maar zonder waarden verandert er niets
    }

    [Fact]
    public void WidgetOrder_ContainsThePluginsSlot()
    {
        Assert.Contains("plugins", Tiles.WidgetAll);
        Assert.Contains("plugins", Tiles.WidgetOrder(new List<string> { "cpu" }));   // oude volgorde zonder dit onderdeel krijgt het erbij
        Assert.NotEqual("plugins", Tiles.WidgetName("plugins"));
    }

    // ---------- meldingen en de taak ----------
    [Fact]
    public void AlertText_NamesWhatWentDown()
    {
        var multi = Val(level: "crit") with { Detail = "✓ A  10 ms\n✕ B  timeout\n✕ C  HTTP 500" };
        Assert.Equal("✕ B  timeout\n✕ C  HTTP 500", WidgetForm.AlertText(multi));
        Assert.Equal("one line", WidgetForm.AlertText(Val() with { Detail = "one line" }));
        Assert.Equal("40%", WidgetForm.AlertText(Val() with { Detail = "" }));
    }

    [Fact]
    public void HostTask_RunsWithoutElevation_OnDemandOnly_AndEscapesPaths()
    {
        string xml = PluginHost.TaskXml("PC\\me", @"C:\Program Files\TaskbarStats\plugins\TaskbarStats.Plugins.exe", @"C:\Users\A&B\AppData\Roaming\TaskbarStats");
        Assert.Contains("<RunLevel>LeastPrivilege</RunLevel>", xml);
        Assert.DoesNotContain("HighestAvailable", xml);
        Assert.DoesNotContain("<Triggers>", xml);                 // alleen op verzoek: geen trigger bij het inloggen
        Assert.Contains("A&amp;B", xml);                           // geldige XML
        Assert.Contains("--watch-app", xml);
        System.Xml.Linq.XDocument.Parse(xml.Replace("encoding=\"UTF-16\"", ""));
        Assert.Equal(PluginHost.InstanceId(@"C:\X"), PluginHost.InstanceId(@"c:\x"));   // zelfde afleiding als de host (hoofdletterloos)
    }

    // ---------- tekenen ----------
    private static MetricsSnapshot SnapWith(params PluginValue[] values) => new()
    {
        RawCpu = 20,
        Plugins = new PluginData(values, values.Select(v => new PluginStatus(v.Plugin, v.PluginTitle, v.Status, v.Message, v.UpdatedUtc, v.TtlSec)).Distinct().ToList()),
    };

    private static Bitmap Draw(AppSettings cfg, MetricsSnapshot snap)
    {
        using var r = new WidgetRenderer(cfg);
        return r.Render(new WidgetRenderContext { Snap = snap, Height = 44 });
    }

    private static AppSettings Cfg(Action<AppSettings>? f = null)
    {
        var s = new AppSettings { ShowCpu = true, ShowGpu = false, ShowMem = false, ShowNetUp = false, ShowNetDown = false, ShowBattery = false, ShowPing = false, DiskSpace = DiskSpaceMode.Off, BorderColor = "" };
        f?.Invoke(s);
        return s;
    }

    [Fact]
    public void Widget_DrawsPluginValues_AndGrowsWithThem()
    {
        using var none = Draw(Cfg(), SnapWith());
        using var bar = Draw(Cfg(), SnapWith(Val()));
        using var two = Draw(Cfg(), SnapWith(Val(), Val(unit: "°C", text: "☁ 4°", label: "") with { Key = "temp", Plugin = "weather" }));
        Assert.True(bar.Width > none.Width);
        Assert.True(two.Width > bar.Width);
    }

    [Theory]
    [InlineData(DisplayStyle.Digital)]
    [InlineData(DisplayStyle.Bar)]
    [InlineData(DisplayStyle.Gauge)]
    public void Widget_EveryStyleDraws_AlsoStaleAndWithLevels(DisplayStyle style)
    {
        foreach (var level in new[] { "", "ok", "warn", "crit" })
            foreach (var stale in new[] { false, true })
            {
                var v = Val(level: level) with { Status = stale ? "error" : "ok" };
                var cfg = Cfg(); cfg.PluginCfg(v.Id).Style = style;
                using var bmp = Draw(cfg, SnapWith(v));
                Assert.True(bmp.Width > 60);
            }
    }

    [Fact]
    public void Widget_PluginWithoutNumber_StaysText_EvenWhenABarIsRequested()
    {
        var v = Val(value: null, unit: "", text: "Amsterdam", label: "");
        var cfg = Cfg(); cfg.PluginCfg(v.Id).Style = DisplayStyle.Bar;
        using var none = Draw(Cfg(), SnapWith());
        using var bmp = Draw(cfg, SnapWith(v));
        Assert.True(bmp.Width > none.Width);
    }

    [Fact]
    public void Widget_HiddenOrSwitchedOff_DrawsNothingExtra()
    {
        using var basis = Draw(Cfg(), SnapWith());
        using var hidden = Draw(Cfg(), SnapWith(Val(hint: false)));                  // plugin stelt voor: niet tonen
        using var off = Draw(Cfg(s => s.ShowPlugins = false), SnapWith(Val()));       // onderdeel uit
        Assert.Equal(basis.Width, hidden.Width);
        Assert.Equal(basis.Width, off.Width);
    }

    [Fact]
    public void Widget_UserColour_IsUsedForTheBar()
    {
        var v = Val(value: 100);
        var cfg = Cfg(s => { s.WidgetOrder = new List<string> { "plugins" }; });
        cfg.ShowCpu = false;
        cfg.PluginCfg(v.Id).Color = "#00FF00";
        using var bmp = Draw(cfg, SnapWith(v));
        bool green = false;
        for (int y = 0; y < bmp.Height && !green; y++)
            for (int x = 0; x < bmp.Width && !green; x++)
            {
                var p = bmp.GetPixel(x, y);
                green = p.G > 200 && p.R < 60 && p.B < 60;
            }
        Assert.True(green, "de balk had de gekozen kleur moeten krijgen");
    }

    [Fact]
    public void Snapshot_CarriesThePluginData()
    {
        Assert.Same(PluginData.Empty, MetricsSnapshot.Empty.Plugins);
    }
}
