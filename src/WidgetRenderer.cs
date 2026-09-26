using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace TaskbarStats;

/// <summary>
/// De invoer voor één tekenronde van het widget: alles wat de renderer nodig heeft en niet zelf bezit.
/// Het venster (<see cref="WidgetForm"/>) vult dit per ronde; de renderer kent het venster niet.
/// </summary>
internal sealed class WidgetRenderContext
{
    /// <summary>De meting voor deze ronde (één complete momentopname).</summary>
    public MetricsSnapshot Snap { get; set; } = MetricsSnapshot.Empty;
    /// <summary>Geschiedenis voor de grafieken (CPU, GPU, MEM, netwerk).</summary>
    public MetricHistory History { get; set; } = new();
    /// <summary>Geschiedenis van de CPU-/GPU-temperatuur (eigen ringen, gevuld door het venster).</summary>
    public Ring? CpuTempHistory { get; set; }
    public Ring? GpuTempHistory { get; set; }
    /// <summary>Schijfruimte per schijf (voor het onderdeel "space").</summary>
    public IReadOnlyList<DriveSpace> Drives { get; set; } = Array.Empty<DriveSpace>();
    /// <summary>Pingstatistiek (tekststijl); mag null zijn (dan "…").</summary>
    public Func<PingStats?>? PingStats { get; set; }
    /// <summary>Pingmetingen voor de grafiekstijl (nieuwste laatst).</summary>
    public Func<double[]>? PingSamples { get; set; }
    /// <summary>Hoogte van het widget in pixels (bepaalt lettergrootte-schaal en indeling).</summary>
    public int Height { get; set; } = 44;
}

/// <summary>
/// Tekent het widget (cellen, meters, balken, grafieken, waardeopmaak) naar een gewone <see cref="Bitmap"/>, zonder venster.
/// Het venster levert per ronde een <see cref="WidgetRenderContext"/>; de renderer bezit lettertypen, puntenbuffers en de kleurregels
/// (ook het Windows-thema: <see cref="WinLight"/>/<see cref="WinPrevalence"/>/<see cref="WinAccent"/> worden door het venster gezet).
/// Alleen voor de UI-thread (gebruikt <see cref="GdiCache"/>). Verdeeld over WidgetRenderer.Graph/Fmt/Theme.cs.
/// </summary>
internal sealed partial class WidgetRenderer : IDisposable
{
    private readonly AppSettings _cfg;
    private Font _font = null!;
    private Font _fontSmall = null!;

    // Invoer van de lopende tekenronde (gezet door Bind).
    private MetricsSnapshot _snap = MetricsSnapshot.Empty;
    private MetricHistory _history = new();
    private IReadOnlyList<DriveSpace> _drives = Array.Empty<DriveSpace>();
    private WidgetRenderContext _ctx = new();
    private int Height => _ctx.Height;

    public WidgetRenderer(AppSettings cfg)
    {
        _cfg = cfg;
    }

    public void Dispose()
    {
        _font?.Dispose();
        _fontSmall?.Dispose();
    }

    private void Bind(WidgetRenderContext ctx)
    {
        _ctx = ctx;
        if (_font is null) ApplyFonts(ctx.Height);   // zonder venster: lettertypen volgen dan de hoogte van de context
        _snap = ctx.Snap;
        _history = ctx.History;
        _drives = ctx.Drives;
    }

    // ---------- Lettertypen ----------

    /// <summary>Maakt de lettertypen opnieuw (bij start en na een wijziging van lettertype, grootte of hoogte).</summary>
    public void ApplyFonts(int targetHeight)
    {
        double scale = Math.Clamp(targetHeight / 40.0, 0.85, 1.4);
        float size = (float)Math.Max(6, Math.Round((_cfg.FontSize - (_cfg.Compact ? 1 : 0)) * scale * 2) / 2);
        _font?.Dispose();
        _fontSmall?.Dispose();
        _font = new Font(_cfg.FontFamily, size, FontStyle.Regular, GraphicsUnit.Point);
        _fontSmall = new Font(_cfg.FontFamily, Math.Max(6f, size - 2), FontStyle.Regular, GraphicsUnit.Point);
    }

    // ---------- Tekenen ----------

    /// <summary>Meet de gewenste breedte (automatische breedte, vaste hoogte) door de cellen op een 1×1-bitmap te tekenen.</summary>
    public int MeasureWidth(WidgetRenderContext ctx, float dpi)
    {
        Bind(ctx);
        using var probe = new Bitmap(1, 1, PixelFormat.Format32bppArgb);
        probe.SetResolution(dpi, dpi);
        using var pg = Graphics.FromImage(probe);
        return Math.Max(60, DrawAll(pg));
    }

