namespace TaskbarStats;

/// <summary>
/// Fullscreen "cockpit": één dicht overzicht van alles (CPU, GPU, geheugen, netwerk, schijven, batterij, systeem,
/// programma's) op een vast canvas van 1920x1080 dat meeschaalt naar het scherm. Klik op een tegel voor de
/// diepgaande weergave; Esc gaat een stap terug (en sluit vanuit het overzicht). Toetsen 1/2/3 = grafiek 1 min / 5 min / 1 uur.
/// Dit venster doet invoer, navigatie, tour en timers; het tekenen zelf staat in <see cref="FullscreenRenderer"/>.
/// </summary>
public sealed partial class FullscreenForm : Form
{
    private readonly DashContext _c;
    private AppSettings Cfg => _c.Cfg;
    private readonly ProcessSampler _procs = new() { TopCount = 12 };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private readonly FullscreenRenderer _r;
    /// <summary>Navigatie- en tourtoestand (pagina, grafiekvenster, scrollstand, hover); de renderer leest dit bij elk beeld.</summary>
    private readonly FullscreenView _view = new() { Win = 300 };
    private BgLayer? _bg;
    private long _procAt;

    public FullscreenForm(DashContext c, Screen screen)
    {
        AppIcon.Apply(this);
        _c = c;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = screen.Bounds;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = FullscreenRenderer.Bg;
        DoubleBuffered = true;
        KeyPreview = true;
        SetStyle(ControlStyles.ResizeRedraw, true);

        _r = new FullscreenRenderer(c, _procs);

        _timer.Tick += (_, _) => { SampleProcs(); TourTick(); Invalidate(); };
        _timer.Start();
        _view.OpenedAt = Environment.TickCount64;
        _c.Metrics.SetSensorsWanted(true);   // LibreHardwareMonitor: hoofdbord, schijven, ventilatoren, klokken, vermogen
        SampleProcs(true);
    }

    private void SampleProcs(bool force = false)
    {
        long now = Environment.TickCount64;
        if (!force && now - _procAt < 2000) return;
        _procAt = now;
        _procs.SampleAsync();
    }

    // ---------- Invoer ----------
    // Pijltjestoetsen worden anders door het formulier zelf afgehandeld (focus verplaatsen) en komen niet als toets aan.
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down or Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_view.Tour)
        {
            StopTour();
            if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Space) { e.Handled = true; Invalidate(); return; }
        }
        else if (e.KeyCode == Keys.Space) { StartTour(); e.Handled = true; Invalidate(); return; }
        switch (Cadence.Feed((int)e.KeyCode))
        {
            case 1: _c.Nudge?.Invoke(1); break;
            case 2: BeginInvoke(new Action(() => { using var m = new MiniForm(Cfg); m.ShowDialog(this); })); break;
        }
        switch (e.KeyCode)
        {
            case Keys.Escape:
            case Keys.Back:
                if (_view.Detail is not null) _view.Detail = null; else Close();
                break;
            case Keys.I: if (_view.Detail is null) OpenSpecs(); break;
            case Keys.D1: _view.Win = 60; break;
            case Keys.D2: _view.Win = 300; break;
            case Keys.D3: _view.Win = 3600; break;
            default: base.OnKeyDown(e); return;
        }
        e.Handled = true;
        Invalidate();
    }

    // ---------- Automatische tour ----------
    // 3× klikken op een lege plek (of spatie) loopt alle pagina's af: overzicht, elk detail en de specificaties.
    private readonly List<string?> _tourPages = new();
    private readonly List<long> _emptyClicks = new();

    private void StartTour()
    {
        _tourPages.Clear();
        _tourPages.Add(null);
        foreach (var id in Tiles.Order(Cfg.FullOrder).Where(i => Tiles.FullOn(Cfg, i)))
        {
            string page = id == "batt" ? "sys" : id;
            if (!_tourPages.Contains(page)) _tourPages.Add(page);
        }
        _tourPages.Add("spec");
        _view.TourCount = _tourPages.Count;
        _view.Tour = true;
        _view.TourIdx = -1;
        TourNext();
    }

    private void StopTour() { _view.Tour = false; }

    private void TourNext()
    {
        _view.TourIdx = (_view.TourIdx + 1) % _tourPages.Count;
        _view.TourAt = Environment.TickCount64;
        _view.Detail = _tourPages[_view.TourIdx];
        if (_view.Detail == "spec") OpenSpecs();
    }

    private void TourTick()
    {
        int tourMs = FullscreenView.TourMillis(Cfg);
        if (!_view.Tour) return;
        if (Environment.TickCount64 - _view.TourAt >= tourMs) TourNext();
        else if (_view.Detail == "spec" && _view.SpecMax > 0)   // lange lijst: rustig meescrollen zodat alles langskomt
            _view.SpecScroll = _view.SpecMax * Math.Clamp((Environment.TickCount64 - _view.TourAt - 800) / Math.Max(1f, tourMs - 2000f), 0f, 1f);
    }

    private void OpenSpecs()
    {
        _view.Detail = "spec";
        _view.SpecScroll = 0;
        HardwareInfo.Refresh(_c.Metrics);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_view.Detail != "spec") return;
        _view.SpecScroll = Math.Clamp(_view.SpecScroll - e.Delta * 0.9f / _r.Scale, 0, _view.SpecMax);
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var h = _r.HitAt(e.Location);
        if (h == _view.Hover) return;
        _view.Hover = h;
        Cursor = h is null ? Cursors.Default : Cursors.Hand;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        string? hover = _view.Hover;
        if (e.Button == MouseButtons.Right) { _c.ShowMenu(Cursor.Position); return; }
        if (e.Button == MouseButtons.Left && hover == "exit") { Close(); return; }
        if (_view.Tour && e.Button == MouseButtons.Left) { StopTour(); Invalidate(); return; }
        if (e.Button == MouseButtons.Left && hover is null)
        {
            long now = Environment.TickCount64;
            _emptyClicks.Add(now);
            _emptyClicks.RemoveAll(t => now - t > 1500);
            if (_emptyClicks.Count >= 3) { _emptyClicks.Clear(); StartTour(); Invalidate(); }
            return;
        }
        if (e.Button != MouseButtons.Left || hover is null) return;
        if (hover.StartsWith('w')) { _view.Win = int.Parse(hover[1..]); }
        else if (hover == "back") _view.Detail = null;
        else if (hover == "spec" && _view.Detail is null) OpenSpecs();
        else if (_view.Detail is null) _view.Detail = hover == "bat" ? "sys" : hover;
        Invalidate();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        _timer.Dispose();
        _c.Metrics.SetSensorsWanted(false);
        _r.Dispose();
        _bg?.Dispose(); _bg = null;
        base.OnFormClosed(e);
    }

    // ---------- Tekenen ----------
    protected override void OnPaint(PaintEventArgs e)
        => _r.Paint(e.Graphics, ClientSize, _view, _c.Metrics.Current, DrawBackground);

    /// <summary>Achtergrondafbeelding (instelling), na het wissen van het canvas en vóór de tegels.</summary>
    private void DrawBackground(Graphics g)
    {
        if (!string.IsNullOrWhiteSpace(Cfg.FullBgImage))
            (_bg ??= new BgLayer(this, 1920, Invalidate, keepSource: false)).Draw(g, Cfg.FullBgImage, Cfg.FullBgMode, Cfg.FullBgOpacity, ClientSize.Width, ClientSize.Height);
        else if (_bg is not null) { _bg.Dispose(); _bg = null; }
    }
}
