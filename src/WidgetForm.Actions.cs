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
                try { Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true }); } catch (Exception dex) { Diag.Swallow(dex); }
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
                      ? Loc.T("Widget: click-through on. Turn it off with a right-click on the tray icon.")
                      : Loc.T("Widget: click-through on. Turn it off with Ctrl+Alt+W or a right-click on the tray icon."));
    }

    private void RegisterClickHotkey()
    {
        // Bezet door een ander programma? Dan blijft het pictogram bij de klok de bediening (melding bij het opstarten).
        _hotkeyClickFailed = !RegisterHotKey(Handle, HotkeyClickId, 0x0001 | 0x0002 /* ALT | CTRL */, 0x57 /* W */);
    }

    private void UnregisterClickHotkey() => UnregisterHotKey(Handle, HotkeyClickId);

    private void AddClickThroughMenuItem(ContextMenuStrip menu)
    {
        var it = new ToolStripMenuItem(Loc.T("Widget click-through (Ctrl+Alt+W)"))
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

        if (_cfg.AlertMemPercent > 0 && _snap.MemPercent >= _cfg.AlertMemPercent)
            Alert("mem>", 30 * 60_000L, Loc.T("High memory use"),
                  Loc.T("Memory is {0:0}% full (threshold {1}%)", _snap.MemPercent, _cfg.AlertMemPercent));

        if (_cfg.AlertCpuTempC > 0 && _snap.CpuTempC is double ct && ct >= _cfg.AlertCpuTempC)
            Alert("cputemp", 15 * 60_000L, Loc.T("High CPU temperature"),
                  Loc.T("The processor is at {0:0} °C (threshold {1} °C)", ct, _cfg.AlertCpuTempC));

        if (_cfg.AlertGpuTempC > 0 && _snap.GpuTempC is double gt && gt >= _cfg.AlertGpuTempC)
            Alert("gputemp", 15 * 60_000L, Loc.T("High GPU temperature"),
                  Loc.T("The graphics card is at {0:0} °C (threshold {1} °C)", gt, _cfg.AlertGpuTempC));

        if (_cfg.AlertDiskTempC > 0 && _snap.DiskTempC is double dt && dt >= _cfg.AlertDiskTempC)
            Alert("disktemp", 15 * 60_000L, Loc.T("High drive temperature"),
                  Loc.T("The drive is at {0:0} °C (threshold {1} °C)", dt, _cfg.AlertDiskTempC));

        if (_cfg.AlertDayNetGb > 0)
        {
            double gb = _usage.Today(_cfg.NetworkAdapter).Total / 1073741824.0;
            if (gb >= _cfg.AlertDayNetGb)
                Alert("daynet:" + DateTime.Now.ToString("yyyyMMdd"), 20 * 3_600_000L, Loc.T("Daily network use"),
                      Loc.T("{0:0.#} GB used today (threshold {1} GB)", gb, _cfg.AlertDayNetGb));
        }
    }

    // ---------- Updatecontrole ----------
    private System.Windows.Forms.Timer? _updTimer;
    private bool _updHooked;

    private void AddUpdateMenuItem(ContextMenuStrip menu)
    {
        if (!_cfg.CheckUpdates || UpdateChecker.Available is not { } u) return;
        var it = new ToolStripMenuItem(Loc.T("New version {0} available", u.Tag.TrimStart('v', 'V')))
            { Tag = "close", Font = new Font(Font, FontStyle.Bold) };
        it.Click += (_, _) => { try { Process.Start(new ProcessStartInfo(u.Url) { UseShellExecute = true }); } catch (Exception dex) { Diag.Swallow(dex); } };
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
        catch (Exception dex) { Diag.Swallow(dex); /* een mislukte controle mag nooit iets kapotmaken */ }
    }

    private void NotifyUpdate()
    {
        if (UpdateChecker.Available is not { } u || !_cfg.CheckUpdates) return;
        if (_cfg.UpdateNotifiedTag == u.Tag) return;
        _cfg.UpdateNotifiedTag = u.Tag;
        _cfg.Save();
        if (_cfg.Notifications)
            Alert("update", 0, Loc.T("New version available"),
                  Loc.T("TaskbarStats {0} is out. Open the widget menu to view the page.", u.Tag.TrimStart('v', 'V')));
    }

    // ---------- Haken vanuit WidgetForm.cs ----------
    // Bij het opstarten (OnShown).
    private void ActionsOnShown()
    {
        ApplyWidgetClickThrough();
        if (_hotkeyClickFailed)
            Alert("wclickkey", 0, "TaskbarStats",
                  Loc.T("Hotkey Ctrl+Alt+W is already used by another program; control widget click-through from the tray icon."));
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
