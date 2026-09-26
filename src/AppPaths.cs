namespace TaskbarStats;

/// <summary>
/// Waar de app zijn gegevens bewaart (instellingen, thema's, verbruik, eigen taalbestanden, logbestand).
/// Volgorde: TASKBARSTATS_DATA (tests) → portable-modus → %AppData%\TaskbarStats.
/// Portable-modus: staat er een bestand <c>portable.txt</c> naast TaskbarStats.exe, dan staat alles in de map <c>data</c> ernaast
/// (handig op een USB-stick; er wordt dan niets in %AppData% geschreven).
/// </summary>
internal static class AppPaths
{
    public const string PortableMarker = "portable.txt";

    public static string? ExeDir { get { var p = Environment.ProcessPath; return p is null ? null : Path.GetDirectoryName(p); } }

    public static bool Portable
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TASKBARSTATS_DATA"))) return false;
            var d = ExeDir;
            return d is not null && File.Exists(Path.Combine(d, PortableMarker));
        }
    }

    public static string DataDir
    {
        get
        {
            var custom = Environment.GetEnvironmentVariable("TASKBARSTATS_DATA");
            if (!string.IsNullOrWhiteSpace(custom)) return custom;
            if (Portable) return Path.Combine(ExeDir!, "data");
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskbarStats");
        }
    }
}
