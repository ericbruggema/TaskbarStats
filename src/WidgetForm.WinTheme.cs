using Microsoft.Win32;

namespace TaskbarStats;

// Het widget volgt (optioneel) het Windows-thema: donker/licht van de taakbalk en de accentkleur.
// De eigen kleurinstellingen van de gebruiker blijven onaangetast; alleen het getekende effect verandert.
public sealed partial class WidgetForm
{
    private static readonly Color WinDark = Color.FromArgb(0x1F, 0x1F, 0x1F);
    private static readonly Color WinLight = Color.FromArgb(0xF3, 0xF3, 0xF3);

    // Gecachet, zodat het tekenen nooit het register hoeft te lezen.
    private bool _winLight;                 // taakbalk is licht
    private bool _winPrevalence;            // accentkleur op de taakbalk (ColorPrevalence)
    private Color _winAccent = Color.FromArgb(0x00, 0x78, 0xD4);
    private System.Windows.Forms.Timer? _winTimer;
    private bool _winHooked;

    private bool FollowTheme => _cfg.WidgetFollowWindows != WinThemeMode.Off;
    private bool FollowAccent => _cfg.WidgetFollowWindows == WinThemeMode.DarkLightAccent;

    /// <summary>Achtergrond van het widget (niet-transparant): eigen kleur of die van het Windows-thema.</summary>
    private Color EffectiveBackground()
    {
        if (!FollowTheme) return C(_cfg.BackgroundColor, Color.FromArgb(20, 20, 20));
        var baseCol = _winLight ? WinLight : WinDark;
        return FollowAccent && _winPrevalence ? Mix(baseCol, _winAccent, 0.30) : baseCol;
    }

    /// <summary>Tekstkleur: eigen kleur, of wit/zwart met goed contrast op de effectieve achtergrond.</summary>
    private Color EffectiveText()
    {
        if (!FollowTheme) return C(_cfg.TextColor, Color.White);
        return Luminance(EffectiveBackground()) > 0.4 ? Color.Black : Color.White;
    }

    /// <summary>Kleur van meters/balken: eigen kleur, of de accentkleur van Windows (bijgesteld voor contrast).</summary>
    private Color EffectiveAccent()
    {
        if (!FollowAccent) return C(_cfg.AccentColor, Color.DodgerBlue);
        var bg = EffectiveBackground();
        var col = _winAccent;
        bool lightBg = Luminance(bg) > 0.4;
        for (int i = 0; i < 10 && Contrast(col, bg) < 3.0; i++) col = Mix(col, lightBg ? Color.Black : Color.White, 0.15);
        return col;
    }

    /// <summary>Grijze sporen (achter meters/balken): op een lichte achtergrond omgekeerd, anders ongewijzigd.</summary>
    private Color EffectiveTrack(Color dark)
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

    /// <summary>Leest het Windows-thema uit het register (alleen lezen). TASKBARSTATS_FAKE_THEME=dark|light|accent overschrijft dat voor tests.</summary>
    private void ReadWindowsTheme()
    {
        bool light = false, prev = false;
        Color accent = _winAccent;
        var fake = Environment.GetEnvironmentVariable("TASKBARSTATS_FAKE_THEME");
        if (!string.IsNullOrEmpty(fake))
        {
            light = fake.Equals("light", StringComparison.OrdinalIgnoreCase);
            prev = fake.Equals("accent", StringComparison.OrdinalIgnoreCase);
            var fa = Environment.GetEnvironmentVariable("TASKBARSTATS_FAKE_ACCENT");
            accent = C(string.IsNullOrEmpty(fa) ? "#C2185B" : fa, accent);
        }
        else
        {
            try
            {
                using var pk = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                // De taakbalk volgt SystemUsesLightTheme (AppsUseLightTheme alleen als terugval).
                light = (pk?.GetValue("SystemUsesLightTheme") ?? pk?.GetValue("AppsUseLightTheme")) is int l && l != 0;
                prev = pk?.GetValue("ColorPrevalence") is int p && p != 0;
            }
            catch { }
            try
            {
                using var dk = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
                if (dk?.GetValue("AccentColor") is int ac)          // 0xAABBGGRR
                    accent = Color.FromArgb(ac & 0xFF, (ac >> 8) & 0xFF, (ac >> 16) & 0xFF);
                else if (dk?.GetValue("ColorizationColor") is int cc)   // 0xAARRGGBB
                    accent = Color.FromArgb((cc >> 16) & 0xFF, (cc >> 8) & 0xFF, cc & 0xFF);
            }
            catch { }
        }
        _winLight = light; _winPrevalence = prev; _winAccent = accent;
    }

    /// <summary>Aan te roepen als de instelling verandert: cache verversen en opnieuw tekenen.</summary>
    private void WinThemeRefresh()
    {
        if (FollowTheme) ReadWindowsTheme();
        BackColor = EffectiveBackground();
        Render();
    }

    private void WinThemeHook()
    {
        if (_winHooked) return;
        _winHooked = true;
        SystemEvents.UserPreferenceChanged += OnWinPrefChanged;
        if (FollowTheme) ReadWindowsTheme();
    }

    private void WinThemeUnhook()
    {
        if (!_winHooked) return;
        _winHooked = false;
        SystemEvents.UserPreferenceChanged -= OnWinPrefChanged;
        _winTimer?.Stop(); _winTimer?.Dispose(); _winTimer = null;
    }

    // Komt binnen op de SystemEvents-thread; het register loopt soms een moment achter, dus even wachten en dan op de UI-thread verversen.
    private void OnWinPrefChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (!FollowTheme) return;
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)) return;
        try
        {
            if (!IsHandleCreated || IsDisposed) return;
            BeginInvoke(() =>
            {
                if (_winTimer is null)
                {
                    _winTimer = new System.Windows.Forms.Timer { Interval = 400 };
                    _winTimer.Tick += (_, _) => { _winTimer!.Stop(); WinThemeRefresh(); };
                }
                _winTimer.Stop(); _winTimer.Start();
            });
        }
        catch { }
    }
}
