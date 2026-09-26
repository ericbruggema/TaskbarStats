using System.Text.Json;
using Xunit;

namespace TaskbarStats.Tests;

public class UpdateCheckerTests : IDisposable
{
    private readonly TempData _data = new();
    private readonly string? _oldUrl = Environment.GetEnvironmentVariable("TASKBARSTATS_UPDATE_URL");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("TASKBARSTATS_UPDATE_URL", _oldUrl);
        _data.Dispose();
    }

    [Theory]
    [InlineData("v1.5.0", "1.4.1", true)]
    [InlineData("1.4.2", "1.4.1", true)]
    [InlineData("1.10", "1.9.9", true)]          // numeriek, niet als tekst vergeleken
    [InlineData("1.4.1", "1.4.1", false)]
    [InlineData("1.4.0", "1.4.1", false)]
    [InlineData("v1.5.0-beta1", "1.4.1", false)] // prerelease op GitHub telt nooit als update
    [InlineData("1.4.0", "1.4.0-beta", true)]    // stabiel na een beta van dezelfde versie
    [InlineData("1.4.0", "1.4.0+abc123", false)] // build-metadata negeren
    [InlineData("1.5.0", "1.4.1+abc123", true)]
    [InlineData("rommel", "1.4.1", false)]
    [InlineData("1.5.0", "rommel", false)]
    [InlineData(null, "1.4.1", false)]
    [InlineData("", "1.4.1", false)]
    [InlineData("1.2.3.4.5", "1.0", false)]      // te veel delen
    public void IsNewer_regels(string? tag, string? current, bool expected)
        => Assert.Equal(expected, UpdateChecker.IsNewer(tag, current));

    [Fact]
    public void TryParseTag_leest_delen_en_prerelease()
    {
        Assert.True(UpdateChecker.TryParseTag("V1.3", out var v, out var pre));
        Assert.Equal(new Version(1, 3, 0, 0), v);
        Assert.False(pre);
        Assert.True(UpdateChecker.TryParseTag("2.0.1-rc1", out v, out pre));
        Assert.Equal(new Version(2, 0, 1, 0), v);
        Assert.True(pre);
        Assert.False(UpdateChecker.TryParseTag("1.a", out _, out _));
    }

    [Fact]
    public void Evaluate_nieuwere_release_geeft_Newer_met_github_url()
    {
        var r = UpdateChecker.Evaluate("""{ "tag_name": "v9.0.0", "html_url": "https://github.com/ericbruggema/TaskbarStats/releases/tag/v9.0.0" }""", "1.4.1");
        Assert.Equal(UpdateStatus.Newer, r.Status);
        Assert.Equal("v9.0.0", r.Info!.Tag);
        Assert.StartsWith("https://github.com/", r.Info.Url);
    }

    [Fact]
    public void Evaluate_vervangt_vreemde_url_door_de_vaste_pagina()
    {
        var r = UpdateChecker.Evaluate("""{ "tag_name": "v9.0.0", "html_url": "https://evil.example.com/x" }""", "1.4.1");
        Assert.Equal(UpdateStatus.Newer, r.Status);
        Assert.StartsWith("https://github.com/ericbruggema/", r.Info!.Url);

        r = UpdateChecker.Evaluate("""{ "tag_name": "v9.0.0", "html_url": "http://github.com/x" }""", "1.4.1");   // geen https
        Assert.StartsWith("https://github.com/ericbruggema/", r.Info!.Url);
    }

    [Theory]
    [InlineData("""{ "tag_name": "v1.4.1" }""")]                         // gelijk
    [InlineData("""{ "tag_name": "v9.0.0", "draft": true }""")]
    [InlineData("""{ "tag_name": "v9.0.0", "prerelease": true }""")]
    [InlineData("""{ "tag_name": "v9.0.0-beta" }""")]
    public void Evaluate_UpToDate(string json)
        => Assert.Equal(UpdateStatus.UpToDate, UpdateChecker.Evaluate(json, "1.4.1").Status);

    [Theory]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{ "tag_name": 5 }""")]
    [InlineData("""{ "tag_name": "onzin" }""")]
    [InlineData("geen json")]
    public void Evaluate_Failed_bij_onbruikbaar_antwoord(string json)
        => Assert.Equal(UpdateStatus.Failed, UpdateChecker.Evaluate(json, "1.4.1").Status);

    [Fact]
    public async Task FetchAsync_leest_bestand_uit_TASKBARSTATS_UPDATE_URL()
    {
        string f = _data.Write("release.json", """{ "tag_name": "v9.9.9" }""");
        Environment.SetEnvironmentVariable("TASKBARSTATS_UPDATE_URL", f);
        Assert.Contains("v9.9.9", await UpdateChecker.FetchAsync());

        Environment.SetEnvironmentVariable("TASKBARSTATS_UPDATE_URL", new Uri(f).AbsoluteUri);   // file://
        Assert.Contains("v9.9.9", await UpdateChecker.FetchAsync());
    }

    [Fact]
    public async Task RunAsync_force_slaat_de_nieuwe_versie_op_en_meldt_Found()
    {
        Environment.SetEnvironmentVariable("TASKBARSTATS_UPDATE_URL", _data.Write("release.json", """{ "tag_name": "v9.9.9" }"""));
        var cfg = AppSettings.Load();
        int found = 0;
        void H() => found++;
        UpdateChecker.Found += H;
        try
        {
            var r = await UpdateChecker.RunAsync(cfg, "1.4.1", force: true);
            Assert.Equal(UpdateStatus.Newer, r.Status);
            Assert.Equal(1, found);
            Assert.Equal("v9.9.9", UpdateChecker.Available!.Tag);
            Assert.Equal("v9.9.9", cfg.UpdateLatestTag);
            Assert.NotNull(cfg.LastUpdateCheck);
            Assert.Equal("v9.9.9", AppSettings.Load().UpdateLatestTag);   // ook opgeslagen

            // Restore: alleen als de gebruiker updatecontrole aan heeft
            cfg.CheckUpdates = true;
            UpdateChecker.Restore(cfg, "1.4.1");
            Assert.NotNull(UpdateChecker.Available);
            UpdateChecker.Restore(cfg, "9.9.9");   // inmiddels bijgewerkt
            Assert.Null(UpdateChecker.Available);
        }
        finally { UpdateChecker.Found -= H; }
    }

    [Fact]
    public async Task RunAsync_zonder_force_en_zonder_opt_in_doet_niets()
    {
        Environment.SetEnvironmentVariable("TASKBARSTATS_UPDATE_URL", _data.Write("release.json", """{ "tag_name": "v9.9.9" }"""));
        var cfg = AppSettings.Load();
        cfg.CheckUpdates = false;
        var r = await UpdateChecker.RunAsync(cfg, "1.4.1", force: false);
        Assert.Equal(UpdateStatus.Skipped, r.Status);
    }

    [Fact]
    public async Task RunAsync_force_met_ontbrekend_bestand_geeft_Failed()
    {
        Environment.SetEnvironmentVariable("TASKBARSTATS_UPDATE_URL", Path.Combine(_data.Dir, "bestaat-niet.json"));
        var r = await UpdateChecker.RunAsync(AppSettings.Load(), "1.4.1", force: true);
        Assert.Equal(UpdateStatus.Failed, r.Status);
    }
}

public class UsageTrackerTests : IDisposable
{
    private readonly TempData _data = new();
    public void Dispose() => _data.Dispose();

    private static string Day(int daysAgo) => DateTime.Now.AddDays(-daysAgo).ToString("yyyy-MM-dd");

    private string Seed(Dictionary<string, Dictionary<string, AdapterUsage>> days)
        => _data.Write("usage.json", JsonSerializer.Serialize(days));

    [Fact]
    public void Nieuw_bestand_is_leeg()
    {
        var t = new UsageTracker(_data.Dir);
        Assert.Equal(0, t.Total().Total);
        Assert.Equal(0, t.DaysTracked());
        Assert.Null(t.BestDay());
    }

    [Fact]
    public void Bewaarde_dagen_worden_geladen_en_opgeteld()
    {
        Seed(new()
        {
            [Day(0)] = new() { ["Wifi"] = new() { Down = 100, Up = 10 }, ["Eth"] = new() { Down = 5, Up = 1 } },
            [Day(1)] = new() { ["Wifi"] = new() { Down = 1000, Up = 100 } },
        });
        var t = new UsageTracker(_data.Dir);

        Assert.Equal(105, t.Today().Down);
        Assert.Equal(11, t.Today("Wifi").Up + t.Today("Eth").Up);
        Assert.Equal(1100, t.Yesterday().Total);
        Assert.Equal(1216, t.Total().Total);
        Assert.Equal(1105, t.Week().Down);
        Assert.Equal(2, t.DaysTracked());
        Assert.Equal(Day(1), t.BestDay()!.Value.day.ToString("yyyy-MM-dd"));
        Assert.Equal(1100, t.BestDay()!.Value.bytes);
    }

    [Fact]
    public void Days_geeft_nieuwste_eerst_en_diepe_kopie()
    {
        Seed(new()
        {
            [Day(0)] = new() { ["A"] = new() { Down = 1 } },
            [Day(2)] = new() { ["A"] = new() { Down = 2 } },
            [Day(10)] = new() { ["A"] = new() { Down = 3 } },
        });
        var t = new UsageTracker(_data.Dir);
        var days = t.Days(5);
        Assert.Equal(2, days.Count);
        Assert.True(days[0].day > days[1].day);
        days[0].perAdapter["A"].Down = 999;   // kopie: raakt de tracker niet
        Assert.Equal(1, t.Today("A").Down);
    }

    [Fact]
    public void Oude_dagen_boven_de_bewaartermijn_worden_opgeruimd()
    {
        Seed(new()
        {
            [Day(500)] = new() { ["A"] = new() { Down = 1 } },
            [Day(1)] = new() { ["A"] = new() { Down = 2 } },
        });
        Assert.Equal(2, new UsageTracker(_data.Dir).Total().Total);
    }

    [Fact]
    public void Save_schrijft_en_een_nieuwe_tracker_leest_hetzelfde()
    {
        Seed(new() { [Day(0)] = new() { ["Wifi"] = new() { Down = 7, Up = 3 } } });
        var t = new UsageTracker(_data.Dir);
        t.Save();
        var again = new UsageTracker(_data.Dir);
        Assert.Equal(10, again.Total().Total);
        Assert.Contains("Wifi", again.KnownAdapters());
    }

    [Fact]
    public void Clear_wist_ook_het_bestand_inhoud()
    {
        Seed(new() { [Day(0)] = new() { ["Wifi"] = new() { Down = 7 } } });
        var t = new UsageTracker(_data.Dir);
        t.Clear();
        Assert.Equal(0, t.Total().Total);
        Assert.Equal(0, new UsageTracker(_data.Dir).Total().Total);
    }

    [Fact]
    public void Kapot_bestand_geeft_lege_tracker()
    {
        _data.Write("usage.json", "{ kapot");
        Assert.Equal(0, new UsageTracker(_data.Dir).Total().Total);
    }

    [Fact]
    public void Sample_en_Save_falen_niet_op_deze_machine()
    {
        var t = new UsageTracker(_data.Dir);
        t.Sample();   // leest de echte netwerktellers, waarden zijn niet voorspelbaar
        t.Save();
        Assert.True(File.Exists(Path.Combine(_data.Dir, "usage.json")));
        Assert.True(t.Session().Total >= 0);
    }

    [Theory]
    [InlineData("Intel(R) Wi-Fi #2", "Intel[R] Wi-Fi _2")]
    [InlineData("a/b\\c", "a_b_c")]
    public void PerfName_vervangt_tekens_zoals_Windows(string input, string expected)
        => Assert.Equal(expected, UsageTracker.PerfName(input));
}

public class DiagTests : IDisposable
{
    private readonly TempData _data = new();
    public void Dispose() => _data.Dispose();

    private static void ThrowSite(string message)
    {
        // Eén vaste aanroepplek: Swallow telt per bestand+lid+regel
        Diag.Swallow(new InvalidOperationException(message));
    }

    [Fact]
    public void Swallow_logt_per_plek_hoogstens_drie_keer()
    {
        string msg = "boom-" + Guid.NewGuid().ToString("N");
        for (int i = 0; i < 10; i++) ThrowSite(msg);

        var log = File.ReadAllLines(Diag.LogPath).Where(l => l.Contains(msg)).ToList();
        Assert.Equal(3, log.Count);
        Assert.All(log, l => Assert.Contains("SWALLOWED", l));
        Assert.All(log, l => Assert.Contains("InvalidOperationException", l));
    }

    [Fact]
    public void Warn_logt_dezelfde_tekst_maar_een_keer()
    {
        string msg = "waarschuwing-" + Guid.NewGuid().ToString("N");
        Diag.Warn("test", msg);
        Diag.Warn("test", msg);
        Assert.Single(File.ReadAllLines(Diag.LogPath), l => l.Contains(msg));
    }

    [Fact]
    public void Error_logt_met_stack_en_hoogstens_drie_keer_per_fout()
    {
        string msg = "fout-" + Guid.NewGuid().ToString("N");
        for (int i = 0; i < 6; i++)
        {
            try { throw new ArgumentException(msg); }
            catch (Exception ex) { Diag.Error("test", ex); }
        }
        // Elke logvermelding bevat de tekst in de eerste regel en nog eens in de stack-omschrijving; tel de regels met "ERROR"
        var entries = File.ReadAllLines(Diag.LogPath).Where(l => l.Contains(" ERROR ") && l.Contains(msg)).ToList();
        Assert.Equal(3, entries.Count);
        Assert.Contains("ArgumentException", File.ReadAllText(Diag.LogPath));
    }

    [Fact]
    public void Diag_map_volgt_TASKBARSTATS_DATA()
    {
        Assert.Equal(_data.Dir, Diag.Dir);
        Assert.Equal(Path.Combine(_data.Dir, "diag.log"), Diag.LogPath);
    }

    [Fact]
    public void Scrub_verwijdert_computernaam_en_gebruikersnaam()
    {
        string pc = Environment.MachineName, user = Environment.UserName;
        if (pc.Length > 2 && !"<user>".Contains(pc, StringComparison.OrdinalIgnoreCase))
        {
            string s = Diag.Scrub("machine " + pc + " ok");
            Assert.DoesNotContain(pc, s, StringComparison.OrdinalIgnoreCase);
        }
        if (user.Length > 2 && !"<pc>".Contains(user, StringComparison.OrdinalIgnoreCase) && !"<user>".Contains(user, StringComparison.OrdinalIgnoreCase))
        {
            string s = Diag.Scrub("hallo " + user + "!");
            Assert.DoesNotContain(user, s, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<user>", s);
        }
    }

    [Fact]
    public void Scrub_vervangt_profielmap()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (profile.Length == 0) return;
        string s = Diag.Scrub(@"file " + profile + @"\x.txt");
        Assert.DoesNotContain(profile, s, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Scrub_laat_gewone_tekst_ongemoeid()
    {
        Assert.Equal("niets bijzonders", Diag.Scrub("niets bijzonders"));
    }

    [Fact]
    public void Report_bevat_versie_taal_en_logregels()
    {
        Diag.Warn("test", "rapport-" + Guid.NewGuid().ToString("N"));
        string r = Diag.Report("nl");
        var asm = typeof(Diag).Assembly;
        string ver = (asm.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .Cast<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? asm.GetName().Version!.ToString());
        Assert.StartsWith("TaskbarStats " + ver, r);
        Assert.StartsWith("1.", ver.TrimStart('v'));   // de csproj-versie (Version) komt in het rapport
        Assert.Contains("Language: nl", r);
        Assert.Contains("--- last log lines ---", r);
    }
}
