namespace TaskbarStats;

/// <summary>Gedeelde hulpjes voor het tonen van plugin-waarden in dashboard en fullscreen (de app leest alleen; de plugin-host schrijft).</summary>
internal static class PluginView
{
    /// <summary>Plugins die een fout melden en geen waarden (meer) hebben: die verdienen een regel met de uitleg.</summary>
    public static List<PluginStatus> Problems(PluginData d)
        => d.Statuses.Where(s => s.Status == "error" && !d.Values.Any(v => v.Plugin == s.Plugin)).ToList();

    /// <summary>Is er iets te tonen? (waarden, of een plugin met een foutmelding)</summary>
    public static bool Any(PluginData d) => d.Values.Count > 0 || Problems(d).Count > 0;

    /// <summary>Tekst van de waarde; een vraagteken erachter als de gegevens verouderd zijn.</summary>
    public static string Text(PluginValue v) => v.Stale ? v.Text + " ?" : v.Text;

    /// <summary>
    /// Kleur van een waarde: verouderd = vervaagd, anders eigen keuze, dan niveau (waarschuwing/kritiek), dan de tip van de plugin,
    /// anders <paramref name="fallback"/>.
    /// </summary>
    public static Color ColorOf(AppSettings cfg, PluginValue v, Color fallback, Color text)
    {
        if (v.Stale) return Color.FromArgb(120, text);
        var own = cfg.PluginColor(v);
        if (own is not null) return Parse(own, fallback);
        return v.Level switch
        {
            "crit" => Parse(cfg.CritColor, Color.Red),
            "warn" => Parse(cfg.WarnColor, Color.Orange),
            _ => v.Color.Length > 0 ? Parse(v.Color, fallback) : fallback,
        };
    }

    private static Color Parse(string hex, Color fb)
    {
        try { return ColorTranslator.FromHtml(hex); }
        catch (Exception ex) when (ex is ArgumentException or FormatException) { return fb; }
    }

    /// <summary>"Bijgewerkt 3 min geleden" voor de detailpagina.</summary>
    public static string Age(DateTime updatedUtc)
    {
        var s = (DateTime.UtcNow - updatedUtc).TotalSeconds;
        if (s < 90) return Loc.T("just now");
        if (s < 5400) return Loc.T("{0} min ago", (int)Math.Round(s / 60));
        return Loc.T("{0} h ago", (int)Math.Round(s / 3600));
    }
}
