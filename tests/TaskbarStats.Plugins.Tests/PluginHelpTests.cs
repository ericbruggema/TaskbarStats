extern alias app;
using PluginHelp = app::TaskbarStats.PluginHelp;
using Xunit;

namespace TaskbarStats.Plugins.Tests;

/// <summary>
/// De uitleg en voorbeelden in het hulpvenster van de app (Instellingen > Plugins > Help en voorbeelden) worden hier echt uitgevoerd:
/// wat daar als resultaat staat, is wat de formuletaal en de recepten werkelijk geven. Zo kan de uitleg niet vanzelf afwijken.
/// </summary>
public class PluginHelpTests
{
    private static readonly DateTimeOffset Noon = T.Noon;

    private static Dictionary<string, object?> Root(string json)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return new Dictionary<string, object?> { ["root"] = Expr.FromJson(doc.RootElement) };
    }

    public static IEnumerable<object[]> FormulaCases()
        => PluginHelp.Formulas().Select(f => new object[] { f.Where + ": " + f.Text });

    [Fact]
    public void Formulas_GiveExactlyWhatTheHelpSays()
    {
        var vars = Root(PluginHelp.SampleJson);
        var wrong = new List<string>();
        foreach (var e in PluginHelp.Formulas())
        {
            string got;
            try { got = Template.Parse(PluginHelp.TemplateOf(e)).Render(new ExprEnv { Vars = vars, Now = () => Noon }); }
            catch (ExprException ex) { got = "ERROR " + ex.Message; }
            if (got != e.Gives) wrong.Add($"{e.Text}  =>  '{got}' (help says '{e.Gives}')");
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    [Fact]
    public void Formulas_AreNotEmpty_AndEveryOneHasAnExplanation()
    {
        var list = PluginHelp.Formulas();
        Assert.True(list.Count >= 15);
        Assert.All(list, e => { Assert.False(string.IsNullOrWhiteSpace(e.Meaning)); Assert.False(string.IsNullOrWhiteSpace(e.Gives)); });
    }

    [Fact]
    public async Task Recipes_RunOnTheirReply_AndShowWhatTheHelpSays()
    {
        var wrong = new List<string>();
        foreach (var e in PluginHelp.Recipes())
        {
            var ini = IniFile.Parse(e.Recipe);
            Assert.Equal("recipe", ini.Str("type", ""));
            // elk geheim dat het recept noemt, krijgt een nepwaarde
            var secrets = Caps.Compute(ini).Secrets.ToDictionary(s => s, _ => "x");
            var h = new FakeHandler { Respond = _ => FakeHandler.Json(e.Reply) };
            var ctx = new PluginContext { Config = ini, Http = T.Http(h), DataDir = T.TempDir(), Now = () => Noon, Secrets = secrets };
            var r = await new RecipePlugin("r", "R").RunAsync(ctx, CancellationToken.None);
            if (r.Error) { wrong.Add($"{e.Title}: error '{r.Message}'"); continue; }
            string got = string.Join("\n", r.Items.Where(i => i.Widget).Select(i => $"{i.Short ?? i.Label}: {i.Text}" + (i.Level is "warn" or "crit" ? $" ({i.Level})" : "")));
            if (got != e.Result) wrong.Add($"{e.Title}: got '{got}', help says '{e.Result}'");
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    [Fact]
    public void Recipes_NeverContainAnActualSecret_AndStartSwitchedOffOrAreHarmless()
    {
        foreach (var e in PluginHelp.Recipes())
        {
            Assert.DoesNotContain("sk-", e.Recipe);
            Assert.DoesNotContain("Bearer ey", e.Recipe);
            // een geheim staat er alleen als {secret:naam}
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(e.Recipe, @"Bearer (\S+)"))
                Assert.StartsWith("{secret:", m.Groups[1].Value);
        }
    }

    [Fact]
    public void HowItWorks_HasEveryStep()
    {
        string t = PluginHelp.HowItWorks();
        foreach (var n in new[] { "1.", "2.", "3.", "4.", "5." }) Assert.Contains(n, t);
    }
}
