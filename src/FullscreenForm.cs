using System.Drawing.Drawing2D;
using System.Text.RegularExpressions;
using LibreHardwareMonitor.Hardware;
using Microsoft.Win32;

namespace TaskbarStats;

/// <summary>
/// Fullscreen "cockpit": één dicht overzicht van alles (CPU, GPU, geheugen, netwerk, schijven, batterij, systeem,
/// programma's) op een vast canvas van 1920x1080 dat meeschaalt naar het scherm. Klik op een tegel voor de
/// diepgaande weergave; Esc gaat een stap terug (en sluit vanuit het overzicht). Toetsen 1/2/3 = grafiek 1 min / 5 min / 1 uur.
/// </summary>
public sealed partial class FullscreenForm : Form
{
    private const float CW = 1920, CH = 1080, M = 16;
    private static readonly Color Bg = Color.FromArgb(11, 13, 18);
    private static readonly Color Dim = Color.FromArgb(150, 160, 175);
    private static readonly Color Green = Color.FromArgb(52, 199, 89), Orange = Color.FromArgb(255, 149, 0);

    private readonly DashContext _c;
    private AppSettings Cfg => _c.Cfg;
    private readonly ProcessSampler _procs = new() { TopCount = 12 };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private readonly List<(string key, RectangleF r)> _hits = new();
    private readonly Font _f, _fs, _fb, _fh, _fbig, _fhuge;
    private string? _detail, _hover;
    private BgLayer? _bg;
    private int _win = 300;
    private float _s = 1, _ox, _oy;
    private long _procAt, _openedAt;
    private static string? _cpuName;

