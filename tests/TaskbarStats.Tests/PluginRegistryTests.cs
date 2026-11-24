using Xunit;

namespace TaskbarStats.Tests;

/// <summary>Plugins toevoegen/verwijderen (registreren), de galerij, goedkeuring en de meldingsregels.</summary>
public class PluginRegistryTests : IDisposable
{
    private readonly TempData _data = new();
    public void Dispose() { PluginFeed.Reset(); _data.Dispose(); }

    private string Ini(string id) => Path.Combine(_data.Dir, "plugins", id + ".ini");

    // ---------- registreren ----------
    [Fact]
    public void NothingExists_UntilItIsAdded()
    {
        Assert.Empty(PluginRegistry.Ids(_data.Dir));
        Assert.False(PluginRegistry.IsRegistered("claude", _data.Dir));
        // een uitvoerbestand zonder toegevoegde plugin is niet te zien
        _data.Write("plugins/claude.json", "{\"plugin\":\"claude\",\"items\":[{\"key\":\"a\",\"text\":\"1\"}]}");
        Assert.Empty(PluginFeed.Read(_data.Dir).Values);
    }

    [Fact]
    public void Add_WritesTheConfig_SwitchedOff_AndWithoutTemplateLines()
    {
        const string tpl = "template_name = Mine\ntemplate_desc = A long enough description of the template.\n\n# my recipe\ntype = recipe\ntitle = Mine\nenabled = true\nurl = https://a.test/x\n[v]\nvalue = 1\n";
        PluginRegistry.Add("mine", tpl, _data.Dir);
        string text = File.ReadAllText(Ini("mine"));
        Assert.DoesNotContain("template_", text);
        Assert.Contains("type = recipe", text);
        Assert.False(PluginConfig.IsEnabledIn(text));   // een nieuwe plugin begint uit
        Assert.Equal(new[] { "mine" }, PluginRegistry.Ids(_data.Dir));
        Assert.Equal("recipe", PluginRegistry.TypeOf("mine", _data.Dir));
        Assert.Throws<IOException>(() => PluginRegistry.Add("mine", tpl, _data.Dir));   // al toegevoegd
        Assert.Throws<ArgumentException>(() => PluginRegistry.Add("bad name!", tpl, _data.Dir));
        Assert.Throws<ArgumentException>(() => PluginRegistry.Add("approved", tpl, _data.Dir));   // gereserveerd
    }

    [Fact]
    public void Ids_BuiltInsFirst_ThenByName_AndOnlyRealPlugins()
    {
        PluginRegistry.Add("zeta", "type = recipe\n", _data.Dir);
        PluginRegistry.Add("alpha", "type = external\n", _data.Dir);
        PluginRegistry.Add("weather", PluginConfig.DefaultText("weather"), _data.Dir);
        PluginRegistry.Add("claude", PluginConfig.DefaultText("claude"), _data.Dir);
        _data.Write("plugins/notes.ini", "just = a file\n");   // geen plugin: geen type
        Assert.Equal(new[] { "claude", "weather", "alpha", "zeta" }, PluginRegistry.Ids(_data.Dir));
        Assert.Equal("", PluginRegistry.TypeOf("notes", _data.Dir));
        Assert.Equal("builtin", PluginRegistry.TypeOf("claude", _data.Dir));
        Assert.Equal("external", PluginRegistry.TypeOf("alpha", _data.Dir));
    }

    [Fact]
    public void Remove_DeletesConfigOutputAndApproval_ButNotSecrets()
    {
        const string ini = "type = recipe\nenabled = true\nurl = https://a.test/x\n[v]\nvalue = 1\n";
        PluginRegistry.Add("mine", ini, _data.Dir);
        _data.Write("plugins/mine.json", "{}");
        PluginRegistry.Approve("mine", ini, _data.Dir);
        TaskbarStats.Plugins.Secrets.Set(_data.Dir, "k", "value-123");
        PluginRegistry.Remove("mine", _data.Dir);
        Assert.False(File.Exists(Ini("mine")));
        Assert.False(File.Exists(Path.Combine(_data.Dir, "plugins", "mine.json")));
        Assert.False(TaskbarStats.Plugins.Approvals.Load(_data.Dir).ContainsKey("mine"));
        Assert.Equal(new[] { "k" }, TaskbarStats.Plugins.Secrets.Names(_data.Dir));   // een geheim kan door een ander recept gebruikt worden
        Assert.Empty(PluginRegistry.Ids(_data.Dir));
    }

