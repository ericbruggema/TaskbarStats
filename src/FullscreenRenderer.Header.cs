
namespace TaskbarStats;

/// <summary>Kop van het fullscreen-scherm: titel, klok, tijdvensterknoppen, afsluitknop en de voortgang van de tour.</summary>
public sealed partial class FullscreenRenderer
{
    // ---------- Kop ----------
    private void DrawHeader(Graphics g)
    {
        if (_v.Detail is null)
        {
            T(g, "TaskbarStats", _fh, TextCol, M + 4, 18);
            T(g, "· " + Environment.MachineName, _f, Dim, M + 138, 21);
            var chip = new RectangleF(M + 330, 16, 210, 32);
            _hits.Add(("spec", chip));
            using (var path = Rounded(chip, 8))
                g.FillPath(GdiCache.Brush(Color.FromArgb(_v.Hover == "spec" ? 50 : 26, 255, 255, 255)), path);
            TC(g, Loc.T("Specifications  (I)"), _fb, TextCol, chip.X + chip.Width / 2, chip.Y + chip.Height / 2);
        }
        else
        {
            var back = new RectangleF(M, 12, 230, 40);
            _hits.Add(("back", back));
            bool hot = _v.Hover == "back";
            using (var path = Rounded(back, 10))
                g.FillPath(GdiCache.Brush(Color.FromArgb(hot ? 50 : 26, 255, 255, 255)), path);
            T(g, "←  " + Loc.T("Back (Esc)"), _fb, TextCol, M + 16, 23);
        }

        TC(g, Now().ToString("HH:mm:ss"), _fbig, TextCol, CW / 2 - 60, 34);
        T(g, Now().ToString("dddd d MMMM yyyy"), _f, Dim, CW / 2 + 40, 26);

        // tijdvenster
        // afsluitknop uiterst rechts (zelfde als Esc in het overzicht)
        var exit = new RectangleF(CW - M - 44, 16, 44, 32);
        _hits.Add(("exit", exit));
        bool exitHot = _v.Hover == "exit";
        using (var path = Rounded(exit, 8))
            g.FillPath(GdiCache.Brush(exitHot ? Color.FromArgb(220, 200, 40, 40) : Color.FromArgb(26, 255, 255, 255)), path);
        TC(g, "✕", _fb, TextCol, exit.X + exit.Width / 2, exit.Y + exit.Height / 2);

        float x = CW - M - 4 - 56;
        var chips = new (int w, string l)[] { (3600, Loc.T("1 h")), (300, "5 m"), (60, "1 m") };
        foreach (var (w, l) in chips)
        {
            var r = new RectangleF(x - 56, 16, 56, 32);
            _hits.Add(($"w{w}", r));
            bool on = _v.Win == w, hot = _v.Hover == $"w{w}";
            using (var path = Rounded(r, 8))
                g.FillPath(GdiCache.Brush(on ? Color.FromArgb(180, Accent) : Color.FromArgb(hot ? 50 : 26, 255, 255, 255)), path);
            TC(g, l, _fb, TextCol, r.X + r.Width / 2, r.Y + r.Height / 2);
            x -= 62;
        }
        TR(g, Loc.T("graph:"), _fs, Dim, x - 4, 24);
        string hint = _v.Tour ? Loc.T("Tour {0}/{1}  ·  click or key = stop", _v.TourIdx + 1, _v.TourCount)
                    : _v.Detail is null ? Loc.T("Click a tile for details  ·  space = tour  ·  Esc or ✕ closes")
                    : Loc.T("Esc: back to the overview");
        TR(g, hint, _fs, _v.Tour ? Accent : Dim, x - 110, 24);
        if (_v.Tour)   // voortgang van de huidige pagina
        {
            float f = Math.Clamp((Ticks() - _v.TourAt) / (float)FullscreenView.TourMillis(Cfg), 0f, 1f);
            var pb = GdiCache.Brush(Color.FromArgb(200, Accent));
            g.FillRectangle(pb, 0, CH - 5, CW * f, 5);
        }
    }
}
