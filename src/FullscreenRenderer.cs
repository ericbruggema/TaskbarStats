using System.Drawing.Drawing2D;
using Microsoft.Win32;

namespace TaskbarStats;

/// <summary>Tekent het fullscreen-scherm (kop, overzicht en detailpagina's) op een canvas van 1920x1080 dat meeschaalt. Bevat geen venster- of invoerlogica: alles komt via <see cref="Paint"/>. Gedeelde tekenhulpen staan hier; de pagina's in de andere <c>FullscreenRenderer.*.cs</c>-bestanden.</summary>
public sealed partial class FullscreenRenderer : IDisposable
{
    internal const float CW = 1920, CH = 1080, M = 16;
    internal static readonly Color Bg = Color.FromArgb(11, 13, 18);
    private static readonly Color Dim = Color.FromArgb(150, 160, 175);
    private static readonly Color Green = Color.FromArgb(52, 199, 89), Orange = Color.FromArgb(255, 149, 0);

    private readonly DashContext _c;
    private AppSettings Cfg => _c.Cfg;
    private readonly ProcessSampler _procs;
    private readonly List<(string key, RectangleF r)> _hits = new();
    private readonly Font _f, _fs, _fb, _fh, _fbig, _fhuge;
    private static string? _cpuName;

    // Invoer van het huidige beeld (alleen tijdens Paint gezet)
    private FullscreenView _v = new();
    private MetricsSnapshot _snap = MetricsSnapshot.Empty;
    private float _s = 1, _ox, _oy;

    /// <summary>Klok en tijdtelling; overschrijfbaar zodat tests een vaste tijd kunnen tekenen.</summary>
    public Func<DateTime> Now { get; set; } = () => DateTime.Now;
    public Func<long> Ticks { get; set; } = () => Environment.TickCount64;

    /// <summary>Bron van de specificatiepagina; standaard <see cref="HardwareInfo"/>. Tests kunnen eigen blokken opgeven.</summary>
    public Func<SpecData> Specs { get; set; } = () => new SpecData(HardwareInfo.Blocks, HardwareInfo.Loading, HardwareInfo.LastError);
    /// <summary>Vraagt de specificaties (opnieuw) op; standaard <see cref="HardwareInfo.Refresh"/>.</summary>
    public Action RefreshSpecs { get; set; }

    public FullscreenRenderer(DashContext c, ProcessSampler procs)
    {
        _c = c;
        _procs = procs;
        RefreshSpecs = () => HardwareInfo.Refresh(_c.Metrics);
        string fam = Cfg.FontFamily;
        _f = new Font(fam, 15f, FontStyle.Regular, GraphicsUnit.Pixel);
        _fs = new Font(fam, 13f, FontStyle.Regular, GraphicsUnit.Pixel);
        _fb = new Font(fam, 15f, FontStyle.Bold, GraphicsUnit.Pixel);
        _fh = new Font(fam, 19f, FontStyle.Bold, GraphicsUnit.Pixel);
        _fbig = new Font(fam, 34f, FontStyle.Bold, GraphicsUnit.Pixel);
        _fhuge = new Font(fam, 54f, FontStyle.Bold, GraphicsUnit.Pixel);
    }

    public void Dispose()
    {
        foreach (var f in new[] { _f, _fs, _fb, _fh, _fbig, _fhuge }) f.Dispose();
    }

    /// <summary>Schaal van het canvas (1920x1080) naar het venster, bij het laatste beeld.</summary>
    public float Scale => _s;

    /// <summary>De klikbare vlakken van het laatste beeld (canvascoördinaten; later getekend = boven).</summary>
    public IReadOnlyList<(string key, RectangleF r)> Hits => _hits;

