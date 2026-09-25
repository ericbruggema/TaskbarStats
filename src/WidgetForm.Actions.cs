using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TaskbarStats;

// Muisacties, klik-door van het widget, extra meldingen en de updatecontrole (zie AppSettings.Actions.cs).
public sealed partial class WidgetForm
{
    // ---------- Muisacties ----------
    private void RunMouseAction(MouseAction a)
    {
        switch (a)
        {
            case MouseAction.TaskManager:
                try { Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true }); } catch { }
                break;
            case MouseAction.Dashboard: _cfg.ShowDashboard = !_cfg.ShowDashboard; ApplyAll(); break;
            case MouseAction.Fullscreen: ToggleFullscreen(); break;
            case MouseAction.Settings: ShowSettings(); break;
            case MouseAction.UsageLog: ShowLog(); break;
            case MouseAction.CopyInfo: CopyInfo(); break;
            case MouseAction.Menu: _menu.Show(Cursor.Position); break;
        }
    }

    // ---------- Klik-door van het widget ----------
    // WS_EX_TRANSPARENT op het (layered) venster: de muis valt dan volledig door het widget heen. Slepen, tooltip en
    // rechtermuismenu werken dan niet meer; het pictogram bij de klok (en Ctrl+Alt+W) blijft de weg terug.
    private const int GWL_EXSTYLE = -20, WS_EX_TRANSPARENT = 0x20;
    private const int HotkeyClickId = 0x7A53;
    private bool _hotkeyClickFailed;
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWL(IntPtr h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWL(IntPtr h, int i, IntPtr v);

    private void ApplyWidgetClickThrough()
    {
        if (!IsHandleCreated) return;
        long ex = GetWL(Handle, GWL_EXSTYLE).ToInt64();
        long want = _cfg.WidgetClickThrough ? ex | WS_EX_TRANSPARENT : ex & ~(long)WS_EX_TRANSPARENT;
        if (want != ex) SetWL(Handle, GWL_EXSTYLE, (IntPtr)want);
        if (_cfg.WidgetClickThrough) { _dragging = false; HideTip(); }
        // Het pictogram bij de klok moet er zijn zolang het widget niet klikbaar is.
        _tray.Visible = _cfg.ShowDashboard || _cfg.WidgetClickThrough;
    }

    private void ToggleWidgetClickThrough()
    {
        _cfg.WidgetClickThrough = !_cfg.WidgetClickThrough;
        _cfg.Save();
        ApplyWidgetClickThrough();
        if (_cfg.WidgetClickThrough)
            Alert("wclick" + Environment.TickCount64, 0, "TaskbarStats",
                  _hotkeyClickFailed
                      ? Loc.Pick("Widget: klik-door aan. Zet het uit met een rechtermuisklik op het pictogram bij de klok.",
                                 "Widget: click-through on. Turn it off with a right-click on the tray icon.")
                      : Loc.Pick("Widget: klik-door aan. Zet het uit met Ctrl+Alt+W of met een rechtermuisklik op het pictogram bij de klok.",
                                 "Widget: click-through on. Turn it off with Ctrl+Alt+W or a right-click on the tray icon."));
    }

    private void RegisterClickHotkey()
    {
        // Bezet door een ander programma? Dan blijft het pictogram bij de klok de bediening (melding bij het opstarten).
        _hotkeyClickFailed = !RegisterHotKey(Handle, HotkeyClickId, 0x0001 | 0x0002 /* ALT | CTRL */, 0x57 /* W */);
    }

    private void UnregisterClickHotkey() => UnregisterHotKey(Handle, HotkeyClickId);

    private void AddClickThroughMenuItem(ContextMenuStrip menu)
    {
        var it = new ToolStripMenuItem(Loc.Pick("Widget klik-door (Ctrl+Alt+W)", "Widget click-through (Ctrl+Alt+W)"))
            { Checked = _cfg.WidgetClickThrough, Tag = "close" };
        it.Click += (_, _) => ToggleWidgetClickThrough();
        menu.Items.Add(it);
    }

    // ---------- Extra meldingen (0 = uit) ----------
    private long _extraAlertAt;

    private void CheckExtraAlerts()
    {
        long now = Environment.TickCount64;
        if (now - _extraAlertAt < 2000) return;
        _extraAlertAt = now;

        if (_cfg.AlertMemPercent > 0 && _metrics.MemPercent >= _cfg.AlertMemPercent)
            Alert("mem>", 30 * 60_000L, Loc.Pick("Veel geheugen in gebruik", "High memory use"),
                  Loc.Pick($"Het geheugen is {_metrics.MemPercent:0}% vol (drempel {_cfg.AlertMemPercent}%)",
                           $"Memory is {_metrics.MemPercent:0}% full (threshold {_cfg.AlertMemPercent}%)"));

        if (_cfg.AlertCpuTempC > 0 && _metrics.CpuTempC is double ct && ct >= _cfg.AlertCpuTempC)
            Alert("cputemp", 15 * 60_000L, Loc.Pick("Hoge CPU-temperatuur", "High CPU temperature"),
                  Loc.Pick($"De processor is {ct:0} °C (drempel {_cfg.AlertCpuTempC} °C)", $"The processor is at {ct:0} °C (threshold {_cfg.AlertCpuTempC} °C)"));

        if (_cfg.AlertGpuTempC > 0 && _metrics.GpuTempC is double gt && gt >= _cfg.AlertGpuTempC)
            Alert("gputemp", 15 * 60_000L, Loc.Pick("Hoge GPU-temperatuur", "High GPU temperature"),
                  Loc.Pick($"De videokaart is {gt:0} °C (drempel {_cfg.AlertGpuTempC} °C)", $"The graphics card is at {gt:0} °C (threshold {_cfg.AlertGpuTempC} °C)"));

        if (_cfg.AlertDiskTempC > 0 && _metrics.DiskTempC is double dt && dt >= _cfg.AlertDiskTempC)
            Alert("disktemp", 15 * 60_000L, Loc.Pick("Hoge schijftemperatuur", "High drive temperature"),
                  Loc.Pick($"De schijf is {dt:0} °C (drempel {_cfg.AlertDiskTempC} °C)", $"The drive is at {dt:0} °C (threshold {_cfg.AlertDiskTempC} °C)"));

        if (_cfg.AlertDayNetGb > 0)
        {
            double gb = _usage.Today(_cfg.NetworkAdapter).Total / 1073741824.0;
            if (gb >= _cfg.AlertDayNetGb)
                Alert("daynet:" + DateTime.Now.ToString("yyyyMMdd"), 20 * 3_600_000L, Loc.Pick("Dagverbruik netwerk", "Daily network use"),
                      Loc.Pick($"Vandaag al {gb:0.#} GB verbruikt (drempel {_cfg.AlertDayNetGb} GB)", $"{gb:0.#} GB used today (threshold {_cfg.AlertDayNetGb} GB)"));
        }
    }

    // ---------- Updatecontrole ----------
    private System.Windows.Forms.Timer? _updTimer;
    private bool _updHooked;

    private void AddUpdateMenuItem(ContextMenuStrip menu)
    {
        if (!_cfg.CheckUpdates || UpdateChecker.Available is not { } u) return;
        var it = new ToolStripMenuItem(Loc.Pick($"Nieuwe versie {u.Tag.TrimStart('v', 'V')} beschikbaar", $"New version {u.Tag.TrimStart('v', 'V')} available"))
            { Tag = "close", Font = new Font(Font, FontStyle.Bold) };
        it.Click += (_, _) => { try { Process.Start(new ProcessStartInfo(u.Url) { UseShellExecute = true }); } catch { } };
        menu.Items.Add(it);
        menu.Items.Add(new ToolStripSeparator());
    }

    private async void StartUpdateCheck()
    {
        try
        {
            var r = await UpdateChecker.RunAsync(_cfg, AboutForm.Version, false);
            if (r.Status == UpdateStatus.Newer) NotifyUpdate();
        }
        catch { /* een mislukte controle mag nooit iets kapotmaken */ }
    }

    private void NotifyUpdate()
    {
        if (UpdateChecker.Available is not { } u || !_cfg.CheckUpdates) return;
        if (_cfg.UpdateNotifiedTag == u.Tag) return;
        _cfg.UpdateNotifiedTag = u.Tag;
        _cfg.Save();
        if (_cfg.Notifications)
            Alert("update", 0, Loc.Pick("Nieuwe versie beschikbaar", "New version available"),
                  Loc.Pick($"TaskbarStats {u.Tag.TrimStart('v', 'V')} is uit. Open het menu van het widget om de pagina te bekijken.",
                           $"TaskbarStats {u.Tag.TrimStart('v', 'V')} is out. Open the widget menu to view the page."));
    }

    // ---------- Haken vanuit WidgetForm.cs ----------
    // Bij het opstarten (OnShown).
    private void ActionsOnShown()
    {
        ApplyWidgetClickThrough();
        if (_hotkeyClickFailed)
            Alert("wclickkey", 0, "TaskbarStats",
                  Loc.Pick("Sneltoets Ctrl+Alt+W is al in gebruik door een ander programma; klik-door van het widget bedien je via het pictogram bij de klok.",
                           "Hotkey Ctrl+Alt+W is already used by another program; control widget click-through from the tray icon."));
        UpdateChecker.Restore(_cfg, AboutForm.Version);
        if (!_updHooked)
        {
            _updHooked = true;
            UpdateChecker.Found += () => { if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(NotifyUpdate)); };
            _updTimer = new System.Windows.Forms.Timer { Interval = 3_600_000 };   // controle zelf houdt 24 uur aan
            _updTimer.Tick += (_, _) => StartUpdateCheck();
            _updTimer.Start();
        }
        StartUpdateCheck();
        ApplyExtraTemps();
    }

    // Na elke wijziging in de instellingen (ApplyAll).
    private void ApplyExtras()
    {
        ApplyWidgetClickThrough();
        ApplyExtraTemps();
        UpdateChecker.Restore(_cfg, AboutForm.Version);
        StartUpdateCheck();
    }

    // Temperatuurmelding nodig? Dan moet het meten van temperaturen aan staan (alleen aanzetten, nooit uit).
    private void ApplyExtraTemps()
    {
        if (_cfg.AlertCpuTempC > 0 || _cfg.AlertGpuTempC > 0) _metrics.EnableTemperatures(true);
    }
}
