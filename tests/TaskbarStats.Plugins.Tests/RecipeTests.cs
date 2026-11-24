extern alias app;
using PluginFeed = app::TaskbarStats.PluginFeed;
using PluginValue = app::TaskbarStats.PluginValue;
using WidgetForm = app::TaskbarStats.WidgetForm;
using System.Net;
using Xunit;

namespace TaskbarStats.Plugins.Tests;

/// <summary>De formuletaal, de recepten (plugins zonder code), de goedkeuring en de geheimen. Zonder internet (nep-HTTP).</summary>
public class RecipeTests
{
    // ---------- hulpjes ----------
    private static readonly DateTimeOffset Noon = T.Noon;

    private static object? Ev(string src, Dictionary<string, object?>? vars = null)
        => Expr.Parse(src).Eval(new ExprEnv { Vars = vars ?? new(), Now = () => Noon });

    private static Dictionary<string, object?> Root(string json)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return new Dictionary<string, object?> { ["root"] = Expr.FromJson(doc.RootElement) };
    }

    private static string Tpl(string t, Dictionary<string, object?>? vars = null)
        => Template.Parse(t).Render(new ExprEnv { Vars = vars ?? new(), Now = () => Noon });

    private static async Task<PluginResult> Run(string ini, FakeHandler? h = null, Dictionary<string, string>? secrets = null, string? dataDir = null)
    {
        var ctx = new PluginContext
        {
            Config = IniFile.Parse(ini), Http = T.Http(h ?? new FakeHandler()), DataDir = dataDir ?? T.TempDir(), Now = () => Noon,
            Secrets = secrets ?? new Dictionary<string, string>(),
        };
        return await new RecipePlugin("r", "R").RunAsync(ctx, CancellationToken.None);
    }

    // ---------- formuletaal ----------
    [Theory]
    [InlineData("1 + 2 * 3", 7.0)]
    [InlineData("(1 + 2) * 3", 9.0)]
    [InlineData("10 / 4", 2.5)]
    [InlineData("10 % 4", 2.0)]
    [InlineData("-3 + 5", 2.0)]
    [InlineData("2 * -3", -6.0)]
    [InlineData("1e2 + .5", 100.5)]
    [InlineData("round(2.5)", 3.0)]
    [InlineData("round(-2.5)", -3.0)]
    [InlineData("round(3.14159, 2)", 3.14)]
    [InlineData("floor(2.9) + ceil(2.1)", 5.0)]
    [InlineData("abs(-4)", 4.0)]
    [InlineData("min(3, 1, 2)", 1.0)]
    [InlineData("max(3, 1, 2)", 3.0)]
    [InlineData("clamp(150, 0, 100)", 100.0)]
    [InlineData("pct(1, 4)", 25.0)]
    [InlineData("pow(2, 10)", 1024.0)]
    [InlineData("sqrt(16)", 4.0)]
    [InlineData("num('12,5%')", 12.5)]
    [InlineData("num(' 7 ')", 7.0)]
    [InlineData("len('abc')", 3.0)]
    public void Numbers(string src, double expected) => Assert.Equal(expected, Assert.IsType<double>(Ev(src)), 6);

    [Theory]
    [InlineData("1 < 2", true)]
    [InlineData("2 <= 2", true)]
    [InlineData("3 > 4", false)]
    [InlineData("'a' == 'A'", true)]               // tekst vergelijkt zonder hoofdletters
    [InlineData("'10' > '9'", true)]               // tekst die getallen zijn, vergelijkt als getallen
    [InlineData("'abc' < 'abd'", true)]
    [InlineData("1 == '1'", true)]
    [InlineData("null == null", true)]
    [InlineData("null == 0", false)]
    [InlineData("null > 1", false)]
    [InlineData("!false && (true || false)", true)]
    [InlineData("1 != 2", true)]
    [InlineData("contains('Hello World', 'WORLD')", true)]
    [InlineData("startswith('abc', 'ab') && endswith('abc', 'bc')", true)]
    [InlineData("matches('v1.2.3', '^v\\d+')", true)]
    [InlineData("isnull(null)", true)]
    public void Booleans(string src, bool expected) => Assert.Equal(expected, Assert.IsType<bool>(Ev(src)));

    [Theory]
    [InlineData("'a' + 'b'", "ab")]
    [InlineData("'n=' + 5", "n=5")]
    [InlineData("upper('abc') + lower('DEF')", "ABCdef")]
    [InlineData("trim('  x ')", "x")]
    [InlineData("replace('a-b-c', '-', '+')", "a+b+c")]
    [InlineData("substr('abcdef', 2, 3)", "cde")]
    [InlineData("substr('abc', 1)", "bc")]
    [InlineData("join(split('a,b,c', ','), '/')", "a/b/c")]
    [InlineData("regex('build 42 ok', '(\\d+)')", "42")]
    [InlineData("regex('key=val', '(\\w+)=(\\w+)', 2)", "val")]
    [InlineData("coalesce(null, '', 'x')", "x")]
    [InlineData("true ? 'yes' : 'no'", "yes")]
    [InlineData("0 ? 'yes' : 'no'", "no")]
    [InlineData("if(1 > 2, 'a', 'b')", "b")]
    [InlineData("'it\\'s'", "it's")]
    [InlineData("bytes(1610612736)", "1.5 GB")]
    [InlineData("bytes(512)", "512 B")]
    [InlineData("bytes(5 * 1024 * 1024 * 1024)", "5.0 GB")]
    [InlineData("duration(7800)", "2h 10m")]
    [InlineData("duration(90000)", "1d 1h")]
    public void Text(string src, string expected) => Assert.Equal(expected, Assert.IsType<string>(Ev(src)));

    [Fact]
    public void PatternsKeepTheirBackslashes()
        => Assert.Equal("42", Ev("regex('x42', '\\d+')"));   // '\d' blijft '\d' (alleen \\ \' \" \n \t zijn escapes)

    [Fact]
    public void MissingDataIsNull_NotAnError()
    {
        var v = Root("{\"a\": {\"b\": 1}, \"list\": [10, 20, 30]}");
        Assert.Null(Ev("a.x.y", v));
        Assert.Null(Ev("nothing.at.all", v));
        Assert.Null(Ev("list[9]", v));
        Assert.Null(Ev("a.b / 0", v));
        Assert.Null(Ev("a.x * 2", v));   // rekenen met niets geeft niets
        Assert.Equal(1.0, Ev("a.b", v));
        Assert.Equal(20.0, Ev("list[1]", v));
        Assert.Equal(20.0, Ev("root.list[1]", v));
        Assert.Equal(3.0, Ev("count(list)", v));
        Assert.Equal(60.0, Ev("sum(list)", v));
        Assert.Equal(20.0, Ev("avg(list)", v));
        Assert.Equal(30.0, Ev("last(list)", v));
        Assert.Equal(1.0, Ev("a['b']", v));
    }

    [Fact]
    public void ListsAndObjects()
    {
        var v = Root("{\"items\": [{\"n\": \"a\", \"s\": 3, \"ok\": true}, {\"n\": \"b\", \"s\": 5, \"ok\": false}, {\"n\": \"c\", \"s\": 7, \"ok\": true}]}");
        Assert.Equal(15.0, Ev("sum(pluck(items, 's'))", v));
        Assert.Equal(2.0, Ev("count(where(items, 'ok', true))", v));
        Assert.Equal("a,b,c", Ev("join(pluck(items, 'n'), ',')", v));
        Assert.Equal(7.0, Ev("max(pluck(items, 's'))", v));
        Assert.Equal("a", Ev("first(items).n", v));
        Assert.Equal(true, Ev("contains(pluck(items, 'n'), 'b')", v));
        Assert.Equal(5.0, Ev("json('{\"x\": {\"y\": 5}}').x.y", v));
        Assert.Null(Ev("json('not json')", v));
    }

    [Fact]
    public void KeysOfTheRootObjectAreDirectNames_AndRootCanBeAList()
    {
        Assert.Equal(7.0, Ev("price", Root("{\"price\": 7}")));
        Assert.Equal("x", Ev("root[0].name", Root("[{\"name\": \"x\"}]")));
    }

    [Fact]
    public void TimeFunctions()
    {
        long t = Noon.ToUnixTimeSeconds();
        var v = new Dictionary<string, object?> { ["reset"] = Noon.AddMinutes(130).UtcDateTime.ToString("o"), ["ts"] = (double)(t - 3600), ["ms"] = (double)((t - 60) * 1000) };
        Assert.Equal((double)t, Ev("now", v));
        Assert.Equal("2h 10m", Ev("until(reset)", v));
        Assert.Equal("1h 00m", Ev("ago(ts)", v));
        Assert.Equal("1m", Ev("ago(ms)", v));   // milliseconden worden herkend
        Assert.Equal(Noon.ToLocalTime().ToString("HH:mm"), Ev("clock(now, 'HH:mm')", v));
        Assert.Null(Ev("until(null)", v));
    }

    [Theory]
    [InlineData("1 +")]
    [InlineData("(1")]
    [InlineData("foo(")]
    [InlineData("nofunction(1)")]
    [InlineData("round()")]
    [InlineData("1 ? 2")]
    [InlineData("'open")]
    [InlineData("1 $ 2")]
    [InlineData("a.")]
    public void SyntaxErrors_AreReadableExceptions(string src) => Assert.Throws<ExprException>(() => Expr.Parse(src));

    [Fact]
    public void Limits_StopRunawayFormulas()
    {
        Assert.Throws<ExprException>(() => Expr.Parse(new string('1', Expr.MaxLength + 1)));
        Assert.Throws<ExprException>(() => Expr.Parse(new string('(', 100) + "1" + new string(')', 100)));
        // te veel knooppunten
        Assert.Throws<ExprException>(() => Expr.Parse(string.Join(" + ", Enumerable.Repeat("1", 400))));
        // een heel zwaar patroon wordt afgekapt (timeout), niet uitgevoerd tot het eind
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try { Ev("matches('aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa!', '(a+)+$')"); } catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { }
        Assert.True(sw.ElapsedMilliseconds < 3000);
    }

    [Fact]
    public void ThereIsNoWayToTouchFilesNetworkOrCommands()
    {
        // de formuletaal heeft alleen deze functies; geen enkele raakt iets buiten de gegevens
        foreach (var banned in new[] { "read", "file", "exec", "run", "http", "get", "open", "delete", "write", "env", "shell", "cmd" })
            Assert.False(Funcs.Table.ContainsKey(banned), banned);
        Assert.Throws<ExprException>(() => Expr.Parse("exec('calc')"));
    }

    // ---------- opmaak ----------
    [Theory]
    [InlineData("{value:0}", "63")]
    [InlineData("{value:0.0}", "62.5")]
    [InlineData("{value:0.00}%", "62.50%")]
    [InlineData("{value:+0.0}", "+62.5")]
    [InlineData("{neg:+0.0}", "-3.0")]
    [InlineData("{big:#,##0}", "1,234,568")]
    [InlineData("{frac:0%}", "63%")]
    [InlineData("{value > 50 ? 'high' : 'low'}", "high")]
    [InlineData("{value > 50 ? 1 : 0}", "1")]
    [InlineData("{{braces}} and {value:0}", "{braces} and 63")]
    [InlineData("a {name} b", "a Kim b")]
    [InlineData("{missing}", "")]
    [InlineData("[{value}]", "[62.5]")]
    public void Templates(string t, string expected)
    {
        var v = new Dictionary<string, object?> { ["value"] = 62.5, ["neg"] = -3.0, ["big"] = 1234567.8, ["frac"] = 0.625, ["name"] = "Kim" };
        Assert.Equal(expected, Tpl(t, v));
    }

    [Fact]
    public void Templates_RoundHalfAwayFromZero_AndNeverShowMinusZero()
    {
        Assert.Equal("63", Template.FormatNumber(62.5, "0"));
        Assert.Equal("-63", Template.FormatNumber(-62.5, "0"));
        Assert.Equal("0", Template.FormatNumber(-0.2, "0"));
        Assert.Equal("0.0", Template.FormatNumber(-0.04, "0.0"));
    }

    [Fact]
    public void Templates_QuotesAndColonsInsideFormulas()
        => Assert.Equal("a:b", Tpl("{x ? 'a:b' : 'c'}", new() { ["x"] = true }));

    [Theory]
    [InlineData("{value")]
    [InlineData("{1 +}")]
    public void Templates_Errors(string t) => Assert.Throws<ExprException>(() => Tpl(t));

    // ---------- recepten ----------
    private const string PiHole = """
        type = recipe
        url = http://pi.hole/admin/api.php?summaryRaw
        [blocked]
        label = Pi-hole blocked
        value = ads_percentage_today
        unit = %
        format = {value:0.0}%
        detail = {ads_blocked_today:#,##0} of {dns_queries_today:#,##0} blocked
        [status]
        value = status == 'enabled' ? 1 : 0
        format = {status == 'enabled' ? 'on' : 'OFF'}
        level = status == 'enabled' ? 'ok' : 'crit'
        notify = true
        widget = false
        """;

    private const string PiHoleReply = "{\"dns_queries_today\":12345,\"ads_blocked_today\":2469,\"ads_percentage_today\":20.0016,\"status\":\"enabled\"}";

    [Fact]
    public async Task Recipe_PiHole_TwoValuesFromOneReply()
    {
        var h = new FakeHandler { Respond = _ => FakeHandler.Json(PiHoleReply) };
        var r = await Run(PiHole, h);
        Assert.False(r.Error, r.Message);
        Assert.Equal("http://pi.hole/admin/api.php?summaryRaw", h.Urls.Single());
        Assert.Equal(2, r.Items.Count);
        var a = r.Items[0];
        Assert.Equal("blocked", a.Key);
        Assert.Equal("20.0%", a.Text);
        Assert.Equal(20.0016, a.Value!.Value, 4);
        Assert.Equal("2,469 of 12,345 blocked", a.Detail);
        Assert.Equal("%", a.Unit);
        var s = r.Items[1];
        Assert.Equal("on", s.Text);
        Assert.Equal("ok", s.Level);
        Assert.False(s.Widget);
        Assert.True(s.Notify);
    }

    [Fact]
    public async Task Recipe_StatusFieldOfTheReplyIsNotShadowedByTheHttpStatus()
    {
        var h = new FakeHandler { Respond = _ => FakeHandler.Json(PiHoleReply.Replace("enabled", "disabled")) };
        var r = await Run(PiHole, h);
        Assert.Equal("OFF", r.Items[1].Text);
        Assert.Equal("crit", r.Items[1].Level);
    }

    [Fact]
    public async Task Recipe_ThresholdsAndInvert_And_When()
    {
        const string ini = """
            type = recipe
            url = https://x.test/a
            [hi]
            value = a
            warn = 70
            crit = 90
            [lo]
            value = a
            invert = true
            warn = 50
            crit = 20
            [hidden]
            when = a > 100
            value = a
            [shown]
            when = a > 10
            value = a
            """;
        var h = new FakeHandler { Respond = _ => FakeHandler.Json("{\"a\": 95}") };
        var r = await Run(ini, h);
        Assert.Equal(new[] { "hi", "lo", "shown" }, r.Items.Select(i => i.Key));
        Assert.Equal("crit", r.Items[0].Level);
        Assert.Equal("ok", r.Items[1].Level);
        Assert.Equal("95", r.Items[0].Text);   // zonder opmaak: het getal zelf
    }

    [Fact]
    public async Task Recipe_OneBrokenFormulaDoesNotHideTheOthers()
    {
        const string ini = "type = recipe\nurl = https://x.test/a\n[bad]\nvalue = 1 +\n[good]\nvalue = 5\n";
        var r = await Run(ini, new FakeHandler { Respond = _ => FakeHandler.Json("{}") });
        Assert.False(r.Error);
        Assert.Equal("?", r.Items[0].Text);
        Assert.Contains("[bad]", r.Message);
        Assert.Equal("5", r.Items[1].Text);
    }

    [Fact]
    public async Task Recipe_TextLinesAndCsvSources()
    {
        var h = new FakeHandler { Respond = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("build 42 ok\nbuild 43 failed\n") } };
        var r = await Run("type = recipe\nurl = https://x.test/t\n[n]\nvalue = count(lines)\n[b]\nvalue = num(regex(last(lines), '(\\d+)'))\nformat = #{value}\n", h);
        Assert.Equal("2", r.Items[0].Text);
        Assert.Equal("#43", r.Items[1].Text);

        var csv = new FakeHandler { Respond = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("name,size\na,3\nb,5\n") } };
        var r2 = await Run("type = recipe\nurl = https://x.test/c\nparse = csv\ncsv_header = true\n[s]\nvalue = sum(pluck(rows, 'size'))\n", csv);
        Assert.Equal("8", r2.Items[0].Text);
    }

    [Fact]
    public async Task Recipe_FileAndCommandSources()
    {
        var dir = T.TempDir();
        var f = Path.Combine(dir, "status.txt");
        File.WriteAllText(f, "free: 212.5 GB\n");
        var r = await Run($"type = recipe\nfile = {f}\n[free]\nvalue = num(regex(text, '([0-9.]+)'))\nunit = GB\nformat = {{value:0}} GB\n");
        Assert.False(r.Error, r.Message);
        Assert.Equal("213 GB", r.Items[0].Text);

        var r2 = await Run("type = recipe\ncmd = echo 41\n[n]\nvalue = num(text) + 1\n");
        Assert.False(r2.Error, r2.Message);
        Assert.Equal("42", r2.Items[0].Text);
    }

    [Fact]
    public async Task Recipe_ErrorsAreReadable()
    {
        Assert.Contains("no source", (await Run("type = recipe\n[a]\nvalue = 1\n")).Message);
        Assert.Contains("no values", (await Run("type = recipe\nurl = https://x.test/\n")).Message);
        var notJson = await Run("type = recipe\nurl = https://x.test/\n[a]\nvalue = 1\n", new FakeHandler { Respond = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{broken") } });
        Assert.Contains("not JSON", notJson.Message);
        var e500 = await Run("type = recipe\nurl = https://x.test/\n[a]\nvalue = 1\n", new FakeHandler { Respond = _ => FakeHandler.Json("{\"error\":{\"message\":\"database is down\"}}", HttpStatusCode.InternalServerError) });
        Assert.Equal("HTTP 500: database is down", e500.Message);
        var help = await Run("type = recipe\nhelp = Run the tool once.\nurl = https://x.test/\n[a]\nvalue = 1\n", new FakeHandler { Respond = _ => FakeHandler.Json("{}", HttpStatusCode.Forbidden) });
        Assert.EndsWith("Run the tool once.", help.Message);
        var rl = await Run("type = recipe\nurl = https://x.test/\n[a]\nvalue = 1\n", new FakeHandler
        {
            Respond = _ => { var m = FakeHandler.Json("{}", (HttpStatusCode)429); m.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(900)); return m; },
        });
        Assert.Equal(900, rl.RetryAfterSec);
        var big = await Run("type = recipe\nurl = https://x.test/\n[a]\nvalue = 1\n", new FakeHandler { Respond = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new string('x', 3 * 1024 * 1024)) } });
        Assert.Contains("too large", big.Message);
        var ftp = await Run("type = recipe\nurl = ftp://x.test/\n[a]\nvalue = 1\n");
        Assert.Contains("http(s)", ftp.Message);
    }

    // ---------- veiligheid ----------
    [Fact]
    public async Task Recipe_RedirectToAnotherHost_IsRefused_ButTheSameHostIsFollowed()
    {
        int n = 0;
        var other = new FakeHandler
        {
            Respond = _ => { var m = new HttpResponseMessage(HttpStatusCode.MovedPermanently); m.Headers.Location = new Uri("https://evil.test/steal"); n++; return m; },
        };
        var r = await Run("type = recipe\nurl = https://x.test/a\nheader = X-Key: abc\n[a]\nvalue = 1\n", other);
        Assert.True(r.Error);
        Assert.Contains("evil.test", r.Message);
        Assert.Single(other.Urls);   // er is niets naar de andere host gestuurd

        int hops = 0;
        var same = new FakeHandler
        {
            Respond = req =>
            {
                if (hops++ == 0) { var m = new HttpResponseMessage(HttpStatusCode.Moved); m.Headers.Location = new Uri("/v2/a", UriKind.Relative); return m; }
                return FakeHandler.Json("{\"a\": 3}");
            },
        };
        var ok = await Run("type = recipe\nurl = https://x.test/a\n[a]\nvalue = a\n", same);
        Assert.False(ok.Error, ok.Message);
        Assert.Equal(new[] { "https://x.test/a", "https://x.test/v2/a" }, same.Urls);
    }

    [Fact]
    public async Task Recipe_VariableCannotChangeTheHost()
    {
        var r = await Run("type = recipe\nurl = https://{secret:host}/x\n[a]\nvalue = 1\n", secrets: new() { ["host"] = "evil.test" });
        Assert.True(r.Error);
        Assert.Contains("does not list", r.Message);
    }

    [Fact]
    public async Task Recipe_Secrets_AreSentButNeverShown()
    {
        var h = new FakeHandler { Respond = _ => FakeHandler.Json("{\"error\":\"bad token supersecret-12345\"}", HttpStatusCode.Unauthorized) };
        var r = await Run("type = recipe\nurl = https://ha.test/api\nheader = Authorization: Bearer {secret:ha}\n[a]\nvalue = 1\n", h, new() { ["ha"] = "supersecret-12345" });
        Assert.Equal("Bearer supersecret-12345", h.AuthHeaders.Single());
        Assert.True(r.Error);
        Assert.DoesNotContain("supersecret", r.Message);
        Assert.DoesNotContain("supersecret", OutputFile.Render("r", "R", r, "error", 60, Noon));
    }

    [Fact]
    public async Task Recipe_MissingSecretOrFile_ExplainsWhatToDo()
    {
        var r = await Run("type = recipe\nurl = https://x.test/\nheader = Authorization: Bearer {secret:nope}\n[a]\nvalue = 1\n");
        Assert.Contains("'nope' is not set", r.Message);
        Assert.Contains("Secrets", r.Message);
        var f = await Run("type = recipe\nurl = https://x.test/\nheader = X: {file:C:\\does\\not\\exist.json#a}\n[a]\nvalue = 1\n");
        Assert.Contains("not found", f.Message);
    }

    [Fact]
    public async Task Recipe_Claude_ReadsTheTokenFromTheCredentialsFile_AndGivesTheSameNumbersAsTheBuiltInPlugin()
    {
        var dir = T.TempDir();
        var cred = Path.Combine(dir, ".credentials.json");
        File.WriteAllText(cred, "{\"claudeAiOauth\":{\"accessToken\":\"tok-SECRET-123\"},\"mcpOAuth\":{\"x\":{\"accessToken\":\"connector-token\"}}}");
        string reply = "{\"five_hour\":{\"utilization\":62.5,\"resets_at\":\"" + Noon.AddMinutes(130).UtcDateTime.ToString("o") + "\"},\"seven_day\":{\"utilization\":null,\"resets_at\":null},\"seven_day_opus\":null}";
        string tpl = File.ReadAllText(Path.Combine(TemplatesDir(), "claude-recipe.ini")).Replace("~/.claude/.credentials.json", cred.Replace('\\', '/'));
        var h = new FakeHandler { Respond = _ => FakeHandler.Json(reply) };
        var recipe = await Run(tpl, h);
        Assert.False(recipe.Error, recipe.Message);
        Assert.Equal("Bearer tok-SECRET-123", h.AuthHeaders.Single());   // en nooit het connector-token
        Assert.Contains("oauth", h.Betas.Single());

        var built = ClaudePlugin.Parse(reply, IniFile.Parse(""), Noon);
        Assert.Equal(built.Items.Select(i => i.Key), recipe.Items.Select(i => i.Key));
        foreach (var (b, r) in built.Items.Zip(recipe.Items))
        {
            Assert.Equal(b.Text, r.Text);
            Assert.Equal(b.Value, r.Value);
            Assert.Equal(b.Level, r.Level);
            Assert.Equal(b.Short, r.Short);
            Assert.Equal(b.Notify, r.Notify);
        }
        Assert.Contains("free again in 2h 10m", recipe.Items[0].Detail);
        Assert.DoesNotContain("tok-SECRET-123", OutputFile.Render("r", "R", recipe, "ok", 300, Noon));

        // zonder aanmelding: een uitleg in plaats van cijfers
        var none = await Run(tpl.Replace(cred.Replace('\\', '/'), "C:/nonexistent/x.json"));
        Assert.True(none.Error);
        Assert.Contains("/login", none.Message);
    }

    internal static string TemplatesDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var c = Path.Combine(dir.FullName, "plugins", "TaskbarStats.Plugins", "templates");
            if (Directory.Exists(c)) return c;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("templates");
    }

    // ---------- rechten en goedkeuring ----------
    [Fact]
    public void Caps_ListWhatARecipeCanDo()
    {
        var c = Caps.Compute(IniFile.Parse("""
            type = recipe
            url = https://API.Example.com/x?k={secret:key1}
            header = Authorization: Bearer {file:~/.claude/.credentials.json#claudeAiOauth.accessToken}
            header = X: {env:MYVAR}
            cmd = docker ps -q
            file = C:\logs\a.txt
            [one]
            url = http://pi.hole/api
            """));
        Assert.Equal(new[] { "api.example.com", "pi.hole" }, c.Hosts);
        Assert.Contains("docker ps -q", c.Commands);
        Assert.Contains(@"C:\logs\a.txt", c.Files);
        Assert.Contains("~/.claude/.credentials.json", c.Files);
        Assert.Equal(new[] { "key1" }, c.Secrets);
        Assert.Equal(new[] { "MYVAR" }, c.EnvVars);
        Assert.Contains("host:pi.hole", c.Lines());
    }

    [Fact]
    public void Caps_Hash_ChangesWhenWhatTheRecipeCanDoChanges_ButNotForTexts()
    {
        string h(string ini) => Caps.Compute(IniFile.Parse(ini)).Hash();
        string a = "type = recipe\nurl = https://a.test/x\n[v]\nvalue = 1\nlabel = One\n";
        Assert.Equal(h(a), h(a.Replace("One", "Two").Replace("value = 1", "value = 2")));   // teksten en formules: geen nieuwe vraag
        Assert.Equal(h(a), h(a.Replace("/x", "/y")));                                      // zelfde host: dezelfde rechten
        Assert.NotEqual(h(a), h(a.Replace("a.test", "b.test")));
        Assert.NotEqual(h(a), h(a + "cmd = calc\n"));
        Assert.NotEqual(h(a), h(a + "header = A: {secret:x}\n"));
        Assert.NotEqual(h(a), h(a + "header = A: {file:C:/x.json#a}\n"));
    }

    [Fact]
    public void Caps_NeedsApproval_OnlyForRecipesAndScript()
    {
        Assert.True(Caps.NeedsApproval("x", IniFile.Parse("type = recipe")));
        Assert.True(Caps.NeedsApproval("script", IniFile.Parse("enabled = true")));
        Assert.False(Caps.NeedsApproval("weather", IniFile.Parse("enabled = true")));
        Assert.False(Caps.NeedsApproval("claude", IniFile.Parse("enabled = true")));
        Assert.False(Caps.NeedsApproval("ext", IniFile.Parse("type = external")));
    }

    private static (PluginRunner runner, string dir, FakeHandler h) Registered(string id, string ini)
    {
        var dir = T.TempDir();
        Directory.CreateDirectory(Path.Combine(dir, "plugins"));
        File.WriteAllText(Path.Combine(dir, "plugins", id + ".ini"), ini);
        var h = new FakeHandler { Respond = _ => FakeHandler.Json("{\"a\": 4}") };
        return (new PluginRunner(dir, Program.AllPlugins(), T.Http(h), () => T.Noon), dir, h);
    }

    [Fact]
    public async Task Runner_RecipeDoesNotRunUntilApproved_AndNeedsNewApprovalWhenItsRightsChange()
    {
        const string ini = "type = recipe\nenabled = true\ntitle = Mine\nurl = https://a.test/x\n[v]\nvalue = a\n";
        var (runner, dir, h) = Registered("mine", ini);
        await Task.WhenAll(runner.Tick(CancellationToken.None));
        Assert.Empty(h.Urls);   // niets opgehaald
        var (st, _) = PluginFeed.Parse("mine", File.ReadAllText(runner.OutputPath("mine")), DateTime.UtcNow);
        Assert.Equal("error", st!.Status);
        Assert.Contains("approval", st.Message);

        Approvals.Approve(dir, "mine", Caps.Compute(IniFile.Parse(ini)).Hash());
        File.SetLastWriteTimeUtc(runner.ConfigPath("mine"), DateTime.UtcNow.AddMinutes(1));   // de host ziet een wijziging en probeert opnieuw
        await Task.WhenAll(runner.Tick(CancellationToken.None));
        Assert.Single(h.Urls);
        var (st2, vals) = PluginFeed.Parse("mine", File.ReadAllText(runner.OutputPath("mine")), DateTime.UtcNow);
        Assert.Equal("ok", st2!.Status);
        Assert.Equal("4", vals.Single().Text);

        // het recept krijgt er een adres bij: opnieuw goedkeuren
        File.WriteAllText(runner.ConfigPath("mine"), ini.Replace("[v]", "header = X: {secret:k}\n[v]"));
        File.SetLastWriteTimeUtc(runner.ConfigPath("mine"), DateTime.UtcNow.AddMinutes(2));
        h.Urls.Clear();
        await Task.WhenAll(runner.Tick(CancellationToken.None));
        Assert.Empty(h.Urls);
    }

    [Fact]
    public async Task Runner_DiscoversRecipes_ButIgnoresExternalUnknownAndReservedFiles()
    {
        var dir = T.TempDir();
        var pd = Path.Combine(dir, "plugins");
        Directory.CreateDirectory(pd);
        File.WriteAllText(Path.Combine(pd, "r1.ini"), "type = recipe\nenabled = true\n");
        File.WriteAllText(Path.Combine(pd, "ext.ini"), "type = external\nenabled = true\n");
        File.WriteAllText(Path.Combine(pd, "noType.ini"), "enabled = true\n");
        File.WriteAllText(Path.Combine(pd, "weather.ini"), "enabled = true\n");
        File.WriteAllText(Path.Combine(pd, "approved.ini"), "type = recipe\n");
        File.WriteAllText(Path.Combine(pd, "bad name!.ini"), "type = recipe\n");
        var runner = new PluginRunner(dir, Program.AllPlugins(), T.Http(new FakeHandler()), () => T.Noon);
        Assert.Equal(new[] { "r1", "weather" }, runner.Discover().Select(p => p.Id));
        Assert.NotNull(runner.Resolve("r1"));
        Assert.NotNull(runner.Resolve("claude"));   // een meegeleverde plugin is altijd te vinden (--once)
        Assert.Null(runner.Resolve("ext"));
        Assert.Null(runner.Resolve("nothing"));
        Assert.Null(runner.Resolve("..\\x"));
    }

    [Fact]
    public async Task Runner_RemovingTheConfig_RemovesTheOutput()
    {
        var (runner, dir, h) = Registered("mine", "type = recipe\nenabled = true\nurl = https://a.test/x\n[v]\nvalue = a\n");
        Approvals.Approve(dir, "mine", Caps.Compute(IniFile.Load(runner.ConfigPath("mine"))).Hash());
        await Task.WhenAll(runner.Tick(CancellationToken.None));
        Assert.True(File.Exists(runner.OutputPath("mine")));
        File.Delete(runner.ConfigPath("mine"));
        await Task.WhenAll(runner.Tick(CancellationToken.None));
        Assert.False(File.Exists(runner.OutputPath("mine")));
    }

    [Fact]
    public async Task Runner_ScriptPlugin_AlsoNeedsApproval()
    {
        var (runner, dir, _) = Registered("script", "enabled = true\n[x]\ncmd = echo 5\n");
        await Task.WhenAll(runner.Tick(CancellationToken.None));
        var (st, _) = PluginFeed.Parse("script", File.ReadAllText(runner.OutputPath("script")), DateTime.UtcNow);
        Assert.Equal("error", st!.Status);
        Assert.Contains("approval", st.Message);
    }

    [Fact]
    public void Approvals_RoundTrip()
    {
        var dir = T.TempDir();
        Assert.False(Approvals.IsApproved(dir, "a", "h1"));
        Approvals.Approve(dir, "a", "h1");
        Approvals.Approve(dir, "b", "h2");
        Assert.True(Approvals.IsApproved(dir, "a", "h1"));
        Assert.False(Approvals.IsApproved(dir, "a", "other"));
        Approvals.Revoke(dir, "a");
        Assert.False(Approvals.IsApproved(dir, "a", "h1"));
        Assert.True(Approvals.IsApproved(dir, "b", "h2"));
        File.WriteAllText(Approvals.FilePath(dir), "{broken");   // kapot bestand = niets goedgekeurd, geen crash
        Assert.False(Approvals.IsApproved(dir, "b", "h2"));
    }

    // ---------- geheimen ----------
    [Fact]
    public void Secrets_AreEncryptedOnDisk_AndRoundTrip()
    {
        var dir = T.TempDir();
        Secrets.Set(dir, "homeassistant", "eyJ-very-secret-token");
        Secrets.Set(dir, "other", "x-123456");
        var file = File.ReadAllText(Secrets.FilePath(dir));
        Assert.DoesNotContain("very-secret", file);
        Assert.DoesNotContain("eyJ", file);
        Assert.Equal(new[] { "homeassistant", "other" }, Secrets.Names(dir));
        var all = Secrets.LoadAll(dir);
        Assert.Equal("eyJ-very-secret-token", all["homeassistant"]);
        Secrets.Set(dir, "other", "replaced-1");
        Assert.Equal("replaced-1", Secrets.LoadAll(dir)["other"]);
        Assert.True(Secrets.Remove(dir, "other"));
        Assert.False(Secrets.Remove(dir, "other"));
        Assert.Equal(new[] { "homeassistant" }, Secrets.Names(dir));
    }

    [Theory]
    [InlineData("good_name-1", true)]
    [InlineData("", false)]
    [InlineData("has space", false)]
    [InlineData("a/b", false)]
    [InlineData("a{b}", false)]
    public void Secrets_NameRules(string name, bool ok) => Assert.Equal(ok, Secrets.ValidName(name));

    [Fact]
    public void Secrets_ABrokenEntryIsSkipped_NotACrash()
    {
        var dir = T.TempDir();
        Secrets.Set(dir, "good", "value-1");
        File.AppendAllText(Secrets.FilePath(dir), "bad\tnot-base64!!\n");
        Assert.Equal("value-1", Secrets.LoadAll(dir)["good"]);
        Assert.False(Secrets.LoadAll(dir).ContainsKey("bad"));
    }

    [Fact]
    public void Scrub_RemovesGivenSecretValues()
        => Assert.Equal("failed with *** and ***", Fmt.Scrub("failed with abc-secret-1 and xyz-secret-2", new[] { "abc-secret-1", "xyz-secret-2" }));

    // ---------- sjablonen ----------
    public static IEnumerable<object[]> TemplateFiles() => Directory.GetFiles(TemplatesDir(), "*.ini").Select(f => new object[] { Path.GetFileName(f) });

    [Theory]
    [MemberData(nameof(TemplateFiles))]
    public void Template_IsValid(string file)
    {
        string text = File.ReadAllText(Path.Combine(TemplatesDir(), file));
        var ini = IniFile.Parse(text);
        Assert.False(string.IsNullOrWhiteSpace(ini.Str("template_name", "")), "template_name");
        Assert.True(ini.Str("template_desc", "").Length > 30, "template_desc");
        Assert.False(ini.Enabled, "a template starts switched off");
        Assert.Null(IniFile.Validate(text));
        string type = ini.Str("type", "");
        Assert.Contains(type, new[] { "recipe", "external" });
        Assert.False(text.Contains("sk-ant-") || text.Contains("Bearer ey"), "no secrets in a template");
        if (type == "external") return;
        // alle formules moeten lezen (syntaxis); ze worden uitgerekend in Template_RunsAgainstItsOwnExampleReply
        foreach (var s in ini.Sections)
        {
            foreach (var (k, v) in s.Entries)
            {
                if (k is "value" or "when" or "level") Expr.Parse(v);
                else if (k is "label" or "short" or "format" or "text" or "detail" or "color") Template.Parse(v);
            }
        }
        Assert.NotEmpty(ini.Sections);
        var caps = Caps.Compute(ini);
        Assert.False(caps.IsEmpty);
        Assert.Matches("^[a-z0-9-]+\\.ini$", file);
    }

    [Fact]
    public void Templates_AllShippedOnes_Exist()
    {
        var names = Directory.GetFiles(TemplatesDir(), "*.ini").Select(Path.GetFileNameWithoutExtension).ToHashSet();
        foreach (var n in new[] { "claude-recipe", "pihole", "homeassistant", "github-stars", "crypto-price", "exchange-rate", "service-status", "air-quality", "docker-running", "disk-free", "http-json", "text-file", "external-json" })
            Assert.Contains(n, names);
    }

    private static string Reply(string template) => template switch
    {
        "pihole" => PiHoleReply,
        "homeassistant" => "{\"entity_id\":\"sensor.living_room_temperature\",\"state\":\"21.46\",\"attributes\":{\"friendly_name\":\"Living room\",\"unit_of_measurement\":\"°C\"},\"last_changed\":\"" + Noon.AddMinutes(-5).UtcDateTime.ToString("o") + "\"}",
        "github-stars" => "{\"full_name\":\"owner/repo\",\"stargazers_count\":1234,\"forks_count\":56,\"open_issues_count\":7}",
        "crypto-price" => "{\"bitcoin\":{\"eur\":68183,\"eur_24h_change\":-6.2}}",
        "exchange-rate" => "{\"amount\":1.0,\"base\":\"EUR\",\"date\":\"2026-11-06\",\"rates\":{\"USD\":1.1165}}",
        "service-status" => "{\"page\":{\"name\":\"GitHub\"},\"status\":{\"indicator\":\"minor\",\"description\":\"Partial outage\"}}",
        "air-quality" => "{\"current\":{\"time\":\"2026-11-07T07:00\",\"pm2_5\":20.3,\"european_aqi\":85}}",
        "http-json" => "{\"price\": 12.34}",
        _ => "{}",
    };

    [Theory]
    [InlineData("pihole")]
    [InlineData("homeassistant")]
    [InlineData("github-stars")]
    [InlineData("crypto-price")]
    [InlineData("exchange-rate")]
    [InlineData("service-status")]
    [InlineData("air-quality")]
    [InlineData("http-json")]
    public async Task Template_RunsAgainstItsOwnExampleReply(string name)
    {
        string text = File.ReadAllText(Path.Combine(TemplatesDir(), name + ".ini"));
        var h = new FakeHandler { Respond = _ => FakeHandler.Json(Reply(name)) };
        var r = await Run(text, h, new() { ["homeassistant"] = "tok-for-test-1" });
        Assert.False(r.Error, name + ": " + r.Message);
        Assert.True(r.Message == "", name + ": " + r.Message);   // geen enkele formule faalt
        Assert.NotEmpty(r.Items);
        Assert.All(r.Items, i => Assert.NotEqual("?", i.Text));
        Assert.All(r.Items, i => Assert.DoesNotContain("{", i.Text + i.Detail));

        switch (name)
        {
            case "pihole": Assert.Equal("20.0%", r.Items[0].Text); break;
            case "homeassistant": Assert.Equal("21.5°", r.Items[0].Text); Assert.Equal("Bearer tok-for-test-1", h.AuthHeaders.Single()); Assert.Contains("5m ago", r.Items[0].Detail); break;
            case "github-stars": Assert.Equal("1,234", r.Items[0].Text); break;
            case "crypto-price": Assert.Equal("€68,183", r.Items[0].Text); Assert.Equal("-6.2%", r.Items[1].Text); Assert.Equal("warn", r.Items[1].Level); break;
            case "exchange-rate": Assert.Equal("1.1165", r.Items[0].Text); break;
            case "service-status": Assert.Equal("MINOR", r.Items[0].Text); Assert.Equal("warn", r.Items[0].Level); break;
            case "air-quality": Assert.Equal("85", r.Items[0].Text); Assert.Equal("crit", r.Items[0].Level); break;
            case "http-json": Assert.Equal("12.3", r.Items[0].Text); break;
        }
    }

    [Fact]
    public async Task Template_DiskFree_And_TextFile_WorkOnThisPc()
    {
        // docker en een eigen bestand zijn er niet op elke pc; de schijf-opdracht en het tekstbestand wel
        string disk = File.ReadAllText(Path.Combine(TemplatesDir(), "disk-free.ini"));
        var r = await Run(disk);
        Assert.False(r.Error, r.Message);
        Assert.Matches("^\\d+ GB$", r.Items[0].Text);
        Assert.Contains(r.Items[0].Level, new[] { "ok", "warn", "crit" });

        var f = Path.Combine(T.TempDir(), "s.txt");
        File.WriteAllText(f, "Backup finished: 17 files\n");
        string tf = File.ReadAllText(Path.Combine(TemplatesDir(), "text-file.ini")).Replace("%USERPROFILE%\\status.txt", f);
        var r2 = await Run(tf);
        Assert.False(r2.Error, r2.Message);
        Assert.Equal("17", r2.Items[0].Text);
        Assert.Contains("Backup finished", r2.Items[0].Detail);
    }
}
