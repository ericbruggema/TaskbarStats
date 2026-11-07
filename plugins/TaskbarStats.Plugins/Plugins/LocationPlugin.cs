using System.Globalization;

namespace TaskbarStats.Plugins;

/// <summary>
/// Waar ben ik: plaatsnaam, coördinaten, nauwkeurigheid en bron (GPS, wifi, IP-adres), snelheid als de ontvanger die geeft, en
/// optioneel de afstand tot een thuisadres. Leest de Windows-locatieservice (Instellingen > Privacy en beveiliging > Locatie).
/// Het widget toont standaard alleen de plaatsnaam; coördinaten staan in de tooltip.
/// </summary>
internal sealed class LocationPlugin : IPlugin
{
    public string Id => "location";
    public string Title => "Location (GPS)";
    public int DefaultIntervalSec => 300;

    public async Task<PluginResult> RunAsync(PluginContext ctx, CancellationToken ct)
    {
        var cfg = ctx.Config;
        string lang = cfg.Str("language", "auto");
        if (lang.Equals("auto", StringComparison.OrdinalIgnoreCase)) lang = Geo.SystemLanguage();
        int interval = cfg.Int("interval", DefaultIntervalSec, 10, 86400);

        var (fix, note) = await Geo.ResolveAsync(cfg.Str("source", "auto"), ctx.Http, lang, cfg.Bool("place_name", true),
            Math.Max(1, interval / 60 - 1), cfg.Int("accuracy", 100, 1, 5000), ctx.Now, ct).ConfigureAwait(false);
        if (fix is null) return PluginResult.Fail(note);
        return Build(fix, note, cfg);
    }

    internal static PluginResult Build(GeoFix fix, string note, IniFile cfg)
    {
        int decimals = cfg.Int("precision", 3, 0, 6);
        var ci = CultureInfo.InvariantCulture;
        string coords = fix.Lat.ToString("F" + decimals, ci) + ", " + fix.Lon.ToString("F" + decimals, ci);
        string acc = fix.AccuracyM is double a ? (a >= 1000 ? $"{a / 1000:0.#} km" : $"{a:0} m") : "";
        string where = fix.Place is null ? coords : fix.Country is null ? fix.Place : $"{fix.Place}, {fix.CountryCode ?? fix.Country}";

        var res = new PluginResult { Message = note };
        res.Items.Add(new PluginItem
        {
            Key = "place", Label = "Location", Short = "", Text = fix.Place ?? coords, Widget = true,
            Detail = $"{where}  ·  {fix.Source}" + (acc != "" ? $"  ·  ±{acc}" : ""),
        });
        res.Items.Add(new PluginItem
        {
            Key = "coords", Label = "Coordinates", Short = "GPS", Text = coords, Widget = false,
            Detail = $"{coords}  ·  {fix.Source}" + (acc != "" ? $"  ·  ±{acc}" : ""),
        });
        if (fix.SpeedMps is double sp && sp >= 0)
            res.Items.Add(new PluginItem
            {
                Key = "speed", Label = "Speed", Short = "", Value = sp * 3.6, Min = 0, Max = 130, Unit = "km/h",
                Text = $"{sp * 3.6:0} km/h", Detail = $"Speed {sp * 3.6:0} km/h", Widget = false,
            });

        // afstand tot thuis (optioneel, 'home = lat, lon')
        if (Geo.TryParseCoords(cfg.Str("home", ""), out var hl, out var ho))
        {
            double km = Geo.DistanceKm(fix.Lat, fix.Lon, hl, ho);
            res.Items.Add(new PluginItem
            {
                Key = "home", Label = "Distance from home", Short = "Home", Value = km, Min = 0, Max = 100, Unit = "km",
                Text = km < 1 ? $"{km * 1000:0} m" : $"{km:0.#} km", Detail = $"{km:0.#} km from home", Widget = false,
            });
        }
        return res;
    }
}
