using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace TaskbarStats;

/// <summary>
/// Tekent het bureaublad-dashboard (tegels, masonry-indeling, grafiekjes) naar een gewone <see cref="Bitmap"/>.
/// Kent geen venster: alle invoer komt expliciet binnen (instellingen, momentopname, schijven, DPI), zodat het zonder
/// window-handle te testen is. <see cref="DashboardForm"/> zorgt voor het layered window en zet de bitmap op het scherm.
/// </summary>
internal sealed class DashboardRenderer : IDisposable
{
    private const int CW = 300, Spacing = 12, Pad = 16;
    private static readonly Color Dim = Color.FromArgb(155, 164, 178);

    private readonly AppSettings _cfg;
    private readonly MetricHistory _history;
    private readonly UsageTracker _usage;
    private readonly ProcessSampler _procs;
    private readonly Func<BgLayer>? _makeBg;
    private BgLayer? _bg;

    // fonts, per render opgehaald uit GdiCache (niet vrijgeven)
    private Font _f = null!, _fs = null!, _fb = null!, _fbig = null!;

    /// <summary>De meting van het huidige beeld: één complete momentopname, zodat alle tegels dezelfde meting tonen.</summary>
    private MetricsSnapshot _snap = MetricsSnapshot.Empty;

    /// <param name="makeBg">Maakt de laag voor de achtergrondafbeelding (heeft een venster nodig); null = nooit een achtergrondafbeelding.</param>
    public DashboardRenderer(AppSettings cfg, MetricHistory history, UsageTracker usage, ProcessSampler procs, Func<BgLayer>? makeBg = null)
    {
        _cfg = cfg; _history = history; _usage = usage; _procs = procs; _makeBg = makeBg;
    }

    private sealed record Tile(int Height, Action<Graphics, RectangleF> Draw);

    /// <summary>De indeling van één beeld: geplaatste tegels (logische eenheden), logische afmetingen, schaal en pixelmaat.</summary>
    private sealed record Plan(List<(Tile t, RectangleF r)> Placed, int TileCount, float LogicalW, float LogicalH, float Scale, int Width, int Height);

    /// <summary>Pixelgrootte van het dashboard voor deze meting (zonder te tekenen).</summary>
    public Size Measure(MetricsSnapshot m, List<DriveSpace> drives, int dpi)
    {
        var plan = BuildPlan(m, drives, dpi);
        return new Size(plan.Width, plan.Height);
    }

