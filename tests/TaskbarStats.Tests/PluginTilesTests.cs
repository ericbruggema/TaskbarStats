using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Xunit;

namespace TaskbarStats.Tests;

/// <summary>Plugin-waarden in het dashboard en het fullscreen-overzicht (tegel "plugins"). Verzonnen waarden, geen host nodig.</summary>
public class PluginTilesTests
{
    internal static PluginData Data(int n = 2, bool error = false)
    {
        var vals = new List<PluginValue>();
        for (int i = 0; i < n; i++)
            vals.Add(new PluginValue("claude", "Claude", "k" + i, "Claude " + i, "c" + i, 40 + i, 0, 100, "%", (40 + i) + "%", "", i == 1 ? "warn" : "ok", "", true, false,
                "ok", "", DateTime.UtcNow, 900));
        var st = new List<PluginStatus> { new("claude", "Claude", error ? "error" : "ok", error ? "Rate limited" : "", DateTime.UtcNow, 900) };
        return new PluginData(vals, st);
    }

    [Fact]
    public void Tiles_ContainPlugins_OnByDefault_AndHaveAName()
    {
        Assert.Contains("plugins", Tiles.All);
        var c = new AppSettings();
        Assert.True(Tiles.DashOn(c, "plugins"));
        Tiles.SetDashOn(c, "plugins", false);
        Assert.False(c.DashPlugins);
        Assert.NotEqual("plugins", Tiles.Name("plugins"));
        Assert.Equal(Tiles.All.Length, Tiles.Order(new List<string> { "cpu" }).Count);
    }

    [Fact]
    public void FullCells_PluginsIsAStripAtTheBottom_AndTheOtherTilesShrink()
    {
        var ids = new[] { "cpu", "gpu", "mem", "net", "disk", "batt", "sys", "proc" };
        var without = Tiles.FullCells(ids, 1920, 1080, 76, 16);
        var with = Tiles.FullCells(ids.Append("plugins"), 1920, 1080, 76, 16);
        Assert.DoesNotContain(without, c => c.ids[0] == "plugins");
        var strip = Assert.Single(with, c => c.ids[0] == "plugins").r;
        Assert.Equal(Tiles.PluginStripHeight, strip.Height);
        Assert.Equal(1920 - 32, strip.Width, 1);
        Assert.Equal(1080 - 16, strip.Bottom, 1);
        foreach (var c in with.Where(c => c.ids[0] != "plugins"))
            Assert.True(c.r.Bottom <= strip.Y - 15.9f, "tegel raakt de strook: " + c.ids[0]);
        Assert.Equal(without.Count, with.Count - 1);
        // alleen de strook
        Assert.Single(Tiles.FullCells(new[] { "plugins" }, 1920, 1080, 76, 16));
    }

    [Fact]
    public void Dashboard_ShowsThePluginsTile_OnlyWhenThereIsData()
    {
        using var td = new TempData();
        var cfg = AppSettings.Load();
        foreach (var id in Tiles.All) Tiles.SetDashOn(cfg, id, id is "cpu" or "plugins");
        cfg.DashColumns = 1; cfg.DashScale = 100; cfg.DashBgImage = "";
        using var r = new DashboardRenderer(cfg, new MetricHistory(), new UsageTracker(td.Dir), new ProcessSampler { TopCount = 5 });
        var drives = new List<DriveSpace>();
        var none = r.Measure(new MetricsSnapshot { RawCpu = 10 }, drives, 96);
        var two = r.Measure(new MetricsSnapshot { RawCpu = 10, Plugins = Data(2) }, drives, 96);
        var ten = r.Measure(new MetricsSnapshot { RawCpu = 10, Plugins = Data(10) }, drives, 96);
        var err = r.Measure(new MetricsSnapshot { RawCpu = 10, Plugins = new PluginData(Array.Empty<PluginValue>(), Data(0, true).Statuses) }, drives, 96);
        Assert.True(two.Height > none.Height);
        Assert.True(ten.Height > two.Height);
        Assert.True(err.Height > none.Height);   // een plugin met een fout verdient een regel
        using var bmp = r.Render(new MetricsSnapshot { RawCpu = 10, Plugins = Data(10) }, drives, 96);
        Assert.Equal(ten, bmp.Size);

        cfg.DashPlugins = false;
        Assert.Equal(none, r.Measure(new MetricsSnapshot { RawCpu = 10, Plugins = Data(2) }, drives, 96));
    }

    [Fact]
    public void PluginView_Helpers()
    {
        var d = Data(1, error: true);
        Assert.True(PluginView.Any(d));
        Assert.Empty(PluginView.Problems(d));   // er zijn waarden: dan toont de rij zelf de verouderde waarde
        var onlyErr = new PluginData(Array.Empty<PluginValue>(), d.Statuses);
        Assert.Single(PluginView.Problems(onlyErr));
        Assert.False(PluginView.Any(PluginData.Empty));
        var v = d.Values[0];
        Assert.Equal(v.Text, PluginView.Text(v));
        Assert.Equal(Color.Red.ToArgb(), PluginView.ColorOf(new AppSettings { CritColor = "#FF0000" }, v with { Level = "crit" }, Color.Blue, Color.White).ToArgb());
        Assert.EndsWith("?", PluginView.Text(v with { Status = "error" }));
    }
}
