using System.Net;
using System.Net.Sockets;

namespace TaskbarStats.Plugins.Tests;

public class ClaudePluginTests
{
    private const string Usage = """
        {"five_hour":{"utilization":37.5,"resets_at":"2026-11-07T14:10:00+00:00"},
         "seven_day":{"utilization":80,"resets_at":"2026-11-10T09:00:00+00:00"},
         "seven_day_opus":null,
         "seven_day_sonnet":{"utilization":12,"resets_at":null},
         "extra_usage":{"is_enabled":false}}
        """;

    private static string Creds(string dir, string token = "tok-SECRET-123")
    {
        var p = Path.Combine(dir, ".credentials.json");
        File.WriteAllText(p, "{\"claudeAiOauth\":{\"accessToken\":\"" + token + "\",\"expiresAt\":9999999999999}}");
        return p;
    }

    [Fact]
    public void Parse_ShowsMainLimits_WithLevelsAndResetTime()
    {
        var r = ClaudePlugin.Parse(Usage, IniFile.Parse(""), T.Noon);
        Assert.False(r.Error);
        Assert.Equal(new[] { "five_hour", "seven_day" }, r.Items.Select(i => i.Key));   // sonnet/opus standaard niet
        var h5 = r.Items[0];
        Assert.Equal(37.5, h5.Value);
        Assert.Equal("38%", h5.Text);
        Assert.Equal("ok", h5.Level);
        Assert.Contains("resets in 2h 10m", h5.Detail);
        Assert.True(h5.Widget && h5.Notify);
        Assert.Equal("warn", r.Items[1].Level);   // 80% >= 70
    }

    [Fact]
    public void Parse_RemainingMode_InvertsValue_ButNotTheLevel()
    {
        var r = ClaudePlugin.Parse(Usage, IniFile.Parse("show = remaining\nshow_reset = true"), T.Noon);
        Assert.Equal(62.5, r.Items[0].Value);
        Assert.Equal("63% · 2h 10m", r.Items[0].Text);
        Assert.Equal("warn", r.Items[1].Level);    // niveau blijft op "gebruikt" gebaseerd
        Assert.Equal(20, r.Items[1].Value);
    }

    [Fact]
    public void Parse_OptionalLimits_NeedTheirSwitch()
    {
        var r = ClaudePlugin.Parse(Usage, IniFile.Parse("seven_day_sonnet = true\nfive_hour = false"), T.Noon);
        Assert.Equal(new[] { "seven_day", "seven_day_sonnet" }, r.Items.Select(i => i.Key));
        Assert.False(r.Items[1].Widget);
        Assert.Equal(12, r.Items[1].Value);
    }

    [Fact]
    public void Parse_GarbageIsAnError()
    {
        Assert.True(ClaudePlugin.Parse("not json", IniFile.Parse(""), T.Noon).Error);
        Assert.True(ClaudePlugin.Parse("{\"a\":1}", IniFile.Parse(""), T.Noon).Error);
    }

    [Fact]
    public async Task Run_SendsBearerToken_AndBetaHeader_ToTheConfiguredAddress()
    {
        var dir = T.TempDir();
        var h = new FakeHandler { Respond = _ => FakeHandler.Json(Usage) };
        var r = await new ClaudePlugin().RunAsync(T.Ctx($"credentials = {Creds(dir)}\nusage_url = https://usage.test/x", h), CancellationToken.None);
        Assert.False(r.Error, r.Message);
        Assert.Equal("https://usage.test/x", h.Urls.Single());
        Assert.Equal("Bearer tok-SECRET-123", h.AuthHeaders.Single());
        Assert.Contains("oauth", h.Betas.Single());
        Assert.DoesNotContain("tok-SECRET-123", OutputFile.Render("claude", "Claude", r, "ok", 300, T.Noon));   // nooit in het uitvoerbestand
    }

    [Theory]
    [InlineData(401, "expired")]
    [InlineData(403, "expired")]
    [InlineData(500, "HTTP 500")]
    public async Task Run_ServerErrors_GiveReadableMessages(int code, string expected)
    {
        var dir = T.TempDir();
        var h = new FakeHandler { Respond = _ => FakeHandler.Json("{}", (HttpStatusCode)code) };
        var r = await new ClaudePlugin().RunAsync(T.Ctx($"credentials = {Creds(dir)}", h), CancellationToken.None);
        Assert.True(r.Error);
        Assert.Contains(expected, r.Message);
        Assert.DoesNotContain("tok-SECRET-123", r.Message);
    }