    /// <summary>Tekent het widget op de opgegeven breedte; de aanroeper geeft de bitmap vrij. <paramref name="drawBackground"/> tekent
    /// (optioneel) een achtergrondafbeelding onder de cellen.</summary>
    public Bitmap Render(WidgetRenderContext ctx, int width, float dpi, Action<Graphics>? drawBackground = null)
    {
        Bind(ctx);
        var bmp = new Bitmap(width, Height, PixelFormat.Format32bppArgb);
        bmp.SetResolution(dpi, dpi);
        using var g = Graphics.FromImage(bmp);
        g.Clear(_cfg.TransparentBackground
            ? Color.FromArgb(1, 0, 0, 0)
            : EffectiveBackground());
        // achtergrondafbeelding (onder de cellen; alleen in de echte tekenronde, niet in de meetronde)
        drawBackground?.Invoke(g);
        DrawAll(g);
        Cadence.Cap(g, width, Height);
        Cadence.Fool(g, width, Height, _font);
        if (!string.IsNullOrWhiteSpace(_cfg.BorderColor))
        {
            var pen = GdiCache.Pen(C(_cfg.BorderColor, Color.Magenta), 1);
            g.SmoothingMode = SmoothingMode.None;
            g.DrawRectangle(pen, 0, 0, width - 1, Height - 1);
        }
        return bmp;
    }

    /// <summary>Meten en tekenen in één keer (zonder venster: tests, voorbeelden).</summary>
    public Bitmap Render(WidgetRenderContext ctx, float dpi = 96f)
        => Render(ctx, MeasureWidth(ctx, dpi), dpi);