    /// <summary>Tekent het hele dashboard; de aanroeper geeft de bitmap vrij.</summary>
    public Bitmap Render(MetricsSnapshot m, List<DriveSpace> drives, int dpi)
    {
        _snap = m;
        GdiCache.Trim();
        var plan = BuildPlan(m, drives, dpi);
        var (logicalW, logicalH, s, w, h) = (plan.LogicalW, plan.LogicalH, plan.Scale, plan.Width, plan.Height);

        string family = _cfg.FontFamily;
        _f = GdiCache.Font(family, 9f); _fs = GdiCache.Font(family, 8f); _fb = GdiCache.Font(family, 10.5f, FontStyle.Bold); _fbig = GdiCache.Font(family, 17f, FontStyle.Bold);
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        bmp.SetResolution(96, 96);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            g.Clear(Color.Transparent);
            g.ScaleTransform(s, s);

            using (var path = Rounded(new RectangleF(0, 0, logicalW, logicalH), 14))
            {
                g.FillPath(GdiCache.Brush(Color.FromArgb(255, Col(_cfg.BackgroundColor, Color.FromArgb(20, 20, 20)))), path);
                g.DrawPath(GdiCache.Pen(Color.FromArgb(40, 255, 255, 255), 1f), path);
            }
            if (plan.TileCount == 0)
                DrawText(g, Loc.T("No tiles enabled — turn them on in Settings (Dashboard tab)"), _f, Dim, 16, 30);
            // achtergrondafbeelding: in pixels, geclipt op de afgeronde vorm, onder de tegels
            if (!string.IsNullOrWhiteSpace(_cfg.DashBgImage) && _makeBg is not null)
            {
                var st = g.Save();
                using (var clipPath = Rounded(new RectangleF(0, 0, logicalW, logicalH), 14)) g.SetClip(clipPath);
                g.ResetTransform();
                (_bg ??= _makeBg()).Draw(g, _cfg.DashBgImage, _cfg.DashBgMode, _cfg.DashBgOpacity, w, h);
                g.Restore(st);
            }
            else if (_bg is not null) { _bg.Dispose(); _bg = null; }
            foreach (var (t, r) in plan.Placed) t.Draw(g, r);
        }
        return bmp;
    }

    /// <summary>Bepaalt welke tegels er zijn, plaatst ze (masonry: elke tegel in de kortste kolom) en berekent de maat.</summary>
    private Plan BuildPlan(MetricsSnapshot m, List<DriveSpace> drives, int dpi)
    {
        var gpus = m.GpuPerLuid.Where(k => k.Key != "" && Metrics.IsRealGpu(k.Key)).OrderBy(k => k.Key).ToList();
        var adapters = m.NetPerAdapter
            .Where(k => k.Value.down >= 1024 || k.Value.up >= 1024 || _usage.Today(k.Key).Total > 0)
            .OrderByDescending(k => k.Value.down + k.Value.up).Take(4).ToList();
        bool limit = _cfg.MonthlyLimitGb > 0;

        var tiles = new List<Tile>();
        foreach (var id in Tiles.Order(_cfg.DashOrder))
        {
            if (!Tiles.DashOn(_cfg, id)) continue;
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

        int cols = Math.Clamp(_cfg.DashColumns, 1, 4);
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

        float s = (float)(Math.Clamp(_cfg.DashScale, 30, 400) / 100.0 * dpi / 96.0);
        int w = (int)Math.Ceiling(logicalW * s), h = (int)Math.Ceiling(logicalH * s);
        return new Plan(placed, tiles.Count, logicalW, logicalH, s, w, h);
    }

    // ---------- Kleuren ----------

    private Color Col(string hex, Color fb) { try { return ColorTranslator.FromHtml(hex); } catch { return fb; } }
    private Color Accent => Col(_cfg.AccentColor, Color.DodgerBlue);
    private Color TextCol => Col(_cfg.TextColor, Color.White);

    private Color Thr(double v)
    {
        if (v >= _cfg.CritThreshold) return Col(_cfg.CritColor, Color.Red);
        if (v >= _cfg.WarnThreshold) return Col(_cfg.WarnColor, Color.Orange);
        return Accent;
    }

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
        var b = GdiCache.Brush(c);
        g.DrawString(s, f, b, x, y);
    }

    private void DrawTextRight(Graphics g, string s, Font f, Color c, float right, float y)
    {
        var b = GdiCache.Brush(c);
        using var sf = new StringFormat { Alignment = StringAlignment.Far };
        g.DrawString(s, f, b, new RectangleF(right - 240, y, 240, 24), sf);
    }

    private void Card(Graphics g, RectangleF r, string title)
    {
        using (var path = Rounded(r, 10))
            g.FillPath(GdiCache.Brush(Color.FromArgb(18, 255, 255, 255)), path);
        DrawText(g, title, _fs, Dim, r.X + 14, r.Y + 8);
    }

    private void Gauge(Graphics g, float cx, float cy, float rad, double pct, Color col, string label)
    {
        var rect = new RectangleF(cx - rad, cy - rad, rad * 2, rad * 2);
        g.DrawArc(GdiCache.Pen(Color.FromArgb(70, 78, 90), 8f), rect, 0, 360);
        if (pct > 0.5)
            g.DrawArc(GdiCache.PenRoundCap(col, 8f), rect, -90, (float)(360.0 * Math.Clamp(pct, 0, 100) / 100.0));
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        var b = GdiCache.Brush(TextCol);
        g.DrawString(label, _fb, b, new RectangleF(cx - rad, cy - rad, rad * 2, rad * 2), sf);
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

    /// <summary>Lijngrafiekje; nieuwste meting rechts. max &lt;= 0 = automatisch schalen.</summary>
    private void Spark(Graphics g, RectangleF r, Ring ring, Color col, double max)
    {
        g.DrawLine(GdiCache.Pen(Color.FromArgb(40, 255, 255, 255), 1f), r.X, r.Bottom, r.Right, r.Bottom);
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
        g.FillPolygon(GdiCache.Brush(Color.FromArgb(46, col)), fill);
        g.DrawLines(GdiCache.PenRoundJoin(col, 1.6f), pts);
    }

    // ---------- Tegels ----------
    private void DrawCpu(Graphics g, RectangleF r)
    {
        var m = _snap;
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
                g.FillRectangle(GdiCache.Brush(Color.FromArgb(70, 78, 90)), x, top, bw, hh);
                float fh = (float)(hh * Math.Clamp(cores[i], 0, 100) / 100.0);
                g.FillRectangle(GdiCache.Brush(Thr(cores[i])), x, top + hh - fh, bw, fh);
            }
        }
        Spark(g, new RectangleF(r.X + 14, r.Y + 130, r.Width - 28, 46), _history.Cpu, Accent, 100);
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
            if (ded > 0 && _snap.VramUsedPerLuid.TryGetValue(luid, out var used))
            {
                DrawText(g, $"VRAM {Metrics.FormatSize(used)} / {Metrics.FormatSize(ded)}", _fs, Dim, r.X + 14, y0 + 31);
                Bar(g, r.X + 150, y0 + 35, r.Width - 164, 100.0 * used / ded, Color.FromArgb(52, 199, 89), 5);
            }
        }
        Spark(g, new RectangleF(r.X + 14, r.Bottom - 54, r.Width - 28, 40), _history.Gpu, Accent, 100);
    }

    private void DrawMem(Graphics g, RectangleF r)
    {
        var m = _snap;
        Card(g, r, Loc.T("Memory"));
        Gauge(g, r.X + 62, r.Y + 80, 34, m.MemPercent, Thr(m.MemPercent), $"{m.MemPercent:0}%");
        DrawText(g, $"{Metrics.FormatSize(m.MemUsedBytes)} / {Metrics.FormatSize(m.MemTotalBytes)}", _fb, TextCol, r.X + 118, r.Y + 58);
        DrawText(g, $"{Loc.T("Free")} {Metrics.FormatSize(m.MemTotalBytes - m.MemUsedBytes)}", _f, Dim, r.X + 118, r.Y + 82);
        Spark(g, new RectangleF(r.X + 14, r.Y + 116, r.Width - 28, 36), _history.Mem, Color.FromArgb(52, 199, 89), 100);
    }

    private void DrawNet(Graphics g, RectangleF r, List<KeyValuePair<string, (double down, double up)>> adapters, bool limit)
    {
        var m = _snap;
        Card(g, r, Loc.T("Network"));
        DrawText(g, $"↓ {Metrics.FormatRate(m.NetDownBytesPerSec)}", _fb, Accent, r.X + 14, r.Y + 30);
        DrawText(g, $"↑ {Metrics.FormatRate(m.NetUpBytesPerSec)}", _f, Color.FromArgb(52, 199, 89), r.X + 14, r.Y + 52);

        var sp = new RectangleF(r.X + 150, r.Y + 28, r.Width - 164, 46);
        double max = Math.Max(1024, Math.Max(_history.NetDown.Max(300), _history.NetUp.Max(300)) * 1.15);
        Spark(g, sp, _history.NetDown, Accent, max);
        Spark(g, sp, _history.NetUp, Color.FromArgb(52, 199, 89), max);

        float y = r.Y + 34 + 66;
        foreach (var (label, u) in new[]
        {
            (Loc.T("Session"), _usage.Session(null)),
            (Loc.T("Today"), _usage.Today(null)),
            (Loc.T("This month"), _usage.Month(null)),
        })
        {
            DrawText(g, label, _fs, Dim, r.X + 14, y);
            DrawTextRight(g, $"↓ {Metrics.FormatBytes(u.Down)}   ↑ {Metrics.FormatBytes(u.Up)}", _fs, TextCol, r.Right - 14, y);
            y += 17;
        }
        if (limit)
        {
            var mu = _usage.Month(_cfg.NetworkAdapter);
            double pct = 100.0 * mu.Total / (_cfg.MonthlyLimitGb * 1073741824.0);
            DrawText(g, $"{Loc.T("Limit")} {Metrics.FormatBytes(mu.Total)} / {_cfg.MonthlyLimitGb} GB", _fs, Dim, r.X + 14, y);
            Bar(g, r.X + 14, y + 16, r.Width - 28, pct, Thr(pct), 5);
            y += 26;
        }
        foreach (var (name, rt) in adapters)
        {
            DrawText(g, Fit(g, name, _fs, r.Width - 28 - g.MeasureString($"↓ {Metrics.FormatRate(rt.down)}  ↑ {Metrics.FormatRate(rt.up)}", _fs).Width - 10), _fs, Dim, r.X + 14, y);
            DrawTextRight(g, $"↓ {Metrics.FormatRate(rt.down)}  ↑ {Metrics.FormatRate(rt.up)}", _fs, TextCol, r.Right - 14, y);
            if (_history.NetPer.TryGetValue(name, out var pr))
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
        var m = _snap;
        Card(g, r, Loc.T("Disks"));
        float y = r.Y + 30;
        foreach (var d in drives)
        {
            DrawText(g, d.Display, _f, TextCol, r.X + 14, y);
            DrawTextRight(g, $"{Metrics.FormatSize(d.Free)} {Loc.T("free of")} {Metrics.FormatSize(d.Total)}", _fs, Dim, r.Right - 14, y + 2);
            Bar(g, r.X + 14, y + 19, r.Width - 28, d.UsedPercent, Thr(d.UsedPercent), 7);
            y += 34;
        }
        DrawText(g, $"R {Metrics.FormatRate(m.DiskReadBytesPerSec)}", _fs, Accent, r.X + 14, y + 2);
        DrawTextRight(g, $"W {Metrics.FormatRate(m.DiskWriteBytesPerSec)}", _fs, Color.FromArgb(255, 149, 0), r.Right - 14, y + 2);
        var sp = new RectangleF(r.X + 14, y + 22, r.Width - 28, 32);
        double max = Math.Max(1024, Math.Max(_history.DiskRead.Max(300), _history.DiskWrite.Max(300)) * 1.15);
        Spark(g, sp, _history.DiskRead, Accent, max);
        Spark(g, sp, _history.DiskWrite, Color.FromArgb(255, 149, 0), max);
    }

    private void DrawBattery(Graphics g, RectangleF r)
    {
        var m = _snap;
        Card(g, r, Loc.T("Battery"));
        double pct = m.BatteryPercent;
        bool charging = m.BatteryCharging, ac = m.BatteryOnAc;
        Color fill = charging || ac ? Color.FromArgb(52, 199, 89)
                   : pct <= 10 ? Col(_cfg.CritColor, Color.Red)
                   : pct <= 20 ? Col(_cfg.WarnColor, Color.Orange) : Accent;

        float bx = r.X + 24, by = r.Y + 36, bw = 32, bh = 60;
        g.FillRectangle(GdiCache.Brush(Color.FromArgb(142, 142, 147)), bx + bw / 2 - 7, by - 4, 14, 4);
        using (var path = Rounded(new RectangleF(bx, by, bw, bh), 5))
        {
            g.FillPath(GdiCache.Brush(Color.FromArgb(42, 42, 45)), path);
            g.DrawPath(GdiCache.Pen(Color.FromArgb(142, 142, 147), 1.6f), path);
        }
        float ih = bh - 5, fh = (float)(ih * pct / 100.0);
        if (fh >= 1)
            using (var fp = Rounded(new RectangleF(bx + 2.5f, by + 2.5f + ih - fh, bw - 5, fh), 3))
                g.FillPath(GdiCache.Brush(fill), fp);
        if (charging) WidgetForm.DrawBolt(g, bx + bw / 2, by + bh / 2, 30);
        else if (ac) WidgetForm.DrawPlug(g, bx + bw / 2, by + bh / 2, 30);

        DrawText(g, $"{pct:0}%", _fbig, TextCol, r.X + 78, r.Y + 36);
        string state = charging ? Loc.T("Charging") : ac ? Loc.T("Plugged in") : Loc.T("On battery");
        string left = !ac && m.BatteryRemainingSec > 0
            ? $" · {m.BatteryRemainingSec / 3600}{Loc.T("h")} {m.BatteryRemainingSec % 3600 / 60:00}m" : "";
        DrawText(g, state + left, _f, Dim, r.X + 80, r.Y + 68);
    }

    private void DrawProcs(Graphics g, RectangleF r)
    {
        Card(g, r, Loc.T("Top programs"));
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
        var m = _snap;
        Card(g, r, Loc.T("System"));
        var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
        DrawText(g, $"{DateTime.Now:HH:mm}", _fbig, TextCol, r.X + 14, r.Y + 26);
        DrawText(g, $"{DateTime.Now:dddd d MMMM}", _f, Dim, r.X + 100, r.Y + 34);
        DrawText(g, $"Uptime  {(int)up.TotalDays} {Loc.T("d")} {up.Hours} {Loc.T("h")} {up.Minutes} m", _f, TextCol, r.X + 14, r.Y + 62);
        string temps = (m.CpuTempC is double c ? $"CPU {c:0}°C" : "") + (m.GpuTempC is double t ? $"   GPU {t:0}°C" : "");
        if (temps != "") DrawText(g, temps, _f, Dim, r.X + 14, r.Y + 80);
    }

    public void Dispose()
    {
        _bg?.Dispose(); _bg = null;
    }
}
