using Xunit;

namespace TaskbarStats.Tests;

/// <summary>Sessiebewaking: wordt een crash van de vorige keer herkend (en een gewone afsluiting niet)?</summary>
public class CrashTests : IDisposable
{
    private readonly TempData _data = new();
    public CrashTests() => Diag.ResetForTests();
    public void Dispose() { Diag.ResetForTests(); _data.Dispose(); }

    private string LogText() => File.Exists(Diag.LogPath) ? File.ReadAllText(Diag.LogPath) : "";

    [Fact]
    public void Eerste_start_is_schoon()
    {
        Diag.BeginSession();
        Assert.False(Diag.PreviousUnclean);
        Assert.Null(Diag.PreviousCrash);
        Assert.True(File.Exists(Path.Combine(_data.Dir, "session.lock")));
    }

    [Fact]
    public void Netjes_afsluiten_wist_het_sessiebestand_en_de_volgende_start_is_schoon()
    {
        Diag.BeginSession();
        Diag.EndSession();
        Assert.False(File.Exists(Path.Combine(_data.Dir, "session.lock")));
        Diag.BeginSession();
        Assert.False(Diag.PreviousUnclean);
        Assert.Null(Diag.PreviousCrash);
    }

    [Fact]
    public void Niet_netjes_afsluiten_zonder_crashvlag_wordt_gelogd_maar_niet_als_crash_gemeld()
    {
        Diag.BeginSession();          // sessie 1 eindigt zonder EndSession (Taakbeheer, stroomuitval)
        Diag.BeginSession();
        Assert.True(Diag.PreviousUnclean);
        Assert.Null(Diag.PreviousCrash);
        Assert.Contains("niet normaal afgesloten", LogText());
    }

    [Fact]
    public void Crashvlag_van_de_foutafhandelaar_wordt_bij_de_volgende_start_gemeld_en_gewist()
    {
        Diag.BeginSession();
        File.WriteAllText(Path.Combine(_data.Dir, "crash.pending"), "2026-01-01 12:00:00 System.InvalidOperationException: boem");
        Diag.BeginSession();
        Assert.True(Diag.PreviousUnclean);
        Assert.NotNull(Diag.PreviousCrash);
        Assert.Contains("boem", Diag.PreviousCrash);
        Assert.Contains("gecrasht", LogText());
        Assert.False(File.Exists(Path.Combine(_data.Dir, "crash.pending")));   // maar één keer melden
        Diag.EndSession();
        Diag.BeginSession();
        Assert.Null(Diag.PreviousCrash);
    }

    [Fact]
    public void Rapport_noemt_de_vorige_crash()
    {
        Diag.BeginSession();
        File.WriteAllText(Path.Combine(_data.Dir, "crash.pending"), "2026-01-01 12:00:00 boem-test");
        Diag.BeginSession();
        var r = Diag.Report("en");
        Assert.Contains("Previous session crashed", r);
        Assert.Contains("boem-test", r);
    }

    [Fact]
    public void Gebeurtenissenzoeker_geeft_null_als_er_niets_is_sinds_nu()
    {
        Assert.Null(Diag.QueryCrashEvent(DateTime.UtcNow));
    }
}
