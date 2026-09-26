using Xunit;

// Alles draait na elkaar: Loc, TASKBARSTATS_DATA en Diag zijn processbrede toestand.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace TaskbarStats.Tests;

/// <summary>Tijdelijke map + TASKBARSTATS_DATA; alles wordt bij Dispose teruggezet en opgeruimd.</summary>
public sealed class TempData : IDisposable
{
    public string Dir { get; }
    private readonly string? _oldData = Environment.GetEnvironmentVariable("TASKBARSTATS_DATA");

    public TempData()
    {
        Dir = Path.Combine(Path.GetTempPath(), "tsstats-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Dir);
        Environment.SetEnvironmentVariable("TASKBARSTATS_DATA", Dir);
    }

    public string Write(string relative, string content)
    {
        var p = Path.Combine(Dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content);
        return p;
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("TASKBARSTATS_DATA", _oldData);
        Loc.Lang = "en";
        Loc.ExternalDir = null;
        try { Directory.Delete(Dir, true); } catch { }
    }
}

public static class RepoRoot
{
    // Zoekt omhoog vanaf de testuitvoer; valt terug op de bronmap (bij een omgeleide BaseOutputPath).
    public static string Find([System.Runtime.CompilerServices.CallerFilePath] string here = "")
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Path.GetDirectoryName(here) ?? "" })
        {
            var d = start.Length == 0 ? null : new DirectoryInfo(start);
            while (d is not null)
            {
                if (File.Exists(Path.Combine(d.FullName, "TaskbarStats.csproj"))) return d.FullName;
                d = d.Parent;
            }
        }
        throw new InvalidOperationException("TaskbarStats.csproj niet gevonden boven " + AppContext.BaseDirectory);
    }
}