    [Fact]
    public async Task Run_RateLimited_AsksToWait()
    {
        var dir = T.TempDir();
        var h = new FakeHandler
        {
            Respond = _ =>
            {
                var m = FakeHandler.Json("{}", (HttpStatusCode)429);
                m.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(900));
                return m;
            },
        };
        var r = await new ClaudePlugin().RunAsync(T.Ctx($"credentials = {Creds(dir)}", h), CancellationToken.None);
        Assert.True(r.Error);
        Assert.Equal(900, r.RetryAfterSec);
    }

    [Fact]
    public async Task Run_FileWithoutSubscriptionSignIn_ExplainsWhat_ToDo()
    {
        var dir = T.TempDir();
        var p = Path.Combine(dir, ".credentials.json");
        File.WriteAllText(p, "{\"mcpOAuth\":{\"x\":{\"accessToken\":\"connector-token\"}}}");
        var h = new FakeHandler();
        var r = await new ClaudePlugin().RunAsync(T.Ctx($"credentials = {p}", h), CancellationToken.None);
        Assert.True(r.Error);
        Assert.Contains("/login", r.Message);
        Assert.Empty(h.Urls);   // zonder abonnementstoken wordt er niets verstuurd (zeker niet het connector-token)
    }

    [Fact]
    public async Task Run_MissingFile_ExplainsWhat_ToDo()
    {
        var r = await new ClaudePlugin().RunAsync(T.Ctx("credentials = " + Path.Combine(T.TempDir(), "nope.json")), CancellationToken.None);
        Assert.True(r.Error);
        Assert.Contains("sign-in", r.Message);
    }
}

public class WeatherAndLocationTests
{
    private const string Forecast = """
        {"current":{"time":"2026-11-07T14:15","temperature_2m":3.9,"relative_humidity_2m":91,"apparent_temperature":-0.4,"precipitation":0.2,"weather_code":3,"wind_speed_10m":16.6,"wind_direction_10m":90,"is_day":1},
         "hourly":{"time":["2026-11-07T13:00","2026-11-07T14:00","2026-11-07T15:00"],"precipitation_probability":[10,35,60]},
         "daily":{"temperature_2m_max":[5.1],"temperature_2m_min":[2.9]}}
        """;

    [Fact]
    public void Weather_Parse_GivesTemperatureWithGlyph_AndDetails()
    {
        var fix = new GeoFix(53.2, 6.5, null, "IP address", "Groningen", "Netherlands", "NL", null);
        var r = WeatherPlugin.Parse(Forecast, fix, "en", false);
        Assert.False(r.Error);
        var temp = r.Items.Single(i => i.Key == "temp");
        Assert.Equal("☁ 4°", temp.Text);
        Assert.Equal("", temp.Short);          // geen label in het widget
        Assert.True(temp.Widget);
        Assert.Contains("Groningen, NL", temp.Detail);
        Assert.Contains("Overcast", temp.Detail);
        Assert.Contains("3°..5°", temp.Detail);
        Assert.Equal(35, r.Items.Single(i => i.Key == "rain").Value);        // het uur van "nu" (14:xx)
        Assert.Equal(91, r.Items.Single(i => i.Key == "humidity").Value);
        Assert.Equal("17 km/h E", r.Items.Single(i => i.Key == "wind").Text);
        Assert.All(r.Items.Where(i => i.Key != "temp"), i => Assert.False(i.Widget));
        Assert.Equal("0°", r.Items.Single(i => i.Key == "feels").Text);      // -0,4 wordt "0", niet "-0"
    }

    [Fact]
    public void Weather_Parse_ImperialAndDutch()
    {
        var fix = new GeoFix(53.2, 6.5, null, "fixed", null, null, null, null);
        var r = WeatherPlugin.Parse(Forecast, fix, "nl", true);
        Assert.Equal("°F", r.Items[0].Unit);
        Assert.Contains("Bewolkt", r.Items[0].Detail);
        Assert.Equal("17 mph O", r.Items.Single(i => i.Key == "wind").Text);
    }

