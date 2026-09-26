using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace TaskbarStats;

/// <summary>Leesmij en credits als film-aftiteling: rustig omhoog scrollende namen, echte cijfers uit de app en een grapje.</summary>
public sealed class CreditsForm : Form
{
    private enum K { Title, Sub, Head, Text, Gap, Big, Fine }

    private readonly List<(K kind, string text)> _lines = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    private readonly Color _bg, _fg, _acc;
    private readonly Font _fTitle, _fSub, _fHead, _fText, _fBig, _fFine;
    private readonly Panel _stage;
    private float _y;
    private float _total;
    private bool _paused;
    private long _last;
    private const float Speed = 42f;   // pixels per seconde

    // Sleutels (Engels); de vertalingen staan in de taalbestanden.
    private static readonly string[] Jokes =
    {
        Loc.N("No PerformanceCounter was harmed in the making of this app."),
        Loc.N("This screen uses almost no CPU. Ironic for a CPU monitor."),
        Loc.N("If you're reading this, your computer still hasn't got any faster."),
        Loc.N("All percentages were rounded by hand, with love."),
        Loc.N("Chrome did nothing to slow this screen down. This time."),
        Loc.N("Made with too much coffee and too little RAM."),
        Loc.N("No fans were needed for this screen. Yours may disagree."),
        Loc.N("While you were reading this, Windows probably updated something."),
        Loc.N("The memory you need is always just slightly more than you have."),
        Loc.N("The answer to 'why is everything slow?' is usually: a browser tab."),
        Loc.N("These credits were not sponsored by your Task Manager."),
        Loc.N("This screen is 100% free, 0% cloud and 3% self-mockery."),};

    public CreditsForm(AppSettings cfg, UsageTracker usage, Metrics metrics)
    {
        AppIcon.Apply(this);
        Text = Loc.T("Readme and credits");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(620, 600);
        DoubleBuffered = true;
        KeyPreview = true;

        _bg = Parse(cfg.BackgroundColor, Color.FromArgb(14, 14, 18));
        _fg = Parse(cfg.TextColor, Color.White);
        _acc = Parse(cfg.AccentColor, Color.DodgerBlue);
        BackColor = _bg;
        string fam = string.IsNullOrWhiteSpace(cfg.FontFamily) ? "Segoe UI" : cfg.FontFamily;
        _fTitle = new Font(fam, 30f, FontStyle.Bold);
        _fSub = new Font(fam, 12f, FontStyle.Italic);
        _fHead = new Font(fam, 15f, FontStyle.Bold);
        _fText = new Font(fam, 12f);
        _fBig = new Font(fam, 20f, FontStyle.Bold);
        _fFine = new Font(fam, 9.5f);

        Build(usage, metrics);

        _stage = new BufferedPanel { Dock = DockStyle.Top, Height = 540, BackColor = _bg };
        _stage.Paint += PaintStage;
        _stage.MouseDown += (_, _) => _paused = !_paused;
        _stage.MouseWheel += (_, e) => _y = Math.Max(-_stage.Height, _y + e.Delta / 2f);

        var bar = new Panel { Dock = DockStyle.Fill, BackColor = _bg };
        var close = new Button { Text = Loc.T("Close"), Location = new Point(504, 12), Size = new Size(100, 30), FlatStyle = FlatStyle.System };
        var hint = new Label { Text = Loc.T("click = pause · wheel = scroll"), AutoSize = true, ForeColor = Color.FromArgb(140, _fg), Location = new Point(16, 20) };
        close.Click += (_, _) => Close();   // het venster is niet modaal: DialogResult alleen sluit hem niet
        bar.Controls.AddRange(new Control[] { close, hint });
        CancelButton = close;
        Controls.Add(bar);
        Controls.Add(_stage);

        _y = _stage.Height;          // begint onder in beeld en scrolt omhoog
        _last = Environment.TickCount64;
        _timer.Tick += (_, _) =>
        {
            long n = Environment.TickCount64;
            float dt = Math.Min(0.1f, (n - _last) / 1000f);
            _last = n;
            if (!_paused) _y -= Speed * dt;
            if (_y < -_total - 40) _y = _stage.Height;
            _stage.Invalidate();
        };
        _timer.Start();
        FormClosed += (_, _) =>
        {
            _timer.Dispose();
            foreach (var f in new[] { _fTitle, _fSub, _fHead, _fText, _fBig, _fFine }) f.Dispose();
        };
    }

    private sealed class BufferedPanel : Panel
    {
        public BufferedPanel() { DoubleBuffered = true; ResizeRedraw = true; }
    }

    private static Color Parse(string? s, Color fallback)
    {
        try { return string.IsNullOrWhiteSpace(s) ? fallback : ColorTranslator.FromHtml(s); } catch { return fallback; }
    }

