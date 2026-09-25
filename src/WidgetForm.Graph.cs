using System.Drawing.Drawing2D;

namespace TaskbarStats;

// Mini-geschiedenisgrafiek als weergavestijl (CPU, GPU, MEM, temperaturen, netwerk, ping).
// Bron: _history (1 Hz, gevuld in Tick; niet tijdens Cadence.Active); temperatuur heeft eigen ringen.
// Tijdens de spaarstand (_idle) wordt niet gemeten: de reeks sluit dan gewoon aan op wat er al was.
public sealed partial class WidgetForm
{
    private readonly Ring _hCpuT = new(300), _hGpuT = new(300);
    private long _graphAt;
    private PointF[] _gp = new PointF[0], _gpoly = new PointF[0];   // hergebruikte puntenbuffers (worden alleen opnieuw gemaakt als het aantal punten wijzigt)
    private double[] _pingCache = Array.Empty<double>();
    private long _pingCacheAt;

    private int GraphN => _cfg.GraphSeconds <= 30 ? 30 : _cfg.GraphSeconds >= 120 ? 120 : 60;

    // Temperatuurgeschiedenis bijhouden, 1x per seconde (aangeroepen vanuit Tick).
    private void SampleGraph()
    {
        long now = Environment.TickCount64;
        if (now - _graphAt < 1000 || Cadence.Active) return;
        _graphAt = now;
        if (_metrics.CpuTempC is double c) _hCpuT.Add(c);
        if (_metrics.GpuTempC is double t) _hGpuT.Add(t);
    }

    private Ring? HistFor(string label) => label switch
    {
        "CPU" => _history.Cpu,
        "GPU" => _history.Gpu,
        "MEM" => _history.Mem,
        _ => null,
    };

    // Cel met label en een omkaderde vlakte; r = het vlak waarin de lijn(en) getekend worden.
    private int GraphCell(Graphics g, int x, string label, out Rectangle r)
    {
        label = Cadence.L(label);
        bool above = _cfg.LabelsAbove;
        var (top, h) = Area(g);
        int gw = (int)Math.Round(_cfg.GraphWidthBase * UiScale), gh = above ? h : Height - 10;
        int lw = above ? LabelWidth(g, label) : DrawLabel(g, x, label);
        int cellW = above ? Math.Max(gw, lw) : lw + 3 + gw;
        if (above) DrawTopLabel(g, x, cellW, label);
        int gx = above ? x + (cellW - gw) / 2 : x + lw + 3;
        int gy = above ? top : (Height - gh) / 2;
        r = new Rectangle(gx, gy, gw, gh);
        using var bg = new SolidBrush(EffectiveTrack(Color.FromArgb(50, 50, 50)));
        g.FillRectangle(bg, r);
        return cellW;
    }

    private void GraphFrame(Graphics g, Rectangle r)
    {
        using var pen = new Pen(EffectiveTrack(Color.FromArgb(110, 110, 110)));
        var sm = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.None;
        g.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
        g.SmoothingMode = sm;
    }

    // Zet de laatste n metingen van de ring om in punten (_gp); geeft het aantal punten terug.
    private int FillPoints(Ring ring, Rectangle r, double max, int n)
    {
        int cnt = Math.Min(ring.Count, n);
        if (cnt == 0 || max <= 0) return 0;
        if (_gp.Length != cnt) _gp = new PointF[cnt];
        float w = r.Width - 1, bottom = r.Bottom - 2, h = r.Height - 3;
        for (int k = 0; k < cnt; k++)
        {
            double v = ring[ring.Count - cnt + k];
            _gp[k] = new PointF(r.X + (n - cnt + k) * w / (n - 1), bottom - (float)(Math.Clamp(v / max, 0, 1) * h));
        }
        return cnt;
    }

    private void PlotLine(Graphics g, Rectangle r, int cnt, Color col, bool fill)
    {
        if (cnt < 2) return;
        if (fill)
        {
            if (_gpoly.Length != cnt + 2) _gpoly = new PointF[cnt + 2];
            Array.Copy(_gp, _gpoly, cnt);
            _gpoly[cnt] = new PointF(_gp[cnt - 1].X, r.Bottom - 2);
            _gpoly[cnt + 1] = new PointF(_gp[0].X, r.Bottom - 2);
            using var fb = new SolidBrush(Color.FromArgb(70, col));
            g.FillPolygon(fb, _gpoly);
        }
        using var pen = new Pen(col, Math.Max(1.2f, (float)(1.3 * UiScale))) { LineJoin = LineJoin.Round };
        g.DrawLines(pen, _gp);
    }