    [Theory]
    [InlineData("Pi-hole", "pi-hole")]
    [InlineData("  My  Recipe!! ", "my-recipe")]
    [InlineData("../evil", "evil")]
    [InlineData("", "plugin")]
    [InlineData("approved", "approved-2")]
    public void UniqueId_GivesAValidFreeId(string wish, string expected) => Assert.Equal(expected, PluginRegistry.UniqueId(wish, _data.Dir));

    [Fact]
    public void UniqueId_AddsANumberWhenTaken()
    {
        PluginRegistry.Add("mine", "type = recipe\n", _data.Dir);
        Assert.Equal("mine-2", PluginRegistry.UniqueId("mine", _data.Dir));
        PluginRegistry.Add("mine-2", "type = recipe\n", _data.Dir);
        Assert.Equal("mine-3", PluginRegistry.UniqueId("Mine", _data.Dir));
    }

    [Fact]
    public void CheckImport_AcceptsRecipesAndRefusesOtherFiles()
    {
        Assert.Null(PluginRegistry.CheckImport("x", "type = recipe\nurl = https://a.test/\n"));
        Assert.Null(PluginRegistry.CheckImport("x", "type = external\n"));
        Assert.NotNull(PluginRegistry.CheckImport("x", "enabled = true\n"));          // geen type
        Assert.NotNull(PluginRegistry.CheckImport("x", "type = recipe\nthis is wrong\n"));   // geen geldig configbestand
    }

    // ---------- galerij ----------
    private static string TemplatesDir()
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

    [Fact]
    public void Gallery_ListsTheBuiltInPluginsAndTheShippedRecipes()
    {
        // de testuitvoer heeft de sjablonen niet naast de exe: laat de galerij ze zien via de map met de bronnen
        var t = PluginRegistry.Templates();
        Assert.Equal(PluginCatalog.Ids, t.Where(x => x.BuiltIn).Select(x => x.Id));
        Assert.All(t.Where(x => x.BuiltIn), x => Assert.False(PluginConfig.IsEnabledIn(x.Text)));
    }

    [Fact]
    public void EveryShippedTemplate_CanBeAdded_StartsSwitchedOff_AndIsNeverApprovedBeforehand()
    {
        foreach (var f in Directory.GetFiles(TemplatesDir(), "*.ini"))
        {
            string id = Path.GetFileNameWithoutExtension(f);
            string text = File.ReadAllText(f);
            Assert.Null(PluginRegistry.CheckImport(id, text));
            PluginRegistry.Add(id, text, _data.Dir);
            string added = File.ReadAllText(Ini(id));
            Assert.False(PluginConfig.IsEnabledIn(added), id);
            Assert.DoesNotContain("template_", added);
            bool needs = PluginRegistry.NeedsApproval(id, added);
            Assert.True(needs == !PluginRegistry.IsApproved(id, added, _data.Dir), id);   // wat goedkeuring vraagt, is niet vooraf goedgekeurd
        }
    }

    // ---------- goedkeuring ----------
    [Fact]
    public void Approval_ListsWhatItAllows_AndIsPerExactList()
    {
        const string ini = "type = recipe\nenabled = true\nurl = https://api.test/x\nheader = A: {secret:key1}\ncmd = docker ps -q\n[v]\nvalue = 1\n";
        var caps = PluginRegistry.DescribeCaps(ini);
        Assert.Contains(caps, c => c.What == "api.test" && !c.Danger);
        Assert.Contains(caps, c => c.What == "docker ps -q" && c.Danger);   // opdrachten vallen op
        Assert.Contains(caps, c => c.What == "key1");
        Assert.True(PluginRegistry.NeedsApproval("mine", ini));
        Assert.False(PluginRegistry.IsApproved("mine", ini, _data.Dir));
        PluginRegistry.Approve("mine", ini, _data.Dir);
        Assert.True(PluginRegistry.IsApproved("mine", ini, _data.Dir));
        Assert.True(PluginRegistry.IsApproved("mine", ini.Replace("value = 1", "value = 2"), _data.Dir));   // alleen een formule: dezelfde rechten
        Assert.False(PluginRegistry.IsApproved("mine", ini.Replace("api.test", "other.test"), _data.Dir));  // ander adres: opnieuw vragen
        Assert.False(PluginRegistry.NeedsApproval("weather", "enabled = true\n"));   // meegeleverde plugins vragen niets
    }

