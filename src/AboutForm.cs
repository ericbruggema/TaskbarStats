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

    public AboutForm(Action? showWelcome = null)
    {
        AppIcon.Apply(this);
        Text = Loc.T("About TaskbarStats");
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
            Text = Loc.T("Version {0}", Version),
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Location = new Point(20, 52),
        };

        version.Click += (_, _) => { if (Cadence.Hit(1, 7, 5000)) Cadence.Go(1, 10000); };

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
        var welcome = new Button
        {
            Text = Loc.T("Welcome screen…"),
            Location = new Point(16, 458),
            Size = new Size(150, 28),
            Visible = showWelcome is not null,
        };
        welcome.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); showWelcome?.Invoke(); };
        var diag = new Button
        {
            Text = Loc.T("Copy diagnostics"),
            Location = new Point(welcome.Visible ? 176 : 16, 458),
            Size = new Size(150, 28),
        };
        diag.Click += (_, _) =>
        {
            try { Clipboard.SetText(Diag.Report(Loc.Lang)); diag.Text = Loc.T("Copied"); }
            catch (Exception dex) { Diag.Swallow(dex); }
        };
        new ToolTip().SetToolTip(diag, Loc.T("Copies version, Windows, screen and the last log lines (no personal names) to paste into a bug report."));
        AcceptButton = ok;
        CancelButton = ok;

        Controls.AddRange(new Control[] { title, version, box, welcome, diag, ok });
    }

    // De brontekst is Engels; elke regel wordt afzonderlijk vertaald (lege regels blijven leeg).
    private static string Body() => string.Join("\n", BodyEn.Replace("\r", "").Split('\n').Select(l => l.Trim().Length == 0 ? "" : Loc.T(l)));   // lang-dynamic: de regels van BodyEn staan hieronder als sleutels

    

    // lang-lines
    private const string BodyEn = """
        A lightweight monitor that floats next to the Windows taskbar's notification area and shows live CPU, GPU, memory, network, disk and temperature figures, matching Task Manager.

        HOW IT IS BUILT
        • C# on .NET 8 with Windows Forms, a single executable.
        • Rendering: everything is drawn with GDI+ into a 32-bit ARGB bitmap and pushed to the screen with UpdateLayeredWindow (per-pixel alpha). That allows a transparent background while the widget stays clickable. The window is owned by the taskbar (Shell_TrayWnd) and always on top.
        • A timer (0.1–1 s) refreshes it; the menu, tooltip and notifications read the same measurements.

        WHAT IS USED
        • Windows Performance Counters (System.Diagnostics.PerformanceCounter):
          – CPU: Processor Information\% Processor Time (total and per core, like Task Manager; optionally % Processor Utility, incl. turbo); clock speed = Processor Frequency × % Processor Performance.
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
