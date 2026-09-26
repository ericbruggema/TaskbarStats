using Xunit;

namespace TaskbarStats.Tests;

/// <summary>Instellingen exporteren/importeren/terugzetten en de plaats van de gegevensmap.</summary>
public class BackupTests : IDisposable
{
    private readonly TempData _data = new();
    public void Dispose() => _data.Dispose();

    [Fact]
    public void ToJson_en_TryParse_geven_dezelfde_instellingen_terug()
    {
        var s = AppSettings.Load();
        s.ShowCpu = false; s.RefreshMs = 2500; s.Language = "de";
        var back = AppSettings.TryParse(s.ToJson());
        Assert.NotNull(back);
        Assert.False(back!.ShowCpu);
        Assert.Equal(2500, back.RefreshMs);
        Assert.Equal("de", back.Language);
    }

    [Theory]
    [InlineData("dit is geen json")]
    [InlineData("{ kapot")]
    [InlineData("[1,2,3]")]
    public void TryParse_weigert_ongeldige_bestanden(string json) => Assert.Null(AppSettings.TryParse(json));

    [Fact]
    public void CopyFrom_neemt_waarden_over_maar_houdt_het_bestandspad()
    {
        var target = AppSettings.Load();
        string path = target.FilePath;
        var other = new AppSettings { ShowCpu = false, RefreshMs = 4000, FontSize = 13, StickToTray = false };
        target.CopyFrom(other);
        Assert.False(target.ShowCpu);
        Assert.Equal(4000, target.RefreshMs);
        Assert.Equal(13, target.FontSize);
        Assert.False(target.StickToTray);
        Assert.Equal(path, target.FilePath);   // FilePath is [JsonIgnore] en blijft van dit object
    }

    [Fact]
    public void CopyFrom_met_nieuwe_instellingen_zet_alles_terug_naar_standaard()
    {
        var s = AppSettings.Load();
        s.ShowCpu = false; s.FontSize = 20; s.RefreshMs = 9999;
        s.CopyFrom(new AppSettings());
        var d = new AppSettings();
        Assert.Equal(d.ShowCpu, s.ShowCpu);
        Assert.Equal(d.FontSize, s.FontSize);
        Assert.Equal(d.RefreshMs, s.RefreshMs);
    }

    [Fact]
    public void Gegevensmap_volgt_TASKBARSTATS_DATA()
    {
        Assert.Equal(_data.Dir, AppPaths.DataDir);
        Assert.False(AppPaths.Portable);   // met een eigen datamap is portable-modus uit
    }

    [Fact]
    public void Zonder_omgevingsvariabele_is_de_standaardmap_appdata()
    {
        Environment.SetEnvironmentVariable("TASKBARSTATS_DATA", null);
        try
        {
            if (AppPaths.Portable) return;   // een portable.txt naast de testhost: niet van toepassing
            Assert.EndsWith(Path.Combine("", "TaskbarStats"), AppPaths.DataDir);
            Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppPaths.DataDir);
        }
        finally { Environment.SetEnvironmentVariable("TASKBARSTATS_DATA", _data.Dir); }
    }
}
