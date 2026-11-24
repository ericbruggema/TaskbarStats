using System.Diagnostics;
using Xunit;

namespace TaskbarStats.Tests;

/// <summary>Het widget mag niet vastlopen door trage aanroepen naar Verkenner of schijven, en een vastloper moet een spoor achterlaten.</summary>
public class FreezeGuardTests : IDisposable
{
    private readonly TempData _data = new();
    public FreezeGuardTests() { Diag.ResetForTests(); Diag.ResetWatchForTests(); }
    public void Dispose() { Diag.ResetForTests(); Diag.ResetWatchForTests(); _data.Dispose(); }

    private string Log() => File.Exists(Diag.LogPath) ? File.ReadAllText(Diag.LogPath) : "";

    private static bool WaitFor(Func<bool> cond, int ms = 4000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms) { if (cond()) return true; Thread.Sleep(10); }
        return cond();
    }

    [Fact]
    public void ShellWatcher_SlowQuery_NeverBlocksTheReader()
    {
        using var w = new ShellWatcher(() => { Thread.Sleep(700); return true; }, () => false, intervalMs: 10);
        var sw = Stopwatch.StartNew();
        bool early = w.FullscreenApp;                 // dit is wat de UI-thread doet: meteen antwoord, ook als Verkenner traag is
        Assert.True(sw.ElapsedMilliseconds < 100);
        Assert.False(early);
        Assert.True(WaitFor(() => w.FullscreenApp), "het antwoord komt zodra de achtergrondthread het heeft");
    }

    [Fact]
    public void ShellWatcher_FollowsTheAnswers_AndSurvivesErrors()
    {
        int calls = 0; bool hidden = false;
        using var w = new ShellWatcher(
            () => { if (Interlocked.Increment(ref calls) == 1) throw new InvalidOperationException("Verkenner antwoordt niet"); return true; },
            () => hidden, intervalMs: 10);
        Assert.True(WaitFor(() => w.FullscreenApp), "na de fout wordt gewoon doorgevraagd");
        Assert.False(w.TaskbarAutoHidden);
        hidden = true;
        Assert.True(WaitFor(() => w.TaskbarAutoHidden));
        hidden = false;
        Assert.True(WaitFor(() => !w.TaskbarAutoHidden));
    }

    [Fact]
    public void Slow_LogsTheFirstTenOfAKind_ThenEveryFiftieth()
    {
        for (int i = 0; i < 49; i++) Diag.Slow("trage-test", 500 + i);
        Assert.Equal(10, Log().Split('\n').Count(l => l.Contains("trage-test duurde")));
        Diag.Slow("trage-test", 999);   // de 50e
        Assert.Equal(11, Log().Split('\n').Count(l => l.Contains("trage-test duurde")));
    }

    [Fact]
    public void UiMark_LogsAStepThatTookLong_ButNotAFastOne()
    {
        Diag.UiMark("snel");
        Diag.UiMark("traag");
        Thread.Sleep(450);
        Assert.Equal("traag", Diag.UiStep);
        Assert.True(Diag.UiStepMs >= 400);
        Diag.UiMark("");
        string log = Log();
        Assert.Contains("ui:traag duurde", log);
        Assert.DoesNotContain("ui:snel", log);
        Assert.Equal("", Diag.UiStep);
    }

    [Fact]
    public void Watchdog_LogsAFreeze_WithTheStep_AndALogLineWhenItRecovers()
    {
        int polls = 0;
        Diag.UiMark("render");
        using var wd = new UiWatchdog(() => { int n = Interlocked.Increment(ref polls); return n >= 3 && n <= 12; }, pollMs: 15, topWindowMs: 20);
        Assert.True(WaitFor(() => Log().Contains("reageert weer")), "hersteld: " + Log());
        string log = Log();
        Assert.Contains("Windows ziet het widget als vastgelopen", log);
        Assert.Contains("stap 'render'", log);
        Assert.Contains("Systeem-CPU", log);
        // een eigen rapport in de crashmap, met de drukste programma's
        Assert.True(WaitFor(() => Directory.Exists(Diag.CrashDir) && Directory.GetFiles(Diag.CrashDir, "crash-*.txt").Length > 0));
        string report = File.ReadAllText(Directory.GetFiles(Diag.CrashDir, "crash-*.txt")[0]);
        Assert.Contains("did not respond", report);
        Assert.Contains("Busiest programs", report);
        Assert.Contains("render", report);
    }

    [Fact]
    public void Watchdog_StaysQuiet_WhenTheWindowKeepsResponding()
    {
        using var wd = new UiWatchdog(() => false, pollMs: 10, topWindowMs: 10);
        Thread.Sleep(150);
        Assert.DoesNotContain("ui-hang", Log());
    }
}
