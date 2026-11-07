namespace TaskbarStats.Plugins.Tests;

public class IniAndModelTests
{
    [Fact]
    public void Ini_ReadsKeysSectionsAndComments()
    {
        var ini = IniFile.Parse(string.Join("\n",
            "# comment", "; another", "enabled = true", "interval = 120", "url = https://x.test/a?b=c&d=e",
            "[Site A]", "url = https://a.test", "expect = 200, 204", "[Site B]", "tcp = nas:22"));
        Assert.True(ini.Enabled);
        Assert.Equal(120, ini.Int("interval", 5));
        Assert.Equal("https://x.test/a?b=c&d=e", ini.Str("url", ""));   // '=' in de waarde blijft heel
        Assert.Equal(2, ini.Sections.Count);
        Assert.Equal("Site A", ini.Sections[0].Name);
        Assert.Equal("200, 204", ini.Sections[0].Get("expect"));
        Assert.Equal("nas:22", ini.Sections[1].Get("TCP"));   // sleutels zijn niet hoofdlettergevoelig
    }

    [Fact]
    public void Ini_KeysBeforeFirstSectionAreRootOnly()
    {
        var ini = IniFile.Parse("enabled = false\n[S]\nenabled = true\n");
        Assert.False(ini.Enabled);
    }

    [Theory]
    [InlineData("true", true)] [InlineData("Yes", true)] [InlineData("ja", true)] [InlineData("1", true)]
    [InlineData("false", false)] [InlineData("off", false)] [InlineData("nee", false)] [InlineData("rubbish", false)]
    public void Ini_Booleans(string v, bool expected) => Assert.Equal(expected, IniFile.Parse("x = " + v).Bool("x", false));

    [Fact]
    public void Ini_NumbersAreClampedAndInvariant()
    {
        var ini = IniFile.Parse("a = 5\nb = 99999\nc = 1,5\nd = nope");
        Assert.Equal(10, ini.Int("a", 60, 10, 100));
        Assert.Equal(100, ini.Int("b", 60, 10, 100));
        Assert.Equal(1.5, ini.Dbl("c", 0));
        Assert.Equal(60, ini.Int("d", 60));
    }

    [Fact]
    public void Ini_Validate_FindsBrokenLines()
    {
        Assert.Null(IniFile.Validate("# x\nenabled = true\n[a]\nk = v\n"));
        Assert.Contains("line 2", IniFile.Validate("# x\nthis is wrong\n")!);
        Assert.Contains("line 1", IniFile.Validate("[unclosed\n")!);
    }

    [Fact]
    public void Output_IsValidJson_WithoutEscapedDegreeSign_AndRoundTripsToTheApp()
    {
        var r = new PluginResult { Message = "hi" };
        r.Items.Add(new PluginItem { Key = "temp", Label = "Weather", Short = "", Value = 3.9, Min = -20, Max = 40, Unit = "°C", Text = "☁ 4°", Detail = "Groningen", Widget = true });
        r.Items.Add(new PluginItem { Key = "n", Label = "NoValue", Text = "ok", Level = "warn", Notify = true, Widget = false });
        string json = OutputFile.Render("weather", "Weather", r, "ok", 900, T.Noon);

        Assert.Contains("°C", json);                 // geen °
        Assert.DoesNotContain("\\u00B0", json);

        // het contract: wat de host schrijft, leest de app
        var (st, vals) = PluginFeed.Parse("weather", json, DateTime.UtcNow);
        Assert.NotNull(st);
        Assert.Equal("weather", st!.Plugin);
        Assert.Equal(2, vals.Count);
        Assert.Equal("", vals[0].Short);                // lege korte naam blijft leeg (= geen label)
        Assert.Equal("weather/temp", vals[0].Id);
        Assert.Equal(3.9, vals[0].Value);
        Assert.Equal(39.8, vals[0].Percent!.Value, 1);   // (3.9 - -20) / 60 * 100
        Assert.Null(vals[1].Value);                      // geen getal
        Assert.Null(vals[1].Percent);
        Assert.True(vals[1].Notify);
        Assert.False(vals[1].WidgetHint);
        Assert.Equal("warn", vals[1].Level);
        Assert.Equal(900 * 3 + 60, vals[0].TtlSec);
    }

    [Fact]
    public void Fmt_Helpers()
    {
        Assert.Equal("0", Fmt.Num(-0.4));
        Assert.Equal("-3", Fmt.Num(-2.6));
        Assert.Equal("2h 10m", Fmt.Duration(TimeSpan.FromMinutes(130)));
        Assert.Equal("1m", Fmt.Duration(TimeSpan.FromSeconds(5)));
        Assert.Equal("1d 2h", Fmt.Duration(TimeSpan.FromHours(26)));
        Assert.Equal("crit", Fmt.Level(95, 70, 90));
        Assert.Equal("warn", Fmt.Level(70, 70, 90));
        Assert.Equal("ok", Fmt.Level(10, 70, 90));
        Assert.DoesNotContain("sk-ant-abc123", Fmt.Scrub("failed with Bearer sk-ant-abc123xyz"));
        Assert.DoesNotContain("topsecret", Fmt.Scrub("https://x.test/?token=topsecret&a=1"));
    }

    [Fact]
    public void ShippedDefaultConfigs_AreValid_AndStartDisabled()
    {
        var dir = T.DefaultsDir();
        foreach (var p in Program.AllPlugins())
        {
            var path = Path.Combine(dir, p.Id + ".ini");
            Assert.True(File.Exists(path), p.Id + ".ini ontbreekt");
            var text = File.ReadAllText(path);
            Assert.Null(IniFile.Validate(text));
            Assert.False(IniFile.Parse(text).Enabled, p.Id + " moet standaard uit staan");
        }
        // de app kent dezelfde plugin-id's
        Assert.Equal(Program.AllPlugins().Select(p => p.Id), TaskbarStats.PluginCatalog.Ids);
    }
}
