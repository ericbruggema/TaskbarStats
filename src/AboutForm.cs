namespace TaskbarStats;

/// <summary>Over-venster: versie, hoe de app is gebouwd, wat er gebruikt is en credits.</summary>
public sealed class AboutForm : Form
{
    /// <summary>Versie uit het project (TaskbarStats.csproj, &lt;Version&gt;).</summary>
    public static string Version
    {
        get
        {
            var v = Application.ProductVersion;
            int plus = v.IndexOf('+');
            return plus >= 0 ? v[..plus] : v;
        }
    }

    public AboutForm()
    {
        Text = Loc.Pick("Over TaskbarStats", "About TaskbarStats");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(560, 500);
        TopMost = true;

        var title = new Label
        {
            Text = "TaskbarStats",
            Font = new Font("Segoe UI", 18, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(16, 12),
        };
        var version = new Label
        {
            Text = Loc.Pick($"Versie {Version}", $"Version {Version}"),
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Location = new Point(20, 52),
        };

        var box = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = SystemColors.Window,
            Font = new Font("Segoe UI", 9.5f),
            Location = new Point(16, 80),
            Size = new Size(528, 366),
            Text = Body().Replace("\n", "\r\n"),
            TabStop = false,
        };
        box.Select(0, 0);

        var ok = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(464, 458),
            Size = new Size(80, 28),
        };
        AcceptButton = ok;
        CancelButton = ok;

        Controls.AddRange(new Control[] { title, version, box, ok });
    }

    private static string Body() => Loc.Lang == "en" ? BodyEn : BodyNl;

    private const string BodyNl = """
        Een lichtgewicht monitor die naast het systeemvak van de Windows-taakbalk zweeft en live CPU, GPU, geheugen, netwerk, schijven en temperaturen toont, met waarden die overeenkomen met Taakbeheer.

        HOE IS HET GEBOUWD
        • C# op .NET 8 met Windows Forms, één uitvoerbaar bestand.
        • Tekenen: alles wordt met GDI+ in een 32-bit ARGB-bitmap getekend en met UpdateLayeredWindow op het scherm gezet (per-pixel alpha). Daardoor kan de achtergrond transparant zijn en blijft het widget toch klikbaar. Het venster is 'owned' door de taakbalk (Shell_TrayWnd) en altijd bovenliggend.
        • Verversen met een timer (0,1–1 s); het menu, de tooltip en de meldingen lezen dezelfde meetwaarden.

        WAT IS ER GEBRUIKT
        • Windows Performance Counters (System.Diagnostics.PerformanceCounter):
          – CPU: Processor Information\% Processor Utility (totaal en per core, zoals Taakbeheer); klokfrequentie = Processor Frequency × % Processor Performance.
          – GPU: GPU Engine\Utilization Percentage per GPU (LUID) en GPU Adapter Memory voor videogeheugen.
          – Netwerk: Network Interface (per adapter). Schijven: PhysicalDisk (lezen/schrijven).
        • GlobalMemoryStatusEx voor het geheugengebruik.
        • DXGI (COM-interop) voor de namen en het videogeheugen van GPU's.
        • LibreHardwareMonitorLib voor CPU-/GPU-temperatuur (vereist administrator).
        • System.Net.NetworkInformation: cumulatieve bytes-tellers per adapter voor het verbruik per dag (opgeslagen in usage.json met System.Text.Json).
        • System.Diagnostics.Process voor de zwaarste programma's in de tooltip.
        • SHQueryUserNotificationState om het widget te verbergen bij volledig scherm.
        • NotifyIcon voor meldingen; een geplande taak (schtasks, hoogste rechten) voor autostart zonder UAC-melding.
        • Instellingen: JSON in %AppData%\TaskbarStats\settings.json.
        • Installer: Inno Setup.

        CREDITS
        • Idee, ontwerp en testen: Eric Bruggema
        • Gebouwd met Claude Code (Anthropic) — https://claude.com/claude-code
        • LibreHardwareMonitorLib (MPL-2.0) — https://github.com/LibreHardwareMonitor/LibreHardwareMonitor
        • HidSharp (Apache-2.0), System.Diagnostics.PerformanceCounter en .NET (MIT)
        • Inno Setup — https://jrsoftware.org

        De genoemde onderdelen vallen onder hun eigen licenties.
        """;

    private const string BodyEn = """
        A lightweight monitor that floats next to the Windows taskbar's notification area and shows live CPU, GPU, memory, network, disk and temperature figures, matching Task Manager.

        HOW IT IS BUILT
        • C# on .NET 8 with Windows Forms, a single executable.
        • Rendering: everything is drawn with GDI+ into a 32-bit ARGB bitmap and pushed to the screen with UpdateLayeredWindow (per-pixel alpha). That allows a transparent background while the widget stays clickable. The window is owned by the taskbar (Shell_TrayWnd) and always on top.
        • A timer (0.1–1 s) refreshes it; the menu, tooltip and notifications read the same measurements.

        WHAT IS USED
        • Windows Performance Counters (System.Diagnostics.PerformanceCounter):
          – CPU: Processor Information\% Processor Utility (total and per core, like Task Manager); clock speed = Processor Frequency × % Processor Performance.
          – GPU: GPU Engine\Utilization Percentage per GPU (LUID) and GPU Adapter Memory for VRAM.
          – Network: Network Interface (per adapter). Disks: PhysicalDisk (read/write).
        • GlobalMemoryStatusEx for memory usage.
        • DXGI (COM interop) for GPU names and video memory.
        • LibreHardwareMonitorLib for CPU/GPU temperature (requires administrator).
        • System.Net.NetworkInformation: cumulative byte counters per adapter for daily usage (stored in usage.json with System.Text.Json).
        • System.Diagnostics.Process for the top processes in the tooltip.
        • SHQueryUserNotificationState to hide the widget in full screen.
        • NotifyIcon for notifications; a scheduled task (schtasks, highest privileges) for autostart without a UAC prompt.
        • Settings: JSON in %AppData%\TaskbarStats\settings.json.
        • Installer: Inno Setup.

        CREDITS
        • Idea, design and testing: Eric Bruggema
        • Built with Claude Code (Anthropic) — https://claude.com/claude-code
        • LibreHardwareMonitorLib (MPL-2.0) — https://github.com/LibreHardwareMonitor/LibreHardwareMonitor
        • HidSharp (Apache-2.0), System.Diagnostics.PerformanceCounter and .NET (MIT)
        • Inno Setup — https://jrsoftware.org

        The listed components remain under their own licences.
        """;
}