    // ---------- meldingen ----------
    private static PluginValue V(string level = "crit", string detail = "98% used, free again in 3h 36m", DateTime? reset = null, bool notify = true, string id = "five_hour", string status = "ok")
        => new("claude", "Claude usage", id, "Claude 5-hour limit", "5h", 98, 0, 100, "%", "98%", detail, level, "", true, notify, status, "", DateTime.UtcNow, 900, reset);

    private static readonly DateTime T0 = new(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);

    private PluginAlertMemory Mem() => new(_data.Dir);

    [Fact]
    public void Alert_ComesOnce_NotEveryFiveMinutes_EvenWhenTheCountdownTextChanges()
    {
        var mem = Mem();
        var cfg = new AppSettings();
        var first = PluginAlertRules.Evaluate(new[] { V() }, cfg, mem, T0);
        Assert.Single(first);
        Assert.True(first[0].Warning);
        // elke volgende meting (andere afteltekst, 5 minuten later) is dezelfde toestand: geen nieuwe melding
        for (int i = 1; i <= 12; i++)
            Assert.Empty(PluginAlertRules.Evaluate(new[] { V(detail: $"98% used, free again in {216 - 5 * i}m") }, cfg, mem, T0.AddMinutes(5 * i)));
    }

    [Fact]
    public void Alert_IsRememberedAcrossARestart()
    {
        var cfg = new AppSettings();
        Assert.Single(PluginAlertRules.Evaluate(new[] { V() }, cfg, Mem(), T0));
        Assert.Empty(PluginAlertRules.Evaluate(new[] { V() }, cfg, Mem(), T0.AddMinutes(10)));   // nieuw geheugenobject = herstart van de app
    }

    [Fact]
    public void Alert_RepeatsOnlyWhenTheUserAsksFor_ItAndEnoughTimeHasPassed()
    {
        var mem = Mem();
        var cfg = new AppSettings { PluginAlertRepeatMin = 60 };
        Assert.Single(PluginAlertRules.Evaluate(new[] { V() }, cfg, mem, T0));
        Assert.Empty(PluginAlertRules.Evaluate(new[] { V() }, cfg, mem, T0.AddMinutes(30)));
        Assert.Single(PluginAlertRules.Evaluate(new[] { V() }, cfg, mem, T0.AddMinutes(61)));
        Assert.Empty(PluginAlertRules.Evaluate(new[] { V() }, cfg, mem, T0.AddMinutes(90)));
    }

    [Fact]
    public void Alert_ANewProblemOrARecoveredStateGivesANewAlert()
    {
        var mem = Mem();
        var cfg = new AppSettings();
        string two(string bad) => "✓ A  1 ms\n" + bad;
        Assert.Single(PluginAlertRules.Evaluate(new[] { V(detail: two("✕ B  timeout"), id: "summary") }, cfg, mem, T0));
        Assert.Empty(PluginAlertRules.Evaluate(new[] { V(detail: two("✕ B  timeout"), id: "summary") }, cfg, mem, T0.AddMinutes(5)));
        // een tweede doel valt uit: andere toestand, wel melden
        Assert.Single(PluginAlertRules.Evaluate(new[] { V(detail: two("✕ B  timeout\n✕ C  HTTP 500"), id: "summary") }, cfg, mem, T0.AddMinutes(10)));
        // weer in orde, en later opnieuw kritiek: weer een melding
        Assert.Empty(PluginAlertRules.Evaluate(new[] { V(level: "ok", id: "summary") }, cfg, mem, T0.AddMinutes(15)));
        Assert.Single(PluginAlertRules.Evaluate(new[] { V(detail: two("✕ B  timeout"), id: "summary") }, cfg, mem, T0.AddMinutes(20)));
    }