    [Theory]
    [InlineData(0, true, "☀")] [InlineData(0, false, "☾")] [InlineData(2, true, "⛅")] [InlineData(61, true, "☂")]
    [InlineData(73, true, "❄")] [InlineData(95, true, "⚡")] [InlineData(45, true, "≋")] [InlineData(3, false, "☁")]
    public void Weather_Glyphs(int code, bool day, string glyph) => Assert.Equal(glyph, WeatherPlugin.Glyph(code, day));

    [Fact]
    public void Weather_EveryCodeHasADescriptionInAllLanguages()
    {
        foreach (var code in new[] { 0, 1, 2, 3, 45, 48, 51, 53, 55, 56, 57, 61, 63, 65, 66, 67, 71, 73, 75, 77, 80, 81, 82, 85, 86, 95, 96, 99 })
            foreach (var lang in new[] { "en", "nl", "de" })
                Assert.NotEqual("?", WeatherPlugin.Describe(code, lang));
        Assert.Equal("NW", WeatherPlugin.Compass(315, "en"));
        Assert.Equal("ZW", WeatherPlugin.Compass(225, "nl"));
        Assert.Equal("N", WeatherPlugin.Compass(359, "de"));
    }

    [Fact]
    public void Weather_Parse_GarbageIsAnError()
        => Assert.True(WeatherPlugin.Parse("{}", new GeoFix(0, 0, null, "x", null, null, null, null), "en", false).Error);

    [Theory]
    [InlineData("52.37, 4.90", true, 52.37, 4.90)]
    [InlineData("-33.9;151.2", true, -33.9, 151.2)]
    [InlineData("Amsterdam", false, 0, 0)]
    [InlineData("91, 0", false, 0, 0)]
    [InlineData("1,2,3", false, 0, 0)]
    public void Geo_ParsesCoordinates(string s, bool ok, double lat, double lon)
    {
        Assert.Equal(ok, Geo.TryParseCoords(s, out var la, out var lo));
        if (ok) { Assert.Equal(lat, la, 6); Assert.Equal(lon, lo, 6); }
    }

    [Fact]
    public void Geo_Distance()
    {
        Assert.Equal(0, Geo.DistanceKm(52, 5, 52, 5), 3);
        Assert.InRange(Geo.DistanceKm(52.37, 4.90, 53.21, 6.57), 135, 150);   // Amsterdam - Groningen
    }

    [Fact]
    public async Task Geo_FixedCoordinatesNeedNoNetwork_AndAreReverseGeocoded()
    {
        Geo.ClearCache();
        var h = new FakeHandler { Respond = r => FakeHandler.Json("{\"city\":\"Zwolle\",\"countryName\":\"Netherlands\",\"countryCode\":\"NL\"}") };
        var (fix, _) = await Geo.ResolveAsync("52.5, 6.09", T.Http(h), "en", true, 30, 100, () => T.Noon, CancellationToken.None);
        Assert.NotNull(fix);
        Assert.Equal("Zwolle", fix!.Place);
        Assert.Single(h.Urls);
        Assert.Contains("latitude=52.5", h.Urls[0]);
        // tweede keer komt uit de cache
        await Geo.ResolveAsync("52.5, 6.09", T.Http(h), "en", true, 30, 100, () => T.Noon, CancellationToken.None);
        Assert.Single(h.Urls);
    }

    [Fact]
    public async Task Geo_PlaceNameIsLookedUp()
    {
        Geo.ClearCache();
        var h = new FakeHandler { Respond = r => FakeHandler.Json("{\"results\":[{\"name\":\"Utrecht\",\"latitude\":52.09,\"longitude\":5.12,\"country\":\"Netherlands\",\"country_code\":\"NL\"}]}") };
        var (fix, _) = await Geo.ResolveAsync("Utrecht", T.Http(h), "en", false, 30, 100, () => T.Noon, CancellationToken.None);
        Assert.Equal(52.09, fix!.Lat, 2);
        Assert.Equal("Utrecht", fix.Place);
        Assert.Contains("name=Utrecht", h.Urls.Single());
    }