    /// <summary>Tekent alle cellen en geeft de gewenste totale breedte terug.</summary>
    private int DrawAll(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

        int x = _cfg.Compact ? 4 : 6;
        int gap = Gap;
        var textCol = EffectiveText();
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
                        x += _cfg.CpuPerCore ? DrawCores(g, x, "CPU", _snap.CpuCores) : DrawMetric(g, x, "CPU", _snap.CpuPercent, _cfg.CpuStyle);
                        if (_cfg.TempMerge && _cfg.ShowCpuTemp && _snap.CpuTempC is double mct) x += DrawTempTag(g, x, mct, textCol);
                        x += gap;
                    }
                    break;
                case "gpu":
                    if (_cfg.ShowGpu)
                    {
                        x += DrawMetric(g, x, "GPU", _snap.GpuPercent, _cfg.GpuStyle);
                        if (_cfg.TempMerge && _cfg.ShowGpuTemp && _snap.GpuTempC is double mgt) x += DrawTempTag(g, x, mgt, textCol);
                        x += gap;
                    }
                    break;
                case "mem":
                    if (_cfg.ShowMem) x += DrawMem(g, x) + gap;
                    break;
                case "batt":
                    if (_cfg.ShowBattery && _snap.BatteryPresent) x += DrawBattery(g, x) + gap;
                    break;
                case "space":
                    foreach (var (label, value, pct) in DiskSpaceCells())
                        x += DrawTextCell(g, x, label, value, PctTemplate, ThresholdColor(pct, textCol)) + gap;
                    break;
                case "cputemp":
                    if (_cfg.ShowCpuTemp && !(_cfg.TempMerge && _cfg.ShowCpu) && _snap.CpuTempC is double ct)
                        x += DrawTemp(g, x, "CPU", ct, _cfg.CpuTempStyle, textCol) + gap;
                    break;
                case "gputemp":
                    if (_cfg.ShowGpuTemp && !(_cfg.TempMerge && _cfg.ShowGpu) && _snap.GpuTempC is double gt)
                        x += DrawTemp(g, x, "GPU", gt, _cfg.GpuTempStyle, textCol) + gap;
                    break;
                case "cpufreq": case "diskbusy": case "disktemp": case "mobotemp":
                    { int ew = DrawExtraItem(g, x, id); if (ew > 0) x += ew + gap; }
                    break;
            }
        }
        return x;
    }

    // ---------- Kleuren & drempels ----------
    internal static Color C(string hex, Color fb)
    {
        try { return ColorTranslator.FromHtml(hex); } catch { return fb; }
    }

    private Color ThresholdColor(double v, Color baseColor)
    {
        if (v >= _cfg.CritThreshold) return C(_cfg.CritColor, Color.Red);
        if (v >= _cfg.WarnThreshold) return C(_cfg.WarnColor, Color.Orange);
        return baseColor;
    }

    // Tekent 'actual' maar reserveert de breedte van 'template' (vaste breedte).
    private int DrawDigital(Graphics g, int x, string actual, string template, Color color)
    {
        float reserve = g.MeasureString(template, _font).Width;
        var sz = g.MeasureString(actual, _font);
        var b = GdiCache.Brush(color);
        g.DrawString(actual, _font, b, x, (Height - sz.Height) / 2);
        return (int)Math.Ceiling(reserve);
    }

    private int Gap => _cfg.Compact ? 6 : 10;

    // Schaalfactor t.o.v. de basishoogte van 40 px: lettertypes en balken groeien mee.
    private double UiScale => Math.Clamp(Height / 40.0, 0.85, 1.4);

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
        var b = GdiCache.Brush(EffectiveText());
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
        var b = GdiCache.Brush(color);
        g.DrawString(value, _font, b, x + (cellW - sz.Width) / 2, top + (h - sz.Height) / 2);
        return cellW;
    }

    // Ping: "PING 12 ms", oranje vanaf 100 ms, rood vanaf 250 ms of als er geen antwoord komt.
    private int DrawPing(Graphics g, int x)
    {
        if (_cfg.PingStyle == TextGraphStyle.Graph) return DrawPingGraph(g, x);
        var st = _ctx.PingStats?.Invoke();
        var textCol = EffectiveText();
        double? stLast = st?.Last;
        double? last = stLast is double l && l >= 0 ? Cadence.M(7, l) : stLast;
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
        _                  => DrawTextCell(g, x, label, Pct(v), PctTemplate, ThresholdColor(v, EffectiveText())),
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
        var bg = GdiCache.Pen(EffectiveTrack(Color.FromArgb(80, 80, 80)), pw);
        var fg = GdiCache.Pen(ThresholdColor(v, EffectiveAccent()), pw);
        g.DrawArc(bg, rect, 0, 360);
        g.DrawArc(fg, rect, -90, (float)(360.0 * Math.Clamp(v, 0, 100) / 100.0));
        var txt = $"{v:0}";
        var sz = g.MeasureString(txt, _fontSmall);
        var tb = GdiCache.Brush(EffectiveText());
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
        var bg = GdiCache.Brush(EffectiveTrack(Color.FromArgb(70, 70, 70)));
        var fg = GdiCache.Brush(ThresholdColor(v, EffectiveAccent()));
        g.FillRectangle(bg, rect);
        g.FillRectangle(fg, new Rectangle(rect.X, rect.Y, (int)(bw * Math.Clamp(v, 0, 100) / 100.0), bh));
        var pen = GdiCache.Pen(EffectiveTrack(Color.FromArgb(110, 110, 110)));
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
        var bg = GdiCache.Brush(EffectiveTrack(Color.FromArgb(70, 70, 70)));
        foreach (var v in cores)
        {
            g.FillRectangle(bg, cx, top, barW, h);
            int fh = (int)(h * Math.Clamp(v, 0, 100) / 100.0);
            var fg = GdiCache.Brush(ThresholdColor(v, EffectiveAccent()));
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
        var fill = GdiCache.Brush(Color.White);
        var edge = GdiCache.Pen(Color.FromArgb(150, 0, 0, 0), 1f);
        g.FillPolygon(fill, poly);
        g.DrawPolygon(edge, poly);
    }

    internal static void DrawPlug(Graphics g, float cx, float cy, float h)
    {
        float s = h / 18f, top = cy - h / 2;
        var pen = GdiCache.PenRoundCap(Color.White, Math.Max(1.3f, 1.6f * s));
        var body = GdiCache.Brush(Color.FromArgb(90, 255, 255, 255));
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
        var m = _snap;
        double pct = m.BatteryPercent;
        bool charging = m.BatteryCharging, ac = m.BatteryOnAc;
        Color fill = charging || ac ? Color.FromArgb(52, 199, 89)
                   : pct <= 10 ? C(_cfg.CritColor, Color.Red)
                   : pct <= 20 ? C(_cfg.WarnColor, Color.Orange)
                   : EffectiveAccent();

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
            var fb = GdiCache.Brush(fill);
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
            var tb = GdiCache.Brush(Color.White);
            g.DrawString(t, _fontSmall, tb, cx - sz.Width / 2, ty);
        }

        int w = bodyW + 2;
        if (_cfg.BatteryPercent == BatteryPercentMode.Beside)
        {
            string t = $"{pct:0}%";
            var sz = g.MeasureString(t, _font);
            var tb = GdiCache.Brush(EffectiveText());
            g.DrawString(t, _font, tb, bx + bodyW + 5, (Height - sz.Height) / 2);
            w = bodyW + 2 + 5 + (int)Math.Ceiling(g.MeasureString("100%", _font).Width);
        }
        return w;
    }

    private int DrawLabel(Graphics g, int x, string label)
    {
        var sz = g.MeasureString(label, _fontSmall);
        var b = GdiCache.Brush(EffectiveText());
        g.DrawString(label, _fontSmall, b, x, (Height - sz.Height) / 2);
        return (int)Math.Ceiling(sz.Width);
    }
}
