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
    private const string TaskName = "TaskbarStats";
    private const string LegacyRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled() => Schtasks($"/Query /TN \"{TaskName}\"") == 0;

    public static void Set(bool enabled)
    {
        RemoveLegacyRunValue();
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
            finally { try { File.Delete(tmp); } catch { } }
        }
        catch { /* best-effort */ }
    }

    // Oudere versies gebruikten HKCU\...\Run; die zou anders naast de taak blijven bestaan.
    private static void RemoveLegacyRunValue()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(LegacyRunKey, true);
            key?.DeleteValue(TaskName, false);
        }
        catch { }
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
