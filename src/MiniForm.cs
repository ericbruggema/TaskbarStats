namespace TaskbarStats;

/// <summary>Klein raster-venster met een timer (hulpvenster).</summary>
public sealed class MiniForm : Form
{
    private const int N = 24, Px = 20;
    private readonly System.Windows.Forms.Timer _t = new() { Interval = 110 };
    private readonly List<Point> _s = new();
    private readonly Random _r = new();
    private readonly Color _acc;
    private Point _dir, _next, _f;
    private int _score;
    private bool _over;

    public MiniForm(AppSettings cfg)
    {
        AppIcon.Apply(this);
        Text = Cadence.Title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(N * Px, N * Px + 28);
        BackColor = Color.FromArgb(18, 18, 20);
        DoubleBuffered = true;
        KeyPreview = true;
        try { _acc = ColorTranslator.FromHtml(cfg.AccentColor); } catch { _acc = Color.DodgerBlue; }
        _t.Tick += (_, _) => Step();
        FormClosed += (_, _) => _t.Dispose();
        Reset();
    }

    private void Reset()
    {
        _s.Clear();
        for (int i = 0; i < 4; i++) _s.Add(new Point(N / 2 - i, N / 2));
        _dir = _next = new Point(1, 0);
        _score = 0; _over = false;
        Food();
        _t.Interval = 110;
        _t.Start();
        Invalidate();
    }

    private void Food()
    {
        do { _f = new Point(_r.Next(N), _r.Next(N)); } while (_s.Contains(_f));
    }

    private void Step()
    {
        _dir = _next;
        var h = new Point(_s[0].X + _dir.X, _s[0].Y + _dir.Y);
        if (h.X < 0 || h.Y < 0 || h.X >= N || h.Y >= N || _s.Contains(h)) { _over = true; _t.Stop(); Invalidate(); return; }
        _s.Insert(0, h);
        if (h == _f) { _score++; Food(); if (_t.Interval > 55) _t.Interval -= 3; }
        else _s.RemoveAt(_s.Count - 1);
        Invalidate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Escape: Close(); break;
            case Keys.Space: if (_over) Reset(); break;
            case Keys.Up or Keys.W: if (_dir.Y == 0) _next = new Point(0, -1); break;
            case Keys.Down or Keys.S: if (_dir.Y == 0) _next = new Point(0, 1); break;
            case Keys.Left or Keys.A: if (_dir.X == 0) _next = new Point(-1, 0); break;
            case Keys.Right or Keys.D: if (_dir.X == 0) _next = new Point(1, 0); break;
            default: base.OnKeyDown(e); return;
        }
        e.Handled = true;
    }

    protected override bool IsInputKey(Keys k) => k is Keys.Up or Keys.Down or Keys.Left or Keys.Right || base.IsInputKey(k);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var grid = new Pen(Color.FromArgb(28, 28, 32)))
            for (int i = 0; i <= N; i++) { g.DrawLine(grid, i * Px, 0, i * Px, N * Px); g.DrawLine(grid, 0, i * Px, N * Px, i * Px); }

        using (var crust = new SolidBrush(Color.FromArgb(160, 100, 40)))
        using (var crumb = new SolidBrush(Color.FromArgb(250, 220, 150)))
        {
            g.FillRectangle(crust, _f.X * Px + 2, _f.Y * Px + 2, Px - 4, Px - 4);
            g.FillRectangle(crumb, _f.X * Px + 4, _f.Y * Px + 4, Px - 8, Px - 8);
        }
        using (var body = new SolidBrush(_acc))
        using (var head = new SolidBrush(ControlPaint.Light(_acc)))
            for (int i = 0; i < _s.Count; i++)
                g.FillRectangle(i == 0 ? head : body, _s[i].X * Px + 1, _s[i].Y * Px + 1, Px - 2, Px - 2);

        using var f = new Font("Segoe UI", 10f);
        string bar = _over
            ? Loc.Pick($"Aangebrand!  Score {_score}   ·   spatie = opnieuw, Esc = sluiten", $"Burnt!  Score {_score}   ·   space = again, Esc = close")
            : Loc.Pick($"Score {_score}   ·   pijltjes of WASD", $"Score {_score}   ·   arrows or WASD");
        g.DrawString(bar, f, Brushes.Gainsboro, 6, N * Px + 4);
    }
}
