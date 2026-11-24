using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace TaskbarStats;

/// <summary>
/// Crashrapporten: bij elke fout die de app opvangt (UI-fout, achtergrondtaak, fatale fout, harde crash uit het Windows-gebeurtenislogboek)
/// komt naast de regel in diag.log een eigen tekstbestand in <c>crashes\</c> met de volledige fout, de omstandigheden en de laatste logregels.
/// Alles blijft op de eigen pc; het logvenster (<see cref="LogsForm"/>) toont, kopieert en exporteert het, en de bestanden staan ook
/// gewoon in de datamap (buiten het programma te openen met Kladblok of de Verkenner).
/// </summary>
internal static partial class Diag
{
    private const int MaxCrashFiles = 30;
    private const int MaxReportsPerSession = 25;
    private static readonly Dictionary<string, int> ReportSeen = new();
    private static int _reportsThisSession;

    /// <summary>De datamap zoals je hem aan de gebruiker toont: het AppData-deel als %AppData% (korter, en zonder de gebruikersnaam in beeld).</summary>
    public static string DisplayDir
    {
        get
        {
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return roaming.Length > 3 && Dir.StartsWith(roaming, StringComparison.OrdinalIgnoreCase) ? "%AppData%" + Dir[roaming.Length..] : Dir;
        }
    }

    /// <summary>Map met de crashrapporten (<c>crashes</c> in de datamap).</summary>
    public static string CrashDir => Path.Combine(Dir, "crashes");

    /// <summary>Een rapport is opgeslagen (pad, fataal?). Kan op elke thread komen; de ontvanger zorgt zelf voor de UI-thread.</summary>
    public static event Action<string, bool>? CrashSaved;

    /// <summary>
    /// Bewaart een crashrapport en geeft het pad terug (null als het niet lukte of als dezelfde fout al te vaak is gemeld).
    /// Mag nooit zelf een fout geven: dit draait in de foutafhandelaar.
    /// </summary>
    public static string? SaveCrashReport(string kind, Exception? ex, string? raw = null, bool fatal = false)
    {
        try
        {
            string key = kind + "|" + (ex?.GetType().Name ?? "-") + "|" + (ex?.StackTrace?.Split('\n').FirstOrDefault() ?? raw?.Split('\n').FirstOrDefault() ?? "");
            lock (Gate)
            {
                ReportSeen.TryGetValue(key, out int n);
                ReportSeen[key] = ++n;
                // een fatale fout of harde crash wordt altijd bewaard; dezelfde fout elke tik zou de map vullen
                if (!fatal && (n > 3 || _reportsThisSession >= MaxReportsPerSession)) return null;
                _reportsThisSession++;
            }

            Directory.CreateDirectory(CrashDir);
            string slug = fatal ? "fatal" : "error";
            string path = Path.Combine(CrashDir, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}-{slug}.txt");
            for (int i = 2; File.Exists(path); i++) path = Path.Combine(CrashDir, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}-{slug}-{i}.txt");
            File.WriteAllText(path, Scrub(BuildCrashText(kind, ex, raw)), new UTF8Encoding(false));
            Write("INFO", "crash", "Crashrapport opgeslagen: " + Path.GetFileName(path));
            PruneCrashReports();
            try { CrashSaved?.Invoke(path, fatal); } catch (Exception cex) { Swallow(cex); }
            return path;
        }
        catch (Exception rex)
        {
            try { Write("WARN", "crash", "Crashrapport niet op te slaan: " + rex.Message); } catch { }
            return null;
        }
    }

    internal static string BuildCrashText(string kind, Exception? ex, string? raw)
    {
        var sb = new StringBuilder();
        sb.AppendLine("TaskbarStats crash report - " + AppInfo.Url);
        sb.AppendLine("Kind: " + kind);
        sb.AppendLine("Time: " + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"));
        sb.AppendLine("Version: " + Version);
        sb.AppendLine("Windows: " + Environment.OSVersion.Version + " (" + RuntimeInformation.OSArchitecture + "), " + RuntimeInformation.FrameworkDescription);
        try { using var id = WindowsIdentity.GetCurrent(); sb.AppendLine("Administrator: " + new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator)); } catch (Exception dex) { Swallow(dex); }
        try { sb.AppendLine("Culture: " + System.Globalization.CultureInfo.CurrentUICulture.Name + " / " + System.Globalization.CultureInfo.CurrentCulture.Name); } catch (Exception dex) { Swallow(dex); }
        try
        {
            using var proc = Process.GetCurrentProcess();
            sb.AppendLine("Uptime: " + TimeSpan.FromMilliseconds(Environment.TickCount64 - Started).ToString(@"d\.hh\:mm\:ss") + ", memory " + (proc.WorkingSet64 >> 20) + " MB, threads " + proc.Threads.Count + ", handles " + proc.HandleCount);
        }
        catch (Exception dex) { Swallow(dex); }
        sb.AppendLine("Command line: " + string.Join(' ', Environment.GetCommandLineArgs().Skip(1)));
        sb.AppendLine();
        sb.AppendLine("--- error ---");
        if (ex is not null) sb.AppendLine(ex is AggregateException ag ? ag.Flatten().ToString() : ex.ToString());
        else sb.AppendLine(raw ?? "(no details)");
        if (ex is not null && raw is not null && raw != ex.ToString()) { sb.AppendLine(); sb.AppendLine(raw); }
        sb.AppendLine();
        sb.AppendLine("--- last log lines ---");
        string[] lines;
        lock (Gate) lines = Recent.ToArray();
        if (lines.Length == 0) sb.AppendLine("(none)");
        foreach (var l in lines) sb.AppendLine(l);
        return sb.ToString();
    }

