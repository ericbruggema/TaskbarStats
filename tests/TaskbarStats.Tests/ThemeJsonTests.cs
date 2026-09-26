using System.Text.Json;
using Xunit;

namespace TaskbarStats.Tests;

public class ThemeJsonTests : IDisposable
{
    private readonly TempData _data = new();
    public void Dispose() => _data.Dispose();

    [Fact]
    public void Json_van_een_thema_is_terug_te_lezen()
    {
        var t = new ThemeData { Name = "X", AccentColor = "#123456", CpuStyle = DisplayStyle.Gauge };
        var back = ThemeStore.Parse(ThemeStore.ToJson(t));
        Assert.Equal("X", back.Name);
        Assert.Equal("#123456", back.AccentColor);
        Assert.Equal(DisplayStyle.Gauge, back.CpuStyle);
    }

    [Fact]
    public void Kapotte_json_geeft_een_JsonException()
        => Assert.ThrowsAny<JsonException>(() => ThemeStore.Parse("{ kapot"));

    [Fact]
    public void Aangepast_standaardthema_op_schijf_vervangt_het_meegeleverde()
    {
        var s = AppSettings.Load();
        var before = ThemeStore.Entries(s);
        Assert.All(before, e => Assert.False(e.Edited));
        int count = before.Count;

        ThemeStore.Write(ThemeStore.FileFor(s, "dark"), new ThemeData { Name = "dark", AccentColor = "#ABCDEF" });   // andere hoofdletters
        ThemeStore.Write(ThemeStore.FileFor(s, "Eigen"), new ThemeData { Name = "Eigen" });

        var all = ThemeStore.Entries(s);
        Assert.Equal(count + 1, all.Count);   // Dark komt niet dubbel voor, Eigen erbij
        var dark = Assert.Single(all, e => e.T.Name == "Dark");
        Assert.True(dark.BuiltIn); Assert.True(dark.Edited);
        Assert.Equal("#ABCDEF", dark.T.AccentColor);
        Assert.False(all.Single(e => e.T.Name == "Eigen").BuiltIn);

        File.Delete(dark.File!);   // "origineel herstellen"
        Assert.False(ThemeStore.Entries(s).Single(e => e.T.Name == "Dark").Edited);
    }

    [Fact]
    public void Specificaties_worden_platte_tekst()
    {
        var blocks = new List<SpecBlock>
        {
            new("CPU", new List<SpecRow> { new("Naam", () => "Test 9000"), new("", () => "los") }),
            new("Geheugen", new List<SpecRow> { new("Totaal", () => throw new InvalidOperationException()) }),
        };
        string t = HardwareInfo.ToText(blocks, new DateTime(2026, 1, 2, 3, 4, 5));
        Assert.StartsWith("TaskbarStats ", t);
        Assert.Contains("2026-01-02 03:04:05", t);
        Assert.Contains("== CPU ==\r\nNaam: Test 9000\r\nlos\r\n", t);
        Assert.Contains("== Geheugen ==\r\nTotaal: \r\n", t);   // een falende waarde breekt het kopiëren niet
    }

    [Fact]
    public void Voorbeeldwaarden_bewegen_en_verdwijnen_weer()
    {
        var m = new Metrics();
        try
        {
            Assert.False(Metrics.DemoActive);
            Metrics.SetDemo(0);
            var a = m.Current;
            Metrics.SetDemo(3);
            var b = m.Current;
            Assert.True(Metrics.DemoActive);
            Assert.NotEqual(a.RawCpu, b.RawCpu);
            Assert.InRange(b.RawCpu, 0, 100); Assert.InRange(b.RawMem, 0, 100);
            Assert.Equal(8, b.RawCores.Length);
        }
        finally { Metrics.ClearDemo(); }
        Assert.False(Metrics.DemoActive);
        Assert.Same(MetricsSnapshot.Empty, m.Current);   // weer de echte (nog lege) meting
    }
}