    [Fact]
    public async Task Geo_UnknownPlace_IsReportedNotThrown()
    {
        Geo.ClearCache();
        var h = new FakeHandler { Respond = r => FakeHandler.Json("{}") };
        var (fix, note) = await Geo.ResolveAsync("Nowhereville", T.Http(h), "en", false, 30, 100, () => T.Noon, CancellationToken.None);
        Assert.Null(fix);
        Assert.Contains("Nowhereville", note);
    }

    [Fact]
    public void Location_Build_HidesCoordinatesInTheWidget_AndAddsHomeDistance()
    {
        var fix = new GeoFix(52.3702, 4.8952, 94, "Wi-Fi", "Amsterdam", "Netherlands", "NL", 5.0);
        var r = LocationPlugin.Build(fix, "", IniFile.Parse("home = 52.0, 5.0\nprecision = 2"));
        var place = r.Items.Single(i => i.Key == "place");
        Assert.Equal("Amsterdam", place.Text);
        Assert.True(place.Widget);
        Assert.Contains("Wi-Fi", place.Detail);
        Assert.Contains("±94 m", place.Detail);
        var coords = r.Items.Single(i => i.Key == "coords");
        Assert.Equal("52.37, 4.90", coords.Text);
        Assert.False(coords.Widget);
        Assert.Equal(18, r.Items.Single(i => i.Key == "speed").Value!.Value, 0);   // 5 m/s = 18 km/h
        Assert.True(r.Items.Single(i => i.Key == "home").Value > 30);
    }
}

public class UptimeAndScriptTests
{
    [Fact]
    public void Uptime_ReadChecks_FromSections()
    {
        var cfg = IniFile.Parse("enabled = true\n[A]\nurl = https://a.test\nexpect = 200,204\n[B]\ntcp = nas:22\n[C]\nping = 192.168.1.1\n[Empty]\nfoo = bar\n");
        var c = UptimePlugin.ReadChecks(cfg);
        Assert.Equal(new[] { "url", "tcp", "ping" }, c.Select(x => x.Kind));
        Assert.Equal(new[] { 200, 204 }, c[0].Expect.OrderBy(x => x));
    }

    [Fact]
    public void Uptime_Build_SummaryLevels()
    {
        var cfg = IniFile.Parse("slow_ms = 1000");
        var ok = UptimePlugin.Build(new[] { new UptimePlugin.Outcome("A", true, 100, ""), new UptimePlugin.Outcome("B", true, 200, "") }, cfg);
        Assert.Equal("2/2", ok.Items[0].Text);
        Assert.Equal("ok", ok.Items[0].Level);

        var slow = UptimePlugin.Build(new[] { new UptimePlugin.Outcome("A", true, 1500, "") }, cfg);
        Assert.Equal("warn", slow.Items[0].Level);

        var down = UptimePlugin.Build(new[] { new UptimePlugin.Outcome("A", true, 100, ""), new UptimePlugin.Outcome("My Site!", false, null, "HTTP 503") }, cfg);
        var sum = down.Items[0];
        Assert.Equal("1/2", sum.Text);
        Assert.Equal("crit", sum.Level);
        Assert.True(sum.Notify);
        Assert.Contains("✕ My Site!  HTTP 503", sum.Detail);
        Assert.Equal("my_site", down.Items[2].Key);
        Assert.Equal("DOWN", down.Items[2].Text);
        Assert.False(down.Items[2].Widget);
        // de melding in de app noemt alleen wat uitgevallen is
        var v = new PluginValue("uptime", "Uptime", "summary", "Uptime", "UP", 50, 0, 100, "%", sum.Text, sum.Detail, "crit", "", true, true, "ok", "", DateTime.UtcNow, 300);
        Assert.Equal("✕ My Site!  HTTP 503", WidgetForm.AlertText(v));
    }

    [Fact]
    public async Task Uptime_HttpChecks()
    {
        var h = new FakeHandler { Respond = r => r.RequestUri!.Host == "down.test" ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : new HttpResponseMessage(HttpStatusCode.OK) };
        using var http = T.Http(h);
        var up = await UptimePlugin.RunCheckAsync(new UptimePlugin.Check("a", "url", "https://up.test", new()), http, 3000, CancellationToken.None);
        var down = await UptimePlugin.RunCheckAsync(new UptimePlugin.Check("b", "url", "https://down.test", new()), http, 3000, CancellationToken.None);
        var expected = await UptimePlugin.RunCheckAsync(new UptimePlugin.Check("c", "url", "https://down.test", new() { 503 }), http, 3000, CancellationToken.None);
        Assert.True(up.Up);
        Assert.False(down.Up);
        Assert.Equal("HTTP 503", down.Error);
        Assert.True(expected.Up);   // 503 is hier het verwachte antwoord
    }

