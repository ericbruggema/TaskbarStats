using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace TaskbarStats;

/// <summary>
/// Het widget: tekent per onderdeel een cel (digitaal, meter of balk), met
/// instelbare kleuren en waarschuwingsdrempels. Rechtermuisknop = instellingen.
/// </summary>
public sealed partial class WidgetForm : Form
{
    private readonly AppSettings _cfg;
    private readonly Metrics _metrics = new();
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly System.Windows.Forms.Timer _topTimer = new();
    private Font _font = null!;
    private Font _fontSmall = null!;
    private ContextMenuStrip _menu = null!;
    private readonly System.Windows.Forms.Timer _menuTimer = new();
    private int _menuOutsideTicks;
    private readonly ToolTip _tip = new() { AutoPopDelay = 30000, InitialDelay = 400, ReshowDelay = 100 };

    // De tooltip verschijnt pas nadat de muis een instelbare tijd (minimaal 2 s) op het widget heeft gestaan; zo heb je tijd
    // om rechts te klikken zonder dat hij ervoor staat. We tonen hem zelf (ToolTip.Show) in plaats van de ingebouwde vertraging.
    private long _hoverAt;
    private bool _tipShown;

    private void CheckTooltip()
    {
        if (!_hover || _menu.Visible || _dragging || _cfg.TooltipDelayMs < 0) { HideTip(); return; }
        if (_tipShown) return;
        if (Environment.TickCount64 - _hoverAt < Math.Max(2000, _cfg.TooltipDelayMs)) return;
        _tipText = BuildTooltip();
        PlaceTip(_tipText);
        _tipShown = true;
    }

    private void HideTip()
    {
        if (!_tipShown) return;
        _tipShown = false;
        _tipText = "";
        _tip.Hide(this);
    }

    // Boven het widget tonen (of eronder als daar geen ruimte is) en binnen het scherm houden.
    private void PlaceTip(string t)
    {
        var sz = TextRenderer.MeasureText(t, SystemFonts.MessageBoxFont);
        int w = sz.Width + 16, h = sz.Height + 12;
        var wa = Screen.FromControl(this).WorkingArea;
        int x = Math.Min(0, wa.Right - (Left + w));
        int y = -(h + 6);
        if (Top + y < wa.Top) y = Height + 6;
        _tip.Show(t, this, x, y);
    }
    private string _tipText = "";
    private readonly List<(ToolStripItem item, Func<string> text)> _live = new();
    private List<DriveSpace> _drives = new();
    private long _drivesAt;
    private Point _dragStart;
    private bool _dragging;

    private readonly UsageTracker _usage;
    private readonly ProcessSampler _procs = new();
    private LogForm? _logForm;
    private bool _hover;
    private long _procAt;
    private bool _hidden;   // tijdelijk verborgen door een volledig-scherm-app

    private readonly NotifyIcon _notify = new() { Icon = SystemIcons.Warning, Text = "TaskbarStats" };
    private readonly System.Windows.Forms.Timer _notifyHide = new() { Interval = 15000 };
    private readonly Dictionary<string, long> _alertAt = new();
    private readonly Dictionary<string, long> _critSince = new();

    // Bureaublad-dashboard
    private DashboardForm? _dash;
    private readonly MetricHistory _history = new();
    private readonly NotifyIcon _tray = new() { Icon = AppIcon.Small, Text = "TaskbarStats" };