    [Fact]
    public void Alert_NotForStaleHiddenOrQuietValues()
    {
        var cfg = new AppSettings();
        Assert.Empty(PluginAlertRules.Evaluate(new[] { V(status: "error") }, cfg, Mem(), T0));   // verouderd
        Assert.Empty(PluginAlertRules.Evaluate(new[] { V(notify: false) }, cfg, Mem(), T0));
        Assert.Empty(PluginAlertRules.Evaluate(new[] { V(level: "warn") }, cfg, Mem(), T0));
        cfg.PluginCfg("claude/five_hour").Show = false;
        Assert.Empty(PluginAlertRules.Evaluate(new[] { V() }, cfg, Mem(), T0));
    }

    [Fact]
    public void ResetSoon_IsAnnouncedOncePerResetMoment_OnlyShortBeforeAndOnlyForHighValues()
    {
        var mem = Mem();
        var cfg = new AppSettings { PluginResetNotifyMin = 5 };
        var reset = T0.AddMinutes(30);
        Assert.Empty(PluginAlertRules.Evaluate(new[] { V(level: "warn", reset: reset) }, cfg, mem, T0));               // nog 30 min: te vroeg
        var soon = PluginAlertRules.Evaluate(new[] { V(level: "warn", reset: reset) }, cfg, mem, T0.AddMinutes(26));  // nog 4 min
        Assert.Single(soon);
        Assert.False(soon[0].Warning);
        Assert.Contains("4", soon[0].Text);
        Assert.Empty(PluginAlertRules.Evaluate(new[] { V(level: "warn", reset: reset) }, cfg, mem, T0.AddMinutes(28)));   // al gemeld
        Assert.Empty(PluginAlertRules.Evaluate(new[] { V(level: "warn", reset: reset) }, cfg, mem, T0.AddMinutes(31)));   // al voorbij
        // het volgende terugzetmoment (5 uur later) is weer een nieuwe melding
        Assert.Single(PluginAlertRules.Evaluate(new[] { V(level: "warn", reset: reset.AddHours(5)) }, cfg, mem, reset.AddHours(5).AddMinutes(-3)));
    }

    [Fact]
    public void ResetSoon_CanBeSwitchedOff_AndIsNotNeededWhenTheValueIsLow()
    {
        var reset = T0.AddMinutes(3);
        Assert.Empty(PluginAlertRules.Evaluate(new[] { V(level: "warn", reset: reset) }, new AppSettings { PluginResetNotifyMin = 0 }, Mem(), T0));
        Assert.Empty(PluginAlertRules.Evaluate(new[] { V(level: "ok", reset: reset) }, new AppSettings { PluginResetNotifyMin = 5 }, Mem(), T0));   // weinig gebruikt: niets te melden
        Assert.Empty(PluginAlertRules.Evaluate(new[] { V(level: "warn", reset: null) }, new AppSettings { PluginResetNotifyMin = 5 }, Mem(), T0));
    }

    [Fact]
    public void Feed_ReadsTheResetTime()
    {
        string json = "{\"plugin\":\"claude\",\"status\":\"ok\",\"ttl\":900,\"items\":[{\"key\":\"five_hour\",\"label\":\"5h\",\"value\":98,\"reset\":\"2026-11-07T12:30:00.0000000Z\"},{\"key\":\"x\",\"label\":\"X\",\"text\":\"1\"}]}";
        var (_, vals) = PluginFeed.Parse("claude", json, DateTime.UtcNow);
        Assert.Equal(new DateTime(2026, 11, 7, 12, 30, 0, DateTimeKind.Utc), vals[0].ResetUtc);
        Assert.Null(vals[1].ResetUtc);
    }

    [Fact]
    public void Settings_AlertDefaults_AreQuiet()
    {
        var c = new AppSettings();
        Assert.Equal(0, c.PluginAlertRepeatMin);     // niet herhalen
        Assert.Equal(5, c.PluginResetNotifyMin);      // wel melden dat een limiet bijna vrij is
    }
}