    /// <summary>Houdt de nieuwste <see cref="MaxCrashFiles"/> rapporten.</summary>
    private static void PruneCrashReports()
    {
        try
        {
            foreach (var f in new DirectoryInfo(CrashDir).EnumerateFiles("*.txt").OrderByDescending(f => f.LastWriteTimeUtc).Skip(MaxCrashFiles))
                try { f.Delete(); } catch (Exception dex) { Swallow(dex); }
        }
        catch (Exception ex) { Swallow(ex); }
    }

    // ------------------------------------------------------------------------------------------------ teruglezen

    /// <summary>Een bestand dat het logvenster toont.</summary>
    internal sealed record LogFile(string Path, string Name, DateTime Time, long Size, bool IsCrash);

    /// <summary>Alle logbestanden: eerst de crashrapporten (nieuwste eerst), dan diag.log(.old) en de log van de plugin-host.</summary>
    internal static List<LogFile> LogFiles()
    {
        var list = new List<LogFile>();
        try
        {
            if (Directory.Exists(CrashDir))
                foreach (var f in new DirectoryInfo(CrashDir).EnumerateFiles("*.txt").OrderByDescending(f => f.LastWriteTimeUtc))
                    list.Add(new LogFile(f.FullName, f.Name, f.LastWriteTime, f.Length, true));
            foreach (var rel in new[] { "diag.log", "diag.log.old", Path.Combine("plugins", "host.log"), Path.Combine("plugins", "host.log.old") })
            {
                var fi = new FileInfo(Path.Combine(Dir, rel));
                if (fi.Exists) list.Add(new LogFile(fi.FullName, rel, fi.LastWriteTime, fi.Length, false));
            }
        }
        catch (Exception ex) { Swallow(ex); }
        return list;
    }

    /// <summary>Leest een logbestand terwijl de app er nog in schrijft (gedeeld lezen); een lang bestand wordt van achteren begrensd.</summary>
    internal static string ReadLog(string path, int maxChars = 400_000)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs, Encoding.UTF8);
            string t = sr.ReadToEnd();
            return t.Length > maxChars ? "…" + Environment.NewLine + t[^maxChars..] : t;
        }
        catch (Exception ex) { Swallow(ex); return ""; }
    }

    /// <summary>Alle logbestanden + een diagnoserapport in één zip, met persoonlijke namen eruit. Geeft het aantal bestanden terug.</summary>
    internal static int ExportBundle(string zipPath)
    {
        int n = 0;
        using var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        void Add(string name, string text)
        {
            var e = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
            w.Write(text);
            n++;
        }
        Add("report.txt", Report(Loc.Lang));
        foreach (var f in LogFiles()) Add((f.IsCrash ? "crashes/" : "") + f.Name.Replace('\\', '/'), Scrub(ReadLog(f.Path, int.MaxValue / 4)));
        return n;
    }

    /// <summary>Verwijdert alle crashrapporten (de logbestanden blijven).</summary>
    internal static int ClearCrashReports()
    {
        int n = 0;
        try
        {
            if (Directory.Exists(CrashDir))
                foreach (var f in new DirectoryInfo(CrashDir).EnumerateFiles("*.txt")) { try { f.Delete(); n++; } catch (Exception dex) { Swallow(dex); } }
        }
        catch (Exception ex) { Swallow(ex); }
        return n;
    }

    /// <summary>Alleen voor tests.</summary>
    internal static void ResetCrashStateForTests() { lock (Gate) { ReportSeen.Clear(); _reportsThisSession = 0; } }
}
