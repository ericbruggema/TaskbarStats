using Microsoft.Win32;

namespace TaskbarStats;

// Verandert het scherm terwijl het programma draait (inloggen via VNC/RDP: eerst 1024x768 en daarna de echte resolutie, een monitor
// erbij of eraf, andere schaling), dan staat het widget op een pixelpositie die bij het vorige scherm hoorde. Dat corrigeren we hier.
public sealed partial class WidgetForm
{
    private List<ScreenInfo> _screens = ScreenMap.Capture();
    private System.Windows.Forms.Timer? _displayTimer;
    private int _displayPass;
    private bool _displayHooked;

    private void DisplayHook()
    {
        if (_displayHooked) return;
        _displayHooked = true;
        _screens = ScreenMap.Capture();
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    private void DisplayUnhook()
    {
        if (!_displayHooked) return;
        _displayHooked = false;
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _displayTimer?.Stop(); _displayTimer?.Dispose(); _displayTimer = null;
    }

    // Beide komen binnen op de SystemEvents-thread.
    private void OnDisplayChanged(object? sender, EventArgs e) => ScheduleDisplayFix();

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        // (opnieuw) verbinden of ontgrendelen: de resolutie kan dan anders zijn dan toen je wegging
        if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.RemoteConnect or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.SessionLogon)
            ScheduleDisplayFix();
    }

    private void ScheduleDisplayFix()
    {
        try
        {
            if (!IsHandleCreated || IsDisposed) return;
            BeginInvoke(() =>
            {
                _displayPass = 0;
                if (_displayTimer is null)
                {
                    _displayTimer = new System.Windows.Forms.Timer();
                    _displayTimer.Tick += (_, _) => DisplayFixStep();
                }
                _displayTimer.Stop();
                _displayTimer.Interval = 700;   // wissels komen vaak als een reeks gebeurtenissen: even laten bezinken
                _displayTimer.Start();
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException) { Diag.Swallow(ex); }
    }

    private void DisplayFixStep()
    {
        _displayTimer?.Stop();
        if (IsDisposed) return;
        var now = ScreenMap.Capture();
        if (!ScreenMap.Same(_screens, now))
        {
            Diag.Info("position", $"Schermen veranderd: {ScreenMap.Describe(_screens)} -> {ScreenMap.Describe(now)}");
            if (!_dragging)
            {
                var p = ScreenMap.Remap(Bounds, _screens, now, workArea: false);
                if (p != Location)
                {
                    Location = p;
                    _cfg.FloatX = p.X; _cfg.FloatY = p.Y;
                    _cfg.Save();
                }
            }
            _dash?.RemapAfterDisplayChange(_screens, now);
            _screens = now;
        }
        // De taakbalk en het systeemvak komen na zo'n wissel pas na een tijdje op hun plek: de hoogte en (bij 'vastplakken') de plek
        // volgen en daarna nog twee keer controleren.
        if (_cfg.AutoHeight) ApplyHeight();
        if (_cfg.StickToTray) FollowTray(true);
        Render();
        if (++_displayPass <= 2 && _displayTimer is not null)
        {
            _displayTimer.Interval = _displayPass == 1 ? 2500 : 6000;
            _displayTimer.Start();
        }
    }
}