    /// <summary>Sleutel van het klikbare vlak op een vensterpunt (uit het laatste beeld), of null.</summary>
    public string? HitAt(Point p)
    {
        float x = (p.X - _ox) / _s, y = (p.Y - _oy) / _s;
        for (int i = _hits.Count - 1; i >= 0; i--)
            if (_hits[i].r.Contains(x, y)) return _hits[i].key;
        return null;
    }

    /// <summary>
    /// Tekent één beeld op <paramref name="g"/> (venstergrootte <paramref name="client"/>): achtergrond, kop en overzicht of detailpagina.
    /// De momentopname en de weergavetoestand komen van buiten; <paramref name="background"/> tekent (na het wissen) eventueel een achtergrondafbeelding.
    /// </summary>
    public void Paint(Graphics g, Size client, FullscreenView view, MetricsSnapshot snap, Action<Graphics>? background = null)
    {
        _v = view;
        _snap = snap;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(Bg);
        background?.Invoke(g);
        _s = Math.Min(client.Width / CW, client.Height / CH);
        _ox = (client.Width - CW * _s) / 2;
        _oy = (client.Height - CH * _s) / 2;
        g.TranslateTransform(_ox, _oy);
        g.ScaleTransform(_s, _s);
        _hits.Clear();
        DrawHeader(g);
        if (_v.Detail is null) DrawOverview(g); else DrawDetail(g);
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
        var b = GdiCache.Brush(c);
        g.DrawString(s, f, b, x, y);
    }

    private void TR(Graphics g, string s, Font f, Color c, float right, float y)
    {
        var b = GdiCache.Brush(c);
        using var sf = new StringFormat { Alignment = StringAlignment.Far };
        g.DrawString(s, f, b, new RectangleF(right - 600, y, 600, 40), sf);
    }

    private void TC(Graphics g, string s, Font f, Color c, float cx, float cy)
    {
        var b = GdiCache.Brush(c);
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(s, f, b, new RectangleF(cx - 200, cy - 40, 400, 80), sf);
    }

    private static string Trunc(string s, int n) => s.Length > n ? s[..(n - 1)] + "…" : s;
    private static string TruncMid(string s, int n) => s.Length <= n ? s : s[..(n / 2 - 1)] + "…" + s[^(n - n / 2)..];

    private void Card(Graphics g, string? key, RectangleF r, string title, string? sub = null)
    {
        if (key is not null) _hits.Add((key, r));
        bool hot = key is not null && key == _v.Hover && _v.Detail is null;
        using (var path = Rounded(r, 12))
        {
            g.FillPath(GdiCache.Brush(Color.FromArgb(hot ? 34 : 20, 255, 255, 255)), path);
            g.DrawPath(GdiCache.Pen(hot ? Color.FromArgb(170, Accent) : Color.FromArgb(30, 255, 255, 255), hot ? 1.8f : 1f), path);
        }
        T(g, title, _fh, TextCol, r.X + 16, r.Y + 10);
        if (sub is not null) TR(g, sub, _fs, Dim, r.Right - 16, r.Y + 15);
        if (hot) TR(g, Loc.T("click for details"), _fs, Color.FromArgb(200, Accent), r.Right - 16, r.Bottom - 24);
    }

    private void Gauge(Graphics g, float cx, float cy, float rad, double pct, Color col, string label)
    {
        float pw = rad * 0.16f;
        var rect = new RectangleF(cx - rad, cy - rad, rad * 2, rad * 2);
        g.DrawArc(GdiCache.Pen(Color.FromArgb(70, 78, 90), pw), rect, 0, 360);
        if (pct > 0.5)
            g.DrawArc(GdiCache.PenRoundCap(col, pw), rect, -90, (float)(360.0 * Math.Clamp(pct, 0, 100) / 100.0));
        TC(g, label, rad >= 50 ? _fbig : _fh, TextCol, cx, cy);
    }

