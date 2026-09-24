using System.Drawing.Drawing2D;
using System.Text;

namespace TaskbarStats;

/// <summary>Kleine hulpfuncties voor timing, tellen van klikken en toetsvolgordes.</summary>
public static class Cadence
{
    private static volatile int _mode;
    private static long _until;

    private static string D(string s) => Encoding.UTF8.GetString(Convert.FromBase64String(s).Select(b => (byte)(b ^ 0x5A)).ToArray());

    /// <summary>Loopt er nu een tijdelijke weergavemodus?</summary>
    public static bool Active => _mode != 0 && Environment.TickCount64 <= _until;
    public static bool On(int mode) => _mode == mode && Environment.TickCount64 <= _until;

    public static void Go(int mode, int ms) { _until = Environment.TickCount64 + ms; _mode = mode; }

    /// <summary>Meetwaarde tijdelijk vervangen (kind: 0 cpu, 1 geheugen, 2 gpu, 5/6 netwerk).</summary>
    public static double M(int kind, double raw)
    {
        int m = _mode;
        if (m == 0) return raw;
        long now = Environment.TickCount64;
        if (now > _until) { _mode = 0; return raw; }
        double t = now / 1000.0;
        if (m == 1)
        {
            if (kind == 0) return 9001;
            if (kind >= 5) return (0.5 + 0.5 * Math.Sin(t * 5 + kind)) * 1.2e9;
            return 50 + 50 * Math.Sin(t * 6 + kind * 1.3);
        }
        return kind switch
        {
            0 => 97 + 3 * Math.Abs(Math.Sin(t * 23)),
            2 => 90 + 10 * Math.Abs(Math.Sin(t * 17)),
            _ => raw,
        };
    }

    public static double[] C(double[] raw)
    {
        int m = _mode;
        if (m == 0) return raw;
        long now = Environment.TickCount64;
        if (now > _until) { _mode = 0; return raw; }
        double t = now / 1000.0;
        var r = new double[raw.Length];
        for (int i = 0; i < r.Length; i++)
            r[i] = m == 1 ? 50 + 50 * Math.Sin(t * 7 + i * 0.7) : 90 + 10 * Math.Abs(Math.Sin(t * 19 + i));
        return r;
    }

    public static string Word => D("Dg8IGBU=");
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
        if (_n >= 10 && H((_n - 10) % 10, 10) == 3620383964u) { _n = 0; return 1; }
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
