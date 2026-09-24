using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace TaskbarStats;

/// <summary>Ringbuffer met de laatste N metingen (nieuwste rechts) voor de grafiekjes.</summary>
public sealed class Ring
{
    private readonly double[] _d;
    private int _n, _head;
    public Ring(int capacity = 3600) => _d = new double[capacity];
    public int Capacity => _d.Length;
    public int Count => _n;
    public void Add(double v) { _d[_head] = v; _head = (_head + 1) % _d.Length; if (_n < _d.Length) _n++; }
    /// <summary>i = 0 is de oudste meting.</summary>
    public double this[int i] => _d[((_head - _n + i) % _d.Length + _d.Length) % _d.Length];
    public double Max() => Max(_n);
    /// <summary>Hoogste waarde in de laatste <paramref name="last"/> metingen.</summary>
    public double Max(int last) { double m = 0; for (int i = Math.Max(0, _n - last); i < _n; i++) m = Math.Max(m, this[i]); return m; }
}

/// <summary>Geschiedenis (1 meting per seconde, ~5 minuten) van de belangrijkste metingen.</summary>
public sealed class MetricHistory
{
    public readonly Ring Cpu = new(), Gpu = new(), Mem = new(), NetDown = new(), NetUp = new(), DiskRead = new(), DiskWrite = new();
    public readonly Dictionary<string, Ring> GpuPer = new(), VramPer = new();
    public readonly List<Ring> Cores = new();
    /// <summary>Per netwerkadapter de geschiedenis van download en upload (bytes/s).</summary>
    public readonly Dictionary<string, (Ring down, Ring up)> NetPer = new();
    private long _at;

    public void Sample(Metrics m)
    {
        long now = Environment.TickCount64;
        if (now - _at < 1000 || Cadence.Active) return;
        _at = now;
        Cpu.Add(m.CpuPercent); Gpu.Add(m.GpuPercent); Mem.Add(m.MemPercent);
        NetDown.Add(m.NetDownBytesPerSec); NetUp.Add(m.NetUpBytesPerSec);
        DiskRead.Add(m.DiskReadBytesPerSec); DiskWrite.Add(m.DiskWriteBytesPerSec);
        foreach (var (luid, v) in m.GpuPerLuid)
        {
            if (luid == "") continue;
            if (!GpuPer.TryGetValue(luid, out var r)) GpuPer[luid] = r = new Ring();
            r.Add(v);
        }
        foreach (var (luid, v) in m.VramUsedPerLuid)
        {
            if (!VramPer.TryGetValue(luid, out var r)) VramPer[luid] = r = new Ring();
            r.Add(v);
        }
        foreach (var (name, r) in m.NetPerAdapter)
        {
            if (!NetPer.TryGetValue(name, out var pr)) NetPer[name] = pr = (new Ring(), new Ring());
            pr.down.Add(r.down); pr.up.Add(r.up);
        }
        while (Cores.Count < m.CpuCores.Length) Cores.Add(new Ring());
        for (int i = 0; i < m.CpuCores.Length; i++) Cores[i].Add(m.CpuCores[i]);
    }
}

/// <summary>Alles wat het dashboard van het widget nodig heeft.</summary>
public sealed class DashContext
{
    public required Metrics Metrics { get; init; }
    public required AppSettings Cfg { get; init; }
    public required UsageTracker Usage { get; init; }
    public required MetricHistory History { get; init; }
    public required Func<List<DriveSpace>> Drives { get; init; }
    public required Action<Point> ShowMenu { get; init; }
    public Action<int>? Nudge { get; init; }
}

/// <summary>
/// Groot bureaublad-dashboard: losse tegels (CPU, GPU, geheugen, netwerk, schijven, batterij, programma's, systeem)
/// met grafiekjes. Halfdoorzichtig, schaalbaar, op de voor- of achtergrond en eventueel klik-door.
/// Tekent zichzelf naar een ARGB-bitmap (UpdateLayeredWindow), net als het taakbalk-widget.
/// </summary>
public sealed class DashboardForm : Form
{
    private const int CW = 300, Spacing = 12, Pad = 16;
    private static readonly Color Dim = Color.FromArgb(155, 164, 178);

