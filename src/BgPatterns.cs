using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace TaskbarStats;

/// <summary>
/// Meegeleverde achtergronden die in code worden getekend (geen bestanden, dus niets te leveren of te lekken).
/// In de instellingen staan ze als pad <c>builtin:naam</c> (<see cref="Path"/>); <see cref="BgLayer"/> laadt ze via <see cref="Render"/>.
/// Ze hebben een transparante of donkere basis, zodat de achtergrondkleur van het thema er doorheen komt en de tekst leesbaar blijft.
/// </summary>
public static class BgPatterns
{
    public const string Prefix = "builtin:";

    public static readonly string[] Names = { "matrix", "grid", "stars", "sunset", "waves", "scanlines", "hearts" };

    public static string Path(string name) => Prefix + name;

    public static bool IsBuiltIn(string? path) => path is not null && path.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    public static string NameOf(string path) => path[Prefix.Length..].Trim().ToLowerInvariant();

    /// <summary>Tekent een achtergrond op de gevraagde maat (PArgb); een onbekende naam geeft null.</summary>
    public static Bitmap? Render(string name, int w, int h)
    {
        name = name.Trim().ToLowerInvariant();
        if (!Names.Contains(name) || w < 8 || h < 8) return null;
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        g.Clear(Color.Transparent);
        var rnd = new Random(name.GetHashCode() ^ 0x5EED);   // vast patroon per naam en maat
        switch (name)
        {
            case "matrix": Matrix(g, w, h, rnd); break;
            case "grid": Grid(g, w, h); break;
            case "stars": Stars(g, w, h, rnd); break;
            case "sunset": Sunset(g, w, h); break;
            case "waves": Waves(g, w, h); break;
            case "scanlines": Scanlines(g, w, h); break;
            case "hearts": Hearts(g, w, h, rnd); break;
        }
        return bmp;
    }

    // Vallende tekens in kolommen, kop helder en de staart vervaagt.
    private static void Matrix(Graphics g, int w, int h, Random rnd)
    {
        const string chars = "0123456789ABCDEFabcdef<>+*=:;";
        int step = Math.Max(12, w / 80);
        using var font = new Font("Consolas", step * 0.9f, FontStyle.Bold, GraphicsUnit.Pixel);
        for (int x = 0; x < w; x += step)
        {
            int len = rnd.Next(6, 26);
            int y0 = rnd.Next(-h / 2, h);
            for (int i = 0; i < len; i++)
            {
                int y = y0 + i * step;
                if (y < -step || y > h) continue;
                int a = i == len - 1 ? 255 : (int)(200 * Math.Pow((double)(i + 1) / len, 1.6));
                var c = i == len - 1 ? Color.FromArgb(a, 200, 255, 200) : Color.FromArgb(a, 0, 255, 65);
                using var br = new SolidBrush(c);
                g.DrawString(chars[rnd.Next(chars.Length)].ToString(), font, br, x, y);
            }
        }
    }

    // Neonraster in perspectief met een gloeiende horizon.
    private static void Grid(Graphics g, int w, int h)
    {
        float hz = h * 0.42f;
        using (var glow = new LinearGradientBrush(new RectangleF(0, hz - h * 0.25f, w, h * 0.3f), Color.Transparent, Color.FromArgb(90, 255, 45, 149), 90f))
            g.FillRectangle(glow, 0, hz - h * 0.25f, w, h * 0.25f);
        using var pv = new Pen(Color.FromArgb(120, 0, 229, 255), 1.4f);
        for (int i = -24; i <= 24; i++)
            g.DrawLine(pv, w / 2f + i * w * 0.02f, hz, w / 2f + i * w * 0.16f, h);
        for (int i = 1; i <= 14; i++)
        {
            float t = i / 14f, y = hz + (h - hz) * t * t;
            g.DrawLine(pv, 0, y, w, y);
        }
        using var ph = new Pen(Color.FromArgb(210, 255, 45, 149), 2.5f);
        g.DrawLine(ph, 0, hz, w, hz);
    }

    private static void Stars(Graphics g, int w, int h, Random rnd)
    {
        using (var path = new GraphicsPath())
        {
            path.AddEllipse(-w * 0.2f, h * 0.55f, w * 1.4f, h * 1.2f);
            using var pg = new PathGradientBrush(path) { CenterColor = Color.FromArgb(120, 130, 90, 220), SurroundColors = new[] { Color.Transparent } };
            g.FillPath(pg, path);
        }
        int n = Math.Max(90, w * h / 2200);
        for (int i = 0; i < n; i++)
        {
            float x = rnd.Next(w), y = rnd.Next(h), r = rnd.NextDouble() < 0.08 ? 3f : 1.6f;
            using var br = new SolidBrush(Color.FromArgb(rnd.Next(140, 255), 255, 255, rnd.Next(200, 255)));
            g.FillEllipse(br, x, y, r, r);
        }
    }

