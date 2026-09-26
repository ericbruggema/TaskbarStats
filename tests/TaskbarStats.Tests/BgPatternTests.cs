using System.Drawing;
using Xunit;

namespace TaskbarStats.Tests;

public class BgPatternTests
{
    [Fact]
    public void Elk_patroon_tekent_iets_en_is_vast()
    {
        foreach (var n in BgPatterns.Names)
        {
            using var a = BgPatterns.Render(n, 320, 180)!;
            using var b = BgPatterns.Render(n, 320, 180)!;
            Assert.NotNull(a);
            bool any = false, same = true;
            for (int y = 0; y < a.Height; y += 3)
                for (int x = 0; x < a.Width; x += 3)
                {
                    var pa = a.GetPixel(x, y);
                    if (pa.A > 0) any = true;
                    if (pa != b.GetPixel(x, y)) same = false;
                }
            Assert.True(any, n + " is leeg");
            Assert.True(same, n + " is niet deterministisch");
            if (Environment.GetEnvironmentVariable("TS_PATTERN_OUT") is { Length: > 0 } dir)
            {
                using var big = BgPatterns.Render(n, 960, 540)!;
                using var flat = new Bitmap(big.Width, big.Height);
                using (var g = Graphics.FromImage(flat)) { g.Clear(ColorTranslator.FromHtml("#0B0F14")); g.DrawImage(big, 0, 0); }
                flat.Save(Path.Combine(dir, n + ".png"));
            }
        }
        Assert.Null(BgPatterns.Render("bestaat-niet", 100, 100));
    }

    [Fact]
    public void Thema_met_achtergrond_zet_en_wist_het_patroon_maar_laat_eigen_afbeeldingen_staan()
    {
        var c = new AppSettings();
        var matrix = ThemeStore.BuiltIn().Single(t => t.Name == "Matrix");
        matrix.ApplyTo(c);
        Assert.Equal("builtin:matrix", c.WidgetBgImage);
        Assert.Equal("builtin:matrix", c.FullBgImage);
        Assert.Equal(matrix.BackgroundOpacity, c.DashBgOpacity);
        Assert.Equal("matrix", ThemeData.Capture(c, "x").Background);

        ThemeStore.BuiltIn().Single(t => t.Name == "Dark").ApplyTo(c);   // thema zonder achtergrond
        Assert.Null(c.WidgetBgImage); Assert.Null(c.DashBgImage); Assert.Null(c.FullBgImage);

        c.DashBgImage = @"C:\eigen\foto.png";
        ThemeStore.BuiltIn().Single(t => t.Name == "Dark").ApplyTo(c);
        Assert.Equal(@"C:\eigen\foto.png", c.DashBgImage);   // eigen afbeelding blijft
        Assert.Null(ThemeData.Capture(c, "x").Background);
    }
}