    [Fact]
    public async Task Uptime_TcpChecks_AgainstALocalListener()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        using var http = T.Http(new FakeHandler());
        var up = await UptimePlugin.RunCheckAsync(new UptimePlugin.Check("a", "tcp", $"127.0.0.1:{port}", new()), http, 3000, CancellationToken.None);
        l.Stop();
        var down = await UptimePlugin.RunCheckAsync(new UptimePlugin.Check("b", "tcp", $"127.0.0.1:{port}", new()), http, 3000, CancellationToken.None);
        var bad = await UptimePlugin.RunCheckAsync(new UptimePlugin.Check("c", "tcp", "no-port", new()), http, 3000, CancellationToken.None);
        Assert.True(up.Up);
        Assert.False(down.Up);
        Assert.False(bad.Up);
    }

    private static IniFile.Section Sec(string text) => IniFile.Parse("[Test]\n" + text).Sections[0];

    [Theory]
    [InlineData("42", 42.0, "42")]
    [InlineData("12,5%", 12.5, "12.5")]
    public void Script_PlainNumber(string raw, double value, string text)
    {
        var it = ScriptPlugin.Interpret(Sec("unit = %"), raw);
        Assert.Equal(value, it.Value);
        Assert.StartsWith(text, it.Text);
    }

    [Fact]
    public void Script_NumberWithText_PlainText_AndJson()
    {
        var a = ScriptPlugin.Interpret(Sec(""), "42|3 builds\nsecond line ignored");
        Assert.Equal(42, a.Value);
        Assert.Equal("3 builds", a.Text);

        var b = ScriptPlugin.Interpret(Sec("short ="), "All fine");
        Assert.Null(b.Value);
        Assert.Equal("All fine", b.Text);
        Assert.Equal("", b.Short);

        var c = ScriptPlugin.Interpret(Sec(""), "{\"value\":7,\"text\":\"seven\",\"detail\":\"d\",\"level\":\"crit\",\"color\":\"#ff0000\"}");
        Assert.Equal(7, c.Value);
        Assert.Equal("seven", c.Text);
        Assert.Equal("crit", c.Level);
        Assert.Equal("#ff0000", c.Color);
    }

    [Fact]
    public void Script_Thresholds_AndInvert()
    {
        Assert.Equal("ok", ScriptPlugin.Interpret(Sec("warn = 70\ncrit = 90"), "10").Level);
        Assert.Equal("warn", ScriptPlugin.Interpret(Sec("warn = 70\ncrit = 90"), "75").Level);
        Assert.Equal("crit", ScriptPlugin.Interpret(Sec("warn = 70\ncrit = 90"), "95").Level);
        Assert.Equal("crit", ScriptPlugin.Interpret(Sec("warn = 20\ncrit = 5\ninvert = true"), "3").Level);
        Assert.Equal("ok", ScriptPlugin.Interpret(Sec("warn = 20\ncrit = 5\ninvert = true"), "50").Level);
        Assert.Equal("", ScriptPlugin.Interpret(Sec(""), "50").Level);   // zonder drempels geen niveau
    }

    [Theory]
    [InlineData("a.b", "{\"a\":{\"b\":5}}", "5")]
    [InlineData("data.items[1].price", "{\"data\":{\"items\":[{\"price\":1},{\"price\":2.5}]}}", "2.5")]
    [InlineData("$.x", "{\"x\":\"hi\"}", "hi")]
    [InlineData("[0]", "[9,8]", "9")]
    public void Script_JsonPath(string path, string json, string expected)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var el = ScriptPlugin.SelectPath(doc.RootElement, path);
        Assert.NotNull(el);
        Assert.Equal(expected, el!.Value.ValueKind == System.Text.Json.JsonValueKind.String ? el.Value.GetString() : el.Value.GetRawText());
    }

    [Fact]
    public void Script_JsonPath_MissingIsNull()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("{\"a\":[1]}");
        Assert.Null(ScriptPlugin.SelectPath(doc.RootElement, "a[5]"));
        Assert.Null(ScriptPlugin.SelectPath(doc.RootElement, "b.c"));
    }

    [Fact]
    public async Task Script_RunsAFile_ACommand_AndAUrl()
    {
        var dir = T.TempDir();
        var f = Path.Combine(dir, "v.txt");
        File.WriteAllText(f, "64|hello");
        var h = new FakeHandler { Respond = r => FakeHandler.Json("{\"bitcoin\":{\"eur\":61234.5}}") };
        string ini = string.Join("\n",
            "enabled = true",
            "[From file]", "file = " + f, "unit = %",
            "[From command]", "cmd = echo 42", "short = Cmd",
            "[From url]", "url = https://api.test/price", "path = bitcoin.eur", "header = X-Test: 1", "short = BTC");
        var r = await new ScriptPlugin().RunAsync(T.Ctx(ini, h, dir), CancellationToken.None);
        Assert.False(r.Error, r.Message);
        Assert.Equal(3, r.Items.Count);
        Assert.Equal("hello", r.Items[0].Text);
        Assert.Equal(64, r.Items[0].Value);
        Assert.Equal(42, r.Items[1].Value);
        Assert.Equal("Cmd", r.Items[1].Short);
        Assert.Equal(61234.5, r.Items[2].Value);
        Assert.Equal("from_url", r.Items[2].Key);
    }

    [Fact]
    public async Task Script_FailingItem_DoesNotStopTheOthers()
    {
        var dir = T.TempDir();
        var f = Path.Combine(dir, "ok.txt");
        File.WriteAllText(f, "5");
        string ini = string.Join("\n", "[Missing]", "file = " + Path.Combine(dir, "nope.txt"), "[Good]", "file = " + f);
        var r = await new ScriptPlugin().RunAsync(T.Ctx(ini, null, dir), CancellationToken.None);
        Assert.Equal(2, r.Items.Count);
        Assert.Equal("?", r.Items[0].Text);
        Assert.Equal("warn", r.Items[0].Level);
        Assert.Equal(5, r.Items[1].Value);
        Assert.Contains("Missing", r.Message);
    }

    [Fact]
    public async Task Script_CommandTimeout_KillsTheProcess()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(600));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ScriptPlugin.RunCommandAsync("ping -n 30 127.0.0.1 >nul", cts.Token));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Script_NoSections_IsAnError()
    {
        var r = await new ScriptPlugin().RunAsync(T.Ctx("enabled = true"), CancellationToken.None);
        Assert.True(r.Error);
    }
}

