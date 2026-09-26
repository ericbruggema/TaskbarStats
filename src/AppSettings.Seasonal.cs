using System.Text.Json.Serialization;

namespace TaskbarStats;

/// <summary>Themas voor een bijzondere dag (nu: het Love-thema op 14 februari) - tijdelijk, alleen na het starten van het programma.</summary>
public static class Seasonal
{
    /// <summary>De datum van vandaag; <c>TASKBARSTATS_TODAY=2026-02-14</c> overschrijft die (voor tests).</summary>
    public static DateTime Today()
        => DateTime.TryParse(Environment.GetEnvironmentVariable("TASKBARSTATS_TODAY"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d) ? d.Date : DateTime.Today;

    /// <summary>Naam van het meegeleverde thema voor deze dag, of null.</summary>
    public static string? ThemeFor(DateTime day) => day is { Month: 2, Day: 14 } ? "Love" : null;
}

public sealed partial class AppSettings
{
    /// <summary>Op bijzondere dagen (14 februari) tijdelijk een feestelijk thema tonen; het eigen thema blijft in het bestand staan.</summary>
    public bool SeasonalThemes { get; set; } = true;

    // Het uiterlijk van de gebruiker zelf zolang een feestthema is toegepast; settings.json (ToJson) schrijft altijd dit.
    private ThemeData? _seasonalOriginal;
    private (string?, BgMode, int, string?, BgMode, int, string?, BgMode, int) _seasonalBg;

    [JsonIgnore] internal bool SeasonalActive => _seasonalOriginal is not null;

    /// <summary>Past <paramref name="t"/> tijdelijk toe (in het geheugen); false als er al een feestthema actief is.</summary>
    internal bool BeginSeasonal(ThemeData t)
    {
        if (_seasonalOriginal is not null) return false;
        _seasonalOriginal = ThemeData.Capture(this, "");
        _seasonalBg = (WidgetBgImage, WidgetBgMode, WidgetBgOpacity, DashBgImage, DashBgMode, DashBgOpacity, FullBgImage, FullBgMode, FullBgOpacity);
        t.ApplyTo(this);
        return true;
    }

    /// <summary>Zet het eigen uiterlijk terug; true als er iets is teruggezet (de aanroeper moet dan opnieuw toepassen).</summary>
    internal bool EndSeasonal()
    {
        if (_seasonalOriginal is null) return false;
        RestoreOriginalInto(this);
        _seasonalOriginal = null;
        return true;
    }

    /// <summary>Een ander thema of een import vervangt het uiterlijk voorgoed: het feestthema hoeft dan niet meer terug.</summary>
    internal void ForgetSeasonal() => _seasonalOriginal = null;

    private void RestoreOriginalInto(AppSettings c)
    {
        _seasonalOriginal!.ApplyTo(c);
        (c.WidgetBgImage, c.WidgetBgMode, c.WidgetBgOpacity, c.DashBgImage, c.DashBgMode, c.DashBgOpacity, c.FullBgImage, c.FullBgMode, c.FullBgOpacity) = _seasonalBg;
    }
}
