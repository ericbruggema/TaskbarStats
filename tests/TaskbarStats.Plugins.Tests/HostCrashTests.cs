using TaskbarStats.Plugins;
using Xunit;

namespace TaskbarStats.Plugins.Tests;

/// <summary>Een fout in de plugin-host komt met volledige stack in host.log en als rapport in &lt;datamap&gt;\crashes (zonder tokens).</summary>
public class HostCrashTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tshost-" + Guid.NewGuid().ToString("N"));
    private readonly string? _oldPath = HostLog.Path;

    public HostCrashTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "plugins"));
        HostLog.Path = Path.Combine(_dir, "plugins", "host.log");
    }

    public void Dispose()
    {
        HostLog.Path = _oldPath;
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private static Exception Boom(string msg)
    {
        try { throw new InvalidOperationException(msg); }
        catch (Exception e) { return e; }
    }

    [Fact]
    public void WriteCrash_SavesReportInTheSharedCrashFolder_AndTheLog()
    {
        HostLog.WriteCrash("FATAL error (the plugin host stopped)", Boom("host-kapot"));
        var file = Assert.Single(Directory.GetFiles(Path.Combine(_dir, "crashes"), "host-crash-*.txt"));
        string t = File.ReadAllText(file);
        Assert.Contains("host-kapot", t);
        Assert.Contains("InvalidOperationException", t);
        Assert.Contains("Version:", t);
        Assert.Contains("host-kapot", File.ReadAllText(HostLog.Path!));
    }

    [Fact]
    public void WriteCrash_RemovesTokens()
    {
        HostLog.WriteCrash("Plugin run error", Boom("antwoord met sk-ant-oat01-GEHEIM123 erin en Authorization: Bearer abc.def-123"));
        string t = File.ReadAllText(Directory.GetFiles(Path.Combine(_dir, "crashes"))[0]);
        Assert.DoesNotContain("GEHEIM123", t);
        Assert.DoesNotContain("abc.def-123", t);
        Assert.DoesNotContain("GEHEIM123", File.ReadAllText(HostLog.Path!));
    }

    [Fact]
    public void WriteCrash_KeepsTheNewest15()
    {
        for (int i = 0; i < 20; i++) HostLog.WriteCrash("x", Boom("n" + i));
        Assert.Equal(15, Directory.GetFiles(Path.Combine(_dir, "crashes"), "host-crash-*.txt").Length);
    }

    [Fact]
    public void WriteCrash_WithoutLogPath_DoesNothing()
    {
        HostLog.Path = null;
        HostLog.WriteCrash("x", Boom("y"));   // geen uitzondering
        Assert.False(Directory.Exists(Path.Combine(_dir, "crashes")));
    }
}