public class RunnerTests
{
    private sealed class FakePlugin : IPlugin
    {
        public string Id => "fake";
        public string Title => "Fake";
        public int DefaultIntervalSec => 30;
        public Func<PluginResult> Next { get; set; } = () => new PluginResult();
        public int Runs;
        public Task<PluginResult> RunAsync(PluginContext ctx, CancellationToken ct) { Runs++; return Task.FromResult(Next()); }
    }

    private static PluginResult Ok(string text)
    {
        var r = new PluginResult();
        r.Items.Add(new PluginItem { Key = "a", Label = "A", Text = text, Value = 1 });
        return r;
    }

    private static (PluginRunner runner, FakePlugin plugin, string dir, Func<DateTimeOffset> now, Action<TimeSpan> advance) Make(string? enabled)
    {
        var dir = T.TempDir();
        var plugin = new FakePlugin();
        var clock = T.Noon;
        var runner = new PluginRunner(dir, new IPlugin[] { plugin }, new HttpClient(new FakeHandler()), T.DefaultsDir(), () => clock);
        Directory.CreateDirectory(Path.Combine(dir, "plugins"));
        if (enabled is not null) File.WriteAllText(runner.ConfigPath("fake"), enabled);
        return (runner, plugin, dir, () => clock, d => clock += d);
    }

    [Fact]
    public async Task Disabled_PluginDoesNotRun_AndLeavesNoOutput()
    {
        var (runner, plugin, _, _, _) = Make(null);   // nog geen config: wordt aangemaakt met enabled = false
        await Task.WhenAll(runner.Tick(CancellationToken.None));
        Assert.Equal(0, plugin.Runs);
        Assert.True(File.Exists(runner.ConfigPath("fake")));
        Assert.False(File.Exists(runner.OutputPath("fake")));
    }

