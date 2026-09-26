using Xunit;

namespace TaskbarStats.Tests;

/// <summary>Eigen lengte per grafiek in het widget: keuze, terugval op de algemene lengte, opslaan en thema's.</summary>
public class GraphSizeTests : IDisposable
{
    private readonly TempData _data = new();
    public void Dispose() => _data.Dispose();

    [Fact]
    public void Zonder_eigen_regel_volgt_elke_grafiek_de_algemene_lengte()
    {
        var s = new AppSettings { GraphLength = GraphLen.Long };
        foreach (var key in AppSettings.GraphKeys) Assert.Equal(60, s.GraphWidthFor(key));
    }

    [Fact]
    public void Eigen_lengte_geldt_alleen_voor_dat_onderdeel()
    {
        var s = new AppSettings { GraphLength = GraphLen.Medium };
        s.GraphSizes["cpu"] = new GraphSize { Len = GraphLen.Tiny };
        s.GraphSizes["net"] = new GraphSize { Len = GraphLen.Custom, Px = 120 };
        Assert.Equal(24, s.GraphWidthFor("cpu"));
        Assert.Equal(120, s.GraphWidthFor("net"));
        Assert.Equal(46, s.GraphWidthFor("gpu"));     // volgt de algemene lengte (Medium)
    }

    [Theory]
    [InlineData(GraphLen.Tiny, 24)]
    [InlineData(GraphLen.Short, 34)]
    [InlineData(GraphLen.Medium, 46)]
    [InlineData(GraphLen.Long, 60)]
    public void Vaste_lengtes(GraphLen len, int px)
    {
        var s = new AppSettings();
        s.GraphSizes["mem"] = new GraphSize { Len = len };
        Assert.Equal(px, s.GraphWidthFor("mem"));
    }

    [Theory]
    [InlineData(5, 16)]
    [InlineData(100, 100)]
    [InlineData(999, 160)]
    public void Eigen_breedte_wordt_begrensd(int asked, int expected)
    {
        var s = new AppSettings();
        s.GraphSizes["ping"] = new GraphSize { Len = GraphLen.Custom, Px = asked };
        Assert.Equal(expected, s.GraphWidthFor("ping"));
    }

    [Fact]
    public void Eigen_lengtes_overleven_opslaan_en_laden_en_een_oud_bestand_zonder_werkt_nog()
    {
        var s = AppSettings.Load();
        s.GraphSizes["cpu"] = new GraphSize { Len = GraphLen.Short };
        s.GraphSizes["gpu"] = new GraphSize { Len = GraphLen.Custom, Px = 77 };
        s.Save();
        var back = AppSettings.Load();
        Assert.Equal(34, back.GraphWidthFor("cpu"));
        Assert.Equal(77, back.GraphWidthFor("gpu"));
        Assert.Equal(back.GraphWidthBase, back.GraphWidthFor("mem"));

        File.WriteAllText(back.FilePath, "{ \"GraphLength\": \"Long\" }");   // bestand van een oudere versie: geen GraphSizes
        var old = AppSettings.Load();
        Assert.Empty(old.GraphSizes);
        Assert.Equal(60, old.GraphWidthFor("cpu"));
    }

    [Fact]
    public void Thema_neemt_de_eigen_lengtes_mee_en_geeft_een_kopie()
    {
        var s = new AppSettings();
        s.GraphSizes["cpu"] = new GraphSize { Len = GraphLen.Long };
        var theme = ThemeData.Capture(s, "t");
        s.GraphSizes["cpu"].Len = GraphLen.Tiny;              // wijzigen na het vastleggen mag het thema niet veranderen
        var target = new AppSettings();
        theme.ApplyTo(target);
        Assert.Equal(60, target.GraphWidthFor("cpu"));
        target.GraphSizes["cpu"].Len = GraphLen.Short;
        Assert.Equal(GraphLen.Long, theme.GraphSizes!["cpu"].Len);
    }

    [Fact]
    public void Thema_zonder_lengtes_wist_de_eigen_lengtes_en_negeert_onbekende_sleutels()
    {
        var target = new AppSettings();
        target.GraphSizes["cpu"] = new GraphSize { Len = GraphLen.Long };
        new ThemeData { Name = "x", GraphSizes = new() { ["bestaat-niet"] = new GraphSize { Len = GraphLen.Long } } }.ApplyTo(target);
        Assert.Empty(target.GraphSizes);
    }
}