    private readonly DashContext _c;
    private AppSettings Cfg => _c.Cfg;
    private readonly ProcessSampler _procs = new() { TopCount = 5 };
    private readonly System.Windows.Forms.Timer _zTimer = new() { Interval = 2000 };
    private long _lastRender, _lastProc;
    private bool _suppressed, _dragging, _closing, _wantHidden;
    private Point _dragStart;

    // fonts, per render aangemaakt
    private Font _f = null!, _fs = null!, _fb = null!, _fbig = null!;

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00080000 /* LAYERED */ | 0x00000080 /* TOOLWINDOW */ | 0x08000000 /* NOACTIVATE */;
            if (_c?.Cfg.DashClickThrough == true) cp.ExStyle |= 0x00000020;   // TRANSPARENT = klik-door
            return cp;
        }
    }

    public DashboardForm(DashContext c)
    {
        _c = c;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(400, 300);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);

        // Op de voorgrond hoeft niets steeds opnieuw naar boven (topmost blijft topmost); alleen de achtergrond-stand herhalen.
        _zTimer.Tick += (_, _) => { EnsureVisible(); if (!Cfg.DashFront) ApplyZ(); };
        _zTimer.Start();
        MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right) { _dragging = false; _c.ShowMenu(Cursor.Position); }
            else if (e.Button == MouseButtons.Left) { _dragging = !Cfg.DashLocked; _dragStart = e.Location; }
        };
        MouseMove += (_, e) =>
        {
            if (!_dragging) return;
            Location = new Point(Location.X + e.X - _dragStart.X, Location.Y + e.Y - _dragStart.Y);
            Cfg.DashX = Location.X; Cfg.DashY = Location.Y;
        };
        MouseUp += (_, _) => { if (_dragging) { _dragging = false; Cfg.Save(); } };
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Render();   // bepaalt de grootte
        if (Cfg.DashX is null || Cfg.DashY is null)
        {
            var wa = Screen.PrimaryScreen!.WorkingArea;
            Cfg.DashX = Math.Max(wa.Left, wa.Right - Width - 40);
            Cfg.DashY = wa.Top + 40;
        }
        Location = new Point(Cfg.DashX!.Value, Cfg.DashY!.Value);
        if (!Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(Bounds)))
            Location = new Point(Screen.PrimaryScreen!.WorkingArea.Left + 40, Screen.PrimaryScreen.WorkingArea.Top + 40);
        ApplySettings();
    }

    public void ResetPosition()
    {
        Cfg.DashX = null; Cfg.DashY = null;
        var wa = Screen.PrimaryScreen!.WorkingArea;
        Cfg.DashX = Math.Max(wa.Left, wa.Right - Width - 40);
        Cfg.DashY = wa.Top + 40;
        Location = new Point(Cfg.DashX.Value, Cfg.DashY.Value);
        Cfg.Save();
    }

    /// <summary>Klik-door, voor/achtergrond, doorzichtigheid, schaal e.d. opnieuw toepassen.</summary>
    public void ApplySettings()
    {
        if (!IsHandleCreated) return;
        int ex = GetWindowLong(Handle, -20);
        ex = Cfg.DashClickThrough ? ex | 0x20 : ex & ~0x20;
        SetWindowLong(Handle, -20, ex);
        ApplyZ();
        Render();
    }

    // "Bureaublad weergeven" (Win+D / knop rechts op de taakbalk) minimaliseert of verbergt vensters. Het dashboard
    // hoort juist op het bureaublad te blijven: we herstellen het meteen (en controleren elke seconde).
    private void EnsureVisible()
    {
        if (!IsHandleCreated || _suppressed || _closing || _wantHidden) return;
        if (IsIconic(Handle) || !IsWindowVisible(Handle))
        {
            ShowWindow(Handle, 4);   // SW_SHOWNOACTIVATE
            ApplyZ();
            Render();
        }
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (_suppressed || _closing || _wantHidden || !IsHandleCreated) return;
        bool minimized = m.Msg == 0x0005 /* WM_SIZE */ && m.WParam.ToInt32() == 1 /* SIZE_MINIMIZED */;
        bool hidden = m.Msg == 0x0018 /* WM_SHOWWINDOW */ && m.WParam == IntPtr.Zero && m.LParam == IntPtr.Zero;
        if (minimized || hidden) BeginInvoke(new Action(EnsureVisible));
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _closing = true;
        base.OnFormClosing(e);
    }

    /// <summary>Tonen (na "Dashboard tonen" aan).</summary>
    public void Present()
    {
        _wantHidden = false;
        if (!Visible) Show(); else ApplySettings();
    }

    /// <summary>Bewust verbergen (na "Dashboard tonen" uit): niet automatisch herstellen.</summary>
    public void HideByUser()
    {
        _wantHidden = true;
        Hide();
    }

    public void SetSuppressed(bool suppressed)
    {
        if (suppressed == _suppressed) return;
        _suppressed = suppressed;
        if (!IsHandleCreated) return;
        ShowWindow(Handle, suppressed ? 0 : 4);
        if (!suppressed) { ApplyZ(); Render(); }
    }

    private void ApplyZ()
    {
        if (!IsHandleCreated || _suppressed) return;
        const uint flags = 0x0001 | 0x0002 | 0x0010;   // NOSIZE | NOMOVE | NOACTIVATE
        if (Cfg.DashFront) SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, flags);            // TOPMOST
        else
        {
            SetWindowPos(Handle, new IntPtr(-2), 0, 0, 0, 0, flags);                           // NOTOPMOST
            SetWindowPos(Handle, new IntPtr(1), 0, 0, 0, 0, flags);                            // BOTTOM
        }
    }

    /// <summary>Aan te roepen bij elke tik van het widget; tekent maximaal 1x per seconde.</summary>
    public void Tick()
    {
        if (!Visible || _suppressed) return;
        long now = Environment.TickCount64;
        if (Cfg.DashProcs && now - _lastProc >= 2000) { _lastProc = now; _procs.SampleAsync(); }
        if (now - _lastRender < 1000) return;
        Render();
    }

    // ---------- Tekenen ----------
    private sealed record Tile(int Height, Action<Graphics, RectangleF> Draw);

    private Color Col(string hex, Color fb) { try { return ColorTranslator.FromHtml(hex); } catch { return fb; } }
    private Color Accent => Col(Cfg.AccentColor, Color.DodgerBlue);
    private Color TextCol => Col(Cfg.TextColor, Color.White);

    private Color Thr(double v)
    {
        if (v >= Cfg.CritThreshold) return Col(Cfg.CritColor, Color.Red);
        if (v >= Cfg.WarnThreshold) return Col(Cfg.WarnColor, Color.Orange);
        return Accent;
    }

    public void Render()
    {
        if (!IsHandleCreated || _suppressed) return;
        _lastRender = Environment.TickCount64;

        var m = _c.Metrics;
        var gpus = m.GpuPerLuid.Where(k => k.Key != "" && Metrics.IsRealGpu(k.Key)).OrderBy(k => k.Key).ToList();
        var drives = _c.Drives();
        var adapters = m.NetPerAdapter
            .Where(k => k.Value.down >= 1024 || k.Value.up >= 1024 || _c.Usage.Today(k.Key).Total > 0)
            .OrderByDescending(k => k.Value.down + k.Value.up).Take(4).ToList();
        bool limit = Cfg.MonthlyLimitGb > 0;

        var tiles = new List<Tile>();
        foreach (var id in Tiles.Order(Cfg.DashOrder))
        {
            if (!Tiles.DashOn(Cfg, id)) continue;
            switch (id)
            {
                case "cpu": tiles.Add(new(190, DrawCpu)); break;
                case "gpu": if (gpus.Count > 0) tiles.Add(new(34 + gpus.Count * 54 + 62, (g, r) => DrawGpu(g, r, gpus))); break;
                case "mem": tiles.Add(new(160, DrawMem)); break;
                case "net": tiles.Add(new(34 + 66 + 3 * 17 + (limit ? 26 : 0) + adapters.Count * 32 + 16, (g, r) => DrawNet(g, r, adapters, limit))); break;
                case "disk": tiles.Add(new(34 + drives.Count * 34 + 22 + 56, (g, r) => DrawDisks(g, r, drives))); break;
                case "batt": if (m.BatteryPresent) tiles.Add(new(112, DrawBattery)); break;
                case "proc": tiles.Add(new(34 + 5 * 18 + 22, DrawProcs)); break;
                case "sys": tiles.Add(new(104, DrawSystem)); break;
            }
        }

        // masonry: elke tegel in de kortste kolom
        int cols = Math.Clamp(Cfg.DashColumns, 1, 4);
        var colY = Enumerable.Repeat((float)Pad, cols).ToArray();
        var placed = new List<(Tile t, RectangleF r)>();
        foreach (var t in tiles)
        {
            int ci = Array.IndexOf(colY, colY.Min());
            var r = new RectangleF(Pad + ci * (CW + Spacing), colY[ci], CW, t.Height);
            placed.Add((t, r));
            colY[ci] += t.Height + Spacing;
        }
        float logicalW = Pad * 2 + cols * CW + (cols - 1) * Spacing;
        float logicalH = tiles.Count == 0 ? 80 : colY.Max() - Spacing + Pad;

        float s = (float)(Math.Clamp(Cfg.DashScale, 30, 400) / 100.0 * DeviceDpi / 96.0);
        int w = (int)Math.Ceiling(logicalW * s), h = (int)Math.Ceiling(logicalH * s);
        if (Width != w || Height != h) { Width = w; Height = h; }

        string family = Cfg.FontFamily;
        _f = new Font(family, 9f); _fs = new Font(family, 8f); _fb = new Font(family, 10.5f, FontStyle.Bold); _fbig = new Font(family, 17f, FontStyle.Bold);
        try
        {
            using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            bmp.SetResolution(96, 96);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                g.Clear(Color.Transparent);
                g.ScaleTransform(s, s);

                using (var path = Rounded(new RectangleF(0, 0, logicalW, logicalH), 14))
                using (var bg = new SolidBrush(Color.FromArgb(255, Col(Cfg.BackgroundColor, Color.FromArgb(20, 20, 20)))))
                using (var edge = new Pen(Color.FromArgb(40, 255, 255, 255), 1f))
                {
                    g.FillPath(bg, path);
                    g.DrawPath(edge, path);
                }
                if (tiles.Count == 0)
                    DrawText(g, Loc.Pick("Geen tegels aan — zet ze aan via Instellingen (tab Dashboard)",
                                         "No tiles enabled — turn them on in Settings (Dashboard tab)"), _f, Dim, 16, 30);
                foreach (var (t, r) in placed) t.Draw(g, r);
            }
            Push(bmp);
        }
        finally { _f.Dispose(); _fs.Dispose(); _fb.Dispose(); _fbig.Dispose(); }
    }

    private void Push(Bitmap bmp)
    {
        IntPtr screenDc = GetDC(IntPtr.Zero), memDc = CreateCompatibleDC(screenDc);
        IntPtr hBmp = bmp.GetHbitmap(Color.FromArgb(0)), old = SelectObject(memDc, hBmp);
        try
        {
            var size = new Size(bmp.Width, bmp.Height);
            var src = new Point(0, 0);
            var dst = new Point(Left, Top);
            byte alpha = (byte)Math.Clamp(Cfg.DashOpacity * 255 / 100, 20, 255);
            var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = alpha, AlphaFormat = 1 };
            UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, 2);
        }
        finally
        {
            SelectObject(memDc, old); DeleteObject(hBmp); DeleteDC(memDc); ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    // ---------- Tekenhulpjes ----------
    private static GraphicsPath Rounded(RectangleF r, float radius) => WidgetForm.RoundedRect(r, radius);

    // Kort een tekst in met "…" zodat hij binnen de beschikbare breedte past (brede lettertypen overlapten de waarden ernaast).
    private static string Fit(Graphics g, string s, Font f, float maxWidth)
    {
        if (g.MeasureString(s, f).Width <= maxWidth) return s;
        while (s.Length > 1 && g.MeasureString(s + "…", f).Width > maxWidth) s = s[..^1];
        return s + "…";
    }

    private void DrawText(Graphics g, string s, Font f, Color c, float x, float y)
    {
        using var b = new SolidBrush(c);
        g.DrawString(s, f, b, x, y);
    }

    private void DrawTextRight(Graphics g, string s, Font f, Color c, float right, float y)
    {
        using var b = new SolidBrush(c);
        using var sf = new StringFormat { Alignment = StringAlignment.Far };
        g.DrawString(s, f, b, new RectangleF(right - 240, y, 240, 24), sf);
    }

    private void Card(Graphics g, RectangleF r, string title)
    {
        using (var path = Rounded(r, 10))
        using (var bg = new SolidBrush(Color.FromArgb(18, 255, 255, 255)))
            g.FillPath(bg, path);
        DrawText(g, title, _fs, Dim, r.X + 14, r.Y + 8);
    }

    private void Gauge(Graphics g, float cx, float cy, float rad, double pct, Color col, string label)
    {
        var rect = new RectangleF(cx - rad, cy - rad, rad * 2, rad * 2);
        using (var bg = new Pen(Color.FromArgb(70, 78, 90), 8f))
            g.DrawArc(bg, rect, 0, 360);
        if (pct > 0.5)
            using (var fg = new Pen(col, 8f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawArc(fg, rect, -90, (float)(360.0 * Math.Clamp(pct, 0, 100) / 100.0));
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using var b = new SolidBrush(TextCol);
        g.DrawString(label, _fb, b, new RectangleF(cx - rad, cy - rad, rad * 2, rad * 2), sf);
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

    /// <summary>Lijngrafiekje; nieuwste meting rechts. max &lt;= 0 = automatisch schalen.</summary>
    private void Spark(Graphics g, RectangleF r, Ring ring, Color col, double max)
    {
        using (var basePen = new Pen(Color.FromArgb(40, 255, 255, 255), 1f))
            g.DrawLine(basePen, r.X, r.Bottom, r.Right, r.Bottom);
        const int win = 300;
        int n = Math.Min(ring.Count, win), start = ring.Count - n;
        if (n < 2) return;
        if (max <= 0) max = Math.Max(1024, ring.Max(win) * 1.15);
        var pts = new PointF[n];
        for (int i = 0; i < n; i++)
        {
            float x = r.Right - (n - 1 - i) * r.Width / (win - 1);
            float y = r.Bottom - (float)(Math.Clamp(ring[start + i] / max, 0, 1) * r.Height);
            pts[i] = new PointF(x, y);
        }
        var fill = pts.Concat(new[] { new PointF(pts[^1].X, r.Bottom), new PointF(pts[0].X, r.Bottom) }).ToArray();
        using (var fb = new SolidBrush(Color.FromArgb(46, col))) g.FillPolygon(fb, fill);
        using (var pen = new Pen(col, 1.6f) { LineJoin = LineJoin.Round }) g.DrawLines(pen, pts);
    }

    // ---------- Tegels ----------
    private void DrawCpu(Graphics g, RectangleF r)
    {
        var m = _c.Metrics;
        string extra = (m.CpuMHz is double f ? $"  ·  {f / 1000:0.00} GHz" : "") + (m.CpuTempC is double t ? $"  ·  {t:0}°C" : "");
        Card(g, r, "CPU" + extra);
        Gauge(g, r.X + 62, r.Y + 92, 34, m.CpuPercent, Thr(m.CpuPercent), $"{m.CpuPercent:0}%");

        var cores = m.CpuCores;
        if (cores.Length > 0)
        {
            float x0 = r.X + 118, w = r.Right - 14 - x0, top = r.Y + 52, hh = 62;
            float bw = Math.Min(14, (w - (cores.Length - 1) * 2f) / cores.Length);
            for (int i = 0; i < cores.Length; i++)
            {
                float x = x0 + i * (bw + 2);
                using (var bg = new SolidBrush(Color.FromArgb(70, 78, 90))) g.FillRectangle(bg, x, top, bw, hh);
                float fh = (float)(hh * Math.Clamp(cores[i], 0, 100) / 100.0);
                using (var fb = new SolidBrush(Thr(cores[i]))) g.FillRectangle(fb, x, top + hh - fh, bw, fh);
            }
        }
        Spark(g, new RectangleF(r.X + 14, r.Y + 130, r.Width - 28, 46), _c.History.Cpu, Accent, 100);
    }

    private void DrawGpu(Graphics g, RectangleF r, List<KeyValuePair<string, double>> gpus)
    {
        Card(g, r, "GPU");
        for (int i = 0; i < gpus.Count; i++)
        {
            var (luid, v) = (gpus[i].Key, gpus[i].Value);
            float y0 = r.Y + 30 + i * 54;
            DrawText(g, Fit(g, Metrics.GpuName(luid), _f, r.Width - 28 - 60), _f, TextCol, r.X + 14, y0);
            DrawTextRight(g, $"{v:0}%", _fb, Thr(v), r.Right - 14, y0 - 1);
            Bar(g, r.X + 14, y0 + 20, r.Width - 28, v, Thr(v), 7);
            long ded = Metrics.GpuDedicatedBytes(luid);
            if (ded > 0 && _c.Metrics.VramUsedPerLuid.TryGetValue(luid, out var used))
            {
                DrawText(g, $"VRAM {Metrics.FormatSize(used)} / {Metrics.FormatSize(ded)}", _fs, Dim, r.X + 14, y0 + 31);
                Bar(g, r.X + 150, y0 + 35, r.Width - 164, 100.0 * used / ded, Color.FromArgb(52, 199, 89), 5);
            }
        }
        Spark(g, new RectangleF(r.X + 14, r.Bottom - 54, r.Width - 28, 40), _c.History.Gpu, Accent, 100);
    }

    private void DrawMem(Graphics g, RectangleF r)
    {
        var m = _c.Metrics;
        Card(g, r, Loc.S("memory"));
        Gauge(g, r.X + 62, r.Y + 80, 34, m.MemPercent, Thr(m.MemPercent), $"{m.MemPercent:0}%");
        DrawText(g, $"{Metrics.FormatSize(m.MemUsedBytes)} / {Metrics.FormatSize(m.MemTotalBytes)}", _fb, TextCol, r.X + 118, r.Y + 58);
        DrawText(g, $"{Loc.Pick("Vrij", "Free")} {Metrics.FormatSize(m.MemTotalBytes - m.MemUsedBytes)}", _f, Dim, r.X + 118, r.Y + 82);
        Spark(g, new RectangleF(r.X + 14, r.Y + 116, r.Width - 28, 36), _c.History.Mem, Color.FromArgb(52, 199, 89), 100);
    }

    private void DrawNet(Graphics g, RectangleF r, List<KeyValuePair<string, (double down, double up)>> adapters, bool limit)
    {
        var m = _c.Metrics;
        Card(g, r, Loc.Pick("Netwerk", "Network"));
        DrawText(g, $"↓ {Metrics.FormatRate(m.NetDownBytesPerSec)}", _fb, Accent, r.X + 14, r.Y + 30);
        DrawText(g, $"↑ {Metrics.FormatRate(m.NetUpBytesPerSec)}", _f, Color.FromArgb(52, 199, 89), r.X + 14, r.Y + 52);

        var sp = new RectangleF(r.X + 150, r.Y + 28, r.Width - 164, 46);
        double max = Math.Max(1024, Math.Max(_c.History.NetDown.Max(300), _c.History.NetUp.Max(300)) * 1.15);
        Spark(g, sp, _c.History.NetDown, Accent, max);
        Spark(g, sp, _c.History.NetUp, Color.FromArgb(52, 199, 89), max);

        float y = r.Y + 34 + 66;
        foreach (var (label, u) in new[]
        {
            (Loc.Pick("Sessie", "Session"), _c.Usage.Session(null)),
            (Loc.Pick("Vandaag", "Today"), _c.Usage.Today(null)),
            (Loc.Pick("Deze maand", "This month"), _c.Usage.Month(null)),
        })
        {
            DrawText(g, label, _fs, Dim, r.X + 14, y);
            DrawTextRight(g, $"↓ {Metrics.FormatBytes(u.Down)}   ↑ {Metrics.FormatBytes(u.Up)}", _fs, TextCol, r.Right - 14, y);
            y += 17;
        }
        if (limit)
        {
            var mu = _c.Usage.Month(Cfg.NetworkAdapter);
            double pct = 100.0 * mu.Total / (Cfg.MonthlyLimitGb * 1073741824.0);
            DrawText(g, $"{Loc.Pick("Limiet", "Limit")} {Metrics.FormatBytes(mu.Total)} / {Cfg.MonthlyLimitGb} GB", _fs, Dim, r.X + 14, y);
            Bar(g, r.X + 14, y + 16, r.Width - 28, pct, Thr(pct), 5);
            y += 26;
        }
        foreach (var (name, rt) in adapters)
        {
            DrawText(g, Fit(g, name, _fs, r.Width - 28 - g.MeasureString($"↓ {Metrics.FormatRate(rt.down)}  ↑ {Metrics.FormatRate(rt.up)}", _fs).Width - 10), _fs, Dim, r.X + 14, y);
            DrawTextRight(g, $"↓ {Metrics.FormatRate(rt.down)}  ↑ {Metrics.FormatRate(rt.up)}", _fs, TextCol, r.Right - 14, y);
            if (_c.History.NetPer.TryGetValue(name, out var pr))
            {
                var sp2 = new RectangleF(r.X + 14, y + 17, r.Width - 28, 12);
                double mx = Math.Max(1024, Math.Max(pr.down.Max(300), pr.up.Max(300)) * 1.15);
                Spark(g, sp2, pr.down, Accent, mx);
                Spark(g, sp2, pr.up, Color.FromArgb(52, 199, 89), mx);
            }
            y += 32;
        }
    }

    private void DrawDisks(Graphics g, RectangleF r, List<DriveSpace> drives)
    {
        var m = _c.Metrics;
        Card(g, r, Loc.S("disks"));
        float y = r.Y + 30;
        foreach (var d in drives)
        {
            DrawText(g, d.Display, _f, TextCol, r.X + 14, y);
            DrawTextRight(g, $"{Metrics.FormatSize(d.Free)} {Loc.S("freeOf")} {Metrics.FormatSize(d.Total)}", _fs, Dim, r.Right - 14, y + 2);
            Bar(g, r.X + 14, y + 19, r.Width - 28, d.UsedPercent, Thr(d.UsedPercent), 7);
            y += 34;
        }
        DrawText(g, $"R {Metrics.FormatRate(m.DiskReadBytesPerSec)}", _fs, Accent, r.X + 14, y + 2);
        DrawTextRight(g, $"W {Metrics.FormatRate(m.DiskWriteBytesPerSec)}", _fs, Color.FromArgb(255, 149, 0), r.Right - 14, y + 2);
        var sp = new RectangleF(r.X + 14, y + 22, r.Width - 28, 32);
        double max = Math.Max(1024, Math.Max(_c.History.DiskRead.Max(300), _c.History.DiskWrite.Max(300)) * 1.15);
        Spark(g, sp, _c.History.DiskRead, Accent, max);
        Spark(g, sp, _c.History.DiskWrite, Color.FromArgb(255, 149, 0), max);
    }

    private void DrawBattery(Graphics g, RectangleF r)
    {
        var m = _c.Metrics;
        Card(g, r, Loc.Pick("Batterij", "Battery"));
        double pct = m.BatteryPercent;
        bool charging = m.BatteryCharging, ac = m.BatteryOnAc;
        Color fill = charging || ac ? Color.FromArgb(52, 199, 89)
                   : pct <= 10 ? Col(Cfg.CritColor, Color.Red)
                   : pct <= 20 ? Col(Cfg.WarnColor, Color.Orange) : Accent;

        float bx = r.X + 24, by = r.Y + 36, bw = 32, bh = 60;
        using (var nub = new SolidBrush(Color.FromArgb(142, 142, 147))) g.FillRectangle(nub, bx + bw / 2 - 7, by - 4, 14, 4);
        using (var path = Rounded(new RectangleF(bx, by, bw, bh), 5))
        using (var bg = new SolidBrush(Color.FromArgb(42, 42, 45)))
        using (var edge = new Pen(Color.FromArgb(142, 142, 147), 1.6f))
        {
            g.FillPath(bg, path);
            g.DrawPath(edge, path);
        }
        float ih = bh - 5, fh = (float)(ih * pct / 100.0);
        if (fh >= 1)
            using (var fp = Rounded(new RectangleF(bx + 2.5f, by + 2.5f + ih - fh, bw - 5, fh), 3))
            using (var fb = new SolidBrush(fill))
                g.FillPath(fb, fp);
        if (charging) WidgetForm.DrawBolt(g, bx + bw / 2, by + bh / 2, 30);
        else if (ac) WidgetForm.DrawPlug(g, bx + bw / 2, by + bh / 2, 30);

        DrawText(g, $"{pct:0}%", _fbig, TextCol, r.X + 78, r.Y + 36);
        string state = charging ? Loc.Pick("Laden", "Charging") : ac ? Loc.Pick("Op netstroom", "Plugged in") : Loc.Pick("Ontladen", "On battery");
        string left = !ac && m.BatteryRemainingSec > 0
            ? $" · {m.BatteryRemainingSec / 3600}{Loc.Pick("u", "h")} {m.BatteryRemainingSec % 3600 / 60:00}m" : "";
        DrawText(g, state + left, _f, Dim, r.X + 80, r.Y + 68);
    }

    private void DrawProcs(Graphics g, RectangleF r)
    {
        Card(g, r, Loc.Pick("Zwaarste programma's", "Top programs"));
        float half = (r.Width - 28) / 2;
        DrawText(g, "CPU", _fs, Dim, r.X + 14, r.Y + 28);
        DrawText(g, "RAM", _fs, Dim, r.X + 14 + half + 8, r.Y + 28);
        for (int i = 0; i < 5; i++)
        {
            float y = r.Y + 44 + i * 18;
            if (_procs.HasCpu && i < _procs.TopCpu.Count)
            {
                var (n, c) = _procs.TopCpu[i];
                DrawText(g, n.Length > 11 ? n[..11] + "…" : n, _f, TextCol, r.X + 14, y);
                DrawTextRight(g, $"{c:0}%", _f, Dim, r.X + 14 + half - 6, y);
            }
            if (i < _procs.TopMem.Count)
            {
                var (n, mem) = _procs.TopMem[i];
                DrawText(g, n.Length > 11 ? n[..11] + "…" : n, _f, TextCol, r.X + 14 + half + 8, y);
                DrawTextRight(g, Metrics.FormatSize(mem), _f, Dim, r.Right - 14, y);
            }
        }
    }

    private void DrawSystem(Graphics g, RectangleF r)
    {
        var m = _c.Metrics;
        Card(g, r, Loc.Pick("Systeem", "System"));
        var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
        DrawText(g, $"{DateTime.Now:HH:mm}", _fbig, TextCol, r.X + 14, r.Y + 26);
        DrawText(g, $"{DateTime.Now:dddd d MMMM}", _f, Dim, r.X + 100, r.Y + 34);
        DrawText(g, $"Uptime  {(int)up.TotalDays} {Loc.Pick("d", "d")} {up.Hours} {Loc.Pick("u", "h")} {up.Minutes} m", _f, TextCol, r.X + 14, r.Y + 62);
        string temps = (m.CpuTempC is double c ? $"CPU {c:0}°C" : "") + (m.GpuTempC is double t ? $"   GPU {t:0}°C" : "");
        if (temps != "") DrawText(g, temps, _f, Dim, r.X + 14, r.Y + 80);
    }

    // ---------- Interop ----------
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref Point pptDst, ref Size psize,
        IntPtr hdcSrc, ref Point pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);

    protected override void Dispose(bool disposing)
    {
        if (disposing) _zTimer.Dispose();
        base.Dispose(disposing);
    }
}