    [Fact]
    public async Task Enabled_RunsOnSchedule_WritesOutput()
    {
        var (runner, plugin, _, _, advance) = Make("enabled = true\ninterval = 30\n");
        plugin.Next = () => Ok("one");
        await Task.WhenAll(runner.Tick(CancellationToken.None));
        Assert.Equal(1, plugin.Runs);
        var (st, vals) = PluginFeed.Parse("fake", File.ReadAllText(runner.OutputPath("fake")), DateTime.UtcNow);
        Assert.Equal("ok", st!.Status);
        Assert.Equal("one", vals.Single().Text);

        await Task.WhenAll(runner.Tick(CancellationToken.None));   // nog niet aan de beurt
        Assert.Equal(1, plugin.Runs);
        advance(TimeSpan.FromSeconds(31));
        plugin.Next = () => Ok("two");
        await Task.WhenAll(runner.Tick(CancellationToken.None));
        Assert.Equal(2, plugin.Runs);
        Assert.Contains("two", File.ReadAllText(runner.OutputPath("fake")));
    }

    [Fact]
    public async Task Failure_KeepsTheLastGoodValues_AsError()
    {
        var (runner, plugin, _, _, advance) = Make("enabled = true\ninterval = 30\n");
        plugin.Next = () => Ok("good");
        await Task.WhenAll(runner.Tick(CancellationToken.None));
        advance(TimeSpan.FromSeconds(31));
        plugin.Next = () => PluginResult.Fail("server said no");
        await Task.WhenAll(runner.Tick(CancellationToken.None));
        var (st, vals) = PluginFeed.Parse("fake", File.ReadAllText(runner.OutputPath("fake")), DateTime.UtcNow);
        Assert.Equal("error", st!.Status);
        Assert.Equal("server said no", st.Message);
        Assert.Equal("good", vals.Single().Text);
        Assert.True(vals.Single().Stale);   // wordt in de app vervaagd getoond
    }

    [Fact]
    public async Task ExceptionInAPlugin_BecomesAnErrorResult()
    {
        var (runner, plugin, _, _, _) = Make("enabled = true\n");
        plugin.Next = () => throw new InvalidOperationException("boom with Bearer abc.def-123");
        await Task.WhenAll(runner.Tick(CancellationToken.None));
        var text = File.ReadAllText(runner.OutputPath("fake"));
        Assert.Contains("\"status\": \"error\"", text);
        Assert.Contains("boom", text);
        Assert.DoesNotContain("abc.def-123", text);   // geheimen worden uit meldingen gehaald
    }

    [Fact]
    public async Task ChangingTheConfig_RunsImmediately_AndDisablingRemovesTheOutput()
    {
        var (runner, plugin, _, _, _) = Make("enabled = true\ninterval = 3600\n");
        plugin.Next = () => Ok("x");
        await Task.WhenAll(runner.Tick(CancellationToken.None));
        Assert.True(File.Exists(runner.OutputPath("fake")));

        File.WriteAllText(runner.ConfigPath("fake"), "enabled = false\ninterval = 3600\n");
        File.SetLastWriteTimeUtc(runner.ConfigPath("fake"), DateTime.UtcNow.AddMinutes(1));   // andere tijdstempel
        await Task.WhenAll(runner.Tick(CancellationToken.None));
        Assert.False(File.Exists(runner.OutputPath("fake")));
        Assert.False(runner.AnyEnabled());
    }

    [Fact]
    public async Task MissingConfig_IsCreatedFromTheShippedDefault()
    {
        var dir = T.TempDir();
        var runner = new PluginRunner(dir, Program.AllPlugins(), new HttpClient(new FakeHandler()), T.DefaultsDir(), () => T.Noon);
        await Task.WhenAll(runner.Tick(CancellationToken.None));
        foreach (var p in Program.AllPlugins())
        {
            Assert.True(File.Exists(runner.ConfigPath(p.Id)), p.Id);
            Assert.False(File.Exists(runner.OutputPath(p.Id)), p.Id);   // allemaal uit
        }
        Assert.False(runner.AnyEnabled());
        Assert.Contains("Claude usage", File.ReadAllText(runner.ConfigPath("claude")));
    }
}