    // Percentage (0-100) of temperatuur (0-100 °C) als lijn/vlak; kleur volgt de huidige waarde.
    private int DrawGraph(Graphics g, int x, string label, double v)
        => DrawGraphRing(g, x, label, v, HistFor(label));

    private int DrawGraphRing(Graphics g, int x, string label, double v, Ring? ring)
    {
        int cell = GraphCell(g, x, label, out var r);
        if (ring is not null) PlotLine(g, r, FillPoints(ring, r, 100, GraphN), ThresholdColor(v, EffectiveAccent()), true);
        GraphFrame(g, r);
        return cell;
    }

    private int DrawTempGraph(Graphics g, int x, string what, double t)
    {
        string label = _cfg.LabelsAbove ? what + "°C" : what + "°";
        return DrawGraphRing(g, x, label, t, what == "CPU" ? _hCpuT : _hGpuT);
    }

    // Netwerk: download en upload als twee lijnen; schaal automatisch of vast (NetGraphMaxMBps).
    private int DrawNetGraph(Graphics g, int x)
    {
        int cell = GraphCell(g, x, "NET", out var r);
        int n = GraphN;
        bool dn = _cfg.ShowNetDown, up = _cfg.ShowNetUp;
        double max;
        if (_cfg.NetGraphMaxMBps > 0) max = _cfg.NetGraphMaxMBps * 1048576.0;
        else
        {
            max = 128 * 1024;   // ondergrens, anders lijkt ruis een piek
            if (dn) max = Math.Max(max, _history.NetDown.Max(n));
            if (up) max = Math.Max(max, _history.NetUp.Max(n));
        }
        var accent = EffectiveAccent();
        var upCol = Color.FromArgb(52, 199, 89);
        if (dn) PlotLine(g, r, FillPoints(_history.NetDown, r, max, n), accent, true);
        if (up) PlotLine(g, r, FillPoints(_history.NetUp, r, max, n), upCol, !dn);
        GraphFrame(g, r);
        return cell;
    }

    // Kopie van de pingmetingen, hooguit 1x per seconde ververst (voorkomt een allocatie per tekenpas).
    private double[] PingSamples()
    {
        long now = Environment.TickCount64;
        if (now - _pingCacheAt >= 900) { _pingCacheAt = now; _pingCache = _metrics.Ping.Samples(); }
        return _pingCache;
    }

    // Ping: lijn van 0 tot max(100 ms, hoogste meting); "geen antwoord" (-1) onderbreekt de lijn en krijgt een rode punt.
    private int DrawPingGraph(Graphics g, int x)
    {
        int cell = GraphCell(g, x, "PING", out var r);
        var s = PingSamples();
        int n = Math.Max(8, GraphN / 2);   // er komt ongeveer om de 2 s een meting
        int cnt = Math.Min(s.Length, n);
        double max = 100;
        for (int i = s.Length - cnt; i < s.Length; i++) max = Math.Max(max, s[i]);
        max = Math.Ceiling(max / 50) * 50;

        var crit = C(_cfg.CritColor, Color.Red);
        double last = cnt == 0 ? 0 : s[^1];
        Color col = cnt == 0 ? EffectiveAccent()
                  : last < 0 || last >= 250 ? crit
                  : last >= 100 ? C(_cfg.WarnColor, Color.Orange) : EffectiveAccent();
        float w = r.Width - 1, bottom = r.Bottom - 2, hgt = r.Height - 3;
        using var pen = new Pen(col, Math.Max(1.2f, (float)(1.3 * UiScale))) { LineJoin = LineJoin.Round };
        using var lost = new SolidBrush(crit);
        float dot = Math.Max(3f, (float)(3 * UiScale));
        float px = 0, py = 0; bool prev = false;
        for (int k = 0; k < cnt; k++)
        {
            double v = s[s.Length - cnt + k];
            float sx = r.X + (n - cnt + k) * w / (n - 1);
            if (v < 0)
            {
                g.FillEllipse(lost, sx - dot / 2, bottom - dot, dot, dot);
                prev = false;
                continue;
            }
            float sy = bottom - (float)(Math.Clamp(v / max, 0, 1) * hgt);
            if (prev) g.DrawLine(pen, px, py, sx, sy);
            px = sx; py = sy; prev = true;
        }
        GraphFrame(g, r);
        return cell;
    }
}
