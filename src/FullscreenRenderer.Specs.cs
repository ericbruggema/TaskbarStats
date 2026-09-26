
namespace TaskbarStats;

/// <summary>Specificatiepagina van het fullscreen-scherm (toets I).</summary>
public sealed partial class FullscreenRenderer
{
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
        var spec = Specs();
        var blocks = spec.Blocks;
        Card(g, null, R, Loc.T("Specifications"),
             spec.Loading ? Loc.T("loading…") : Loc.T("mouse wheel = scroll"));
        if (blocks.Count == 0)
        {
            T(g, spec.Loading ? Loc.T("Collecting hardware information…")
                                      : Loc.T("No information available (retrying automatically)."), _f, Dim, R.X + 20, R.Y + 60);
            if (!spec.Loading)
            {
                if (spec.LastError.Length > 0) T(g, spec.LastError, _f, Dim, R.X + 20, R.Y + 90);
                RefreshSpecs();   // begrensd tot 1 poging per 10 s
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
            float x = area.X + c * (cw + gap), y = area.Y - _v.SpecScroll + ys[c];
            float h = head + b.Rows.Count * rowH + pad;
            var r = new RectangleF(x, y, cw, h);
            if (r.Bottom > area.Y && r.Y < area.Bottom)
            {
                using (var path = Rounded(r, 10))
                { g.FillPath(GdiCache.Brush(Color.FromArgb(14, 255, 255, 255)), path); g.DrawPath(GdiCache.Pen(Color.FromArgb(26, 255, 255, 255)), path); }
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
        _v.SpecMax = Math.Max(0, total - area.Height);
        if (_v.SpecScroll > _v.SpecMax) _v.SpecScroll = _v.SpecMax;
        if (_v.SpecMax > 0)   // schuifbalk
        {
            float th = Math.Max(40, area.Height * area.Height / total);
            float ty = area.Y + (area.Height - th) * (_v.SpecScroll / _v.SpecMax);
            var sb = GdiCache.Brush(Color.FromArgb(70, 255, 255, 255));
            g.FillRectangle(sb, R.Right - 7, ty, 4, th);
        }
    }
}
