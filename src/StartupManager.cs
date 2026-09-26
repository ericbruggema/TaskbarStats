using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace TaskbarStats;

/// <summary>
/// Beheert of TaskbarStats met Windows meestart. De app vraagt administrator-rechten, en Windows start
/// zulke programma's niet vanuit de Run-registersleutel. Daarom gebruiken we een geplande taak
/// ("bij inloggen", hoogste rechten, ook op batterij) — zonder UAC-prompt bij het opstarten.
/// Vereist dat de aanroeper administrator is (de app zelf en de installer zijn dat).
/// </summary>
public static class StartupManager
{
    // TASKBARSTATS_TASK = andere taaknaam (voor installatietests, zodat de echte autostart-taak ongemoeid blijft).
    private static readonly string TaskName = Environment.GetEnvironmentVariable("TASKBARSTATS_TASK") ?? "TaskbarStats";
    private const string LegacyRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    // schtasks.exe starten kost ~30 ms; het resultaat onthouden zodat het menu niet bij elke rechtsklik wacht.
    private static bool? _cached;

    public static bool IsEnabled() => _cached ??= Schtasks($"/Query /TN \"{TaskName}\"") == 0;

    public static void Set(bool enabled)
    {
        RemoveLegacyRunValue();
        _cached = null;   // opnieuw opvragen na een wijziging
        try
        {
            if (!enabled) { Schtasks($"/Delete /TN \"{TaskName}\" /F"); return; }

            string exe = Environment.ProcessPath ?? Application.ExecutablePath;
            string user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name) ?? "";
            string xml = $"""
                <?xml version="1.0" encoding="UTF-16"?>
                <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
                  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{user}</UserId></LogonTrigger></Triggers>
                  <Principals><Principal id="Author"><UserId>{user}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
                  <Settings>
                    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                    <AllowStartOnDemand>true</AllowStartOnDemand>
                    <Enabled>true</Enabled>
                  </Settings>
                  <Actions Context="Author"><Exec><Command>{SecurityElement.Escape(exe)}</Command></Exec></Actions>
                </Task>
                """;
            string tmp = Path.Combine(Path.GetTempPath(), $"TaskbarStats-task-{Environment.ProcessId}.xml");
            File.WriteAllText(tmp, xml, Encoding.Unicode);
            try { Schtasks($"/Create /TN \"{TaskName}\" /XML \"{tmp}\" /F"); }
            finally { try { File.Delete(tmp); } catch (Exception dex) { Diag.Swallow(dex); } }
        }
        catch (Exception dex) { Diag.Swallow(dex); /* best-effort */ }
    }

    // Oudere versies gebruikten HKCU\...\Run; die zou anders naast de taak blijven bestaan.
    private static void RemoveLegacyRunValue()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(LegacyRunKey, true);
            key?.DeleteValue(TaskName, false);
        }
        catch (Exception dex) { Diag.Swallow(dex); }
    }

    private static int Schtasks(string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("schtasks.exe", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (p is null) return -1;
            p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            p.WaitForExit(10000);
            return p.HasExited ? p.ExitCode : -1;
        }
        catch { return -1; }
    }
}
