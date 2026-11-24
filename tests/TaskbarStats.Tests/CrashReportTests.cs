using System.IO.Compression;
using Xunit;

namespace TaskbarStats.Tests;

/// <summary>Crashrapporten: bewaard bij elke opgevangen fout, begrensd, zonder persoonlijke namen, terug te lezen en te exporteren.</summary>
public class CrashReportTests : IDisposable
{
    private readonly TempData _data = new();
    public CrashReportTests() => Diag.ResetForTests();
    public void Dispose() { Diag.ResetForTests(); _data.Dispose(); }

    private static Exception Boom(string msg)
    {
        try { throw new InvalidOperationException(msg, new ArgumentException("inner-oorzaak")); }
        catch (Exception e) { return e; }
    }

    [Fact]
    public void Report_ContainsErrorCauseVersionAndLastLogLines()
    {
        Diag.Info("test", "een regel voor het rapport");
        string? path = Diag.SaveCrashReport("UI error", Boom("kapot-bericht"));
        Assert.NotNull(path);
        Assert.StartsWith(Diag.CrashDir, path);
        string t = File.ReadAllText(path!);
        Assert.Contains("Kind: UI error", t);
        Assert.Contains("kapot-bericht", t);
        Assert.Contains("inner-oorzaak", t);          // de oorzaak (InnerException) staat er ook in
        Assert.Contains("Version:", t);
        Assert.Contains("een regel voor het rapport", t);
        Assert.Contains("--- last log lines ---", t);
    }

    [Fact]
    public void Report_HasNoProfilePath()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string? path = Diag.SaveCrashReport("UI error", null, "kon " + Path.Combine(profile, "geheim", "bestand.txt") + " niet lezen");
        string t = File.ReadAllText(path!);
        Assert.DoesNotContain(profile, t, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("%USERPROFILE%", t);
    }

    [Fact]
    public void SameNonFatalError_IsSavedAtMostThreeTimes_ButFatalAlways()
    {
        var e = Boom("steeds-dezelfde");
        for (int i = 0; i < 6; i++) Diag.SaveCrashReport("UI error", e);
        Assert.Equal(3, Directory.GetFiles(Diag.CrashDir, "crash-*.txt").Length);
        for (int i = 0; i < 2; i++) Assert.NotNull(Diag.SaveCrashReport("FATAL", e, null, fatal: true));
        Assert.Equal(5, Directory.GetFiles(Diag.CrashDir, "crash-*.txt").Length);
    }

    [Fact]
    public void OnlyTheNewest30ReportsAreKept()
    {
        for (int i = 0; i < 34; i++) Diag.SaveCrashReport("FATAL", null, "fout " + i, fatal: true);
        Assert.Equal(30, Directory.GetFiles(Diag.CrashDir, "crash-*.txt").Length);
    }

    [Fact]
    public void CrashSavedEvent_ReportsPathAndFatalFlag()
    {
        var got = new List<(string Path, bool Fatal)>();
        void Handler(string p, bool f) => got.Add((p, f));
        Diag.CrashSaved += Handler;
        try
        {
            Diag.SaveCrashReport("UI error", Boom("a"));
            Diag.SaveCrashReport("FATAL", Boom("b"), null, fatal: true);
        }
        finally { Diag.CrashSaved -= Handler; }
        Assert.Equal(2, got.Count);
        Assert.False(got[0].Fatal);
        Assert.True(got[1].Fatal);
        Assert.True(File.Exists(got[1].Path));
    }

    [Fact]
    public void ReportFailure_NeverThrows()
    {
        // de crashmap kan niet worden aangemaakt: een bestand heet al "crashes"
        File.WriteAllText(Path.Combine(_data.Dir, "crashes"), "geen map");
        Assert.Null(Diag.SaveCrashReport("UI error", Boom("x")));
    }

