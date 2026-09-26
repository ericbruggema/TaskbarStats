using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace TaskbarStats;

/// <summary>
/// Diagnose: een klein logbestand (<c>diag.log</c> in de datamap), vangnet voor crashes en een tekst om bij een bug-melding te plakken.
/// Niets wordt verstuurd; het is alleen een bestand op de eigen pc. Bewust "stil falen" (bijv. een sensor die niet bestaat) gaat via
/// <see cref="Swallow"/>: de fout wordt per plek één keer vastgelegd in plaats van zonder spoor genegeerd.
/// </summary>
internal static class Diag
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, int> Seen = new();
    private static readonly Queue<string> Recent = new();
    private static readonly long Started = Environment.TickCount64;
    private const long MaxLog = 256 * 1024;

    /// <summary>Map van het logbestand: dezelfde als de instellingen (TASKBARSTATS_DATA overschrijft).</summary>
    public static string Dir
    {
        get
        {
            var custom = Environment.GetEnvironmentVariable("TASKBARSTATS_DATA");
            return !string.IsNullOrWhiteSpace(custom) ? custom : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskbarStats");
        }
    }
    public static string LogPath => Path.Combine(Dir, "diag.log");

    /// <summary>Legt een waarschuwing vast (een keer per tekst).</summary>
    public static void Warn(string area, string message)
    {
        lock (Gate) { if (Seen.TryGetValue(area + message, out int n) && n >= 1) return; Seen[area + message] = 1; }
        Write("WARN", area, message);
    }

    /// <summary>
    /// Vervanger van een lege <c>catch { }</c>: de fout is verwacht of niet erg, maar wordt wel (per plek de eerste 3 keer) vastgelegd.
    /// </summary>
    public static void Swallow(Exception ex, [CallerFilePath] string file = "", [CallerMemberName] string member = "", [CallerLineNumber] int line = 0)
    {
        string where = Path.GetFileNameWithoutExtension(file) + "." + member + ":" + line;
        lock (Gate)
        {
            Seen.TryGetValue(where, out int n);
            Seen[where] = ++n;
            if (n > 3) return;
        }
        Write("SWALLOWED", where, ex.GetType().Name + ": " + ex.Message);
    }

    /// <summary>Legt een echte fout vast met stack.</summary>
    public static void Error(string area, Exception ex)
    {
        string key = area + "|" + ex.GetType().Name + "|" + (ex.StackTrace?.Split('\n').FirstOrDefault() ?? "");
        lock (Gate)
        {
            Seen.TryGetValue(key, out int n);
            Seen[key] = ++n;
            if (n > 3) return;   // dezelfde fout elke tik zou het logbestand vullen
        }
        Write("ERROR", area, ex.ToString());
    }

    private static void Write(string level, string area, string message)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level,-9} [{area}] {message.Replace("\r", "").Replace("\n", "\n    ")}";
        lock (Gate)
        {
            Recent.Enqueue(line);
            while (Recent.Count > 60) Recent.Dequeue();
            try
            {
                Directory.CreateDirectory(Dir);
                var fi = new FileInfo(LogPath);
                if (fi.Exists && fi.Length > MaxLog) { File.Copy(LogPath, LogPath + ".old", true); File.Delete(LogPath); }
                File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch { /* het logbestand mag de app nooit hinderen */ }
        }
    }

    // ------------------------------------------------------------------------------------------------ vangnet

    /// <summary>Aan te roepen in Main, vóór het eerste venster: fouten in een timer/teken-code stoppen de app niet meer stil.</summary>
    public static void Init()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Error("UI", e.Exception);                 // de app draait door; de fout staat in het logbestand
        TaskScheduler.UnobservedTaskException += (_, e) => { Error("Task", e.Exception); e.SetObserved(); };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) Error("FATAL", ex); else Write("FATAL", "?", e.ExceptionObject?.ToString() ?? "");
            TryRestart();
        };
    }

    /// <summary>Na een fatale fout één keer opnieuw starten (geen herstartlus: niet als we net al herstart zijn en snel weer stuk gaan).</summary>
    private static void TryRestart()
    {
        try
        {
            bool restarted = Environment.GetCommandLineArgs().Contains("--restarted");
            if (restarted && Environment.TickCount64 - Started < 60_000) return;
            if (Environment.GetEnvironmentVariable("TASKBARSTATS_NORESTART") == "1") return;
            var exe = Environment.ProcessPath;
            if (exe is null) return;
            Process.Start(new ProcessStartInfo(exe, "--restarted") { UseShellExecute = false });   // een verhoogd proces start zonder nieuwe UAC-vraag opnieuw verhoogd
            Write("INFO", "restart", "App na fatale fout opnieuw gestart.");
        }
        catch (Exception ex) { Write("ERROR", "restart", ex.Message); }
    }

    // ------------------------------------------------------------------------------------------------ rapport

    /// <summary>Tekst om in een bug-melding te plakken: versie, Windows, scherm, taal en de laatste logregels (persoonlijke namen verwijderd).</summary>
    public static string Report(string language)
    {
        var sb = new StringBuilder();
        var asm = Assembly.GetExecutingAssembly();
        string ver = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? asm.GetName().Version?.ToString() ?? "?";
        sb.AppendLine("TaskbarStats " + ver);
        sb.AppendLine("Windows: " + Environment.OSVersion.Version + " (" + RuntimeInformation.OSArchitecture + "), " + RuntimeInformation.FrameworkDescription);
        sb.AppendLine("Language: " + language + ", UI culture " + System.Globalization.CultureInfo.CurrentUICulture.Name + ", regional " + System.Globalization.CultureInfo.CurrentCulture.Name);
        try { sb.AppendLine("Screens: " + string.Join(", ", Screen.AllScreens.Select(s => $"{s.Bounds.Width}x{s.Bounds.Height}{(s.Primary ? "*" : "")}"))); } catch { }
        try { using var id = WindowsIdentity.GetCurrent(); sb.AppendLine("Administrator: " + new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator)); } catch { }
        sb.AppendLine("Uptime: " + TimeSpan.FromMilliseconds(Environment.TickCount64 - Started).ToString(@"d\.hh\:mm\:ss") + ", memory " + (Process.GetCurrentProcess().WorkingSet64 >> 20) + " MB");
        sb.AppendLine("Missing translations: " + Loc.Missing.Count);
        sb.AppendLine("--- last log lines ---");
        string[] lines;
        lock (Gate) lines = Recent.ToArray();
        if (lines.Length == 0) sb.AppendLine("(none)");
        foreach (var l in lines) sb.AppendLine(l);
        return Scrub(sb.ToString());
    }

    /// <summary>Haalt gebruikersnaam, computernaam en profielmap uit de tekst (voor publiek plakken).</summary>
    public static string Scrub(string text)
    {
        try
        {
            text = text.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
            if (Environment.UserName.Length > 2) text = text.Replace(Environment.UserName, "<user>", StringComparison.OrdinalIgnoreCase);
            if (Environment.MachineName.Length > 2) text = text.Replace(Environment.MachineName, "<pc>", StringComparison.OrdinalIgnoreCase);
        }
        catch { }
        return text;
    }
}