    public FullscreenForm(DashContext c, Screen screen)
    {
        AppIcon.Apply(this);
        _c = c;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = screen.Bounds;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = Bg;
        DoubleBuffered = true;
        KeyPreview = true;
        SetStyle(ControlStyles.ResizeRedraw, true);

        string fam = Cfg.FontFamily;
        _f = new Font(fam, 15f, FontStyle.Regular, GraphicsUnit.Pixel);
        _fs = new Font(fam, 13f, FontStyle.Regular, GraphicsUnit.Pixel);
        _fb = new Font(fam, 15f, FontStyle.Bold, GraphicsUnit.Pixel);
        _fh = new Font(fam, 19f, FontStyle.Bold, GraphicsUnit.Pixel);
        _fbig = new Font(fam, 34f, FontStyle.Bold, GraphicsUnit.Pixel);
        _fhuge = new Font(fam, 54f, FontStyle.Bold, GraphicsUnit.Pixel);

        _timer.Tick += (_, _) => { SampleProcs(); TourTick(); Invalidate(); };
        _timer.Start();
        _openedAt = Environment.TickCount64;
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
        if (_tour)
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
                if (_detail is not null) _detail = null; else Close();
                break;
            case Keys.I: if (_detail is null) OpenSpecs(); break;
            case Keys.D1: _win = 60; break;
            case Keys.D2: _win = 300; break;
            case Keys.D3: _win = 3600; break;
            default: base.OnKeyDown(e); return;
        }
        e.Handled = true;
        Invalidate();
    }

    private float _specScroll, _specMax;

    // ---------- Automatische tour ----------
    // 3× klikken op een lege plek (of spatie) loopt alle pagina's af: overzicht, elk detail en de specificaties.
    private bool _tour;
    private long _tourAt;
    private int _tourIdx;
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
        _tour = true;
        _tourIdx = -1;
        TourNext();
    }

    private void StopTour() { _tour = false; }

    private void TourNext()
    {
        _tourIdx = (_tourIdx + 1) % _tourPages.Count;
        _tourAt = Environment.TickCount64;
        _detail = _tourPages[_tourIdx];
        if (_detail == "spec") OpenSpecs();
    }

    private int TourMs => Math.Clamp(Cfg.TourSeconds, 3, 300) * 1000;

    private void TourTick()
    {
        if (!_tour) return;
        if (Environment.TickCount64 - _tourAt >= TourMs) TourNext();
        else if (_detail == "spec" && _specMax > 0)   // lange lijst: rustig meescrollen zodat alles langskomt
            _specScroll = _specMax * Math.Clamp((Environment.TickCount64 - _tourAt - 800) / Math.Max(1f, TourMs - 2000f), 0f, 1f);
    }

    private void OpenSpecs()
    {
        _detail = "spec";
        _specScroll = 0;
        HardwareInfo.Refresh(_c.Metrics);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_detail != "spec") return;
        _specScroll = Math.Clamp(_specScroll - e.Delta * 0.9f / _s, 0, _specMax);
        Invalidate();
    }

    private string? HitAt(Point p)
    {
        float x = (p.X - _ox) / _s, y = (p.Y - _oy) / _s;
        for (int i = _hits.Count - 1; i >= 0; i--)
            if (_hits[i].r.Contains(x, y)) return _hits[i].key;
        return null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var h = HitAt(e.Location);
        if (h == _hover) return;
        _hover = h;
        Cursor = h is null ? Cursors.Default : Cursors.Hand;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Right) { _c.ShowMenu(Cursor.Position); return; }
        if (e.Button == MouseButtons.Left && _hover == "exit") { Close(); return; }
        if (_tour && e.Button == MouseButtons.Left) { StopTour(); Invalidate(); return; }
        if (e.Button == MouseButtons.Left && _hover is null)
        {
            long now = Environment.TickCount64;
            _emptyClicks.Add(now);
            _emptyClicks.RemoveAll(t => now - t > 1500);
            if (_emptyClicks.Count >= 3) { _emptyClicks.Clear(); StartTour(); Invalidate(); }
            return;
        }
        if (e.Button != MouseButtons.Left || _hover is null) return;
        if (_hover.StartsWith('w')) { _win = int.Parse(_hover[1..]); }
        else if (_hover == "back") _detail = null;
        else if (_hover == "spec" && _detail is null) OpenSpecs();
        else if (_detail is null) _detail = _hover == "bat" ? "sys" : _hover;
        Invalidate();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        _timer.Dispose();
        _c.Metrics.SetSensorsWanted(false);
        foreach (var f in new[] { _f, _fs, _fb, _fh, _fbig, _fhuge }) f.Dispose();
        _bg?.Dispose(); _bg = null;
        base.OnFormClosed(e);
    }

    // ---------- Tekenen ----------
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(Bg);
        if (!string.IsNullOrWhiteSpace(Cfg.FullBgImage))
            (_bg ??= new BgLayer(this, 1920, Invalidate, keepSource: false)).Draw(g, Cfg.FullBgImage, Cfg.FullBgMode, Cfg.FullBgOpacity, ClientSize.Width, ClientSize.Height);
        else if (_bg is not null) { _bg.Dispose(); _bg = null; }
        _s = Math.Min(ClientSize.Width / CW, ClientSize.Height / CH);
        _ox = (ClientSize.Width - CW * _s) / 2;
        _oy = (ClientSize.Height - CH * _s) / 2;
        g.TranslateTransform(_ox, _oy);
        g.ScaleTransform(_s, _s);
        _hits.Clear();
        DrawHeader(g);
        if (_detail is null) DrawOverview(g); else DrawDetail(g);
    }

    private Color Col(string hex, Color fb) { try { return ColorTranslator.FromHtml(hex); } catch { return fb; } }
    private Color Accent => Col(Cfg.AccentColor, Color.DodgerBlue);
    private Color TextCol => Col(Cfg.TextColor, Color.White);

    private Color Thr(double v)
    {
        if (v >= Cfg.CritThreshold) return Col(Cfg.CritColor, Color.Red);
        if (v >= Cfg.WarnThreshold) return Col(Cfg.WarnColor, Color.Orange);
        return Accent;
    }

    private static GraphicsPath Rounded(RectangleF r, float radius) => WidgetForm.RoundedRect(r, radius);

    private void T(Graphics g, string s, Font f, Color c, float x, float y)
    {
        using var b = new SolidBrush(c);
        g.DrawString(s, f, b, x, y);
    }

    private void TR(Graphics g, string s, Font f, Color c, float right, float y)
    {
        using var b = new SolidBrush(c);
        using var sf = new StringFormat { Alignment = StringAlignment.Far };
        g.DrawString(s, f, b, new RectangleF(right - 600, y, 600, 40), sf);
    }

    private void TC(Graphics g, string s, Font f, Color c, float cx, float cy)
    {
        using var b = new SolidBrush(c);
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(s, f, b, new RectangleF(cx - 200, cy - 40, 400, 80), sf);
    }

    private static string Trunc(string s, int n) => s.Length > n ? s[..(n - 1)] + "…" : s;
    private static string TruncMid(string s, int n) => s.Length <= n ? s : s[..(n / 2 - 1)] + "…" + s[^(n - n / 2)..];

    private void Card(Graphics g, string? key, RectangleF r, string title, string? sub = null)
    {
        if (key is not null) _hits.Add((key, r));
        bool hot = key is not null && key == _hover && _detail is null;
        using (var path = Rounded(r, 12))
        using (var bg = new SolidBrush(Color.FromArgb(hot ? 34 : 20, 255, 255, 255)))
        using (var pen = new Pen(hot ? Color.FromArgb(170, Accent) : Color.FromArgb(30, 255, 255, 255), hot ? 1.8f : 1f))
        {
            g.FillPath(bg, path);
            g.DrawPath(pen, path);
        }
        T(g, title, _fh, TextCol, r.X + 16, r.Y + 10);
        if (sub is not null) TR(g, sub, _fs, Dim, r.Right - 16, r.Y + 15);
        if (hot) TR(g, Loc.Pick("klik voor details", "click for details"), _fs, Color.FromArgb(200, Accent), r.Right - 16, r.Bottom - 24);
    }

    private void Gauge(Graphics g, float cx, float cy, float rad, double pct, Color col, string label)
    {
        float pw = rad * 0.16f;
        var rect = new RectangleF(cx - rad, cy - rad, rad * 2, rad * 2);
        using (var bg = new Pen(Color.FromArgb(70, 78, 90), pw)) g.DrawArc(bg, rect, 0, 360);
        if (pct > 0.5)
            using (var fg = new Pen(col, pw) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawArc(fg, rect, -90, (float)(360.0 * Math.Clamp(pct, 0, 100) / 100.0));
        TC(g, label, rad >= 50 ? _fbig : _fh, TextCol, cx, cy);
    }

    private void Bar(Graphics g, float x, float y, float w, double pct, Color col, float h = 8)
    {
        using (var bgp = Rounded(new RectangleF(x, y, w, h), h / 2))
        using (var bg = new SolidBrush(Color.FromArgb(70, 78, 90)))
            g.FillPath(bg, bgp);
        float fw = (float)(w * Math.Clamp(pct, 0, 100) / 100.0);
        if (fw >= h)
            using (var fp = Rounded(new RectangleF(x, y, fw, h), h / 2))
            using (var fb = new SolidBrush(col))
                g.FillPath(fb, fp);
        else if (fw > 0.5f)
            using (var fb = new SolidBrush(col))
                g.FillRectangle(fb, x, y, fw, h);
    }

    private string WinLabel() => _win switch { 60 => Loc.Pick("1 min", "1 min"), 300 => Loc.Pick("5 min", "5 min"), _ => Loc.Pick("1 uur", "1 hour") };

    private static string Pct(double v) => $"{v:0}%";
    private static string Rate(double v) => Metrics.FormatRate(v);
    private static string SizeStr(double v) => Metrics.FormatSize(v);

    /// <summary>Grafiek over het gekozen tijdvenster; max &lt;= 0 = automatisch schalen.</summary>
    private void Graph(Graphics g, RectangleF r, IReadOnlyList<(Ring ring, Color col)> series, double max, Func<double, string> fmt, bool axes = true)
    {
        int win = _win;
        if (max <= 0)
        {
            double mm = 0;
            foreach (var (ring, _) in series) mm = Math.Max(mm, ring.Max(win));
            max = Math.Max(1024, mm * 1.15);
        }
        float left = axes ? 64 : 0;
        var p = new RectangleF(r.X + left, r.Y + (axes ? 8 : 0), r.Width - left - (axes ? 8 : 0), r.Height - (axes ? 30 : 0));
        int lines = axes ? 4 : 2;
        using (var grid = new Pen(Color.FromArgb(34, 255, 255, 255), 1f))
            for (int k = 0; k <= lines; k++)
            {
                float y = p.Bottom - p.Height * k / lines;
                g.DrawLine(grid, p.X, y, p.Right, y);
                if (axes) TR(g, fmt(max * k / lines), _fs, Dim, p.X - 8, y - 8);
            }
        if (axes)
        {
            T(g, "-" + WinLabel(), _fs, Dim, p.X, p.Bottom + 6);
            TR(g, Loc.Pick("nu", "now"), _fs, Dim, p.Right, p.Bottom + 6);
        }
        foreach (var (ring, col) in series)
        {
            int n = Math.Min(ring.Count, win), start = ring.Count - n;
            if (n < 2) continue;
            var pts = new PointF[n];
            for (int i = 0; i < n; i++)
                pts[i] = new PointF(p.Right - (n - 1 - i) * p.Width / (win - 1),
                                    p.Bottom - (float)(Math.Clamp(ring[start + i] / max, 0, 1) * p.Height));
            var fill = pts.Concat(new[] { new PointF(pts[^1].X, p.Bottom), new PointF(pts[0].X, p.Bottom) }).ToArray();
            using (var fb = new SolidBrush(Color.FromArgb(40, col))) g.FillPolygon(fb, fill);
            using (var pen = new Pen(col, 1.8f) { LineJoin = LineJoin.Round }) g.DrawLines(pen, pts);
        }
    }

    private (double cur, double min, double avg, double max) Stat(Ring r)
    {
        int n = Math.Min(r.Count, _win);
        if (n == 0) return (0, 0, 0, 0);
        double mn = double.MaxValue, mx = 0, sum = 0;
        for (int i = r.Count - n; i < r.Count; i++) { mn = Math.Min(mn, r[i]); mx = Math.Max(mx, r[i]); sum += r[i]; }
        return (r[r.Count - 1], mn, sum / n, mx);
    }

    private void StatRows(Graphics g, float x, float y, float w, Ring ring, Func<double, string> fmt)
    {
        var s = Stat(ring);
        var rows = new (string l, double v)[]
        {
            (Loc.Pick("Nu", "Now"), s.cur), (Loc.Pick("Min", "Min"), s.min), (Loc.Pick("Gemiddeld", "Average"), s.avg), (Loc.Pick("Max", "Max"), s.max),
        };
        for (int i = 0; i < rows.Length; i++)
        {
            T(g, rows[i].l, _f, Dim, x, y + i * 24);
            TR(g, fmt(rows[i].v), _fb, TextCol, x + w, y + i * 24);
        }
    }

    private void KeyValue(Graphics g, float x, float y, float w, string k, string v)
    {
        T(g, k, _f, Dim, x, y);
        TR(g, v, _f, TextCol, x + w, y);
    }

    private void ProcList(Graphics g, float x, float y, float w, int rows, bool cpu)
    {
        int count = cpu ? (_procs.HasCpu ? _procs.TopCpu.Count : 0) : _procs.TopMem.Count;
        if (cpu && !_procs.HasCpu) T(g, "…", _f, Dim, x, y);
        double top = cpu ? (count > 0 ? _procs.TopCpu[0].cpu : 1) : (count > 0 ? _procs.TopMem[0].mem : 1);
        for (int i = 0; i < Math.Min(rows, count); i++)
        {
            float yy = y + i * 24;
            string name = cpu ? _procs.TopCpu[i].name : _procs.TopMem[i].name;
            double val = cpu ? _procs.TopCpu[i].cpu : _procs.TopMem[i].mem;
            using (var bar = new SolidBrush(Color.FromArgb(28, Accent)))
                g.FillRectangle(bar, x, yy + 1, (float)(w * Math.Clamp(val / Math.Max(1e-9, top), 0, 1)), 21);
            T(g, Trunc(name, 24), _f, TextCol, x + 6, yy + 2);
            TR(g, cpu ? $"{val:0.0}%" : SizeStr(val), _f, Dim, x + w - 6, yy + 2);
        }
    }

    private static string CpuName()
    {
        if (_cpuName is not null) return _cpuName;
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            _cpuName = (k?.GetValue("ProcessorNameString") as string)?.Trim() ?? "";
        }
        catch { _cpuName = ""; }
        return _cpuName;
    }

    /// <summary>Adapters met verkeer nu, in het gekozen venster of vandaag (meest actief eerst).</summary>
    private List<string> ActiveAdapters(int max)
    {
        var m = _c.Metrics;
        bool Active(string n)
        {
            m.NetPerAdapter.TryGetValue(n, out var r0);
            if (r0.down >= 1024 || r0.up >= 1024) return true;
            if (_c.History.NetPer.TryGetValue(n, out var pr) && (pr.down.Max(_win) >= 1024 || pr.up.Max(_win) >= 1024)) return true;
            return _c.Usage.Today(n).Total > 0;
        }
        return m.NetPerAdapter.Keys.Where(Active)
            .OrderByDescending(n => { m.NetPerAdapter.TryGetValue(n, out var r0); return r0.down + r0.up + _c.Usage.Today(n).Total; })
            .Take(max).ToList();
    }

    private List<KeyValuePair<string, double>> Gpus()
        => _c.Metrics.GpuPerLuid.Where(k => k.Key != "" && Metrics.IsRealGpu(k.Key)).OrderBy(k => k.Key).ToList();

    // ---------- Sensoren (LibreHardwareMonitor) ----------
    private sealed record SRow(string Name, string Value, SensorType Type, double Raw);
    private static readonly Regex NumSuffix = new(@"^(.*?)\s*#?(\d+)(\s*\(.*\))?$", RegexOptions.Compiled);

    private static bool IsGpuHw(HardwareType t) => t is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;
    private static string Fmt(SensorType t, double v) => SensorInfo.FormatValue(t, v);
    private Color TempColor(double v) => v >= 90 ? Col(Cfg.CritColor, Color.Red) : v >= 75 ? Col(Cfg.WarnColor, Color.Orange) : TextCol;

    private static int Prio(SensorType t) => t switch
    {
        SensorType.Power => 0, SensorType.Temperature => 1, SensorType.Clock => 2, SensorType.Voltage => 3, SensorType.Current => 4,
        SensorType.Fan => 5, SensorType.Control => 6, SensorType.Level => 7, SensorType.Load => 8, _ => 9,
    };

    /// <summary>Sensoren die "niet ondersteund" melden (bijv. 255 °C, -1 FPS) horen niet in de lijst.</summary>
    private static bool PlausibleValue(SensorInfo s)
    {
        double v = s.Value ?? 0;
        return s.Type switch
        {
            SensorType.Temperature => v > -30 && v < 150,
            SensorType.Fan or SensorType.Clock or SensorType.Data or SensorType.SmallData or SensorType.Throughput or SensorType.Factor => v >= 0,
            _ => true,
        };
    }

    /// <summary>Sensoren met een oplopend nummer ("Core #1..#16", "Fan #2") worden één regel met bereik.</summary>
    private static List<SRow> Rows(IEnumerable<SensorInfo> sensors)
    {
        var rows = new List<SRow>();
        var groups = sensors.Where(s => s.Value.HasValue && PlausibleValue(s)).GroupBy(s =>
        {
            var m = NumSuffix.Match(s.Name);
            string key = m.Success && m.Groups[1].Value.Length > 0 ? m.Groups[1].Value + m.Groups[3].Value : s.Name;
            return (key, s.Type, s.Hardware);
        });
        foreach (var grp in groups)
        {
            var list = grp.ToList();
            if (list.Count > 1 && NumSuffix.IsMatch(list[0].Name))
            {
                double mn = list.Min(x => x.Value!.Value), mx = list.Max(x => x.Value!.Value);
                string val = Math.Abs(mx - mn) < 1e-9 ? Fmt(grp.Key.Type, mx) : $"{Fmt(grp.Key.Type, mn)} – {Fmt(grp.Key.Type, mx)}";
                rows.Add(new SRow($"{grp.Key.Item1} (×{list.Count})", val, grp.Key.Type, mx));
            }
            else foreach (var s in list) rows.Add(new SRow(s.Name, Fmt(s.Type, s.Value!.Value), s.Type, s.Value!.Value));
        }
        return rows.OrderBy(r => Prio(r.Type)).ThenBy(r => r.Name).ToList();
    }

    private float SensorList(Graphics g, float x, float y, float w, int maxRows, List<SRow> rows)
    {
        int shown = Math.Min(maxRows, rows.Count);
        for (int i = 0; i < shown; i++)
        {
            var r = rows[i];
            float yy = y + i * 24;
            T(g, Trunc(r.Name, Math.Max(12, (int)(w / 8.5f))), _f, Dim, x, yy);
            TR(g, r.Value, _f, r.Type == SensorType.Temperature ? TempColor(r.Raw) : TextCol, x + w, yy);
        }
        if (rows.Count > shown) T(g, $"+{rows.Count - shown} …", _fs, Dim, x, y + shown * 24);
        return shown * 24 + (rows.Count > shown ? 20 : 0);
    }

    private string? SensorHint()
    {
        if (_c.Metrics.Sensors.Count > 0) return null;
        return Environment.TickCount64 - _openedAt < 10000
            ? Loc.Pick("Sensoren laden…", "Loading sensors…")
            : IsAdmin()
                ? Loc.Pick("Geen sensorgegevens beschikbaar op deze computer.", "No sensor data available on this computer.")
                : Loc.Pick("Geen sensorgegevens — LibreHardwareMonitor heeft administrator-rechten nodig.", "No sensor data — LibreHardwareMonitor needs administrator rights.");
    }

    private static readonly bool AdminRights =
        new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent())
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    private static bool IsAdmin() => AdminRights;

    private string? CpuPower()
    {
        var p = _c.Metrics.Sensors.Where(s => s.HwType == HardwareType.Cpu && s.Type == SensorType.Power && s.Value.HasValue).ToList();
        var pick = p.FirstOrDefault(s => s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase)) ?? p.FirstOrDefault();
        return pick is null ? null : Fmt(pick.Type, pick.Value!.Value);
    }

    private List<SensorInfo> GpuSensors(string gpuName)
    {
        var all = _c.Metrics.Sensors.Where(s => IsGpuHw(s.HwType) && s.Value.HasValue).ToList();
        var hws = all.Select(s => s.Hardware).Distinct().ToList();
        string? hw = hws.FirstOrDefault(h => h.Contains(gpuName, StringComparison.OrdinalIgnoreCase) || gpuName.Contains(h, StringComparison.OrdinalIgnoreCase))
                     ?? (hws.Count == 1 ? hws[0] : null);
        return hw is null ? new List<SensorInfo>() : all.Where(s => s.Hardware == hw).ToList();
    }

    private string GpuInfo(string gpuName)
    {
        var mine = GpuSensors(gpuName);
        if (mine.Count == 0) return "";
        var parts = new List<string>();
        var temp = mine.FirstOrDefault(s => s.Type == SensorType.Temperature && s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                   ?? mine.FirstOrDefault(s => s.Type == SensorType.Temperature);
        if (temp is not null) parts.Add(Fmt(temp.Type, temp.Value!.Value));
        var pw = mine.FirstOrDefault(s => s.Type == SensorType.Power);
        if (pw is not null) parts.Add(Fmt(pw.Type, pw.Value!.Value));
        var ck = mine.FirstOrDefault(s => s.Type == SensorType.Clock && s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase));
        if (ck is not null) parts.Add(Fmt(ck.Type, ck.Value!.Value));
        var fan = mine.FirstOrDefault(s => s.Type == SensorType.Fan);
        if (fan is not null) parts.Add(Fmt(fan.Type, fan.Value!.Value));
        return string.Join("  ·  ", parts);
    }

    /// <summary>Per fysieke schijf: temperatuur en gezondheid (uit SMART via LibreHardwareMonitor).</summary>
    private List<(string name, string text)> StorageLines()
    {
        var res = new List<(string, string)>();
        foreach (var grp in _c.Metrics.Sensors.Where(s => s.HwType == HardwareType.Storage && s.Value.HasValue).GroupBy(s => s.Hardware))
        {
            var parts = new List<string>();
            var t = grp.FirstOrDefault(s => s.Type == SensorType.Temperature);
            if (t is not null) parts.Add(Fmt(t.Type, t.Value!.Value));
            var used = grp.FirstOrDefault(s => s.Name.Contains("Percentage Used", StringComparison.OrdinalIgnoreCase));
            var life = grp.FirstOrDefault(s => s.Name.Contains("Remaining Life", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("Life", StringComparison.OrdinalIgnoreCase));
            if (used is not null) parts.Add($"{100 - used.Value!.Value:0}% {Loc.Pick("gezond", "health")}");
            else if (life is not null) parts.Add($"{life.Value!.Value:0}% {Loc.Pick("levensduur", "life")}");
            if (parts.Count > 0) res.Add((grp.Key, string.Join("  ·  ", parts)));
        }
        return res;
    }

    // ---------- Kop ----------
    private void DrawHeader(Graphics g)
    {
        if (_detail is null)
        {
            T(g, "TaskbarStats", _fh, TextCol, M + 4, 18);
            T(g, "· " + Environment.MachineName, _f, Dim, M + 138, 21);
            var chip = new RectangleF(M + 330, 16, 210, 32);
            _hits.Add(("spec", chip));
            using (var path = Rounded(chip, 8))
            using (var bg = new SolidBrush(Color.FromArgb(_hover == "spec" ? 50 : 26, 255, 255, 255)))
                g.FillPath(bg, path);
            TC(g, Loc.Pick("Specificaties  (I)", "Specifications  (I)"), _fb, TextCol, chip.X + chip.Width / 2, chip.Y + chip.Height / 2);
        }
        else
        {
            var back = new RectangleF(M, 12, 230, 40);
            _hits.Add(("back", back));
            bool hot = _hover == "back";
            using (var path = Rounded(back, 10))
            using (var bg = new SolidBrush(Color.FromArgb(hot ? 50 : 26, 255, 255, 255)))
                g.FillPath(bg, path);
            T(g, "←  " + Loc.Pick("Terug (Esc)", "Back (Esc)"), _fb, TextCol, M + 16, 23);
        }

        TC(g, DateTime.Now.ToString("HH:mm:ss"), _fbig, TextCol, CW / 2 - 60, 34);
        T(g, DateTime.Now.ToString("dddd d MMMM yyyy"), _f, Dim, CW / 2 + 40, 26);

        // tijdvenster
        // afsluitknop uiterst rechts (zelfde als Esc in het overzicht)
        var exit = new RectangleF(CW - M - 44, 16, 44, 32);
        _hits.Add(("exit", exit));
        bool exitHot = _hover == "exit";
        using (var path = Rounded(exit, 8))
        using (var bg = new SolidBrush(exitHot ? Color.FromArgb(220, 200, 40, 40) : Color.FromArgb(26, 255, 255, 255)))
            g.FillPath(bg, path);
        TC(g, "✕", _fb, TextCol, exit.X + exit.Width / 2, exit.Y + exit.Height / 2);

        float x = CW - M - 4 - 56;
        var chips = new (int w, string l)[] { (3600, Loc.Pick("1 u", "1 h")), (300, "5 m"), (60, "1 m") };
        foreach (var (w, l) in chips)
        {
            var r = new RectangleF(x - 56, 16, 56, 32);
            _hits.Add(($"w{w}", r));
            bool on = _win == w, hot = _hover == $"w{w}";
            using (var path = Rounded(r, 8))
            using (var bg = new SolidBrush(on ? Color.FromArgb(180, Accent) : Color.FromArgb(hot ? 50 : 26, 255, 255, 255)))
                g.FillPath(bg, path);
            TC(g, l, _fb, TextCol, r.X + r.Width / 2, r.Y + r.Height / 2);
            x -= 62;
        }
        TR(g, Loc.Pick("grafiek:", "graph:"), _fs, Dim, x - 4, 24);
        string hint = _tour ? Loc.Pick($"Tour {_tourIdx + 1}/{_tourPages.Count}  ·  klik of toets = stop", $"Tour {_tourIdx + 1}/{_tourPages.Count}  ·  click or key = stop")
                    : _detail is null ? Loc.Pick("Klik op een tegel voor details  ·  spatie = tour  ·  Esc of ✕ sluit", "Click a tile for details  ·  space = tour  ·  Esc or ✕ closes")
                    : Loc.Pick("Esc: terug naar het overzicht", "Esc: back to the overview");
        TR(g, hint, _fs, _tour ? Accent : Dim, x - 110, 24);
        if (_tour)   // voortgang van de huidige pagina
        {
            float f = Math.Clamp((Environment.TickCount64 - _tourAt) / (float)TourMs, 0f, 1f);
            using var pb = new SolidBrush(Color.FromArgb(200, Accent));
            g.FillRectangle(pb, 0, CH - 5, CW * f, 5);
        }
    }

    // ---------- Overzicht ----------
    private void DrawOverview(Graphics g)
    {
        float top = 76;
        var visible = Tiles.Order(_c.Cfg.FullOrder).Where(id => Tiles.FullOn(_c.Cfg, id));
        var cells = Tiles.FullCells(visible, CW, CH, top, M);
        if (cells.Count == 0)
        {
            T(g, Loc.Pick("Geen onderdelen aan — zet ze aan via Instellingen (tab Fullscreen).", "No components enabled — turn them on in Settings (Fullscreen tab)."), _f, Dim, M + 8, top + 20);
            return;
        }
        foreach (var (ids, r) in cells) DrawCell(g, ids, r);
    }

    private void DrawCell(Graphics g, string[] c, RectangleF r)
    {
        if (c.Length == 2)   // batterij + systeem boven elkaar
        {
            TileBattery(g, new RectangleF(r.X, r.Y, r.Width, 190));
            TileSystem(g, new RectangleF(r.X, r.Y + 190 + M, r.Width, Math.Max(120, r.Height - 190 - M)));
            return;
        }
        switch (c[0])
        {
            case "cpu": TileCpu(g, r); break;
            case "gpu": TileGpu(g, r); break;
            case "mem": TileMem(g, r); break;
            case "net": TileNet(g, r); break;
            case "disk": TileDisk(g, r); break;
            case "batt": TileBattery(g, r); break;
            case "sys": TileSystem(g, r); break;
            case "proc": TileProcs(g, r); break;
        }
    }

    private void TileCpu(Graphics g, RectangleF r)
    {
        var m = _c.Metrics;
        string sub = $"{m.CpuCores.Length} {Loc.Pick("kernen", "cores")}" + (m.CpuMHz is double f ? $"  ·  {f / 1000:0.00} GHz" : "") + (m.CpuTempC is double t ? $"  ·  {t:0}°C" : "") + (CpuPower() is string pw ? $"  ·  {pw}" : "");
        Card(g, "cpu", r, "CPU", sub);
        T(g, Trunc(CpuName(), 60), _fs, Dim, r.X + 16, r.Y + 36);
        Gauge(g, r.X + 96, r.Y + 140, 62, m.CpuPercent, Thr(m.CpuPercent), $"{m.CpuPercent:0}%");
        StatRows(g, r.X + 16, r.Y + 224, 170, _c.History.Cpu, Pct);

        var cores = m.CpuCores;
        if (cores.Length > 0)
        {
            float x0 = r.X + 232, w = r.Right - 16 - x0, y0 = r.Y + 62;
            int cols = cores.Length <= 8 ? 4 : cores.Length <= 24 ? 6 : 8;
            int rows = (cores.Length + cols - 1) / cols;
            float cw = w / cols, ch = Math.Min(48, 230f / rows);
            for (int i = 0; i < cores.Length; i++)
            {
                float x = x0 + (i % cols) * cw, y = y0 + (i / cols) * ch;
                var cell = new RectangleF(x + 2, y + 2, cw - 4, ch - 4);
                using (var path = Rounded(cell, 6))
                using (var bg = new SolidBrush(Color.FromArgb(24, 255, 255, 255)))
                    g.FillPath(bg, path);
                Bar(g, cell.X + 6, cell.Bottom - 8, cell.Width - 12, cores[i], Thr(cores[i]), 4);
                T(g, $"C{i}", _fs, Dim, cell.X + 8, cell.Y + 3);
                TR(g, $"{cores[i]:0}%", _fb, TextCol, cell.Right - 8, cell.Y + 3);
            }
        }
        Graph(g, new RectangleF(r.X + 16, r.Y + 322, r.Width - 32, Math.Max(60, r.Height - 322 - 12)), new[] { (_c.History.Cpu, Accent) }, 100, Pct);
    }

    private void TileGpu(Graphics g, RectangleF r)
    {
        var gpus = Gpus();
        var m = _c.Metrics;
        Card(g, "gpu", r, "GPU", m.GpuTempC is double t ? $"{t:0}°C" : null);
        if (gpus.Count == 0) { T(g, Loc.Pick("Geen GPU-gegevens", "No GPU data"), _f, Dim, r.X + 16, r.Y + 50); return; }
        float sh = (r.Height - 52) / gpus.Count;
        for (int i = 0; i < gpus.Count; i++)
        {
            var (luid, v) = (gpus[i].Key, gpus[i].Value);
            float y0 = r.Y + 44 + i * sh;
            T(g, Trunc(Metrics.GpuName(luid), 32), _fb, TextCol, r.X + 16, y0);
            TR(g, $"{v:0}%", _fbig, Thr(v), r.Right - 16, y0 - 10);
            Bar(g, r.X + 16, y0 + 30, r.Width - 32, v, Thr(v), 8);
            long ded = Metrics.GpuDedicatedBytes(luid);
            float gy = y0 + 46;
            if (ded > 0 && m.VramUsedPerLuid.TryGetValue(luid, out var used))
            {
                T(g, $"VRAM  {SizeStr(used)} / {SizeStr(ded)}", _fs, Dim, r.X + 16, y0 + 46);
                Bar(g, r.X + 190, y0 + 50, r.Width - 206, 100.0 * used / ded, Green, 6);
                gy = y0 + 66;
            }
            string info = GpuInfo(Metrics.GpuName(luid));
            if (info != "") { T(g, info, _fs, Dim, r.X + 16, gy); gy += 20; }
            if (_c.History.GpuPer.TryGetValue(luid, out var ring))
                Graph(g, new RectangleF(r.X + 16, gy, r.Width - 32, Math.Max(50, y0 + sh - gy - 14)), new[] { (ring, Accent) }, 100, Pct, false);
        }
    }

    private void TileMem(Graphics g, RectangleF r)
    {
        var m = _c.Metrics;
        Card(g, "mem", r, Loc.S("memory"), $"{SizeStr(m.MemTotalBytes)}");
        Gauge(g, r.X + 82, r.Y + 122, 50, m.MemPercent, Thr(m.MemPercent), $"{m.MemPercent:0}%");
        T(g, $"{SizeStr(m.MemUsedBytes)}", _fbig, TextCol, r.X + 150, r.Y + 84);
        T(g, $"{Loc.Pick("in gebruik van", "in use of")} {SizeStr(m.MemTotalBytes)}", _f, Dim, r.X + 152, r.Y + 126);
        T(g, $"{Loc.Pick("Vrij", "Free")}  {SizeStr(m.MemTotalBytes - m.MemUsedBytes)}", _f, Dim, r.X + 152, r.Y + 150);
        Graph(g, new RectangleF(r.X + 16, r.Y + 190, r.Width - 32, 140), new[] { (_c.History.Mem, Green) }, 100, Pct);
        T(g, Loc.Pick("Meeste geheugen", "Top memory"), _fs, Dim, r.X + 16, r.Y + 348);
        ProcList(g, r.X + 16, r.Y + 370, r.Width - 32, 5, false);
    }

    private void TileNet(Graphics g, RectangleF r)
    {
        var m = _c.Metrics;
        Card(g, "net", r, Loc.Pick("Netwerk", "Network"));
        T(g, $"↓ {Rate(m.NetDownBytesPerSec)}", _fbig, Accent, r.X + 16, r.Y + 44);
        T(g, $"↑ {Rate(m.NetUpBytesPerSec)}", _fh, Green, r.X + 16, r.Y + 90);
        Graph(g, new RectangleF(r.X + 210, r.Y + 44, r.Width - 226, 92), new[] { (_c.History.NetDown, Accent), (_c.History.NetUp, Green) }, 0, Rate, false);

        float y = r.Y + 150;
        foreach (var (label, u) in new[]
        {
            (Loc.Pick("Sessie", "Session"), _c.Usage.Session(null)), (Loc.Pick("Vandaag", "Today"), _c.Usage.Today(null)),
            (Loc.Pick("Gisteren", "Yesterday"), _c.Usage.Yesterday(null)), (Loc.Pick("7 dagen", "7 days"), _c.Usage.Week(null)),
            (Loc.Pick("Deze maand", "This month"), _c.Usage.Month(null)),
        })
        {
            T(g, label, _f, Dim, r.X + 16, y);
            TR(g, $"↓ {Metrics.FormatBytes(u.Down)}    ↑ {Metrics.FormatBytes(u.Up)}", _f, TextCol, r.Right - 16, y);
            y += 24;
        }
        if (Cfg.MonthlyLimitGb > 0)
        {
            var mu = _c.Usage.Month(Cfg.NetworkAdapter);
            double pct = 100.0 * mu.Total / (Cfg.MonthlyLimitGb * 1073741824.0);
            T(g, $"{Loc.Pick("Limiet", "Limit")}  {Metrics.FormatBytes(mu.Total)} / {Cfg.MonthlyLimitGb} GB", _fs, Dim, r.X + 16, y + 4);
            Bar(g, r.X + 16, y + 26, r.Width - 32, pct, Thr(pct), 7);
            y += 44;
        }
        y += 6;
        foreach (var name in ActiveAdapters(8))
        {
            if (y > r.Bottom - 46) break;
            m.NetPerAdapter.TryGetValue(name, out var rt);
            T(g, TruncMid(name, 30), _fs, Dim, r.X + 16, y);
            TR(g, $"↓ {Rate(rt.down)}  ↑ {Rate(rt.up)}", _fs, TextCol, r.Right - 16, y);
            if (_c.History.NetPer.TryGetValue(name, out var pr))
                Graph(g, new RectangleF(r.X + 16, y + 20, r.Width - 32, 22), new[] { (pr.down, Accent), (pr.up, Green) }, 0, Rate, false);
            y += 48;
        }
    }

    private void TileDisk(Graphics g, RectangleF r)
    {
        var m = _c.Metrics;
        var drives = _c.Drives();
        Card(g, "disk", r, Loc.S("disks"));
        float y = r.Y + 44;
        foreach (var d in drives.Take(6))
        {
            T(g, d.Display, _fb, TextCol, r.X + 16, y);
            TR(g, $"{SizeStr(d.Free)} {Loc.S("freeOf")} {SizeStr(d.Total)}  ({d.UsedPercent:0}%)", _fs, Dim, r.Right - 16, y + 2);
            Bar(g, r.X + 16, y + 24, r.Width - 32, d.UsedPercent, Thr(d.UsedPercent), 8);
            y += 44;
        }
        if (drives.Count > 6) { T(g, $"+{drives.Count - 6} {Loc.Pick("meer — klik voor alle", "more — click for all")}", _fs, Dim, r.X + 16, y - 8); y += 14; }
        T(g, $"R  {Rate(m.DiskReadBytesPerSec)}", _fb, Accent, r.X + 16, y + 4);
        TR(g, $"W  {Rate(m.DiskWriteBytesPerSec)}", _fb, Orange, r.Right - 16, y + 4);
        float gh = 96;
        Graph(g, new RectangleF(r.X + 16, y + 32, r.Width - 32, gh), new[] { (_c.History.DiskRead, Accent), (_c.History.DiskWrite, Orange) }, 0, Rate, false);
        y += 32 + gh + 12;
        foreach (var (name, rt) in m.DiskPerDisk.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (y > r.Bottom - 26) break;
            T(g, Trunc(name, 22), _fs, Dim, r.X + 16, y);
            TR(g, $"R {Rate(rt.read)}   W {Rate(rt.write)}", _fs, TextCol, r.Right - 16, y);
            y += 21;
        }
        foreach (var (name, text) in StorageLines())
        {
            if (y > r.Bottom - 26) break;
            T(g, TruncMid(name, 26), _fs, Dim, r.X + 16, y);
            TR(g, text, _fs, TextCol, r.Right - 16, y);
            y += 21;
        }
    }

    private void TileBattery(Graphics g, RectangleF r)
    {
        var m = _c.Metrics;
        Card(g, "bat", r, Loc.Pick("Batterij", "Battery"));
        if (!m.BatteryPresent) { T(g, Loc.Pick("Geen batterij (desktop-pc)", "No battery (desktop PC)"), _f, Dim, r.X + 16, r.Y + 56); return; }
        DrawBatteryBig(g, r.X + 34, r.Y + 50, 46, 100, m);
        T(g, $"{m.BatteryPercent:0}%", _fhuge, TextCol, r.X + 110, r.Y + 46);
        T(g, BatteryState(m), _f, Dim, r.X + 112, r.Y + 116);
    }

    private static string BatteryState(Metrics m)
    {
        string state = m.BatteryCharging ? Loc.Pick("Laden", "Charging") : m.BatteryOnAc ? Loc.Pick("Op netstroom", "Plugged in") : Loc.Pick("Ontladen", "On battery");
        return state + (!m.BatteryOnAc && m.BatteryRemainingSec > 0
            ? $"  ·  {m.BatteryRemainingSec / 3600}{Loc.Pick("u", "h")} {m.BatteryRemainingSec % 3600 / 60:00}m {Loc.Pick("resterend", "left")}" : "");
    }

    private void DrawBatteryBig(Graphics g, float x, float y, float w, float h, Metrics m)
    {
        double pct = m.BatteryPercent;
        Color fill = m.BatteryCharging || m.BatteryOnAc ? Green
                   : pct <= 10 ? Col(Cfg.CritColor, Color.Red) : pct <= 20 ? Col(Cfg.WarnColor, Color.Orange) : Accent;
        using (var nub = new SolidBrush(Color.FromArgb(142, 142, 147))) g.FillRectangle(nub, x + w / 2 - 9, y - 6, 18, 6);
        using (var path = Rounded(new RectangleF(x, y, w, h), 7))
        using (var bg = new SolidBrush(Color.FromArgb(42, 42, 45)))
        using (var edge = new Pen(Color.FromArgb(142, 142, 147), 2f))
        {
            g.FillPath(bg, path);
            g.DrawPath(edge, path);
        }
        float ih = h - 6, fh = (float)(ih * pct / 100.0);
        if (fh >= 1)
            using (var fp = Rounded(new RectangleF(x + 3, y + 3 + ih - fh, w - 6, fh), 4))
            using (var fb = new SolidBrush(fill))
                g.FillPath(fb, fp);
        if (m.BatteryCharging) WidgetForm.DrawBolt(g, x + w / 2, y + h / 2, h * 0.5f);
        else if (m.BatteryOnAc) WidgetForm.DrawPlug(g, x + w / 2, y + h / 2, h * 0.5f);
    }

    private void TileSystem(Graphics g, RectangleF r)
    {
        var m = _c.Metrics;
        Card(g, "sys", r, Loc.Pick("Systeem", "System"));
        T(g, DateTime.Now.ToString("HH:mm"), _fhuge, TextCol, r.X + 16, r.Y + 40);
        T(g, DateTime.Now.ToString("dddd d MMMM"), _f, Dim, r.X + 205, r.Y + 62);
        var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
        float y = r.Y + 110;
        KeyValue(g, r.X + 16, y, r.Width - 32, "Uptime", $"{(int)up.TotalDays} {Loc.Pick("d", "d")} {up.Hours} {Loc.Pick("u", "h")} {up.Minutes} m"); y += 24;
        KeyValue(g, r.X + 16, y, r.Width - 32, Loc.Pick("Computer", "Computer"), Environment.MachineName); y += 24;
        KeyValue(g, r.X + 16, y, r.Width - 32, "OS", Trunc(System.Runtime.InteropServices.RuntimeInformation.OSDescription, 34)); y += 24;
        KeyValue(g, r.X + 16, y, r.Width - 32, Loc.Pick("Werkgeheugen", "Memory"), SizeStr(m.MemTotalBytes)); y += 24;
        if (m.CpuTempC is double c) { KeyValue(g, r.X + 16, y, r.Width - 32, "CPU", $"{c:0}°C"); y += 24; }
        if (m.GpuTempC is double t) KeyValue(g, r.X + 16, y, r.Width - 32, "GPU", $"{t:0}°C");
    }

    private void TileProcs(Graphics g, RectangleF r)
    {
        Card(g, "proc", r, Loc.Pick("Zwaarste programma's", "Top programs"));
        T(g, "CPU", _fs, Dim, r.X + 16, r.Y + 42);
        ProcList(g, r.X + 16, r.Y + 62, r.Width - 32, 7, true);
        float y2 = r.Y + 62 + 7 * 24 + 14;
        T(g, "RAM", _fs, Dim, r.X + 16, y2);
        ProcList(g, r.X + 16, y2 + 20, r.Width - 32, 7, false);
    }

    // ---------- Details ----------
    private void DrawDetail(Graphics g)
    {
        var R = new RectangleF(M, 76, CW - 2 * M, CH - 76 - M);
        switch (_detail)
        {
            case "cpu": DetailCpu(g, R); break;
            case "gpu": DetailGpu(g, R); break;
            case "mem": DetailMem(g, R); break;
            case "net": DetailNet(g, R); break;
            case "disk": DetailDisk(g, R); break;
            case "sys": DetailSys(g, R); break;
            case "proc": DetailProcs(g, R); break;
            case "spec": DetailSpecs(g, R); break;
        }
    }

    // Kop van de specificatiepagina: waarde inkorten zodat hij in de kolom past.
    private string FitText(Graphics g, string s, Font f, float maxW)
    {
        if (g.MeasureString(s, f).Width <= maxW) return s;
        int lo = 1, hi = s.Length;
        while (lo < hi) { int mid = (lo + hi + 1) / 2; if (g.MeasureString(s[..mid] + "…", f).Width <= maxW) lo = mid; else hi = mid - 1; }
        return s[..lo] + "…";
    }

    private void DetailSpecs(Graphics g, RectangleF R)
    {
        var blocks = HardwareInfo.Blocks;
        Card(g, null, R, Loc.Pick("Specificaties", "Specifications"),
             HardwareInfo.Loading ? Loc.Pick("bezig met laden…", "loading…") : Loc.Pick("muiswiel = scrollen", "mouse wheel = scroll"));
        if (blocks.Count == 0)
        {
            T(g, HardwareInfo.Loading ? Loc.Pick("Hardware-informatie wordt verzameld…", "Collecting hardware information…")
                                      : Loc.Pick("Geen informatie beschikbaar (nieuwe poging volgt automatisch).", "No information available (retrying automatically)."), _f, Dim, R.X + 20, R.Y + 60);
            if (!HardwareInfo.Loading)
            {
                if (HardwareInfo.LastError.Length > 0) T(g, HardwareInfo.LastError, _f, Dim, R.X + 20, R.Y + 90);
                HardwareInfo.Refresh(_c.Metrics);   // begrensd tot 1 poging per 10 s
            }
            return;
        }
        var area = new RectangleF(R.X + 12, R.Y + 48, R.Width - 24, R.Height - 58);
        const int cols = 3; const float gap = 14, rowH = 23, head = 36, pad = 12;
        float cw = (area.Width - gap * (cols - 1)) / cols;
        var ys = new float[cols];
        g.SetClip(area);
        foreach (var b in blocks)
        {
            int c = 0;
            for (int i = 1; i < cols; i++) if (ys[i] < ys[c]) c = i;
            float x = area.X + c * (cw + gap), y = area.Y - _specScroll + ys[c];
            float h = head + b.Rows.Count * rowH + pad;
            var r = new RectangleF(x, y, cw, h);
            if (r.Bottom > area.Y && r.Y < area.Bottom)
            {
                using (var path = Rounded(r, 10))
                using (var bg = new SolidBrush(Color.FromArgb(14, 255, 255, 255)))
                using (var pen = new Pen(Color.FromArgb(26, 255, 255, 255)))
                { g.FillPath(bg, path); g.DrawPath(pen, path); }
                T(g, b.Title, _fb, Accent, x + 14, y + 8);
                float ry = y + head;
                foreach (var row in b.Rows)
                {
                    float kw = string.IsNullOrEmpty(row.Key) ? 0 : Math.Min(g.MeasureString(row.Key, _fs).Width + 14, cw * 0.42f);
                    if (row.Key != "") T(g, FitText(g, row.Key, _fs, cw * 0.42f), _fs, Dim, x + 14, ry);
                    string v = row.Value();
                    TR(g, FitText(g, v, _fs, cw - 28 - kw), _fs, TextCol, x + cw - 14, ry);
                    ry += rowH;
                }
            }
            ys[c] += h + gap;
        }
        g.ResetClip();
        float total = ys.Max();
        _specMax = Math.Max(0, total - area.Height);
        if (_specScroll > _specMax) _specScroll = _specMax;
        if (_specMax > 0)   // schuifbalk
        {
            float th = Math.Max(40, area.Height * area.Height / total);
            float ty = area.Y + (area.Height - th) * (_specScroll / _specMax);
            using var sb = new SolidBrush(Color.FromArgb(70, 255, 255, 255));
            g.FillRectangle(sb, R.Right - 7, ty, 4, th);
        }
    }

    private void DetailCpu(Graphics g, RectangleF R)
    {
        var m = _c.Metrics;
        Card(g, null, R, "CPU — " + Loc.Pick("details", "details"), Trunc(CpuName(), 70));
        float gw = 1240;
        var cores = m.CpuCores;
        int ccols = cores.Length <= 8 ? 4 : cores.Length <= 24 ? 6 : 8;
        int crows = cores.Length == 0 ? 0 : (cores.Length + ccols - 1) / ccols;
        float cellH = crows == 0 ? 0 : Math.Min(130f, 400f / crows);
        float cpuGh = Math.Max(260f, R.Bottom - 16 - (R.Y + 50) - crows * cellH - 24);
        Graph(g, new RectangleF(R.X + 16, R.Y + 50, gw, cpuGh), new[] { (_c.History.Cpu, Accent) }, 100, Pct);

        if (cores.Length > 0)
        {
            int cols = ccols;
            int rows = crows;
            float y0 = R.Y + 50 + cpuGh + 24, cw = gw / cols, ch = cellH;
            for (int i = 0; i < cores.Length; i++)
            {
                float cx0 = R.X + 16 + (i % cols) * cw, cy0 = y0 + (i / cols) * ch;
                var cell = new RectangleF(cx0 + 3, cy0 + 3, cw - 6, ch - 6);
                using (var path = Rounded(cell, 8))
                using (var bg = new SolidBrush(Color.FromArgb(22, 255, 255, 255)))
                    g.FillPath(bg, path);
                T(g, Loc.Pick("Kern", "Core") + $" {i}", _fs, Dim, cell.X + 10, cell.Y + 6);
                TR(g, $"{cores[i]:0}%", _fb, Thr(cores[i]), cell.Right - 10, cell.Y + 5);
                if (i < _c.History.Cores.Count)
                    Graph(g, new RectangleF(cell.X + 8, cell.Y + 28, cell.Width - 16, cell.Height - 36), new[] { (_c.History.Cores[i], Accent) }, 100, Pct, false);
            }
        }

        float rx = R.X + gw + 40, rw = R.Right - 16 - rx;
        T(g, Loc.Pick("Totaal", "Total"), _fs, Dim, rx, R.Y + 50);
        Gauge(g, rx + 70, R.Y + 150, 56, m.CpuPercent, Thr(m.CpuPercent), $"{m.CpuPercent:0}%");
        StatRows(g, rx + 160, R.Y + 96, rw - 160, _c.History.Cpu, Pct);
        float y = R.Y + 236;
        KeyValue(g, rx, y, rw, Loc.Pick("Kernen", "Cores"), $"{cores.Length}"); y += 24;
        if (m.CpuMHz is double f) { KeyValue(g, rx, y, rw, Loc.Pick("Klokfrequentie", "Clock speed"), $"{f / 1000:0.00} GHz"); y += 24; }
        if (m.CpuTempC is double t) { KeyValue(g, rx, y, rw, Loc.Pick("Temperatuur", "Temperature"), $"{t:0}°C"); y += 24; }
        y += 12;
        T(g, Loc.Pick("Sensoren", "Sensors"), _fs, Dim, rx, y);
        var cpuRows = Rows(_c.Metrics.Sensors.Where(x => x.HwType == HardwareType.Cpu && x.Type != SensorType.Load));
        float usedH;
        if (cpuRows.Count == 0) { T(g, SensorHint() ?? (IsAdmin() ? Loc.Pick("Temperatuur, vermogen en klokken van deze processor worden niet door de sensorbibliotheek ondersteund.", "Temperature, power and clocks of this processor are not supported by the sensor library.") : Loc.Pick("Geen CPU-sensoren beschikbaar (administrator-rechten nodig).", "No CPU sensors available (administrator rights needed).")), _fs, Dim, rx, y + 22); usedH = 26; }
        else usedH = SensorList(g, rx, y + 22, rw, 9, cpuRows);
        y += 22 + usedH + 14;
        T(g, Loc.Pick("Zwaarste programma's (CPU)", "Top programs (CPU)"), _fs, Dim, rx, y);
        ProcList(g, rx, y + 22, rw, 8, true);
    }

    private void DetailGpu(Graphics g, RectangleF R)
    {
        var gpus = Gpus();
        var m = _c.Metrics;
        Card(g, null, R, "GPU — " + Loc.Pick("details", "details"), m.GpuTempC is double tt ? $"{tt:0}°C" : null);
        if (gpus.Count == 0) { T(g, Loc.Pick("Geen GPU-gegevens", "No GPU data"), _f, Dim, R.X + 16, R.Y + 56); return; }
        float sh = (R.Height - 50) / gpus.Count;
        float col = (R.Width - 64) / 3;
        for (int i = 0; i < gpus.Count; i++)
        {
            var (luid, v) = (gpus[i].Key, gpus[i].Value);
            string gpuName = Metrics.GpuName(luid);
            float y0 = R.Y + 48 + i * sh, hh = sh - 20;
            T(g, gpuName, _fh, TextCol, R.X + 16, y0);
            TR(g, $"{v:0}%", _fbig, Thr(v), R.Right - 16, y0 - 8);
            float gh = hh - 50;
            float x1 = R.X + 16, x2 = R.X + 32 + col, x3 = R.X + 48 + 2 * col;

            T(g, Loc.Pick("Belasting", "Load"), _fs, Dim, x1, y0 + 34);
            if (_c.History.GpuPer.TryGetValue(luid, out var ring))
            {
                Graph(g, new RectangleF(x1, y0 + 54, col - 190, gh), new[] { (ring, Accent) }, 100, Pct);
                StatRows(g, x1 + col - 170, y0 + 60, 170, ring, Pct);
            }
            long ded = Metrics.GpuDedicatedBytes(luid);
            T(g, Loc.Pick("Videogeheugen (VRAM)", "Video memory (VRAM)"), _fs, Dim, x2, y0 + 34);
            if (ded > 0 && _c.History.VramPer.TryGetValue(luid, out var vr))
            {
                Graph(g, new RectangleF(x2, y0 + 54, col - 190, gh), new[] { (vr, Green) }, ded, SizeStr);
                var s2 = Stat(vr);
                KeyValue(g, x2 + col - 170, y0 + 60, 170, Loc.Pick("In gebruik", "Used"), SizeStr(s2.cur));
                KeyValue(g, x2 + col - 170, y0 + 84, 170, Loc.Pick("Totaal", "Total"), SizeStr(ded));
                KeyValue(g, x2 + col - 170, y0 + 108, 170, "Max", SizeStr(s2.max));
            }
            else T(g, Loc.Pick("Geen VRAM-gegevens", "No VRAM data"), _f, Dim, x2, y0 + 60);

            T(g, Loc.Pick("Sensoren", "Sensors"), _fs, Dim, x3, y0 + 34);
            var rows = Rows(GpuSensors(gpuName).Where(x => x.Type != SensorType.Load));
            if (rows.Count == 0) T(g, SensorHint() ?? Loc.Pick("Geen sensoren voor deze kaart.", "No sensors for this card."), _fs, Dim, x3, y0 + 60);
            else SensorList(g, x3, y0 + 58, col - 8, Math.Max(4, (int)((gh - 4) / 24)), rows);
        }
    }

    private void DetailMem(Graphics g, RectangleF R)
    {
        var m = _c.Metrics;
        Card(g, null, R, Loc.S("memory") + " — details", SizeStr(m.MemTotalBytes));
        float gw = 1240;
        Graph(g, new RectangleF(R.X + 16, R.Y + 50, gw, 420), new[] { (_c.History.Mem, Green) }, 100, Pct);
        float rx = R.X + gw + 40, rw = R.Right - 16 - rx;
        Gauge(g, rx + 70, R.Y + 120, 56, m.MemPercent, Thr(m.MemPercent), $"{m.MemPercent:0}%");
        StatRows(g, rx + 160, R.Y + 70, rw - 160, _c.History.Mem, Pct);
        float y = R.Y + 200;
        KeyValue(g, rx, y, rw, Loc.Pick("In gebruik", "In use"), SizeStr(m.MemUsedBytes)); y += 24;
        KeyValue(g, rx, y, rw, Loc.Pick("Beschikbaar", "Available"), SizeStr(m.MemTotalBytes - m.MemUsedBytes)); y += 24;
        KeyValue(g, rx, y, rw, Loc.Pick("Totaal", "Total"), SizeStr(m.MemTotalBytes)); y += 40;
        T(g, Loc.Pick("Meeste geheugen", "Top memory"), _fs, Dim, rx, y);
        ProcList(g, rx, y + 22, rw, 12, false);

        // onderaan: gebruik in GB als tweede grafiek
        T(g, Loc.Pick("Meeste geheugen — alle", "Top memory — all"), _fs, Dim, R.X + 16, R.Y + 500);
        float py = R.Y + 522;
        for (int i = 0; i < Math.Min(12, _procs.TopMem.Count); i++)
        {
            var (name, mem) = _procs.TopMem[i];
            double pct = 100.0 * mem / Math.Max(1, m.MemTotalBytes);
            T(g, Trunc(name, 30), _f, TextCol, R.X + 16, py + i * 34);
            Bar(g, R.X + 300, py + i * 34 + 6, 800, pct * 4, Green, 10);
            TR(g, $"{SizeStr(mem)}  ({pct:0.0}%)", _f, Dim, R.X + gw - 8, py + i * 34);
        }
    }

    private void DetailNet(Graphics g, RectangleF R)
    {
        var m = _c.Metrics;
        Card(g, null, R, Loc.Pick("Netwerk — details", "Network — details"));
        float gw = 1240;
        T(g, $"↓ {Rate(m.NetDownBytesPerSec)}", _fbig, Accent, R.X + 16, R.Y + 44);
        T(g, $"↑ {Rate(m.NetUpBytesPerSec)}", _fbig, Green, R.X + 330, R.Y + 44);
        if (Cfg.ShowPing)
        {
            var ps = m.Ping.Stats();
            var pc = ps.Last is null ? TextCol : ps.Last < 0 || ps.Last >= 250 ? Col(Cfg.CritColor, Color.Red) : ps.Last >= 100 ? Col(Cfg.WarnColor, Color.Orange) : TextCol;
            T(g, $"Ping {ps.LastText}", _fbig, pc, R.X + 660, R.Y + 44);
            var dp = ps.Details.Split("  ·  ");
            T(g, $"{ps.Host}  ·  {dp[0]}", _fs, Dim, R.X + 900, R.Y + 46);
            if (dp.Length > 2) T(g, $"{dp[1]}  ·  {dp[2]}", _fs, Dim, R.X + 900, R.Y + 68);
        }

        var names = m.NetPerAdapter.Keys.Union(_c.Usage.KnownAdapters()).Distinct()
            .OrderByDescending(n => { m.NetPerAdapter.TryGetValue(n, out var r0); return r0.down + r0.up + _c.Usage.Month(n).Total; })
            .ThenBy(n => n).ToList();
        var active = ActiveAdapters(8);
        int mrows = (active.Count + 1) / 2;
        const float miniH = 120;
        float gridH = active.Count == 0 ? 0 : 26 + mrows * (miniH + 8);
        float tableH = (Math.Min(names.Count, 10) + 1) * 26 + 24;
        float netGh = Math.Max(200f, R.Bottom - (R.Y + 96) - gridH - tableH - 60);
        Graph(g, new RectangleF(R.X + 16, R.Y + 96, gw, netGh), new[] { (_c.History.NetDown, Accent), (_c.History.NetUp, Green) }, 0, Rate);

        // kleine grafiekjes per actieve adapter (2 per rij)
        float gy = R.Y + 96 + netGh + 30;
        if (active.Count > 0)
        {
            T(g, Loc.Pick("Per adapter", "Per adapter"), _fs, Dim, R.X + 16, gy - 4);
            gy += 22;
            float cellW = (gw - 8) / 2;
            for (int i = 0; i < active.Count; i++)
            {
                string n = active[i];
                float cx0 = R.X + 16 + (i % 2) * (cellW + 8), cy0 = gy + (i / 2) * (miniH + 8);
                var cell = new RectangleF(cx0, cy0, cellW, miniH);
                using (var path = Rounded(cell, 8))
                using (var bg = new SolidBrush(Color.FromArgb(22, 255, 255, 255)))
                    g.FillPath(bg, path);
                m.NetPerAdapter.TryGetValue(n, out var rt);
                T(g, TruncMid(n, 34), _fs, Dim, cell.X + 10, cell.Y + 6);
                TR(g, $"↓ {Rate(rt.down)}   ↑ {Rate(rt.up)}", _fb, TextCol, cell.Right - 10, cell.Y + 5);
                if (_c.History.NetPer.TryGetValue(n, out var pr))
                    Graph(g, new RectangleF(cell.X + 10, cell.Y + 28, cell.Width - 20, cell.Height - 36), new[] { (pr.down, Accent), (pr.up, Green) }, 0, Rate, false);
            }
            gy += mrows * (miniH + 8);
        }

        // tabel met alle adapters
        float ty = gy + 8;
        string[] heads = { Loc.Pick("Adapter", "Adapter"), Loc.Pick("Nu ↓", "Now ↓"), Loc.Pick("Nu ↑", "Now ↑"), Loc.Pick("Sessie", "Session"), Loc.Pick("Vandaag", "Today"), Loc.Pick("Gisteren", "Yesterday"), Loc.Pick("7 dagen", "7 days"), Loc.Pick("Maand", "Month") };
        float[] xs = { 0, 330, 450, 570, 720, 870, 1020, 1150 };
        for (int i = 0; i < heads.Length; i++)
        {
            if (i == 0) T(g, heads[i], _fs, Dim, R.X + 16 + xs[i], ty);
            else TR(g, heads[i], _fs, Dim, R.X + 16 + xs[i] + 110, ty);
        }
        float y = ty + 26;
        foreach (var n in names)
        {
            if (y > R.Bottom - 30) break;
            m.NetPerAdapter.TryGetValue(n, out var rt);
            T(g, TruncMid(n, 40), _f, TextCol, R.X + 16, y);
            var cells = new[] { Rate(rt.down), Rate(rt.up), Metrics.FormatBytes(_c.Usage.Session(n).Total), Metrics.FormatBytes(_c.Usage.Today(n).Total),
                                Metrics.FormatBytes(_c.Usage.Yesterday(n).Total), Metrics.FormatBytes(_c.Usage.Week(n).Total), Metrics.FormatBytes(_c.Usage.Month(n).Total) };
            for (int i = 0; i < cells.Length; i++) TR(g, cells[i], _f, TextCol, R.X + 16 + xs[i + 1] + 110, y);
            y += 26;
        }

        float rx = R.X + gw + 40, rw = R.Right - 16 - rx;
        T(g, Loc.Pick("Downloadsnelheid", "Download speed"), _fs, Dim, rx, R.Y + 50);
        StatRows(g, rx, R.Y + 74, rw, _c.History.NetDown, Rate);
        T(g, Loc.Pick("Uploadsnelheid", "Upload speed"), _fs, Dim, rx, R.Y + 190);
        StatRows(g, rx, R.Y + 214, rw, _c.History.NetUp, Rate);
        float ry = R.Y + 330;
        T(g, Loc.Pick("Totaal verbruik (alle adapters)", "Total usage (all adapters)"), _fs, Dim, rx, ry);
        ry += 24;
        foreach (var (label, u) in new[]
        {
            (Loc.Pick("Sessie", "Session"), _c.Usage.Session(null)), (Loc.Pick("Vandaag", "Today"), _c.Usage.Today(null)),
            (Loc.Pick("Gisteren", "Yesterday"), _c.Usage.Yesterday(null)), (Loc.Pick("7 dagen", "7 days"), _c.Usage.Week(null)),
            (Loc.Pick("Deze maand", "This month"), _c.Usage.Month(null)),
        })
        {
            T(g, label, _f, Dim, rx, ry);
            TR(g, $"↓ {Metrics.FormatBytes(u.Down)}   ↑ {Metrics.FormatBytes(u.Up)}", _f, TextCol, rx + rw, ry);
            ry += 26;
        }
        if (Cfg.MonthlyLimitGb > 0)
        {
            var mu = _c.Usage.Month(Cfg.NetworkAdapter);
            double pct = 100.0 * mu.Total / (Cfg.MonthlyLimitGb * 1073741824.0);
            T(g, $"{Loc.Pick("Maandlimiet", "Monthly limit")}  {Metrics.FormatBytes(mu.Total)} / {Cfg.MonthlyLimitGb} GB", _fs, Dim, rx, ry + 8);
            Bar(g, rx, ry + 32, rw, pct, Thr(pct), 10);
        }
    }

    private void DetailDisk(Graphics g, RectangleF R)
    {
        var m = _c.Metrics;
        var drives = _c.Drives();
        Card(g, null, R, Loc.S("disks") + " — details");
        float gw = 1240;
        T(g, $"R  {Rate(m.DiskReadBytesPerSec)}", _fbig, Accent, R.X + 16, R.Y + 44);
        T(g, $"W  {Rate(m.DiskWriteBytesPerSec)}", _fbig, Orange, R.X + 380, R.Y + 44);
        float diskGh = Math.Max(240f, R.Bottom - (R.Y + 96) - (drives.Count * 34 + 84));
        Graph(g, new RectangleF(R.X + 16, R.Y + 96, gw, diskGh), new[] { (_c.History.DiskRead, Accent), (_c.History.DiskWrite, Orange) }, 0, Rate);

        float y = R.Y + 96 + diskGh + 24;
        T(g, Loc.Pick("Stations", "Drives"), _fs, Dim, R.X + 16, y);
        y += 24;
        foreach (var d in drives)
        {
            T(g, d.Display, _fb, TextCol, R.X + 16, y);
            Bar(g, R.X + 90, y + 6, 640, d.UsedPercent, Thr(d.UsedPercent), 12);
            TR(g, $"{SizeStr(d.Used)} {Loc.Pick("gebruikt", "used")}  ·  {SizeStr(d.Free)} {Loc.Pick("vrij", "free")}  ·  {SizeStr(d.Total)}  ({d.UsedPercent:0}%)", _f, Dim, R.X + gw + 8, y);
            y += 34;
        }

        float rx = R.X + gw + 40, rw = R.Right - 16 - rx;
        T(g, Loc.Pick("Lezen", "Read"), _fs, Dim, rx, R.Y + 50);
        StatRows(g, rx, R.Y + 74, rw, _c.History.DiskRead, Rate);
        T(g, Loc.Pick("Schrijven", "Write"), _fs, Dim, rx, R.Y + 190);
        StatRows(g, rx, R.Y + 214, rw, _c.History.DiskWrite, Rate);
        float ry = R.Y + 330;
        T(g, Loc.Pick("Fysieke schijven", "Physical disks"), _fs, Dim, rx, ry);
        ry += 24;
        foreach (var (name, rt) in m.DiskPerDisk.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            T(g, Trunc(name, 22), _f, TextCol, rx, ry);
            TR(g, $"R {Rate(rt.read)}   W {Rate(rt.write)}", _f, Dim, rx + rw, ry);
            ry += 26;
        }
        var stLines = StorageLines();
        ry += 14;
        T(g, Loc.Pick("Temperatuur en gezondheid", "Temperature and health"), _fs, Dim, rx, ry);
        ry += 24;
        if (stLines.Count == 0) T(g, SensorHint() ?? Loc.Pick("Geen SMART-gegevens beschikbaar.", "No SMART data available."), _fs, Dim, rx, ry);
        foreach (var (name, text) in stLines)
        {
            T(g, TruncMid(name, 30), _f, TextCol, rx, ry);
            TR(g, text, _f, Dim, rx + rw, ry);
            ry += 26;
        }
        var stRows = Rows(_c.Metrics.Sensors.Where(x => x.HwType == HardwareType.Storage && x.Type != SensorType.Load));
        if (stRows.Count > 0)
        {
            ry += 10;
            T(g, Loc.Pick("Alle schijfsensoren", "All disk sensors"), _fs, Dim, rx, ry);
            SensorList(g, rx, ry + 22, rw, Math.Max(3, (int)((R.Bottom - 20 - (ry + 22)) / 24)), stRows);
        }
    }

    private void DetailSys(Graphics g, RectangleF R)
    {
        var m = _c.Metrics;
        Card(g, null, R, Loc.Pick("Batterij en systeem — details", "Battery and system — details"));
        float half = (R.Width - 48) / 2;

        // batterij
        float x = R.X + 16, y = R.Y + 56;
        T(g, Loc.Pick("Batterij", "Battery"), _fs, Dim, x, y);
        if (m.BatteryPresent)
        {
            DrawBatteryBig(g, x + 20, y + 40, 90, 190, m);
            T(g, $"{m.BatteryPercent:0}%", _fhuge, TextCol, x + 150, y + 36);
            T(g, BatteryState(m), _fh, Dim, x + 152, y + 110);
            KeyValue(g, x + 152, y + 160, 420, Loc.Pick("Netstroom", "AC power"), m.BatteryOnAc ? Loc.Pick("aangesloten", "connected") : Loc.Pick("niet aangesloten", "not connected"));
            KeyValue(g, x + 152, y + 186, 420, Loc.Pick("Laden", "Charging"), m.BatteryCharging ? Loc.Pick("ja", "yes") : Loc.Pick("nee", "no"));
        }
        else T(g, Loc.Pick("Geen batterij aanwezig (desktop-pc)", "No battery present (desktop PC)"), _f, Dim, x, y + 30);

        // systeem
        float sx = R.X + 32 + half, sy = R.Y + 56;
        T(g, Loc.Pick("Systeem", "System"), _fs, Dim, sx, sy);
        T(g, DateTime.Now.ToString("HH:mm:ss"), _fhuge, TextCol, sx, sy + 26);
        var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
        float yy = sy + 110;
        void kv(string k, string v) { KeyValue(g, sx, yy, half - 16, k, v); yy += 26; }
        kv(Loc.Pick("Computer", "Computer"), Environment.MachineName);
        kv(Loc.Pick("Gebruiker", "User"), Environment.UserName);
        kv("OS", System.Runtime.InteropServices.RuntimeInformation.OSDescription);
        kv(Loc.Pick("Opgestart", "Booted"), (DateTime.Now - up).ToString("yyyy-MM-dd HH:mm"));
        kv("Uptime", $"{(int)up.TotalDays} {Loc.Pick("d", "d")} {up.Hours} {Loc.Pick("u", "h")} {up.Minutes} m");
        kv("CPU", Trunc(CpuName(), 46));
        kv(Loc.Pick("Kernen", "Cores"), $"{m.CpuCores.Length} ({Environment.ProcessorCount} threads)");
        kv(Loc.Pick("Werkgeheugen", "Memory"), SizeStr(m.MemTotalBytes));
        if (m.CpuTempC is double c) kv(Loc.Pick("CPU-temperatuur", "CPU temperature"), $"{c:0}°C");
        if (m.GpuTempC is double t) kv(Loc.Pick("GPU-temperatuur", "GPU temperature"), $"{t:0}°C");

        // videokaarten en schermen onderaan links
        float by = R.Y + 340;
        T(g, Loc.Pick("Videokaarten", "Graphics cards"), _fs, Dim, x, by);
        by += 24;
        foreach (var kv2 in Gpus())
        {
            long ded = Metrics.GpuDedicatedBytes(kv2.Key);
            KeyValue(g, x, by, half - 16, Trunc(Metrics.GpuName(kv2.Key), 40), ded > 0 ? SizeStr(ded) + " VRAM" : "");
            by += 26;
        }
        by += 16;
        T(g, Loc.Pick("Schermen", "Displays"), _fs, Dim, x, by);
        by += 24;
        foreach (var sc in Screen.AllScreens)
        {
            KeyValue(g, x, by, half - 16, sc.DeviceName.TrimStart('\\', '.') + (sc.Primary ? " *" : ""), $"{sc.Bounds.Width} × {sc.Bounds.Height}");
            by += 26;
        }

        // Hoofdbord, ventilatoren, geheugen, batterij en overige sensoren (LibreHardwareMonitor)
        float sy2 = R.Y + 470;
        T(g, Loc.Pick("Hoofdbord, ventilatoren en overige sensoren", "Motherboard, fans and other sensors"), _fs, Dim, sx, sy2);
        var others = Rows(_c.Metrics.Sensors.Where(x => x.HwType != HardwareType.Cpu && !IsGpuHw(x.HwType) && x.HwType != HardwareType.Storage));
        if (others.Count == 0) T(g, SensorHint() ?? Loc.Pick("Geen extra sensoren gevonden.", "No additional sensors found."), _fs, Dim, sx, sy2 + 24);
        else
        {
            int perCol = Math.Max(3, (int)((R.Bottom - 16 - (sy2 + 24)) / 24));
            float cw2 = (half - 40) / 2;
            for (int ci = 0; ci < 2; ci++)
            {
                var slice = others.Skip(ci * perCol).Take(perCol).ToList();
                if (slice.Count == 0) break;
                SensorList(g, sx + ci * (cw2 + 24), sy2 + 24, cw2, perCol, slice);
            }
        }
    }

    private void DetailProcs(Graphics g, RectangleF R)
    {
        Card(g, null, R, Loc.Pick("Zwaarste programma's — details", "Top programs — details"));
        float half = (R.Width - 64) / 2;
        T(g, Loc.Pick("Meeste CPU", "Most CPU"), _fh, TextCol, R.X + 16, R.Y + 50);
        ProcList(g, R.X + 16, R.Y + 90, half, 12, true);
        T(g, Loc.Pick("Meeste geheugen", "Most memory"), _fh, TextCol, R.X + 48 + half, R.Y + 50);
        ProcList(g, R.X + 48 + half, R.Y + 90, half, 12, false);
        T(g, Loc.Pick("Gegroepeerd per programma (alle processen van dezelfde naam opgeteld). Ververst elke 2 seconden.",
                      "Grouped per program (all processes with the same name added together). Refreshes every 2 seconds."),
          _fs, Dim, R.X + 16, R.Bottom - 34);
    }
}
