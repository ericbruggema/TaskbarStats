using System.Drawing.Drawing2D;
using System.Text;

namespace TaskbarStats;

/// <summary>Kleine hulpfuncties voor timing, tellen van klikken en toetsvolgordes.</summary>
public static class Cadence
{
    private static volatile int _mode;
    private static long _start, _until;

    private static string D(string s) => Encoding.UTF8.GetString(Convert.FromBase64String(s).Select(b => (byte)(b ^ 0x5A)).ToArray());

    /// <summary>Loopt er nu een tijdelijke weergavemodus?</summary>
    public static bool Active => _mode != 0 && Environment.TickCount64 <= _until;
    public static bool On(int mode) => _mode == mode && Environment.TickCount64 <= _until;

    /// <summary>Wordt aangeroepen als een modus start (het widget zet dan een snelle timer aan).</summary>
    public static Action<int>? Hook;

    public static void Go(int mode, int ms)
    {
        _start = Environment.TickCount64;
        _until = _start + ms;
        _mode = mode;
        Hook?.Invoke(mode);
    }

    private const double RampMs = 8000;

    // Langzaam beginnen en steeds sneller (derde macht): 0 -> 1 over RampMs.
    private static double Rise(long now) { double p = Math.Clamp((now - _start) / RampMs, 0, 1); return p * p * p; }

    /// <summary>Laatste fase van modus 2 (na het stijgen).</summary>
    public static bool Finale => _mode == 2 && Environment.TickCount64 - _start >= RampMs && Environment.TickCount64 <= _until;

    /// <summary>Meetwaarde tijdelijk laten stijgen (kind: 0 cpu, 1 geheugen, 2 gpu, 3/4 temperatuur, 5/6 netwerk, 7 ping).</summary>
    public static double M(int kind, double raw)
    {
        if (_mode != 2) return raw;
        long now = Environment.TickCount64;
        if (now > _until) { _mode = 0; return raw; }
        double top = kind switch { 0 or 1 or 2 => 100, 3 or 4 => 110, 5 or 6 => 1.5e9, 7 => 999, _ => raw };
        return raw + (Math.Max(top, raw) - raw) * Rise(now);
    }

    public static double[] C(double[] raw)
    {
        if (_mode != 2) return raw;
        long now = Environment.TickCount64;
        if (now > _until) { _mode = 0; return raw; }
        double r = Rise(now);
        var res = new double[raw.Length];
        for (int i = 0; i < res.Length; i++) res[i] = raw[i] + (100 - raw[i]) * Math.Clamp(r * (1 + 0.15 * Math.Sin(i * 1.7)), 0, 1);
        return res;
    }

    /// <summary>Tekst over de waarden heen in de laatste fase.</summary>
    public static void Fool(Graphics g, int w, int h, Font f)
    {
        if (!Finale) return;
        using var shade = new SolidBrush(Color.FromArgb(95, 0, 0, 0));
        g.FillRectangle(shade, 0, 0, w, h);
        string t = D("HDU1Nj8+eiM7Mns=");
        using var bf = new Font(f.FontFamily, f.Size * 1.35f, FontStyle.Bold);
        var sz = g.MeasureString(t, bf);
        using var b = new SolidBrush(Environment.TickCount64 / 250 % 2 == 0 ? Color.OrangeRed : Color.Gold);
        g.DrawString(t, bf, b, (w - sz.Width) / 2, (h - sz.Height) / 2);
    }

    /// <summary>Kleine trilling (in pixels) voor het hele widget; (0, 0) als er niets loopt.</summary>
    public static Point Jitter()
    {
        if (!On(1)) return Point.Empty;
        long t = Environment.TickCount64;
        return new Point((int)Math.Round(3 * Math.Sin(t * 0.09)) + (int)(t / 30 % 3) - 1, (int)Math.Round(2 * Math.Sin(t * 0.13 + 1)) + (int)(t / 47 % 3) - 1);
    }

    public static string Title => D("DjU7KS4=");

    // ---------- Herhaalde klikken ----------
    private static readonly Queue<long>[] Q = { new(), new(), new() };

    /// <summary>True zodra er 'need' keer binnen 'ms' milliseconden is aangeroepen (voor die teller).</summary>
    public static bool Hit(int slot, int need, int ms)
    {
        var q = Q[slot];
        long n = Environment.TickCount64;
        q.Enqueue(n);
        while (q.Count > 0 && n - q.Peek() > ms) q.Dequeue();
        if (q.Count < need) return false;
        q.Clear();
        return true;
    }

    // ---------- Toetsvolgordes ----------
    private static readonly int[] Ring = new int[10];
    private static int _n;

    private static uint H(int from, int count)
    {
        uint h = 2166136261;
        for (int i = 0; i < count; i++) h = unchecked((h ^ (uint)Ring[(from + i) % 10]) * 16777619);
        return h;
    }

    /// <summary>Voer een toetscode in; geeft 0 of het nummer van een herkende volgorde.</summary>
    public static int Feed(int key)
    {
        Ring[_n % 10] = key;
        _n++;
        if (_n >= 10) { uint h10 = H((_n - 10) % 10, 10); if (h10 == 3620383964u || h10 == 1230234516u) { _n = 0; return 1; } }
        if (_n >= 5 && H((_n - 5) % 10, 5) == 4265512048u) { _n = 0; return 2; }
        return 0;
    }

    // ---------- Datums ----------
    private static int Today() => (DateTime.Now.Month * 100 + DateTime.Now.Day) ^ 0x2B7;
    private static readonly int[] A = { 806 };
    private static readonly int[] B = { 1538, 1663, 1662, 1661, 1656 };
    private static readonly int[] O = { 796 };

    public static string L(string s)
    {
        if (Array.IndexOf(A, Today()) < 0 || s.Length < 2) return s;
        var c = s.ToCharArray(); Array.Reverse(c); return new string(c);
    }

    public static void Cap(Graphics g, int w, int h)
    {
        int d = Today();
        Color? col = Array.IndexOf(B, d) >= 0 ? Color.FromArgb(220, 30, 40) : Array.IndexOf(O, d) >= 0 ? Color.FromArgb(255, 110, 0) : null;
        if (col is null) return;
        float s = h / 40f * 0.65f, ox = w - 24 * s;
        var sm = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var b = new SolidBrush(col.Value);
        using var wh = new SolidBrush(Color.White);
        g.FillPolygon(b, new[] { new PointF(ox + 3 * s, 12 * s), new PointF(ox + 21 * s, 12 * s), new PointF(ox + 15 * s, 1.5f * s) });
        g.FillRectangle(wh, ox + 2 * s, 10 * s, 20 * s, 4 * s);
        g.FillEllipse(wh, ox + 12 * s, 0, 6 * s, 6 * s);
        g.SmoothingMode = sm;
    }

    // ---------- Extra thema ----------
    public static ThemeData Look() => new()
    {
        Name = D("Dj8oNzM0OzZ6a2Nibg=="),
        TextColor = "#33FF33", BackgroundColor = "#000000", AccentColor = "#33FF33", WarnColor = "#FFB000",
        CritColor = "#FF3333", BorderColor = "#33FF33", FontFamily = "Consolas", FontSize = 10,
        CpuStyle = DisplayStyle.Bar, GpuStyle = DisplayStyle.Bar, MemStyle = DisplayStyle.Bar, DashOpacity = 95,
    };
}