    // Het venster is altijd een 'layered window' met per-pixel alpha. Bij een transparante
    // achtergrond tekenen we alpha=1 (onzichtbaar, maar wel klikbaar). Met een TransparencyKey
    // werd de rechtermuisknop op de doorzichtige pixels doorgegeven aan het venster eronder,
    // waardoor het menu vaak niet verscheen.
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00080000 /* WS_EX_LAYERED */ | 0x00000080 /* WS_EX_TOOLWINDOW */;
            return cp;
        }
    }

    public WidgetForm(AppSettings cfg)
    {
        _cfg = cfg;
        _usage = new UsageTracker(Path.GetDirectoryName(_cfg.FilePath)!);
        Loc.Lang = _cfg.Language;
        ApplyFonts();

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
        BackColor = C(_cfg.BackgroundColor, Color.FromArgb(20, 20, 20));
        Height = TargetHeight();
        Width = 240;

        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        UpdateStyles();

        ApplyTransparency();
        _metrics.SetNetworkAdapter(_cfg.NetworkAdapter);
        _metrics.SetGpuLuid(_cfg.GpuLuid);
        _metrics.EnableTemperatures(_cfg.ShowCpuTemp || _cfg.ShowGpuTemp);
        ApplyExtraWanted();
        _metrics.Ping.Configure(_cfg.ShowPing, _cfg.PingHost);
        _metrics.SetCpuMode(_cfg.CpuUtility);
        StartSampler();

        BuildContextMenu();
        _tray.ContextMenuStrip = _menu;                       // pictogram naast de klok: zelfde menu (ook bij klik-door)
        _tray.DoubleClick += (_, _) => ToggleClickThrough();

        _timer.Interval = Math.Max(50, _cfg.RefreshMs);
        _timer.Tick += (_, _) => Tick();
        _timer.Start();

        _topTimer.Interval = 200;
        _topTimer.Tick += (_, _) => OnTopTick();
        _topTimer.Start();
        Deactivate += (_, _) => KeepOnTop();

        _menuTimer.Interval = 250;
        _menuTimer.Tick += (_, _) => CheckMenuHover();

        _notifyHide.Tick += (_, _) => { _notifyHide.Stop(); _notify.Visible = false; };

        MouseEnter += (_, _) => { _hover = true; _hoverAt = Environment.TickCount64; _procAt = _hoverAt; _procs.SampleAsync(); };
        MouseLeave += (_, _) => { _hover = false; HideTip(); _procs.Reset(); };
        MouseDoubleClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            RunMouseAction(_cfg.DoubleClickAction);
        };
        MouseDown += OnMouseDown;
        _fx.Tick += (_, _) => FxTick();
        Cadence.Hook = mode => { if (IsHandleCreated) BeginInvoke(new Action(() => FxStart(mode))); };
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Height = TargetHeight();
        if (_cfg.FloatX is null || _cfg.FloatY is null)
            (_cfg.FloatX, _cfg.FloatY) = ComputeDefaultPosition();
        Location = new Point(_cfg.FloatX!.Value, _cfg.FloatY!.Value);
        // Stond het widget op een scherm dat er niet meer is? Dan terug naar de standaardpositie.
        if (!Screen.AllScreens.Any(sc => sc.Bounds.IntersectsWith(Bounds)))
        {
            (_cfg.FloatX, _cfg.FloatY) = ComputeDefaultPosition();
            Location = new Point(_cfg.FloatX!.Value, _cfg.FloatY!.Value);
        }
        TrySetTaskbarOwner();
        Tick();
        if (!_cfg.WelcomeShown) BeginInvoke(new Action(ShowWelcome));
        SyncDashboard();
        ActionsOnShown();
    }

    private WelcomeForm? _welcome;
    private CreditsForm? _credits;
    private readonly System.Windows.Forms.Timer _fx = new() { Interval = 40 };
    private Point _fxBase;
    private bool _fxOn;

    private void ShowWelcome()
    {
        if (_welcome is { IsDisposed: false }) { _welcome.Activate(); return; }
        _welcome = new WelcomeForm(code => { _cfg.Language = code; Loc.Lang = code; Persist(); },
            _cfg.CheckUpdates, v => { _cfg.CheckUpdates = v; Persist(); ApplyExtras(); });
        _welcome.Show();
        if (!_cfg.WelcomeShown) { _cfg.WelcomeShown = true; _cfg.Save(); }   // eenmalig
    }

    // Maak het widget een 'owned window' van de taakbalk: het blijft dan altijd boven
    // de taakbalk, ook wanneer je die aanwijst (geen knipperen/verdwijnen meer).
    private void TrySetTaskbarOwner()
    {
        var tb = FindWindow("Shell_TrayWnd", null);
        if (tb != IntPtr.Zero) SetWindowLongPtr(Handle, GWLP_HWNDPARENT, tb);
    }

    private const int GWLP_HWNDPARENT = -8;
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern IntPtr FindWindow(string? cls, string? title);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr newLong);

    private void Tick()
    {
        // Metingen (PerformanceCounters, ~20 ms) en verbruik bijhouden (~7 ms) draaien op de sampler-thread; de UI-thread
        // tekent hier alleen met de meest recente waarden, zodat slepen en het menu soepel blijven.
        _idle = !Visible && _dash is not { Visible: true } && _full is null;
        _metrics.Ping.Idle = _idle;
        if (_idle) { CheckAlerts(); return; }
        _history.Sample(_metrics);
        SampleGraph();
        if (_hover && Environment.TickCount64 - _procAt >= 700) { _procAt = Environment.TickCount64; _procs.SampleAsync(); }
        RefreshDrives(false);
        CheckAlerts();
        if (_menu.Visible) UpdateLiveItems();
        else UpdateTooltip();
        Render();
        _dash?.Tick();
        // Ongebruikt geheugen teruggeven aan Windows (de werkset kruipt anders op door het tekenen en de runtime).
        if (Environment.TickCount64 - _trimAt > 60_000) { _trimAt = Environment.TickCount64; SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1); }
    }

    [DllImport("kernel32.dll")] private static extern bool SetProcessWorkingSetSize(IntPtr process, nint min, nint max);

    // ---------- Sampler-thread ----------
    private volatile bool _idle;
    private long _trimAt;
    private Thread? _samplerThread;
    private readonly ManualResetEventSlim _stopSampler = new(false);

    private void StartSampler()
    {
        _samplerThread = new Thread(() =>
        {
            long lastUsage = 0;
            while (!_stopSampler.IsSet)
            {
                long t0 = Environment.TickCount64;
                // Niemand kijkt (widget verborgen, geen dashboard/fullscreen): zeldzamer meten (meldingen blijven werken).
                try { _metrics.Update(); } catch { }
                if (t0 - lastUsage >= 5000) { lastUsage = t0; try { _usage.Sample(); } catch { } }
                int wait = _idle ? 5000 : Math.Max(20, _cfg.RefreshMs - (int)(Environment.TickCount64 - t0));
                _stopSampler.Wait(wait);
            }
        })
        { IsBackground = true, Name = "TaskbarStats sampler", Priority = ThreadPriority.BelowNormal };
        _samplerThread.Start();
    }

    private void StopSampler()
    {
        _stopSampler.Set();
        _samplerThread?.Join(1500);
    }

    // Schijfruimte verandert langzaam; maximaal elke 5 s opnieuw opvragen (of geforceerd bij het menu).
    private void RefreshDrives(bool force)
    {
        if (!force && Environment.TickCount64 - _drivesAt < 5000) return;
        _drivesAt = Environment.TickCount64;
        _fixedDrives = Metrics.GetDriveSpaces();
        if (_cfg.IncludeNetworkDrives) RefreshNetworkDrives(); else _netDrives = new();
        _drives = _fixedDrives.Concat(_netDrives).ToList();
    }

    private List<DriveSpace> _fixedDrives = new(), _netDrives = new();
    private bool _netDrivesBusy;

    // Netwerkschijven kunnen traag of onbereikbaar zijn: op de achtergrond opvragen zodat de app nooit blokkeert.
    private void RefreshNetworkDrives()
    {
        if (_netDrivesBusy) return;
        _netDrivesBusy = true;
        Task.Run(() =>
        {
            var result = Metrics.GetDriveSpaces(DriveType.Network);
            try
            {
                BeginInvoke(new Action(() =>
                {
                    _netDrives = result;
                    _netDrivesBusy = false;
                    _drives = _fixedDrives.Concat(_netDrives).ToList();
                }));
            }
            catch { _netDrivesBusy = false; }
        });
    }

    private void UpdateLiveItems()
    {
        foreach (var (item, text) in _live)
        {
            string t = text();
            if (item.Text != t) item.Text = t;
        }
    }

    private void UpdateTooltip()
    {
        if (!_tipShown) return;
        string t = BuildTooltip();
        if (t == _tipText) return;
        _tipText = t;
        PlaceTip(t);
    }

    private string BuildTooltip(bool forceProcs = false)
    {
        var m = _metrics;
        var sb = new StringBuilder();
        string mhz = m.CpuMHz is double f ? $"  @ {f / 1000:0.00} GHz" : "";
        sb.AppendLine($"CPU  {m.CpuPercent:0}%{mhz}");
        foreach (var (luid, v) in m.GpuPerLuid.OrderBy(k => k.Key))
        {
            if (luid == "" || !Metrics.IsRealGpu(luid)) continue;
            string vram = "";
            long ded = Metrics.GpuDedicatedBytes(luid);
            if (ded > 0 && m.VramUsedPerLuid.TryGetValue(luid, out var used))
                vram = $"  VRAM {Metrics.FormatSize(used)} / {Metrics.FormatSize(ded)}";
            sb.AppendLine($"GPU  {Metrics.GpuName(luid)}  {v:0}%{vram}");
        }
        sb.AppendLine($"{Loc.S("memory")}  {Metrics.FormatSize(m.MemUsedBytes)} / {Metrics.FormatSize(m.MemTotalBytes)} ({m.MemPercent:0}%)");
        if (m.BatteryPresent)
        {
            string state = m.BatteryCharging ? Loc.Pick("laden", "charging")
                         : m.BatteryOnAc ? Loc.Pick("op netstroom", "plugged in")
                         : Loc.Pick("ontladen", "on battery");
            string left = !m.BatteryOnAc && m.BatteryRemainingSec > 0
                ? $"  ·  {m.BatteryRemainingSec / 3600}{Loc.Pick("u", "h")} {m.BatteryRemainingSec % 3600 / 60:00}m {Loc.Pick("resterend", "left")}" : "";
            sb.AppendLine($"{Loc.Pick("Batterij", "Battery")}  {m.BatteryPercent:0}%  {state}{left}");
        }
        if (m.CpuTempC is double ct) sb.AppendLine($"CPU  {ct:0}°C");
        if (m.GpuTempC is double gt) sb.AppendLine($"GPU  {gt:0}°C");
        AppendExtraTooltip(sb);
        if (_cfg.ShowPing)
        {
            var ps = m.Ping.Stats();
            sb.AppendLine($"Ping  {ps.Host}  {ps.LastText}");
            if (ps.Count > 1) sb.AppendLine($"   {ps.Details}");
        }

        if (_hover || forceProcs)
        {
            sb.AppendLine();
            sb.AppendLine("Top CPU:  " + (_procs.HasCpu
                ? string.Join(",  ", _procs.TopCpu.Select(p => $"{p.name} {p.cpu:0}%")) : "…"));
            sb.AppendLine("Top RAM:  " + string.Join(",  ", _procs.TopMem.Select(p => $"{p.name} {Metrics.FormatSize(p.mem)}")));
        }

        sb.AppendLine();
        sb.AppendLine($"{Loc.S("upload")}/{Loc.S("download")}  ↑ {Metrics.FormatRate(m.NetUpBytesPerSec)}  ↓ {Metrics.FormatRate(m.NetDownBytesPerSec)}");
        foreach (var (label, u) in new[]
        {
            (Loc.Pick("Sessie", "Session"), _usage.Session()),
            (Loc.Pick("Vandaag", "Today"), _usage.Today()),
            (Loc.Pick("Deze maand", "This month"), _usage.Month()),
        })
            sb.AppendLine($"   {label}  ↓ {Metrics.FormatBytes(u.Down)}  ↑ {Metrics.FormatBytes(u.Up)}");
        if (_cfg.MonthlyLimitGb > 0)
        {
            var mu = _usage.Month(_cfg.NetworkAdapter);
            sb.AppendLine($"   {Loc.Pick("Maandlimiet", "Monthly limit")}  {Metrics.FormatBytes(mu.Total)} / {_cfg.MonthlyLimitGb} GB ({100.0 * mu.Total / (_cfg.MonthlyLimitGb * 1073741824.0):0}%)");
        }
        sb.AppendLine();
        foreach (var (name, r) in m.NetPerAdapter.OrderBy(k => k.Key))
        {
            var td = _usage.Today(name);
            sb.AppendLine($"   {name}  ↓ {Metrics.FormatRate(r.down)}  ↑ {Metrics.FormatRate(r.up)}   |   {Loc.Pick("vandaag", "today")} ↓ {Metrics.FormatBytes(td.Down)}  ↑ {Metrics.FormatBytes(td.Up)}");
        }

        sb.AppendLine();
        sb.AppendLine($"{Loc.S("diskIo")}  R {Metrics.FormatRate(m.DiskReadBytesPerSec)}  W {Metrics.FormatRate(m.DiskWriteBytesPerSec)}");
        foreach (var (name, r) in m.DiskPerDisk.OrderBy(k => k.Key))
            sb.AppendLine($"   {name}  R {Metrics.FormatRate(r.read)}  W {Metrics.FormatRate(r.write)}");
        foreach (var d in _drives)
            sb.AppendLine($"   {DriveLine(d)}");
        return sb.ToString().TrimEnd();
    }

    // ---------- Meldingen ----------
    private void CheckAlerts()
    {
        if (!_cfg.Notifications || _hidden || Cadence.Active) return;
        long now = Environment.TickCount64;

        void crit(string name, double v)
        {
            if (v >= _cfg.CritThreshold)
            {
                if (!_critSince.TryGetValue(name, out long since)) _critSince[name] = now;
                else if (now - since >= _cfg.CritSeconds * 1000L)
                    Alert("crit:" + name, 15 * 60_000, Loc.Pick("Hoge belasting", "High load"),
                          Loc.Pick($"{name} staat al {_cfg.CritSeconds} s op {v:0}%", $"{name} has been at {v:0}% for {_cfg.CritSeconds} s"));
            }
            else _critSince.Remove(name);
        }
        if (_cfg.ShowCpu) crit("CPU", _metrics.CpuPercent);
        if (_cfg.ShowGpu) crit("GPU", _metrics.GpuPercent);
        if (_cfg.ShowMem) crit(Loc.S("memory"), _metrics.MemPercent);

        if (_metrics.BatteryPresent && !_metrics.BatteryOnAc)
        {
            double bp = _metrics.BatteryPercent;
            string bt = Loc.Pick("Batterij bijna leeg", "Battery low");
            if (bp <= 10)
                Alert("bat10", 20 * 60_000L, bt, Loc.Pick($"Nog {bp:0}% — sluit de lader aan", $"{bp:0}% left — plug in the charger"));
            else if (bp <= 20)
                Alert("bat20", 30 * 60_000L, bt, Loc.Pick($"Nog {bp:0}% over", $"{bp:0}% remaining"));
        }

        foreach (var d in _drives)
            if (d.UsedPercent >= _cfg.DiskFullPercent)
                Alert("disk:" + d.Name, 6 * 3_600_000L, Loc.Pick("Schijf bijna vol", "Disk almost full"),
                      Loc.Pick($"{d.Name} is {d.UsedPercent:0}% vol, nog {Metrics.FormatSize(d.Free)} vrij",
                               $"{d.Name} is {d.UsedPercent:0}% full, {Metrics.FormatSize(d.Free)} free"));

        if (_cfg.MonthlyLimitGb > 0)
        {
            double pct = 100.0 * _usage.Month(_cfg.NetworkAdapter).Total / (_cfg.MonthlyLimitGb * 1073741824.0);
            string month = DateTime.Now.ToString("yyyyMM");
            string title = Loc.Pick("Datalimiet", "Data limit");
            if (pct >= 100)
                Alert("limit100:" + month, 40L * 86_400_000, title,
                      Loc.Pick($"Maandlimiet van {_cfg.MonthlyLimitGb} GB is bereikt", $"Monthly limit of {_cfg.MonthlyLimitGb} GB reached"));
            else if (pct >= 80)
                Alert("limit80:" + month, 40L * 86_400_000, title,
                      Loc.Pick($"{pct:0}% van de maandlimiet ({_cfg.MonthlyLimitGb} GB) gebruikt", $"{pct:0}% of the monthly limit ({_cfg.MonthlyLimitGb} GB) used"));
        }

        CheckExtraAlerts();
    }

    private void Alert(string key, long cooldownMs, string title, string text)
    {
        long now = Environment.TickCount64;
        if (_alertAt.TryGetValue(key, out long at) && now - at < cooldownMs) return;
        _alertAt[key] = now;
        _notify.BalloonTipTitle = title;
        _notify.BalloonTipText = text;
        _notify.BalloonTipIcon = ToolTipIcon.Warning;
        _notify.Visible = true;
        _notify.ShowBalloonTip(10000);
        _notifyHide.Stop();
        _notifyHide.Start();
    }

    // ---------- Volledig scherm ----------
    private void OnTopTick()
    {
        CheckTooltip();
        if (_cfg.AutoHeight && Environment.TickCount64 - _heightCheckedAt > 5000) ApplyHeight();
        bool fs = _cfg.HideInFullscreen && IsFullscreenAppRunning();
        _dash?.SetSuppressed(fs && _cfg.DashFront);
        if (fs != _hidden)
        {
            _hidden = fs;
            ShowWindow(Handle, fs ? SW_HIDE : SW_SHOWNOACTIVATE);
            if (!fs) Render();
        }
        KeepOnTop();
    }

    // QUNS_BUSY (2), QUNS_RUNNING_D3D_FULL_SCREEN (3), QUNS_PRESENTATION_MODE (4)
    private static bool IsFullscreenAppRunning()
    {
        try { return SHQueryUserNotificationState(out int st) == 0 && st is 2 or 3 or 4; }
        catch { return false; }
    }

    private const int SW_HIDE = 0, SW_SHOWNOACTIVATE = 4;
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);

    // Alle tooltip-informatie (plus tijdstip en versie) naar het klembord.
    private void CopyInfo()
    {
        try
        {
            _procs.Sample();
            string text = $"TaskbarStats {AboutForm.Version} - {DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\n\r\n" +
                          BuildTooltip(true).Replace("\r\n", "\n").Replace("\n", "\r\n");
            Clipboard.SetText(text);
            _tip.Show(Loc.Pick("✔ Gekopieerd naar klembord", "✔ Copied to clipboard"), this, 0, -Height, 1500);
        }
        catch { /* klembord kan tijdelijk bezet zijn */ }
    }

    // ---------- Bureaublad-dashboard ----------
    private void SyncDashboard()
    {
        _dashShown = _cfg.ShowDashboard;
        if (_cfg.ShowDashboard)
        {
            if (_dash is null || _dash.IsDisposed)
            {
                _dash = new DashboardForm(MakeContext());
                _dash.FormClosed += (_, _) => _dash = null;
            }
            _dash.Present();
            _tray.Visible = true;
        }
        else
        {
            _dash?.HideByUser();
            _tray.Visible = _cfg.WidgetClickThrough;
        }
    }

    private DashContext MakeContext() => new()
    {
        Metrics = _metrics, Cfg = _cfg, Usage = _usage, History = _history,
        Drives = () => _drives, ShowMenu = p => _menu.Show(p), Nudge = Nudge,
    };

    // Tijdelijke effecten: snelle timer zolang ze lopen (trillen = het hele widget een paar pixels heen en weer, stijgen = vaker tekenen).
    private void FxStart(int mode)
    {
        if (!_fxOn) { _fxBase = Location; _fxOn = true; }
        _fx.Interval = mode == 1 ? 30 : 60;
        _fx.Start();
    }

    private void FxTick()
    {
        if (_dragging) { _fx.Stop(); _fxOn = false; return; }
        if (Cadence.Active)
        {
            if (Cadence.On(1)) { var j = Cadence.Jitter(); Location = new Point(_fxBase.X + j.X, _fxBase.Y + j.Y); }
            else Render();
            return;
        }
        _fx.Stop();
        if (_fxOn) { _fxOn = false; Location = _fxBase; }
        Render();
    }

    private void Nudge(int code)
    {
        if (code != 1) return;
        var t = Cadence.Look();
        t.ApplyTo(_cfg);
        try { ThemeStore.Write(ThemeStore.FileFor(_cfg, t.Name), t); } catch { }
        ApplyAll();
    }

    // ---------- Fullscreen dashboard ----------
    private FullscreenForm? _full;

    private void ToggleFullscreen()
    {
        if (_full is { IsDisposed: false }) { _full.Close(); return; }
        var screen = Screen.AllScreens.FirstOrDefault(s => s.DeviceName == _cfg.FullMonitor) ?? Screen.FromControl(this);
        _full = new FullscreenForm(MakeContext(), screen);
        _full.FormClosed += (_, _) => _full = null;
        _full.Show();
        _full.Activate();
    }

    private ToolStripMenuItem BuildFullMenu()
    {
        var open = new ToolStripMenuItem(Loc.Pick("Fullscreen dashboard (Ctrl+Alt+F)", "Fullscreen dashboard (Ctrl+Alt+F)")) { Tag = "close" };
        open.Click += (_, _) => ToggleFullscreen();
        return open;
    }

    // Fullscreen-tour: hoe lang elke pagina blijft staan (start in het fullscreen-scherm zelf).
    private ToolStripMenuItem BuildTourMenu()
    {
        var m = new ToolStripMenuItem(Loc.Pick("Fullscreen-tour: tijd per pagina", "Fullscreen tour: time per page"));
        var items = new List<(ToolStripMenuItem mi, int s)>();
        foreach (int secs in new[] { 5, 10, 15, 20, 30, 60 })
        {
            var mi = new ToolStripMenuItem($"{secs} s") { Checked = _cfg.TourSeconds == secs };
            mi.Click += (_, _) =>
            {
                _cfg.TourSeconds = secs;
                foreach (var (x, v) in items) x.Checked = v == secs;
                _cfg.Save();
            };
            items.Add((mi, secs));
            m.DropDownItems.Add(mi);
        }
        m.DropDownItems.Add(new ToolStripSeparator());
        m.DropDownItems.Add(new ToolStripMenuItem(Loc.Pick("Starten: 3× klikken op een lege plek (of spatie)", "Start: click 3× on an empty spot (or space)")) { Enabled = false });
        return m;
    }

    private void ToggleClickThrough()
    {
        _cfg.DashClickThrough = !_cfg.DashClickThrough;
        _cfg.Save();
        _dash?.ApplySettings();
        Alert("clickthrough" + Environment.TickCount64, 0, "TaskbarStats",
              _cfg.DashClickThrough
                  ? Loc.Pick("Dashboard: klik-door aan (Ctrl+Alt+D of dubbelklik op het pictogram om uit te zetten)",
                             "Dashboard: click-through on (Ctrl+Alt+D or double-click the tray icon to turn off)")
                  : Loc.Pick("Dashboard: klik-door uit", "Dashboard: click-through off"));
    }

    // Sneltoets Ctrl+Alt+D: klik-door van het dashboard aan/uit
    private const int HotkeyId = 0x7A51, HotkeyFullId = 0x7A52;
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RegisterHotKey(Handle, HotkeyId, 0x0001 | 0x0002 /* ALT | CTRL */, 0x44 /* D */);
        RegisterHotKey(Handle, HotkeyFullId, 0x0001 | 0x0002, 0x46 /* F */);
        RegisterClickHotkey();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        UnregisterHotKey(Handle, HotkeyId);
        UnregisterHotKey(Handle, HotkeyFullId);
        UnregisterClickHotkey();
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312 /* WM_HOTKEY */)
        {
            int id = m.WParam.ToInt32();
            if (id == HotkeyId) { ToggleClickThrough(); return; }
            if (id == HotkeyFullId) { ToggleFullscreen(); return; }
            if (id == HotkeyClickId) { ToggleWidgetClickThrough(); return; }
        }
        base.WndProc(ref m);
    }

    private ToolStripMenuItem BuildDashMenu()
    {
        var m = new ToolStripMenuItem(Loc.Pick("Bureaublad-dashboard", "Desktop dashboard"));
        AddCheck(m, Loc.Pick("Dashboard tonen", "Show dashboard"), _cfg.ShowDashboard,
                 v => { _cfg.ShowDashboard = v; SyncDashboard(); Persist(); });
        AddCheck(m, Loc.Pick("Klik-door (Ctrl+Alt+D)", "Click-through (Ctrl+Alt+D)"), _cfg.DashClickThrough,
                 v => { _cfg.DashClickThrough = v; Persist(); });
        AddCheck(m, Loc.Pick("Positie vergrendelen", "Lock position"), _cfg.DashLocked,
                 v => { _cfg.DashLocked = v; Persist(); });
        return m;
    }

    // ---------- Instellingenvenster ----------
    private SettingsForm? _settings;
    private bool _dashShown, _lastNetDrives;

    private void ResetWidgetPosition()
    {
        (_cfg.FloatX, _cfg.FloatY) = ComputeDefaultPosition();
        Location = new Point(_cfg.FloatX!.Value, _cfg.FloatY!.Value);
        Persist();
    }

    private void ShowSettings()
    {
        if (_settings is { IsDisposed: false }) { _settings.WindowState = FormWindowState.Normal; _settings.Activate(); return; }
        _settings = new SettingsForm(_cfg, new SettingsHost
        {
            Apply = ApplyAll,
            ResetDash = () => _dash?.ResetPosition(),
            ResetWidget = ResetWidgetPosition,
            ShowLog = ShowLog,
            Drives = () => _drives.ToList(),
        });
        _settings.FormClosed += (_, _) => _settings = null;
        _settings.Show();
    }

    /// <summary>Past alle instellingen toe op widget, dashboard en fullscreen-scherm (aangeroepen door het instellingenvenster).</summary>
    private void ApplyAll()
    {
        BackColor = C(_cfg.BackgroundColor, Color.Black);
        ApplyHeight();
        ApplyFonts();
        _metrics.EnableTemperatures(_cfg.ShowCpuTemp || _cfg.ShowGpuTemp);
        ApplyExtraWanted();
        _metrics.Ping.Configure(_cfg.ShowPing, _cfg.PingHost);
        _metrics.SetCpuMode(_cfg.CpuUtility);
        _metrics.SetGpuLuid(_cfg.GpuLuid);
        _metrics.SetNetworkAdapter(_cfg.NetworkAdapter);
        if (_cfg.IncludeNetworkDrives != _lastNetDrives) { _lastNetDrives = _cfg.IncludeNetworkDrives; RefreshDrives(true); }
        _timer.Interval = Math.Max(50, _cfg.RefreshMs);
        if (_cfg.ShowDashboard != _dashShown) { _dashShown = _cfg.ShowDashboard; SyncDashboard(); }
        ApplyExtras();
        Persist();
        _full?.Invalidate();
    }

    private void ShowLog()
    {
        if (_logForm is null || _logForm.IsDisposed) _logForm = new LogForm(_usage);
        _logForm.Show();
        _logForm.WindowState = FormWindowState.Normal;
        _logForm.Activate();
    }

    private static string DriveLine(DriveSpace d)
        => $"{d.Display}  {Metrics.FormatSize(d.Free)} {Loc.S("freeOf")} {Metrics.FormatSize(d.Total)} ({d.UsedPercent:0}% {Loc.S("usedWord")})";


    private long _lastRaise;

    // SetWindowPos(TOPMOST) op een venster dat eigendom is van de taakbalk (ander proces) blokkeert de UI-thread
    // 100-700 ms per aanroep; elke 200 ms uitvoeren gaf daarom merkbaar haperen (ook bij het verslepen van het
    // dashboard). Het widget is 'owned' door de taakbalk en blijft daardoor vanzelf erboven; we zetten het alleen
    // nog opnieuw bovenop als er echt een ander zichtbaar topmost-venster overheen staat, en hooguit elke 2 s.
    private void KeepOnTop()
    {
        if (_hidden) return;
        long now = Environment.TickCount64;
        if (now - _lastRaise < 2000 || !CoveredByTopmostWindow()) return;
        _lastRaise = now;
        SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private bool CoveredByTopmostWindow()
    {
        int self = Environment.ProcessId;
        IntPtr h = Handle;
        for (int guard = 0; guard < 300; guard++)
        {
            h = GetWindow(h, 3 /* GW_HWNDPREV */);
            if (h == IntPtr.Zero) return false;
            if (!IsWindowVisible(h)) continue;
            if ((GetWindowLong(h, -20 /* GWL_EXSTYLE */) & 0x8 /* WS_EX_TOPMOST */) == 0) return false;
            GetWindowThreadProcessId(h, out uint pid);
            if (pid == (uint)self) continue;                                                   // onze eigen vensters
            if (DwmGetWindowAttribute(h, 14 /* DWMWA_CLOAKED */, out int cloaked, 4) == 0 && cloaked != 0) continue;
            if (!GetWindowRect(h, out var r) || r.Right - r.Left < 20 || r.Bottom - r.Top < 20) continue;
            return true;
        }
        return false;
    }

    [StructLayout(LayoutKind.Sequential)] private struct WRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out WRect rect);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out int value, int size);

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010;
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    private (int x, int y) ComputeDefaultPosition()
    {
        if (TaskbarHost.TryGetTrayRect(out var tray))
            return (tray.Left - Width - _cfg.TrayGap, tray.Top + Math.Max(0, (tray.Height - Height) / 2));
        var b = Screen.PrimaryScreen!.Bounds;
        return (b.Right - Width - 220, b.Bottom - Height - 3);
    }

    private void ApplyTransparency() => Render();

    // ---------- Kleuren & drempels ----------
    private static Color C(string hex, Color fb)
    {
        try { return ColorTranslator.FromHtml(hex); } catch { return fb; }
    }

    private Color ThresholdColor(double v, Color baseColor)
    {
        if (v >= _cfg.CritThreshold) return C(_cfg.CritColor, Color.Red);
        if (v >= _cfg.WarnThreshold) return C(_cfg.WarnColor, Color.Orange);
        return baseColor;
    }

    // ---------- Tekenen ----------
    private BgLayer? _bg;

    private void Render()
    {
        if (!IsHandleCreated || !Visible || _hidden) return;
        float dpi = DeviceDpi;

        // Eerst meten (automatische breedte, vaste hoogte), dan pas op de juiste maat tekenen.
        int wanted;
        using (var probe = new Bitmap(1, 1, PixelFormat.Format32bppArgb))
        {
            probe.SetResolution(dpi, dpi);
            using var pg = Graphics.FromImage(probe);
            wanted = Math.Max(60, DrawAll(pg));
        }
        if (wanted != Width) { int r = Right; Width = wanted; Left = r - wanted; }

        using var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        bmp.SetResolution(dpi, dpi);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(_cfg.TransparentBackground
                ? Color.FromArgb(1, 0, 0, 0)
                : C(_cfg.BackgroundColor, Color.FromArgb(20, 20, 20)));
            // achtergrondafbeelding (onder de cellen; alleen in de echte tekenronde, niet in de meetronde)
            (_bg ??= new BgLayer(this, 512, Render)).Draw(g, _cfg.WidgetBgImage, _cfg.WidgetBgMode, _cfg.WidgetBgOpacity, Width, Height);
            DrawAll(g);
            Cadence.Cap(g, Width, Height);
            Cadence.Fool(g, Width, Height, _font);
            if (!string.IsNullOrWhiteSpace(_cfg.BorderColor))
            {
                using var pen = new Pen(C(_cfg.BorderColor, Color.Magenta), 1);
                g.SmoothingMode = SmoothingMode.None;
                g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }
        PushBitmap(bmp);
    }

    /// <summary>Tekent alle cellen en geeft de gewenste totale breedte terug.</summary>
    private int DrawAll(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

        int x = _cfg.Compact ? 4 : 6;
        int gap = Gap;
        var textCol = C(_cfg.TextColor, Color.White);
        foreach (var id in Tiles.WidgetOrder(_cfg.WidgetOrder))
        {
            switch (id)
            {
                case "net":
                    if (_cfg.ShowNetUp || _cfg.ShowNetDown) x += DrawNetwork(g, x) + gap;
                    break;
                case "ping":
                    if (_cfg.ShowPing) x += DrawPing(g, x) + gap;
                    break;
                case "disk":
                    if (_cfg.ShowDisk) x += DrawDisk(g, x) + gap;
                    break;
                case "cpu":
                    if (_cfg.ShowCpu)
                    {
                        x += _cfg.CpuPerCore ? DrawCores(g, x, "CPU", _metrics.CpuCores) : DrawMetric(g, x, "CPU", _metrics.CpuPercent, _cfg.CpuStyle);
                        if (_cfg.TempMerge && _cfg.ShowCpuTemp && _metrics.CpuTempC is double mct) x += DrawTempTag(g, x, mct, textCol);
                        x += gap;
                    }
                    break;
                case "gpu":
                    if (_cfg.ShowGpu)
                    {
                        x += DrawMetric(g, x, "GPU", _metrics.GpuPercent, _cfg.GpuStyle);
                        if (_cfg.TempMerge && _cfg.ShowGpuTemp && _metrics.GpuTempC is double mgt) x += DrawTempTag(g, x, mgt, textCol);
                        x += gap;
                    }
                    break;
                case "mem":
                    if (_cfg.ShowMem) x += DrawMem(g, x) + gap;
                    break;
                case "batt":
                    if (_cfg.ShowBattery && _metrics.BatteryPresent) x += DrawBattery(g, x) + gap;
                    break;
                case "space":
                    foreach (var (label, value, pct) in DiskSpaceCells())
                        x += DrawTextCell(g, x, label, value, PctTemplate, ThresholdColor(pct, textCol)) + gap;
                    break;
                case "cputemp":
                    if (_cfg.ShowCpuTemp && !(_cfg.TempMerge && _cfg.ShowCpu) && _metrics.CpuTempC is double ct)
                        x += DrawTemp(g, x, "CPU", ct, _cfg.CpuTempStyle, textCol) + gap;
                    break;
                case "gputemp":
                    if (_cfg.ShowGpuTemp && !(_cfg.TempMerge && _cfg.ShowGpu) && _metrics.GpuTempC is double gt)
                        x += DrawTemp(g, x, "GPU", gt, _cfg.GpuTempStyle, textCol) + gap;
                    break;
                case "cpufreq": case "diskbusy": case "disktemp": case "mobotemp":
                    { int ew = DrawExtraItem(g, x, id); if (ew > 0) x += ew + gap; }
                    break;
            }
        }
        return x;
    }

    private void PushBitmap(Bitmap bmp)
    {
        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memDc = CreateCompatibleDC(screenDc);
        IntPtr hBmp = bmp.GetHbitmap(Color.FromArgb(0));
        IntPtr old = SelectObject(memDc, hBmp);
        try
        {
            var size = new Size(bmp.Width, bmp.Height);
            var src = new Point(0, 0);
            var dst = new Point(Left, Top);
            var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, 2);
        }
        finally
        {
            SelectObject(memDc, old);
            DeleteObject(hBmp);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref Point pptDst, ref Size psize,
        IntPtr hdcSrc, ref Point pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);

    // Tekent 'actual' maar reserveert de breedte van 'template' (vaste breedte).
    private int DrawDigital(Graphics g, int x, string actual, string template, Color color)
    {
        float reserve = g.MeasureString(template, _font).Width;
        var sz = g.MeasureString(actual, _font);
        using var b = new SolidBrush(color);
        g.DrawString(actual, _font, b, x, (Height - sz.Height) / 2);
        return (int)Math.Ceiling(reserve);
    }

    private int Gap => _cfg.Compact ? 6 : 10;

    // Schaalfactor t.o.v. de basishoogte van 40 px: lettertypes en balken groeien mee.
    private double UiScale => Math.Clamp(Height / 40.0, 0.85, 1.4);

    // Hoogte: automatisch = taakbalkhoogte (minus wat marge), anders de vaste waarde.
    private int TargetHeight()
    {
        if (_cfg.AutoHeight && TaskbarHost.TryGetTaskbarRect(out var r) && r.Width >= r.Height)
            return Math.Clamp(r.Height - 4, 28, 80);
        return Math.Clamp(_cfg.WidgetHeight, 28, 80);
    }

    private long _heightCheckedAt;

    private void ApplyHeight()
    {
        _heightCheckedAt = Environment.TickCount64;
        int h = TargetHeight();
        if (h == Height) return;
        int dh = h - Height;
        Height = h;
        if (IsHandleCreated) { Top -= dh / 2; _cfg.FloatY = Top; }   // verticaal gecentreerd blijven
        ApplyFonts();
        Render();
    }

    private void ApplyFonts()
    {
        double scale = Math.Clamp(TargetHeight() / 40.0, 0.85, 1.4);
        float size = (float)Math.Max(6, Math.Round((_cfg.FontSize - (_cfg.Compact ? 1 : 0)) * scale * 2) / 2);
        _font?.Dispose();
        _fontSmall?.Dispose();
        _font = new Font(_cfg.FontFamily, size, FontStyle.Regular, GraphicsUnit.Point);
        _fontSmall = new Font(_cfg.FontFamily, Math.Max(6f, size - 2), FontStyle.Regular, GraphicsUnit.Point);
    }

    private IEnumerable<(string label, string value, double pct)> DiskSpaceCells()
    {
        switch (_cfg.DiskSpace)
        {
            case DiskSpaceMode.Total when _drives.Count > 0:
                long t = _drives.Sum(d => d.Total), u = _drives.Sum(d => d.Used);
                double p = t <= 0 ? 0 : 100.0 * u / t;
                yield return ("DSK", Pct(p), p);
                break;
            case DiskSpaceMode.Each:
                foreach (var d in _drives)
                    yield return (d.Name, Pct(d.UsedPercent), d.UsedPercent);
                break;
            case DiskSpaceMode.Single:
                foreach (var d in _drives.Where(d => string.Equals(d.Name, _cfg.DiskSpaceDrive, StringComparison.OrdinalIgnoreCase)))
                    yield return (d.Name, Pct(d.UsedPercent), d.UsedPercent);
                break;
        }
    }

    // Verticale ruimte voor de grafiek: bij "labels boven" schuift die onder een klein label.
    private (int top, int h) Area(Graphics g)
    {
        if (!_cfg.LabelsAbove) return (5, Height - 10);
        int top = (int)Math.Ceiling(_fontSmall.GetHeight(g)) + 1;
        return (top, Math.Max(10, Height - top - 3));
    }

    private int LabelWidth(Graphics g, string label) => (int)Math.Ceiling(g.MeasureString(label, _fontSmall).Width);

    // Label gecentreerd boven een cel van breedte cellW.
    private void DrawTopLabel(Graphics g, int x, int cellW, string label)
    {
        float w = g.MeasureString(label, _fontSmall).Width;
        using var b = new SolidBrush(C(_cfg.TextColor, Color.White));
        g.DrawString(label, _fontSmall, b, x + (cellW - w) / 2, 0);
    }

    // Tekstcel: "CPU 12%" op één regel, of (labels boven) het label boven de waarde.
    private int DrawTextCell(Graphics g, int x, string label, string value, string valueTemplate, Color color)
    {
        label = Cadence.L(label);
        if (!_cfg.LabelsAbove) return DrawDigital(g, x, $"{label} {value}", $"{label} {valueTemplate}", color);

        var (top, h) = Area(g);
        int cellW = (int)Math.Ceiling(Math.Max(g.MeasureString(label, _fontSmall).Width,
                                               g.MeasureString(valueTemplate, _font).Width));
        DrawTopLabel(g, x, cellW, label);
        var sz = g.MeasureString(value, _font);
        using var b = new SolidBrush(color);
        g.DrawString(value, _font, b, x + (cellW - sz.Width) / 2, top + (h - sz.Height) / 2);
        return cellW;
    }

    // Ping: "PING 12 ms", oranje vanaf 100 ms, rood vanaf 250 ms of als er geen antwoord komt.
    private int DrawPing(Graphics g, int x)
    {
        if (_cfg.PingStyle == TextGraphStyle.Graph) return DrawPingGraph(g, x);
        var st = _metrics.Ping.Stats();
        var textCol = C(_cfg.TextColor, Color.White);
        double? last = st.Last is double l && l >= 0 ? Cadence.M(7, l) : st.Last;
        Color col = last is null ? textCol
                  : last < 0 || last >= 250 ? C(_cfg.CritColor, Color.Red)
                  : last >= 100 ? C(_cfg.WarnColor, Color.Orange) : textCol;
        string txt = last is null ? "…" : last < 0 ? "✕" : $"{last:0} ms";
        return DrawTextCell(g, x, "PING", txt, "999 ms", col);
    }

    private int DrawMetric(Graphics g, int x, string label, double v, DisplayStyle style) => style switch
    {
        DisplayStyle.Gauge => DrawGauge(g, x, label, v),
        DisplayStyle.Bar   => DrawBar(g, x, label, v),
        DisplayStyle.Graph => DrawGraph(g, x, label, v),
        _                  => DrawTextCell(g, x, label, Pct(v), PctTemplate, ThresholdColor(v, C(_cfg.TextColor, Color.White))),
    };

    // Temperatuur als eigen cel: cijfer ("CPU 51°"), meter of balk (0-100 °C); als meter/balk met "CPU°" om het van het percentage te onderscheiden.
    private int DrawTemp(Graphics g, int x, string what, double t, DisplayStyle style, Color textCol) => style switch
    {
        DisplayStyle.Gauge => DrawGauge(g, x, _cfg.LabelsAbove ? what + "°C" : what + "°", t),
        DisplayStyle.Bar => DrawBar(g, x, _cfg.LabelsAbove ? what + "°C" : what + "°", t),
        DisplayStyle.Graph => DrawTempGraph(g, x, what, t),
        _ => DrawTextCell(g, x, _cfg.LabelsAbove ? what + "°C" : what, $"{t:0}°", "100°", ThresholdColor(t, textCol)),
    };

    // Temperatuur klein achter de CPU-/GPU-cel ("CPU 16%  51°"): smaller dan een eigen cel.
    private int DrawTempTag(Graphics g, int x, double t, Color textCol)
        => 4 + DrawDigital(g, x + 4, $"{t:0}°", "100°", ThresholdColor(t, textCol));

    private int DrawGauge(Graphics g, int x, string label, double v)
    {
        label = Cadence.L(label);
        bool above = _cfg.LabelsAbove;
        var (top, h) = Area(g);
        int d = above ? h : Height - 10;
        int lw = above ? LabelWidth(g, label) : DrawLabel(g, x, label);
        int cellW = above ? Math.Max(d, lw) : lw + 3 + d;
        if (above) DrawTopLabel(g, x, cellW, label);
        int gx = above ? x + (cellW - d) / 2 : x + lw + 3;
        int gy = above ? top : (Height - d) / 2;
        int pw = above ? 3 : Math.Max(3, (int)Math.Round(4 * UiScale)), inset = above ? 2 : 0;

        var rect = new Rectangle(gx + inset, gy + inset, d - 2 * inset, d - 2 * inset);
        using var bg = new Pen(Color.FromArgb(80, 80, 80), pw);
        using var fg = new Pen(ThresholdColor(v, C(_cfg.AccentColor, Color.DodgerBlue)), pw);
        g.DrawArc(bg, rect, 0, 360);
        g.DrawArc(fg, rect, -90, (float)(360.0 * Math.Clamp(v, 0, 100) / 100.0));
        var txt = $"{v:0}";
        var sz = g.MeasureString(txt, _fontSmall);
        using var tb = new SolidBrush(C(_cfg.TextColor, Color.White));
        g.DrawString(txt, _fontSmall, tb, gx + (d - sz.Width) / 2, gy + (d - sz.Height) / 2);
        return cellW;
    }

    private int DrawBar(Graphics g, int x, string label, double v)
    {
        label = Cadence.L(label);
        bool above = _cfg.LabelsAbove;
        var (top, h) = Area(g);
        int bw = (int)Math.Round(44 * UiScale), bh = Math.Min((int)Math.Round(12 * UiScale), above ? h : Height - 12);
        int lw = above ? LabelWidth(g, label) : DrawLabel(g, x, label);
        int cellW = above ? Math.Max(bw, lw) : lw + 3 + bw;
        if (above) DrawTopLabel(g, x, cellW, label);
        int bx = above ? x + (cellW - bw) / 2 : x + lw + 3;
        int by = above ? top + (h - bh) / 2 : (Height - bh) / 2;
        var rect = new Rectangle(bx, by, bw, bh);
        using var bg = new SolidBrush(Color.FromArgb(70, 70, 70));
        using var fg = new SolidBrush(ThresholdColor(v, C(_cfg.AccentColor, Color.DodgerBlue)));
        g.FillRectangle(bg, rect);
        g.FillRectangle(fg, new Rectangle(rect.X, rect.Y, (int)(bw * Math.Clamp(v, 0, 100) / 100.0), bh));
        using var pen = new Pen(Color.FromArgb(110, 110, 110));
        g.DrawRectangle(pen, rect);
        return cellW;
    }

    private int DrawCores(Graphics g, int x, string label, double[] cores)
    {
        label = Cadence.L(label);
        bool above = _cfg.LabelsAbove;
        var (top, h) = Area(g);
        int barW = Math.Max(3, (int)Math.Round(4 * UiScale)), gap = 1;
        int coresW = Math.Max(barW, cores.Length * (barW + gap));
        int lw = above ? LabelWidth(g, label) : DrawLabel(g, x, label);
        int cellW = above ? Math.Max(coresW, lw) : lw + 3 + coresW;
        if (above) DrawTopLabel(g, x, cellW, label);
        int cx = above ? x + (cellW - coresW) / 2 : x + lw + 3;
        using var bg = new SolidBrush(Color.FromArgb(70, 70, 70));
        foreach (var v in cores)
        {
            g.FillRectangle(bg, cx, top, barW, h);
            int fh = (int)(h * Math.Clamp(v, 0, 100) / 100.0);
            using var fg = new SolidBrush(ThresholdColor(v, C(_cfg.AccentColor, Color.DodgerBlue)));
            g.FillRectangle(fg, cx, top + (h - fh), barW, fh);
            cx += barW + gap;
        }
        return cellW;
    }

    // ---------- Batterij ----------
    internal static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    internal static void DrawBolt(Graphics g, float cx, float cy, float h)
    {
        float s = h / 16f;
        var pts = new (float x, float y)[] { (5, 0), (0, 8.5f), (4, 8.5f), (3, 16), (10, 6.5f), (5.5f, 6.5f), (8, 0) };
        var poly = pts.Select(p => new PointF(cx - 5 * s + p.x * s, cy - h / 2 + p.y * s)).ToArray();
        using var fill = new SolidBrush(Color.White);
        using var edge = new Pen(Color.FromArgb(150, 0, 0, 0), 1f);
        g.FillPolygon(fill, poly);
        g.DrawPolygon(edge, poly);
    }

    internal static void DrawPlug(Graphics g, float cx, float cy, float h)
    {
        float s = h / 18f, top = cy - h / 2;
        using var pen = new Pen(Color.White, Math.Max(1.3f, 1.6f * s)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var body = new SolidBrush(Color.FromArgb(90, 255, 255, 255));
        g.DrawLine(pen, cx - 3 * s, top, cx - 3 * s, top + 4 * s);
        g.DrawLine(pen, cx + 3 * s, top, cx + 3 * s, top + 4 * s);
        using var path = RoundedRect(new RectangleF(cx - 6 * s, top + 4 * s, 12 * s, 8 * s), 3 * s);
        g.FillPath(body, path);
        g.DrawPath(pen, path);
        g.DrawLine(pen, cx, top + 12 * s, cx, top + 18 * s);
    }

    // Staande batterij: van onderaf gevuld, kleur naar niveau/status, bliksem (laden) of stekker (op netstroom).
    private int DrawBattery(Graphics g, int x)
    {
        var m = _metrics;
        double pct = m.BatteryPercent;
        bool charging = m.BatteryCharging, ac = m.BatteryOnAc;
        Color fill = charging || ac ? Color.FromArgb(52, 199, 89)
                   : pct <= 10 ? C(_cfg.CritColor, Color.Red)
                   : pct <= 20 ? C(_cfg.WarnColor, Color.Orange)
                   : C(_cfg.AccentColor, Color.DodgerBlue);

        int bodyH = Math.Max(20, Height - 14);
        int bodyW = Math.Max(16, (int)Math.Round(bodyH * 0.58));
        int bx = x + 1, by = (Height - bodyH) / 2 + 2;   // + ruimte voor het nopje
        int nubW = Math.Max(6, bodyW / 3);

        using (var nub = new SolidBrush(Color.FromArgb(142, 142, 147)))
            g.FillRectangle(nub, bx + (bodyW - nubW) / 2, by - 3, nubW, 3);
        using (var path = RoundedRect(new RectangleF(bx, by, bodyW, bodyH), 4))
        using (var bg = new SolidBrush(Color.FromArgb(42, 42, 45)))
        using (var edge = new Pen(Color.FromArgb(142, 142, 147), 1.4f))
        {
            g.FillPath(bg, path);
            g.DrawPath(edge, path);
        }

        int ih = bodyH - 4;
        float fh = (float)(ih * pct / 100.0);
        if (fh >= 1)
        {
            using var fp = RoundedRect(new RectangleF(bx + 2, by + 2 + ih - fh, bodyW - 4, fh), 2);
            using var fb = new SolidBrush(fill);
            g.FillPath(fb, fp);
        }

        bool inside = _cfg.BatteryPercent == BatteryPercentMode.Inside;
        bool bolt = charging, plug = !charging && ac;
        float cx = bx + bodyW / 2f, cy = by + bodyH / 2f;
        if (bolt || plug)
        {
            float sh = inside ? bodyH * 0.32f : bodyH * 0.52f;
            float scy = inside ? by + bodyH * 0.27f : cy;
            if (bolt) DrawBolt(g, cx, scy, sh); else DrawPlug(g, cx, scy, sh);
        }
        if (inside)
        {
            string t = $"{pct:0}";
            var sz = g.MeasureString(t, _fontSmall);
            float ty = (bolt || plug) ? by + bodyH * 0.66f - sz.Height / 2 : cy - sz.Height / 2;
            using var tb = new SolidBrush(Color.White);
            g.DrawString(t, _fontSmall, tb, cx - sz.Width / 2, ty);
        }

        int w = bodyW + 2;
        if (_cfg.BatteryPercent == BatteryPercentMode.Beside)
        {
            string t = $"{pct:0}%";
            var sz = g.MeasureString(t, _font);
            using var tb = new SolidBrush(C(_cfg.TextColor, Color.White));
            g.DrawString(t, _font, tb, bx + bodyW + 5, (Height - sz.Height) / 2);
            w = bodyW + 2 + 5 + (int)Math.Ceiling(g.MeasureString("100%", _font).Width);
        }
        return w;
    }

    private int DrawLabel(Graphics g, int x, string label)
    {
        var sz = g.MeasureString(label, _fontSmall);
        using var b = new SolidBrush(C(_cfg.TextColor, Color.White));
        g.DrawString(label, _fontSmall, b, x, (Height - sz.Height) / 2);
        return (int)Math.Ceiling(sz.Width);
    }

    // ---------- Muis ----------
    private void OnMouseDown(object? s, MouseEventArgs e)
    {
        HideTip();
        if (e.Button == MouseButtons.Middle) { RunMouseAction(_cfg.MiddleClickAction); return; }
        if (e.Button == MouseButtons.Left) { _dragging = !_cfg.LockPosition; _dragStart = e.Location; if (Cadence.Hit(0, 10, 4000)) Cadence.Go(2, 11500); }
        else if (e.Button == MouseButtons.Right) { _dragging = false; _menu.Show(this, e.Location); }
    }
    private void OnMouseUp(object? s, MouseEventArgs e)
    {
        if (_dragging) { _dragging = false; _cfg.Save(); }
    }
    private void OnMouseMove(object? s, MouseEventArgs e)
    {
        if (_dragging)
        {
            Location = new Point(Location.X + e.X - _dragStart.X, Location.Y + e.Y - _dragStart.Y);
            _cfg.FloatX = Location.X; _cfg.FloatY = Location.Y;
        }
    }

    // ---------- Menu ----------
    private bool _keepMenuOpen;

    // Aanroepen in een Click-handler: het menu (en alle submenu's) blijft dan open.
    private void Keep()
    {
        _keepMenuOpen = true;
        BeginInvoke(new Action(() => _keepMenuOpen = false));
    }

    // Volgorde in WinForms: ItemClicked -> Closing -> (pas daarna) de Click-handler van het item.
    // Daarom wordt hier bepaald of het menu openblijft: standaard wel, behalve bij items met
    // Tag = "close" (Afsluiten, Over, dialogen, ...). Een item dat een submenu opent telt niet mee.
    private void OnDropDownItemClicked(object? s, ToolStripItemClickedEventArgs e)
    {
        if (e.ClickedItem is ToolStripMenuItem { HasDropDownItems: true }) return;
        _keepMenuOpen = e.ClickedItem?.Tag as string != "close";
        BeginInvoke(new Action(() => _keepMenuOpen = false));
    }

    private void OnDropDownClosing(object? s, ToolStripDropDownClosingEventArgs ev)
    {
        if (_keepMenuOpen && ev.CloseReason is ToolStripDropDownCloseReason.ItemClicked
                                           or ToolStripDropDownCloseReason.AppFocusChange)
            ev.Cancel = true;
    }

    private void HookDropDowns(ToolStripItemCollection items)
    {
        foreach (ToolStripItem i in items)
        {
            if (i is ToolStripMenuItem mi && mi.HasDropDownItems)
            {
                mi.DropDown.Closing -= OnDropDownClosing;
                mi.DropDown.Closing += OnDropDownClosing;
                mi.DropDown.ItemClicked -= OnDropDownItemClicked;
                mi.DropDown.ItemClicked += OnDropDownItemClicked;
                HookDropDowns(mi.DropDownItems);
            }
        }
    }

    // Vinkje verplaatsen naar het aangeklikte item (keuzelijstje).
    private static void MarkOnly(ToolStripMenuItem it)
    {
        if (it.Owner is null) return;
        foreach (ToolStripItem sib in it.Owner.Items)
            if (sib is ToolStripMenuItem m) m.Checked = ReferenceEquals(m, it);
    }

    private static bool CursorInDropDowns(ToolStripItemCollection items)
    {
        foreach (ToolStripItem i in items)
            if (i is ToolStripMenuItem mi && mi.HasDropDownItems && mi.DropDown.Visible)
                if (mi.DropDown.Bounds.Contains(Cursor.Position) || CursorInDropDowns(mi.DropDownItems))
                    return true;
        return false;
    }

    // Menu sluit vanzelf als de muis ~1 s niet meer boven het menu (of een submenu) is.
    private void CheckMenuHover()
    {
        if (!_menu.Visible) { _menuTimer.Stop(); return; }
        bool inside = _menu.Bounds.Contains(Cursor.Position) || CursorInDropDowns(_menu.Items);
        _menuOutsideTicks = inside ? 0 : _menuOutsideTicks + 1;
        if (_menuOutsideTicks >= 4) _menu.Close();
    }

    // Na een taalwissel: menu sluiten en direct op dezelfde plek heropenen (opnieuw opgebouwd in de
    // nieuwe taal), met het Taal-submenu weer open.
    private void ReopenMenu()
    {
        var pt = _menu.Bounds.Location;
        _menu.Close();
        _menu.Show(pt);
        if (_menu.Items.Cast<ToolStripItem>().FirstOrDefault(i => i.Tag as string == "lang") is ToolStripMenuItem l)
            l.ShowDropDown();
    }

    private void BuildContextMenu()
    {
        _menu = new ContextMenuStrip();
        _menu.Opening += (_, _) => { HideTip(); RefreshMenu(_menu); };
        _menu.Opened += (_, _) => { _menuOutsideTicks = 0; _menuTimer.Start(); };
        _menu.Closed += (_, _) => _menuTimer.Stop();
        _menu.Closing += OnDropDownClosing;
        _menu.ItemClicked += OnDropDownItemClicked;
        RefreshMenu(_menu);
    }

    private void RefreshMenu(ContextMenuStrip menu)
    {
        menu.Items.Clear();
        foreach (var f in _menuFonts) f.Dispose();
        _menuFonts.Clear();
        _live.Clear();
        RefreshDrives(true);
        AddUpdateMenuItem(menu);

        var settingsItem = new ToolStripMenuItem(Loc.Pick("Instellingen (uiterlijk, indeling, thema's)…", "Settings (looks, layout, themes)…")) { Tag = "close" };
        settingsItem.Font = new Font(settingsItem.Font, FontStyle.Bold);
        settingsItem.Click += (_, _) => ShowSettings();
        menu.Items.Add(settingsItem);

        // Thema-snelkeuze: één klik past een thema toe (beheren doe je in Instellingen → Thema's).
        var themes = new ToolStripMenuItem(Loc.Pick("Thema", "Theme"));
        void addTheme(ThemeData t, bool builtIn)
        {
            var it = new ToolStripMenuItem(t.Name) { Tag = "close" };
            it.Click += (_, _) => { t.ApplyTo(_cfg); ApplyAll(); };
            themes.DropDownItems.Add(it);
        }
        foreach (var t in ThemeStore.BuiltIn()) addTheme(t, true);
        var mine = ThemeStore.User(_cfg);
        if (mine.Count > 0) themes.DropDownItems.Add(new ToolStripSeparator());
        foreach (var t in mine) addTheme(t, false);
        menu.Items.Add(themes);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(BuildUsageMenu());
        menu.Items.Add(BuildDashMenu());
        menu.Items.Add(BuildFullMenu());
        menu.Items.Add(BuildTourMenu());

        var resetItem = new ToolStripMenuItem(Loc.S("resetPos")) { Tag = "close" };
        resetItem.Click += (_, _) => ResetWidgetPosition();
        menu.Items.Add(resetItem);
        AddClickThroughMenuItem(menu);

        menu.Items.Add(new ToolStripSeparator());
        var copy = new ToolStripMenuItem(Loc.Pick("Kopieer info naar klembord", "Copy info to clipboard")) { Tag = "close" };
        copy.Click += (_, _) => CopyInfo();
        menu.Items.Add(copy);

        var welcome = new ToolStripMenuItem(Loc.Pick("Welkomstscherm", "Welcome screen")) { Tag = "close" };
        welcome.Click += (_, _) => ShowWelcome();
        menu.Items.Add(welcome);

        var readme = new ToolStripMenuItem(Loc.Pick("Leesmij en credits", "Readme and credits")) { Tag = "close" };
        readme.Click += (_, _) =>
        {
            if (_credits is null || _credits.IsDisposed) _credits = new CreditsForm(_cfg, _usage, _metrics);
            _credits.Show();
            _credits.Activate();
        };
        menu.Items.Add(readme);

        var about = new ToolStripMenuItem(Loc.Pick("Over TaskbarStats…", "About TaskbarStats…")) { Tag = "close" };
        about.Click += (_, _) => { using var f = new AboutForm(); f.ShowDialog(this); };
        menu.Items.Add(about);

        menu.Items.Add(new ToolStripSeparator());
        var exit = new ToolStripMenuItem(Loc.S("exit")) { Tag = "close" };
        // Het widget zelf sluiten (niet Application.Exit): dat loopt door alle open vensters terwijl wij tijdens het sluiten
        // het dashboard en het fullscreen-venster sluiten, wat "Collection was modified" gaf.
        exit.Click += (_, _) => Close();
        menu.Items.Add(exit);

        HookDropDowns(menu.Items);
    }

    // Item met live bijgewerkte tekst zolang het menu openstaat.
    private void Live(ToolStripItem item, Func<string> text)
    {
        item.Text = text();
        _live.Add((item, text));
    }

    private readonly List<Font> _menuFonts = new();

    private ToolStripMenuItem BuildUsageMenu()
    {
        var m = new ToolStripMenuItem(Loc.Pick("Verbruik", "Usage"));
        var periods = new (string label, Func<string?, AdapterUsage> get)[]
        {
            (Loc.Pick("Sessie", "Session"),          a => _usage.Session(a)),
            (Loc.Pick("Vandaag", "Today"),           a => _usage.Today(a)),
            (Loc.Pick("Gisteren", "Yesterday"),      a => _usage.Yesterday(a)),
            (Loc.Pick("Laatste 7 dagen", "Last 7 days"), a => _usage.Week(a)),
            (Loc.Pick("Deze maand", "This month"),   a => _usage.Month(a)),
        };
        void fill(ToolStripMenuItem parent, string? adapter)
        {
            foreach (var (label, get) in periods)
            {
                var it = new ToolStripMenuItem(label);
                it.Click += (_, _) => Keep();
                Live(it, () => { var u = get(adapter); return $"{label}   ↓ {Metrics.FormatBytes(u.Down)}  ↑ {Metrics.FormatBytes(u.Up)}"; });
                parent.DropDownItems.Add(it);
            }
        }

        m.DropDownItems.Add(new ToolStripMenuItem(Loc.S("allAdapters")) { Enabled = false });
        fill(m, null);
        var adapters = _usage.KnownAdapters();
        if (adapters.Count > 0) m.DropDownItems.Add(new ToolStripSeparator());
        foreach (var name in adapters)
        {
            var sub = new ToolStripMenuItem(name);
            fill(sub, name);
            m.DropDownItems.Add(sub);
        }
        m.DropDownItems.Add(new ToolStripSeparator());

        var log = new ToolStripMenuItem(Loc.Pick("Log bekijken…", "View log…")) { Tag = "close" };
        log.Click += (_, _) => ShowLog();
        m.DropDownItems.Add(log);
        return m;
    }

    private static readonly (string name, string hex)[] BorderPalette =
    {
        ("Magenta", "#FF00FF"), ("Rood", "#FF3B30"), ("Oranje", "#FF9500"),
        ("Geel", "#FFCC00"), ("Limoen", "#AEEA00"), ("Groen", "#34C759"),
        ("Cyaan", "#00E5FF"), ("Blauw", "#0A84FF"), ("Paars", "#AF52DE"),
        ("Roze", "#FF2D95"), ("Wit", "#FFFFFF"), ("Grijs", "#8E8E93"),
        ("Teal", "#00BFA5"), ("Goud", "#D4AF37"), ("Zwart", "#000000"),
    };

    private static Bitmap SwatchImage(string hex)
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        try { g.Clear(ColorTranslator.FromHtml(hex)); } catch { g.Clear(Color.Gray); }
        g.DrawRectangle(Pens.DimGray, 0, 0, 15, 15);
        return bmp;
    }

    private void AddCheck(ToolStripDropDownItem parent, string text, bool val, Action<bool> onToggle)
    {
        var item = new ToolStripMenuItem(text) { Checked = val, CheckOnClick = true };
        item.Click += (_, _) => { Keep(); onToggle(item.Checked); };
        parent.DropDownItems.Add(item);
    }
    private void AddCheck(ContextMenuStrip parent, string text, bool val, Action<bool> onToggle)
    {
        var item = new ToolStripMenuItem(text) { Checked = val, CheckOnClick = true };
        item.Click += (_, _) => { Keep(); onToggle(item.Checked); };
        parent.Items.Add(item);
    }

    private void Persist() { _cfg.Save(); Render(); _dash?.ApplySettings(); }

    // Wijziging die de breedte kan beïnvloeden: menu open houden, opslaan, opnieuw meten.
    private void Relayout() { Keep(); _cfg.Save(); Render(); }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _cfg.Save();
        _timer.Stop();
        _topTimer.Stop();
        _menuTimer.Stop();
        _notifyHide.Stop();
        _notify.Visible = false;
        _notify.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _dash?.Close();
        _full?.Close();
        _bg?.Dispose();
        _usage.Save();
        _tip.Dispose();
        StopSampler();   // eerst de thread stoppen, dan pas de tellers opruimen
        _metrics.Dispose();
        base.OnFormClosing(e);
    }
}
