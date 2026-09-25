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

    private static readonly (string nl, string en)[] Jokes =
    {
        ("Geen enkele PerformanceCounter is gewond geraakt bij het maken van deze app.", "No PerformanceCounter was harmed in the making of this app."),
        ("Dit scherm gebruikt bijna geen CPU. Ironisch voor een CPU-monitor.", "This screen uses almost no CPU. Ironic for a CPU monitor."),
        ("Als je dit leest, is je computer nog steeds niet sneller geworden.", "If you're reading this, your computer still hasn't got any faster."),
        ("Alle percentages zijn met liefde handmatig afgerond.", "All percentages were rounded by hand, with love."),
        ("Chrome heeft niets gedaan om dit scherm te vertragen. Deze keer.", "Chrome did nothing to slow this screen down. This time."),
        ("Gemaakt met te veel koffie en te weinig RAM.", "Made with too much coffee and too little RAM."),
        ("Geen ventilatoren waren nodig om dit scherm te maken. Jouw ventilator denkt daar anders over.", "No fans were needed for this screen. Yours may disagree."),
        ("Terwijl je dit las, heeft Windows waarschijnlijk iets geüpdatet.", "While you were reading this, Windows probably updated something."),
        ("Het percentage geheugen dat je nodig hebt is altijd net iets meer dan je hebt.", "The memory you need is always just slightly more than you have."),
        ("Op de vraag 'waarom is alles traag?' is het antwoord meestal: een browsertabblad.", "The answer to 'why is everything slow?' is usually: a browser tab."),
        ("Deze aftiteling is niet gesponsord door je taakbeheerder.", "These credits were not sponsored by your Task Manager."),
        ("Dit scherm is 100% gratis, 0% cloud en 3% zelfspot.", "This screen is 100% free, 0% cloud and 3% self-mockery."),
    };

    public CreditsForm(AppSettings cfg, UsageTracker usage, Metrics metrics)
    {
        AppIcon.Apply(this);
        Text = Loc.Pick("Leesmij en credits", "Readme and credits");
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
        var close = new Button { Text = Loc.Pick("Sluiten", "Close"), Location = new Point(504, 12), Size = new Size(100, 30), FlatStyle = FlatStyle.System };
        var hint = new Label { Text = Loc.Pick("klik = pauze · scrollwiel = handmatig", "click = pause · wheel = scroll"), AutoSize = true, ForeColor = Color.FromArgb(140, _fg), Location = new Point(16, 20) };
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
        L(K.Sub, Loc.Pick($"Versie {AboutForm.Version}", $"Version {AboutForm.Version}"));
        L(K.Gap, "");
        L(K.Sub, Loc.Pick("Een Eric Bruggema-productie", "An Eric Bruggema production"));
        L(K.Gap, ""); L(K.Gap, "");

        L(K.Head, Loc.Pick("In de hoofdrollen", "Starring"));
        L(K.Text, Loc.Pick("Je CPU  als  De Altijd Overwerkte Held", "Your CPU  as  The Perpetually Overworked Hero"));
        L(K.Text, Loc.Pick("Het RAM  als  Bijna Vol", "The RAM  as  Almost Full"));
        L(K.Text, Loc.Pick("Chrome  als  De Zware Jongen", "Chrome  as  The Heavy"));
        L(K.Text, Loc.Pick("De ventilator  als  Het Geluid op de Achtergrond", "The fan  as  The Sound in the Background"));
        L(K.Text, Loc.Pick("Je netwerkkabel  als  De Stille Kracht", "Your network cable  as  The Silent Force"));
        L(K.Gap, ""); L(K.Gap, "");

        L(K.Head, Loc.Pick("Idee, ontwerp en testen", "Idea, design and testing"));
        L(K.Text, "Eric Bruggema");
        L(K.Gap, "");
        L(K.Head, Loc.Pick("Programmeerwerk", "Programming"));
        L(K.Text, Loc.Pick("Claude Code (Anthropic)", "Claude Code (Anthropic)"));
        L(K.Gap, "");
        L(K.Head, Loc.Pick("Sensoren", "Sensors"));
        L(K.Text, "LibreHardwareMonitor (MPL-2.0)");
        L(K.Gap, "");
        L(K.Head, Loc.Pick("Met dank aan", "With thanks to"));
        L(K.Text, "HidSharp (Apache-2.0)");
        L(K.Text, ".NET (MIT)");
        L(K.Text, "Inno Setup");
        L(K.Fine, Loc.Pick("De genoemde onderdelen vallen onder hun eigen licenties.", "The listed components remain under their own licences."));
        L(K.Gap, ""); L(K.Gap, "");

        L(K.Head, Loc.Pick("Wat TaskbarStats over jou weet", "What TaskbarStats knows about you"));
        foreach (var s in Stats(usage, metrics)) L(K.Text, s);
        L(K.Gap, ""); L(K.Gap, "");

        L(K.Big, Loc.Pick("— EINDE —", "— THE END —"));
        L(K.Gap, "");
        var j = Jokes[Random.Shared.Next(Jokes.Length)];
        L(K.Sub, Loc.Pick(j.nl, j.en));
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
                res.Add(Loc.Pick($"Bijgehouden over {days} {(days == 1 ? "dag" : "dagen")}:", $"Tracked over {days} {(days == 1 ? "day" : "days")}:"));
                res.Add($"↓ {Sz(tot.Down)}   ↑ {Sz(tot.Up)}");
                double mb = tot.Down / 1048576.0;
                var fun = ((DateTime.Now.DayOfYear + days) % 4) switch
                {
                    0 => Loc.Pick($"Dat zijn zo’n {N0(mb / 4)} mp3’s", $"That’s about {N0(mb / 4)} mp3s"),
                    1 => Loc.Pick($"Dat zijn zo’n {N0(mb / 1.44)} floppydisks", $"That’s about {N0(mb / 1.44)} floppy disks"),
                    2 => Loc.Pick($"Dat zijn zo’n {N0(mb / 3)} foto’s", $"That’s about {N0(mb / 3)} photos"),
                    _ => Loc.Pick($"Dat zijn zo’n {N0(mb / 700)} cd-roms vol", $"That’s about {N0(mb / 700)} CD-ROMs full"),
                };
                if (mb >= 1) res.Add(fun);
                if (usage.BestDay() is { } b)
                    res.Add(Loc.Pick($"Drukste dag: {b.day:d MMMM} ({Sz(b.bytes)})", $"Busiest day: {b.day:MMMM d} ({Sz(b.bytes)})"));
            }
            else res.Add(Loc.Pick("Nog geen netwerkverbruik bijgehouden.", "No network usage tracked yet."));
        }
        catch { }

        try
        {
            var up = DateTime.Now - Process.GetCurrentProcess().StartTime;
            res.Add(Loc.Pick($"Deze sessie draait al {Dur(up)}", $"This session has been running for {Dur(up)}"));
            res.Add(Loc.Pick($"CPU-piek {m.PeakCpu:0}%   ·   RAM-piek {m.PeakMem:0}%", $"CPU peak {m.PeakCpu:0}%   ·   RAM peak {m.PeakMem:0}%"));
            res.Add(Loc.Pick($"Snelste download: {Metrics.FormatRate(m.PeakDown, false)}", $"Fastest download: {Metrics.FormatRate(m.PeakDown, false)}"));
            var win = TimeSpan.FromMilliseconds(Environment.TickCount64);
            res.Add(Loc.Pick($"Windows draait al {Dur(win)} zonder herstart", $"Windows has been up for {Dur(win)} without a restart"));
        }
        catch { }
        return res;
    }

    private static string Dur(TimeSpan t)
    {
        if (t.TotalDays >= 1) return Loc.Pick($"{(int)t.TotalDays} d {t.Hours} u", $"{(int)t.TotalDays} d {t.Hours} h");
        if (t.TotalHours >= 1) return Loc.Pick($"{(int)t.TotalHours} u {t.Minutes} m", $"{(int)t.TotalHours} h {t.Minutes} m");
        return Loc.Pick($"{(int)t.TotalMinutes} m", $"{(int)t.TotalMinutes} min");
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
