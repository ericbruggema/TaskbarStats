using System.Diagnostics;
using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace TaskbarStats;

/// <summary>
/// Beheert de plugin-host (<c>plugins\TaskbarStats.Plugins.exe</c> naast de app): starten, stoppen en controleren of hij draait.
/// De host draait bewust <b>zonder administratorrechten</b>: de app is verhoogd, dus start hij de host niet zelf maar via een geplande taak
/// "TaskbarStats Plugins" met <c>LeastPrivilege</c> (zonder trigger: alleen op verzoek). Plugin-code krijgt zo nooit meer rechten dan jijzelf.
/// </summary>
public static class PluginHost
{
    public const string TaskName = "TaskbarStats Plugins";
    private const string ProcessName = "TaskbarStats.Plugins";

    private static long _lastTry;
    private static int _busy;

    public static string HostExe => Path.Combine(AppPaths.ExeDir ?? AppContext.BaseDirectory, "plugins", "TaskbarStats.Plugins.exe");
    public static bool Installed => File.Exists(HostExe);
    public static string LogPath => Path.Combine(AppPaths.DataDir, "plugins", "host.log");

    public static bool IsRunning()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName(ProcessName))
                using (p) return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { Diag.Swallow(ex); }
        return false;
    }

    /// <summary>Zelfde afleiding als in de host: bepaalt de naam van de stop-gebeurtenis voor deze datamap.</summary>
    internal static string InstanceId(string dataDir)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dataDir.ToLowerInvariant())))[..16];

    /// <summary>Elke ~30 s aangeroepen door de sampler: staat er een plugin aan en draait de host niet, start hem dan (hooguit eens per 2 minuten).</summary>
    public static void Maintain()
    {
        if (!Installed || !PluginConfig.AnyEnabled() || IsRunning()) return;
        if (Environment.TickCount64 - _lastTry < 120_000) return;
        EnsureRunning();
    }

    /// <summary>Start de host op de achtergrond (via de taak). Veilig om vaak aan te roepen.</summary>
    public static void EnsureRunning()
    {
        if (!Installed) return;
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        _lastTry = Environment.TickCount64;
        Task.Run(() =>
        {
            try
            {
                if (IsRunning()) return;
                if (!CreateTask()) { Diag.Warn("plugins", "could not create the scheduled task for the plugin host"); return; }
                int rc = RunSchtasks($"/Run /TN \"{TaskName}\"", out string output);
                if (rc != 0) Diag.Warn("plugins", $"starting the plugin host failed (exit {rc}): {output.Trim()}");
                else Diag.Info("plugins", "plugin host started");
            }
            catch (Exception ex) { Diag.Swallow(ex); }
            finally { Interlocked.Exchange(ref _busy, 0); }
        });
    }

    /// <summary>Vraagt de host netjes te stoppen (stop-gebeurtenis) en ruimt hem anders op.</summary>
    public static void Stop()
    {
        try
        {
            using var ev = EventWaitHandle.OpenExisting("Local\\TaskbarStats_Plugins_Stop_" + InstanceId(AppPaths.DataDir));
            ev.Set();
        }
        catch (Exception ex) when (ex is WaitHandleCannotBeOpenedException or UnauthorizedAccessException or IOException) { Diag.Swallow(ex); }

        var sw = Stopwatch.StartNew();
        while (IsRunning() && sw.ElapsedMilliseconds < 3000) Thread.Sleep(100);
        if (!IsRunning()) return;
        foreach (var p in Process.GetProcessesByName(ProcessName))
        {
            using (p) { try { p.Kill(); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { Diag.Swallow(ex); } }
        }
    }

    // ---------- geplande taak ----------
    private static bool CreateTask()
    {
        string user = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
        string xml = TaskXml(user, HostExe, AppPaths.DataDir);
        string tmp = Path.Combine(Path.GetTempPath(), "TaskbarStats-plugins-task.xml");
        File.WriteAllText(tmp, xml, Encoding.Unicode);
        try
        {
            int rc = RunSchtasks($"/Create /TN \"{TaskName}\" /XML \"{tmp}\" /F", out string output);
            if (rc != 0) Diag.Warn("plugins", $"schtasks /Create failed (exit {rc}): {output.Trim()}");
            return rc == 0;
        }
        finally { try { File.Delete(tmp); } catch (IOException) { } }
    }

    internal static string TaskXml(string user, string exe, string dataDir)
    {
        static string E(string s) => SecurityElement.Escape(s) ?? "";
        return $"""
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo><Description>TaskbarStats plugin helper. Runs WITHOUT administrator rights; started by TaskbarStats when a plugin is enabled.</Description></RegistrationInfo>
  <Principals>
    <Principal id="Author"><UserId>{E(user)}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>false</StartWhenAvailable>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Hidden>true</Hidden>
    <Enabled>true</Enabled>
  </Settings>
  <Actions Context="Author">
    <Exec><Command>{E(exe)}</Command><Arguments>--data "{E(dataDir)}" --watch-app</Arguments></Exec>
  </Actions>
</Task>
""";
    }

    private static int RunSchtasks(string args, out string output)
    {
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"), args)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEndAsync();
        var e = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(15000)) { try { p.Kill(); } catch (InvalidOperationException) { } output = "timeout"; return -1; }
        output = o.Result + e.Result;
        return p.ExitCode;
    }
}