    // Lucht met zon aan de horizon (met de horizontale sneden).
    private static void Sunset(Graphics g, int w, int h)
    {
        using (var sky = new LinearGradientBrush(new Rectangle(0, 0, w, h + 1), Color.FromArgb(0, 26, 11, 46), Color.FromArgb(230, 254, 180, 123), 90f))
        {
            sky.InterpolationColors = new ColorBlend
            {
                Positions = new[] { 0f, 0.55f, 0.8f, 1f },
                Colors = new[] { Color.FromArgb(0, 26, 11, 46), Color.FromArgb(140, 255, 60, 172), Color.FromArgb(200, 255, 126, 95), Color.FromArgb(230, 254, 180, 123) },
            };
            g.FillRectangle(sky, 0, 0, w, h);
        }
        float r = h * 0.28f, cx = w * 0.5f, cy = h * 0.66f;
        using (var sun = new LinearGradientBrush(new RectangleF(cx - r, cy - r, r * 2, r * 2), Color.FromArgb(255, 255, 214, 102), Color.FromArgb(255, 255, 60, 172), 90f))
            g.FillEllipse(sun, cx - r, cy - r, r * 2, r * 2);
        using var sunPath = new GraphicsPath();
        sunPath.AddEllipse(cx - r, cy - r, r * 2, r * 2);
        g.SetClip(sunPath);
        for (int i = 0; i < 6; i++)   // horizontale sneden onderin de zon
        {
            float y = cy + r * (0.05f + i * 0.16f), th = 1.5f + i * 1.6f;
            using var cut = new SolidBrush(Color.Transparent);
            g.CompositingMode = CompositingMode.SourceCopy;
            g.FillRectangle(cut, cx - r, y, r * 2, th);
            g.CompositingMode = CompositingMode.SourceOver;
        }
        g.ResetClip();
    }

    private static void Waves(Graphics g, int w, int h)
    {
        for (int layer = 0; layer < 5; layer++)
        {
            float baseY = h * (0.45f + layer * 0.12f), amp = h * (0.05f + layer * 0.012f), len = w / (1.6f + layer * 0.5f), ph = layer * 1.3f;
            var pts = new List<PointF> { new(0, h) };
            for (int x = 0; x <= w; x += 6) pts.Add(new PointF(x, baseY + (float)Math.Sin(x / len * Math.PI * 2 + ph) * amp));
            pts.Add(new PointF(w, h));
            using var br = new SolidBrush(Color.FromArgb(46 + layer * 10, 0, 140 + layer * 18, 216));
            g.FillPolygon(br, pts.ToArray());
        }
    }

    // Oude beeldbuis: horizontale lijntjes en donkere hoeken.
    private static void Scanlines(Graphics g, int w, int h)
    {
        using var p = new Pen(Color.FromArgb(22, 255, 255, 255), 1f);
        for (int y = 0; y < h; y += 3) g.DrawLine(p, 0, y, w, y);
        using var path = new GraphicsPath();
        path.AddEllipse(-w * 0.25f, -h * 0.4f, w * 1.5f, h * 1.8f);
        using var vg = new PathGradientBrush(path) { CenterColor = Color.FromArgb(0, 255, 255, 255), SurroundColors = new[] { Color.FromArgb(190, 0, 0, 0) } };
        var clip = g.Clip; g.SetClip(new Rectangle(0, 0, w, h));
        g.FillRectangle(vg, 0, 0, w, h);
        g.Clip = clip;
    }

    private static void Hearts(Graphics g, int w, int h, Random rnd)
    {
        int n = Math.Max(14, w * h / 30000);
        for (int i = 0; i < n; i++)
        {
            float size = rnd.Next(14, 54), x = rnd.Next(w), y = rnd.Next(h);
            using var font = new Font("Segoe UI Symbol", size, FontStyle.Regular, GraphicsUnit.Pixel);
            var st = g.Save();
            g.TranslateTransform(x, y);
            g.RotateTransform(rnd.Next(-30, 30));
            using var br = new SolidBrush(Color.FromArgb(rnd.Next(60, 150), 255, rnd.Next(60, 140), rnd.Next(120, 190)));
            g.DrawString("♥", font, br, 0, 0);
            g.Restore(st);
        }
    }
}