    // ---------- Inhoud ----------
    private void Build(UsageTracker usage, Metrics metrics)
    {
        void L(K k, string t) => _lines.Add((k, t));

        L(K.Title, "TaskbarStats");
        L(K.Sub, Loc.T("Version {0}", AboutForm.Version));
        L(K.Gap, "");
        L(K.Sub, Loc.T("An Eric Bruggema production"));
        L(K.Gap, ""); L(K.Gap, "");

        L(K.Head, Loc.T("Starring"));
        L(K.Text, Loc.T("Your CPU  as  The Perpetually Overworked Hero"));
        L(K.Text, Loc.T("The RAM  as  Almost Full"));
        L(K.Text, Loc.T("Chrome  as  The Heavy"));
        L(K.Text, Loc.T("The fan  as  The Sound in the Background"));
        L(K.Text, Loc.T("Your network cable  as  The Silent Force"));
        L(K.Gap, ""); L(K.Gap, "");

        L(K.Head, Loc.T("Idea, design and testing"));
        L(K.Text, "Eric Bruggema");
        L(K.Gap, "");
        L(K.Head, Loc.T("Programming"));
        L(K.Text, Loc.T("Claude Code (Anthropic)"));
        L(K.Gap, "");
        L(K.Head, Loc.T("Sensors"));
        L(K.Text, "LibreHardwareMonitor (MPL-2.0)");
        L(K.Gap, "");
        L(K.Head, Loc.T("With thanks to"));
        L(K.Text, "HidSharp (Apache-2.0)");
        L(K.Text, ".NET (MIT)");
        L(K.Text, "Inno Setup");
        L(K.Fine, Loc.T("The listed components remain under their own licences."));
        L(K.Gap, ""); L(K.Gap, "");

        L(K.Head, Loc.T("What TaskbarStats knows about you"));
        foreach (var s in Stats(usage, metrics)) L(K.Text, s);
        L(K.Gap, ""); L(K.Gap, "");

        L(K.Big, Loc.T("— THE END —"));
        L(K.Gap, "");
        var j = Jokes[Random.Shared.Next(Jokes.Length)];
        L(K.Sub, Loc.T(j));   // lang-dynamic: j komt uit Jokes (Loc.N)
        L(K.Gap, ""); L(K.Gap, "");
    }

    private static string N0(double v) => v.ToString("N0", CultureInfo.CurrentCulture);

    private static string Sz(long bytes) => Metrics.FormatSize(Math.Max(0, bytes));

    private static IEnumerable<string> Stats(UsageTracker usage, Metrics m)
    {
        var res = new List<string>();
        try
        {
            var tot = usage.Total();
            int days = usage.DaysTracked();
            if (days > 0 && tot.Total > 0)
            {
                res.Add(Loc.P("Tracked over {0} day:|Tracked over {0} days:", days));
                res.Add($"↓ {Sz(tot.Down)}   ↑ {Sz(tot.Up)}");
                double mb = tot.Down / 1048576.0;
                var fun = ((DateTime.Now.DayOfYear + days) % 4) switch
                {
                    0 => Loc.T("That’s about {0} mp3s", N0(mb / 4)),
                    1 => Loc.T("That’s about {0} floppy disks", N0(mb / 1.44)),
                    2 => Loc.T("That’s about {0} photos", N0(mb / 3)),
                    _ => Loc.T("That’s about {0} CD-ROMs full", N0(mb / 700)),
                };
                if (mb >= 1) res.Add(fun);
                if (usage.BestDay() is { } b)
                    res.Add(Loc.T("Busiest day: {0:MMMM d} ({1})", b.day, Sz(b.bytes)));
            }
            else res.Add(Loc.T("No network usage tracked yet."));
        }
        catch (Exception dex) { Diag.Swallow(dex); }

        try
        {
            var up = DateTime.Now - Process.GetCurrentProcess().StartTime;
            res.Add(Loc.T("This session has been running for {0}", Dur(up)));
            res.Add(Loc.T("CPU peak {0:0}%   ·   RAM peak {1:0}%", m.PeakCpu, m.PeakMem));
            res.Add(Loc.T("Fastest download: {0}", Metrics.FormatRate(m.PeakDown, false)));
            var win = TimeSpan.FromMilliseconds(Environment.TickCount64);
            res.Add(Loc.T("Windows has been up for {0} without a restart", Dur(win)));
        }
        catch (Exception dex) { Diag.Swallow(dex); }
        return res;
    }

    private static string Dur(TimeSpan t)
    {
        if (t.TotalDays >= 1) return Loc.T("{0} d {1} h", (int)t.TotalDays, t.Hours);
        if (t.TotalHours >= 1) return Loc.T("{0} h {1} m", (int)t.TotalHours, t.Minutes);
        return Loc.T("{0} min", (int)t.TotalMinutes);
    }

    // ---------- Tekenen ----------
    private Font FontFor(K k) => k switch
    {
        K.Title => _fTitle, K.Sub => _fSub, K.Head => _fHead, K.Big => _fBig, K.Fine => _fFine, _ => _fText,
    };

    private Color ColorFor(K k) => k switch
    {
        K.Title or K.Big => _acc,
        K.Head => _acc,
        K.Fine => Color.FromArgb(150, _fg),
        _ => _fg,
    };

    private float Gap(K k) => k switch { K.Gap => 18f, K.Title => 10f, K.Head => 8f, K.Big => 14f, _ => 6f };

    private void PaintStage(object? s, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int w = _stage.Width;
        var fmt = new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.Word };

        float y = _y;
        foreach (var (kind, text) in _lines)
        {
            if (kind == K.Gap) { y += Gap(kind); continue; }
            var f = FontFor(kind);
            var sz = g.MeasureString(text, f, w - 60);
            if (y + sz.Height > 0 && y < _stage.Height)
            {
                using var b = new SolidBrush(ColorFor(kind));
                g.DrawString(text, f, b, new RectangleF(30, y, w - 60, sz.Height), fmt);
            }
            y += sz.Height + Gap(kind);
        }
        _total = y - _y;

        // Zachte fade boven en onder
        using (var top = new LinearGradientBrush(new Rectangle(0, 0, w, 70), _bg, Color.FromArgb(0, _bg), 90f))
            g.FillRectangle(top, 0, 0, w, 70);
        using (var bot = new LinearGradientBrush(new Rectangle(0, _stage.Height - 70, w, 70), Color.FromArgb(0, _bg), _bg, 90f))
            g.FillRectangle(bot, 0, _stage.Height - 70, w, 70);
    }
}
