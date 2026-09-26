namespace TaskbarStats;

// Het widget volgt (optioneel) het Windows-thema: donker/licht van de taakbalk en de accentkleur.
// De eigen kleurinstellingen van de gebruiker blijven onaangetast; alleen het getekende effect verandert.
// De renderer leest de gecachete themawaarden die het venster erin zet (WidgetForm.WinTheme.cs leest het register).
internal sealed partial class WidgetRenderer
{
    private static readonly Color TaskbarDark = Color.FromArgb(0x1F, 0x1F, 0x1F);
    private static readonly Color TaskbarLight = Color.FromArgb(0xF3, 0xF3, 0xF3);

    /// <summary>Taakbalk is licht.</summary>
    public bool WinLight { get; set; }
    /// <summary>Accentkleur op de taakbalk (ColorPrevalence).</summary>
    public bool WinPrevalence { get; set; }
    public Color WinAccent { get; set; } = Color.FromArgb(0x00, 0x78, 0xD4);

    private bool FollowTheme => _cfg.WidgetFollowWindows != WinThemeMode.Off;
    private bool FollowAccent => _cfg.WidgetFollowWindows == WinThemeMode.DarkLightAccent;

    /// <summary>Achtergrond van het widget (niet-transparant): eigen kleur of die van het Windows-thema.</summary>
    public Color EffectiveBackground()
    {
        if (!FollowTheme) return C(_cfg.BackgroundColor, Color.FromArgb(20, 20, 20));
        var baseCol = WinLight ? TaskbarLight : TaskbarDark;
        return FollowAccent && WinPrevalence ? Mix(baseCol, WinAccent, 0.30) : baseCol;
    }

    /// <summary>Tekstkleur: eigen kleur, of wit/zwart met goed contrast op de effectieve achtergrond.</summary>
    public Color EffectiveText()
    {
        if (!FollowTheme) return C(_cfg.TextColor, Color.White);
        return Luminance(EffectiveBackground()) > 0.4 ? Color.Black : Color.White;
    }

    /// <summary>Kleur van meters/balken: eigen kleur, of de accentkleur van Windows (bijgesteld voor contrast).</summary>
    public Color EffectiveAccent()
    {
        if (!FollowAccent) return C(_cfg.AccentColor, Color.DodgerBlue);
        var bg = EffectiveBackground();
        var col = WinAccent;
        bool lightBg = Luminance(bg) > 0.4;
        for (int i = 0; i < 10 && Contrast(col, bg) < 3.0; i++) col = Mix(col, lightBg ? Color.Black : Color.White, 0.15);
        return col;
    }

    /// <summary>Grijze sporen (achter meters/balken): op een lichte achtergrond omgekeerd, anders ongewijzigd.</summary>
    public Color EffectiveTrack(Color dark)
    {
        if (!FollowTheme || Luminance(EffectiveBackground()) <= 0.4) return dark;
        return Color.FromArgb(dark.A, 275 - dark.R, 275 - dark.G, 275 - dark.B);
    }

    private static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        (int)Math.Round(a.R + (b.R - a.R) * t), (int)Math.Round(a.G + (b.G - a.G) * t), (int)Math.Round(a.B + (b.B - a.B) * t));

    private static double Luminance(Color c)
    {
        static double f(int v) { double s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * f(c.R) + 0.7152 * f(c.G) + 0.0722 * f(c.B);
    }

    private static double Contrast(Color a, Color b)
    {
        double l1 = Luminance(a), l2 = Luminance(b);
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }
}
