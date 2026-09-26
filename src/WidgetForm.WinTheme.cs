using Microsoft.Win32;

namespace TaskbarStats;

// Het widget volgt (optioneel) het Windows-thema: donker/licht van de taakbalk en de accentkleur.
// De eigen kleurinstellingen van de gebruiker blijven onaangetast; alleen het getekende effect verandert.
public sealed partial class WidgetForm
{
    // De themawaarden (donker/licht, accent) staan gecachet in de renderer, zodat het tekenen nooit het register hoeft te lezen.
    private Color EffectiveBackground() => _renderer.EffectiveBackground();

    private System.Windows.Forms.Timer? _winTimer;
    private bool _winHooked;

    private bool FollowTheme => _cfg.WidgetFollowWindows != WinThemeMode.Off;
    private bool FollowAccent => _cfg.WidgetFollowWindows == WinThemeMode.DarkLightAccent;

    /// <summary>Leest het Windows-thema uit het register (alleen lezen). TASKBARSTATS_FAKE_THEME=dark|light|accent overschrijft dat voor tests.</summary>
    private void ReadWindowsTheme()
    {
        bool light = false, prev = false;
        Color accent = _renderer.WinAccent;
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
            catch (Exception dex) { Diag.Swallow(dex); }
            try
            {
                using var dk = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
                if (dk?.GetValue("AccentColor") is int ac)          // 0xAABBGGRR
                    accent = Color.FromArgb(ac & 0xFF, (ac >> 8) & 0xFF, (ac >> 16) & 0xFF);
                else if (dk?.GetValue("ColorizationColor") is int cc)   // 0xAARRGGBB
                    accent = Color.FromArgb((cc >> 16) & 0xFF, (cc >> 8) & 0xFF, cc & 0xFF);
            }
            catch (Exception dex) { Diag.Swallow(dex); }
        }
        _renderer.WinLight = light; _renderer.WinPrevalence = prev; _renderer.WinAccent = accent;
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
        catch (Exception dex) { Diag.Swallow(dex); }
    }
}