    private void Bar(Graphics g, float x, float y, float w, double pct, Color col, float h = 8)
    {
        using (var bgp = Rounded(new RectangleF(x, y, w, h), h / 2))
            g.FillPath(GdiCache.Brush(Color.FromArgb(70, 78, 90)), bgp);
        float fw = (float)(w * Math.Clamp(pct, 0, 100) / 100.0);
        if (fw >= h)
            using (var fp = Rounded(new RectangleF(x, y, fw, h), h / 2))
                g.FillPath(GdiCache.Brush(col), fp);
        else if (fw > 0.5f)
            g.FillRectangle(GdiCache.Brush(col), x, y, fw, h);
    }

    private string WinLabel() => _v.Win switch { 60 => Loc.T("1 min"), 300 => Loc.T("5 min"), _ => Loc.T("1 hour") };

    private static string Pct(double v) => $"{v:0}%";
    private static string Rate(double v) => Metrics.FormatRate(v);
    private static string SizeStr(double v) => Metrics.FormatSize(v);

    /// <summary>Grafiek over het gekozen tijdvenster; max &lt;= 0 = automatisch schalen.</summary>
    private void Graph(Graphics g, RectangleF r, IReadOnlyList<(Ring ring, Color col)> series, double max, Func<double, string> fmt, bool axes = true)
    {
        int win = _v.Win;
        if (max <= 0)
        {
            double mm = 0;
            foreach (var (ring, _) in series) mm = Math.Max(mm, ring.Max(win));
            max = Math.Max(1024, mm * 1.15);
        }
        float left = axes ? 64 : 0;
        var p = new RectangleF(r.X + left, r.Y + (axes ? 8 : 0), r.Width - left - (axes ? 8 : 0), r.Height - (axes ? 30 : 0));
        int lines = axes ? 4 : 2;
        var grid = GdiCache.Pen(Color.FromArgb(34, 255, 255, 255), 1f);
        for (int k = 0; k <= lines; k++)
        {
            float y = p.Bottom - p.Height * k / lines;
            g.DrawLine(grid, p.X, y, p.Right, y);
            if (axes) TR(g, fmt(max * k / lines), _fs, Dim, p.X - 8, y - 8);
        }
        if (axes)
        {
            T(g, "-" + WinLabel(), _fs, Dim, p.X, p.Bottom + 6);
            TR(g, Loc.T("now"), _fs, Dim, p.Right, p.Bottom + 6);
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
            g.FillPolygon(GdiCache.Brush(Color.FromArgb(40, col)), fill);
            g.DrawLines(GdiCache.PenRoundJoin(col, 1.8f), pts);
        }
    }

    private (double cur, double min, double avg, double max) Stat(Ring r)
    {
        int n = Math.Min(r.Count, _v.Win);
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
            (Loc.T("Now"), s.cur), (Loc.T("Min"), s.min), (Loc.T("Average"), s.avg), (Loc.T("Max"), s.max),
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
            g.FillRectangle(GdiCache.Brush(Color.FromArgb(28, Accent)), x, yy + 1, (float)(w * Math.Clamp(val / Math.Max(1e-9, top), 0, 1)), 21);
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
        var m = _snap;
        bool Active(string n)
        {
            m.NetPerAdapter.TryGetValue(n, out var r0);
            if (r0.down >= 1024 || r0.up >= 1024) return true;
            if (_c.History.NetPer.TryGetValue(n, out var pr) && (pr.down.Max(_v.Win) >= 1024 || pr.up.Max(_v.Win) >= 1024)) return true;
            return _c.Usage.Today(n).Total > 0;
        }
        return m.NetPerAdapter.Keys.Where(Active)
            .OrderByDescending(n => { m.NetPerAdapter.TryGetValue(n, out var r0); return r0.down + r0.up + _c.Usage.Today(n).Total; })
            .Take(max).ToList();
    }

    private List<KeyValuePair<string, double>> Gpus()
        => _snap.GpuPerLuid.Where(k => k.Key != "" && Metrics.IsRealGpu(k.Key)).OrderBy(k => k.Key).ToList();
}
