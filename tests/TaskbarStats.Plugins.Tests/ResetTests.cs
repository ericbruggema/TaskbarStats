using System.Text.Json;
using Xunit;

namespace TaskbarStats.Plugins.Tests;

/// <summary>De terugzettijd van een waarde (<c>reset</c>): de app kan kort van tevoren melden dat een limiet bijna vrij is.</summary>
public class ResetTests
{
    private static readonly DateTimeOffset Noon = T.Noon;

    [Fact]
    public void BuiltInClaude_GivesTheResetTimeOfEachLimit_AndItEndsUpInTheOutputFile()
    {
        string json = "{\"five_hour\":{\"utilization\":50,\"resets_at\":\"" + Noon.AddMinutes(90).UtcDateTime.ToString("o") + "\"},\"seven_day\":{\"utilization\":null,\"resets_at\":null}}";
        var r = ClaudePlugin.Parse(json, IniFile.Parse(""), Noon);
        Assert.Equal(Noon.AddMinutes(90).ToUnixTimeSeconds(), r.Items[0].Reset!.Value.ToUnixTimeSeconds());
        Assert.Null(r.Items[1].Reset);
        using var doc = JsonDocument.Parse(OutputFile.Render("claude", "Claude", r, "ok", 300, Noon));
        var items = doc.RootElement.GetProperty("items");
        Assert.Equal(Noon.AddMinutes(90).UtcDateTime, items[0].GetProperty("reset").GetDateTime().ToUniversalTime());
        Assert.False(items[1].TryGetProperty("reset", out _));   // geen tijd: geen veld
    }

    [Fact]
    public async Task Recipe_ResetFormula_GivesATime_FromAnIsoTextOrANumber()
    {
        var h = new FakeHandler { Respond = _ => FakeHandler.Json("{\"at\":\"" + Noon.AddMinutes(20).UtcDateTime.ToString("o") + "\",\"epoch\":" + Noon.AddMinutes(40).ToUnixTimeSeconds() + ",\"v\":5}") };
        var ctx = new PluginContext
        {
            Config = IniFile.Parse("type = recipe\nurl = https://x.test/\n[a]\nvalue = v\nreset = at\n[b]\nvalue = v\nreset = epoch\n[c]\nvalue = v\n[d]\nvalue = v\nreset = nothing\n"),
            Http = T.Http(h), DataDir = T.TempDir(), Now = () => Noon,
        };
        var r = await new RecipePlugin("r", "R").RunAsync(ctx, CancellationToken.None);
        Assert.False(r.Error, r.Message);
        Assert.Equal(Noon.AddMinutes(20).ToUnixTimeSeconds(), r.Items[0].Reset!.Value.ToUnixTimeSeconds());
        Assert.Equal(Noon.AddMinutes(40).ToUnixTimeSeconds(), r.Items[1].Reset!.Value.ToUnixTimeSeconds());
        Assert.Null(r.Items[2].Reset);
        Assert.Null(r.Items[3].Reset);   // een formule zonder uitkomst: geen tijd, geen fout
    }

    [Fact]
    public void TheClaudeTemplate_Sets_ResetForBothLimits()
    {
        var ini = IniFile.Parse(File.ReadAllText(Path.Combine(RecipeTests.TemplatesDir(), "claude-recipe.ini")));
        Assert.Equal("five_hour.resets_at", ini.Sections.Single(s => s.Name == "five_hour").Get("reset"));
        Assert.Equal("seven_day.resets_at", ini.Sections.Single(s => s.Name == "seven_day").Get("reset"));
    }
}