    [Fact]
    public void ManagedCrash_FromPendingFile_DoesNotMakeASecondReport()
    {
        // een fatale fout schrijft zijn rapport al bij het optreden; bij de volgende start komt daar geen kopie bij
        Diag.BeginSession();
        string? r = Diag.SaveCrashReport("FATAL", Boom("echte crash"), null, fatal: true);
        File.WriteAllText(Path.Combine(_data.Dir, "crash.pending"), "2026-01-01 10:00:00 echte crash");
        Diag.ResetForTests();
        Diag.BeginSession();   // sessie.lock stond nog: de vorige sessie was onnet
        Assert.NotNull(Diag.PreviousCrash);
        Assert.Single(Directory.GetFiles(Diag.CrashDir, "crash-*.txt"));
        Assert.True(File.Exists(r!));
    }

    [Fact]
    public void LogFiles_ListCrashReportsFirst_ThenTheLogs()
    {
        _data.Write("diag.log", "regel in het log\n");
        _data.Write("diag.log.old", "oud\n");
        _data.Write(Path.Combine("plugins", "host.log"), "host regel\n");
        Diag.SaveCrashReport("UI error", Boom("rapport"));
        var files = Diag.LogFiles();
        Assert.True(files[0].IsCrash);
        Assert.Equal(new[] { "diag.log", "diag.log.old", Path.Combine("plugins", "host.log") }, files.Where(f => !f.IsCrash).Select(f => f.Name).ToArray());
    }

    [Fact]
    public void ReadLog_WorksWhileTheFileIsOpenForWriting()
    {
        string p = _data.Write("diag.log", "eerste\n");
        using var writer = new FileStream(p, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        writer.Write("tweede\n"u8);
        writer.Flush();
        Assert.Contains("tweede", Diag.ReadLog(p));
    }

    [Fact]
    public void ExportBundle_HasReportLogsAndCrashReports_WithoutProfilePath()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _data.Write("diag.log", "gelezen uit " + Path.Combine(profile, "x.txt") + "\n");
        _data.Write(Path.Combine("plugins", "host.log"), "host\n");
        Diag.SaveCrashReport("FATAL", Boom("in de zip"), null, fatal: true);
        string zip = Path.Combine(_data.Dir, "uit.zip");
        int n = Diag.ExportBundle(zip);
        using var z = ZipFile.OpenRead(zip);
        var names = z.Entries.Select(e => e.FullName).ToList();
        Assert.Equal(n, names.Count);
        Assert.Contains("report.txt", names);
        Assert.Contains("diag.log", names);
        Assert.Contains(names, x => x.StartsWith("crashes/crash-") && x.EndsWith(".txt"));
        Assert.Contains(names, x => x.EndsWith("host.log"));
        foreach (var e in z.Entries)
        {
            using var r = new StreamReader(e.Open());
            Assert.DoesNotContain(profile, r.ReadToEnd(), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ClearCrashReports_RemovesReportsOnly()
    {
        _data.Write("diag.log", "blijft\n");
        Diag.SaveCrashReport("FATAL", Boom("weg"), null, fatal: true);
        Assert.Equal(1, Diag.ClearCrashReports());
        Assert.Empty(Directory.GetFiles(Diag.CrashDir, "*.txt"));
        Assert.True(File.Exists(Path.Combine(_data.Dir, "diag.log")));
    }

    [Fact]
    public void LogsWindow_ShowsTheNewestCrashReport_AndBuildsWhenEmpty()
    {
        Exception? err = null; int count = 0, emptyCount = -1; string shown = "";
        var t = new Thread(() =>
        {
            try
            {
                using (var empty = new LogsForm()) { empty.CreateControl(); emptyCount = empty.FileCount; }
                Diag.SaveCrashReport("FATAL", Boom("zichtbaar-in-venster"), null, fatal: true);
                using var f = new LogsForm();
                f.CreateControl();
                count = f.FileCount; shown = f.ShownText;
            }
            catch (Exception e) { err = e; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start(); t.Join();
        Assert.Null(err);
        Assert.True(emptyCount >= 0);
        Assert.True(count >= 1);
        Assert.Contains("zichtbaar-in-venster", shown);
    }
}
