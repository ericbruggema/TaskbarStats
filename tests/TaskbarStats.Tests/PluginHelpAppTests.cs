using Xunit;

namespace TaskbarStats.Tests;

/// <summary>Het hulpvenster van de plugins (uitleg en voorbeelden) en de efficiëntere leesroutes van het register.</summary>
public class PluginHelpAppTests : IDisposable
{
    private readonly TempData _data = new();
    public void Dispose() { PluginFeed.Reset(); _data.Dispose(); }

    [Fact]
    public void OwnProgramExample_IsReadByTheApp()
    {
        var (st, vals) = PluginFeed.Parse("mytool", PluginHelp.OwnProgramJson, DateTime.UtcNow);
        Assert.NotNull(st);
        Assert.Equal("ok", st!.Status);
        var v = Assert.Single(vals);
        Assert.Equal("12", v.Text);
        Assert.Equal("warn", v.Level);
        Assert.Equal(24.0, v.Percent!.Value, 6);   // 12 van 0..50
    }

    [Fact]
    public void HelpWindow_Builds_WithAllTabs()
    {
        Exception? err = null; int tabs = 0;
        var t = new Thread(() =>
        {
            try
            {
                using var f = new PluginHelpForm();
                f.CreateControl();
                tabs = f.Controls.OfType<TabControl>().Single().TabPages.Count;
            }
            catch (Exception e) { err = e; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start(); t.Join();
        Assert.Null(err);
        Assert.Equal(4, tabs);
    }

    [Fact]
    public void Entries_ReadEachConfigOnce_InTheSameOrderAsIds()
    {
        string dir = Path.Combine(_data.Dir, "plugins");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "zz.ini"), "type = recipe\nenabled = true\n");
        File.WriteAllText(Path.Combine(dir, "weather.ini"), "enabled = false\n");
        File.WriteAllText(Path.Combine(dir, "aa.ini"), "type = external\n");
        File.WriteAllText(Path.Combine(dir, "junk.ini"), "# not a plugin\n");   // geen type: geen plugin
        var entries = PluginRegistry.Entries(_data.Dir);
        Assert.Equal(new[] { "weather", "aa", "zz" }, entries.Select(e => e.Id).ToArray());
        Assert.Equal(PluginRegistry.Ids(_data.Dir), entries.Select(e => e.Id).ToList());
        Assert.Contains("enabled = true", entries.Single(e => e.Id == "zz").Text);
    }

    [Fact]
    public void Signature_ChangesWithAConfigOrAnApproval_NotWithAnOutputFile()
    {
        string dir = Path.Combine(_data.Dir, "plugins");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "a.ini"), "type = recipe\n");
        string s1 = PluginRegistry.Signature(_data.Dir);
        Assert.Equal(s1, PluginRegistry.Signature(_data.Dir));
        File.WriteAllText(Path.Combine(dir, "a.json"), "{}");   // uitvoer van de host: telt niet mee
        Assert.Equal(s1, PluginRegistry.Signature(_data.Dir));
        File.WriteAllText(Path.Combine(dir, "a.ini"), "type = recipe\nenabled = true\n");
        Assert.NotEqual(s1, PluginRegistry.Signature(_data.Dir));
        string s2 = PluginRegistry.Signature(_data.Dir);
        File.WriteAllText(Path.Combine(dir, "approved.json"), "{}");
        Assert.NotEqual(s2, PluginRegistry.Signature(_data.Dir));
    }
}
