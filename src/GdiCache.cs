using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace TaskbarStats;

/// <summary>
/// Gedeelde cache van GDI+-penselen, pennen en lettertypen voor de tekenroutines van widget, dashboard en fullscreen.
/// Eerder maakte elke tekenpas tientallen SolidBrush/Pen-objecten aan (en gaf ze weer vrij); nu worden ze per kleur/dikte hergebruikt.
/// Alleen voor de UI-thread (niet thread-veilig, bewust zonder lock); alle tekenpassen lopen op de UI-thread.
/// De teruggegeven objecten zijn van de cache: NIET vrijgeven (geen using) en NIET aanpassen (kleur, breedte, cap, join);
/// voor pennen met ronde uiteinden of ronde hoeken zijn er aparte varianten.
/// Begrenzing: <see cref="Trim"/> (aan het begin van elke tekenpas) leegt de cache als hij te groot wordt; <see cref="Clear"/> bij afsluiten.
/// </summary>
internal static class GdiCache
{
    private const int MaxEntries = 256;

    private static readonly Dictionary<int, SolidBrush> _brushes = new();
    private static readonly Dictionary<(int argb, float width, byte kind), Pen> _pens = new();
    private static readonly Dictionary<(string family, float size, FontStyle style, GraphicsUnit unit), Font> _fonts = new();
    private static int _uiThread;

    /// <summary>Alleen de tests zetten dit uit (ze draaien na elkaar, maar op wisselende threads).</summary>
    internal static bool ThreadCheck = true;

    [Conditional("DEBUG")]
    private static void CheckThread()
    {
        if (!ThreadCheck) return;
        int id = Environment.CurrentManagedThreadId;
        if (_uiThread == 0) _uiThread = id;
        Debug.Assert(_uiThread == id, "GdiCache is alleen voor de UI-thread");
    }

    /// <summary>Effen penseel in deze kleur (alpha telt mee).</summary>
    public static SolidBrush Brush(Color c)
    {
        CheckThread();
        int key = c.ToArgb();
        if (!_brushes.TryGetValue(key, out var b)) _brushes[key] = b = new SolidBrush(c);
        return b;
    }

    /// <summary>Gewone pen (standaard uiteinden en hoeken).</summary>
    public static Pen Pen(Color c, float width = 1f) => GetPen(c, width, 0);

    /// <summary>Pen met ronde uiteinden (StartCap/EndCap = Round).</summary>
    public static Pen PenRoundCap(Color c, float width) => GetPen(c, width, 1);

    /// <summary>Pen met ronde hoeken (LineJoin = Round).</summary>
    public static Pen PenRoundJoin(Color c, float width) => GetPen(c, width, 2);

    private static Pen GetPen(Color c, float width, byte kind)
    {
        CheckThread();
        var key = (c.ToArgb(), width, kind);
        if (!_pens.TryGetValue(key, out var p))
        {
            p = new Pen(c, width);
            if (kind == 1) { p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; }
            else if (kind == 2) p.LineJoin = LineJoin.Round;
            _pens[key] = p;
        }
        return p;
    }

    /// <summary>Lettertype (zelfde parameters als de Font-constructor).</summary>
    public static Font Font(string family, float size, FontStyle style = FontStyle.Regular, GraphicsUnit unit = GraphicsUnit.Point)
    {
        CheckThread();
        var key = (family, size, style, unit);
        if (!_fonts.TryGetValue(key, out var f)) _fonts[key] = f = new System.Drawing.Font(family, size, style, unit);
        return f;
    }

    /// <summary>Aan te roepen aan het begin van een tekenpas (niet tijdens): leegt de cache als er te veel in zit (bv. bij veel verschillende kleuren).</summary>
    public static void Trim()
    {
        if (_brushes.Count + _pens.Count + _fonts.Count > MaxEntries) Clear();
    }

    /// <summary>Geeft alles vrij (bij afsluiten of als de cache te groot wordt).</summary>
    public static void Clear()
    {
        foreach (var b in _brushes.Values) b.Dispose();
        foreach (var p in _pens.Values) p.Dispose();
        foreach (var f in _fonts.Values) f.Dispose();
        _brushes.Clear(); _pens.Clear(); _fonts.Clear();
    }
}
