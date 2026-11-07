using System.Globalization;
using System.Text.Json;

namespace TaskbarStats.Plugins;

/// <summary>
/// Het weer nu (zon, wolken, regen, temperatuur, luchtvochtigheid, wind, regenkans) via Open-Meteo: gratis, zonder sleutel of account.
/// De plaats komt uit de Windows-locatieservice (GPS/wifi), het IP-adres, een plaatsnaam of vaste coördinaten (zie <see cref="Geo"/>).
/// Open-Meteo krijgt alleen de coördinaten te zien.
/// </summary>
internal sealed class WeatherPlugin : IPlugin
{
    public string Id => "weather";
    public string Title => "Weather";
    public int DefaultIntervalSec => 900;

    public static string ForecastUrl = "https://api.open-meteo.com/v1/forecast";

    public async Task<PluginResult> RunAsync(PluginContext ctx, CancellationToken ct)
    {
        var cfg = ctx.Config;
        string lang = cfg.Str("language", "auto");
        if (lang.Equals("auto", StringComparison.OrdinalIgnoreCase)) lang = Geo.SystemLanguage();
        bool imperial = cfg.Str("units", "metric").Equals("imperial", StringComparison.OrdinalIgnoreCase);

        var (fix, note) = await Geo.ResolveAsync(cfg.Str("location", "auto"), ctx.Http, lang, true,
            cfg.Int("location_cache", 30, 1, 1440), cfg.Int("accuracy", 1000, 1, 5000), ctx.Now, ct).ConfigureAwait(false);
        if (fix is null) return PluginResult.Fail(note);

        string url = ForecastUrl
            + "?latitude=" + fix.Lat.ToString("F4", CultureInfo.InvariantCulture)
            + "&longitude=" + fix.Lon.ToString("F4", CultureInfo.InvariantCulture)
            + "&current=temperature_2m,relative_humidity_2m,apparent_temperature,precipitation,weather_code,wind_speed_10m,wind_direction_10m,is_day"
            + "&hourly=precipitation_probability&daily=temperature_2m_max,temperature_2m_min&forecast_days=2&timezone=auto"
            + (imperial ? "&temperature_unit=fahrenheit&wind_speed_unit=mph&precipitation_unit=inch" : "");
        string body;
        try { body = await ctx.Http.GetStringAsync(url, ct).ConfigureAwait(false); }
        catch (HttpRequestException ex) { return PluginResult.Fail("Weather service not reachable: " + ex.Message); }

        var res = Parse(body, fix, lang, imperial);
        if (note != "" && !res.Error) res.Message = note;
        return res;
    }

    internal static PluginResult Parse(string json, GeoFix fix, string lang, bool imperial)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var cur = root.GetProperty("current");
            double temp = cur.GetProperty("temperature_2m").GetDouble();
            double hum = Num(cur, "relative_humidity_2m");
            double feels = Num(cur, "apparent_temperature", temp);
            double precip = Num(cur, "precipitation");
            int code = (int)Num(cur, "weather_code");
            double wind = Num(cur, "wind_speed_10m");
            double dir = Num(cur, "wind_direction_10m");
            bool day = Num(cur, "is_day", 1) >= 1;
            string curTime = cur.TryGetProperty("time", out var ct0) ? ct0.GetString() ?? "" : "";

            // regenkans van het huidige uur
            double rain = 0;
            if (root.TryGetProperty("hourly", out var hr) && hr.TryGetProperty("time", out var times) && hr.TryGetProperty("precipitation_probability", out var probs))
            {
                string hourKey = curTime.Length >= 13 ? curTime[..13] : "";
                int i = 0;
                foreach (var t in times.EnumerateArray())
                {
                    if ((t.GetString() ?? "").StartsWith(hourKey, StringComparison.Ordinal) && i < probs.GetArrayLength())
                    {
                        var pv = probs[i];
                        if (pv.ValueKind == JsonValueKind.Number) rain = pv.GetDouble();
                        break;
                    }
                    i++;
                }
            }
            double? tmax = null, tmin = null;
            if (root.TryGetProperty("daily", out var d))
            {
                if (d.TryGetProperty("temperature_2m_max", out var mx) && mx.GetArrayLength() > 0 && mx[0].ValueKind == JsonValueKind.Number) tmax = mx[0].GetDouble();
                if (d.TryGetProperty("temperature_2m_min", out var mn) && mn.GetArrayLength() > 0 && mn[0].ValueKind == JsonValueKind.Number) tmin = mn[0].GetDouble();
            }

            string deg = imperial ? "°F" : "°C", windUnit = imperial ? "mph" : "km/h", rainUnit = imperial ? "in" : "mm";
            string desc = Describe(code, lang), glyph = Glyph(code, day), compass = Compass(dir, lang);
            string place = fix.Place is null ? "" : fix.Country is null ? fix.Place : $"{fix.Place}, {fix.CountryCode ?? fix.Country}";

            var res = new PluginResult();
            string detail = (place != "" ? place + " - " : "") + desc + $", {Label(lang, "feels")} {Fmt.Num(feels)}°"
                + (tmax is double hi && tmin is double lo ? $", {Fmt.Num(lo)}°..{Fmt.Num(hi)}°" : "");
            res.Items.Add(new PluginItem
            {
                Key = "temp", Label = Label(lang, "weather"), Short = "", Value = temp, Min = imperial ? 0 : -20, Max = imperial ? 100 : 40, Unit = deg,
                Text = $"{glyph} {Fmt.Num(temp)}°", Detail = detail, Widget = true,
            });
            res.Items.Add(new PluginItem
            {
                Key = "humidity", Label = Label(lang, "humidity"), Short = Label(lang, "humidity-short"), Value = hum, Unit = "%",
                Text = Fmt.Num(hum) + "%", Detail = $"{Label(lang, "humidity")} {Fmt.Num(hum)}%", Widget = false,
            });
            res.Items.Add(new PluginItem
            {
                Key = "rain", Label = Label(lang, "rain"), Short = Label(lang, "rain-short"), Value = rain, Unit = "%",
                Text = Fmt.Num(rain) + "%", Detail = $"{Label(lang, "rain")} {Fmt.Num(rain)}%, {Fmt.Num(precip, imperial ? 2 : 1)} {rainUnit}", Widget = false,
            });
            res.Items.Add(new PluginItem
            {
                Key = "wind", Label = Label(lang, "wind"), Short = Label(lang, "wind-short"), Value = wind, Min = 0, Max = imperial ? 60 : 100, Unit = windUnit,
                Text = $"{Fmt.Num(wind)} {windUnit} {compass}", Detail = $"{Label(lang, "wind")} {Fmt.Num(wind)} {windUnit} {compass}", Widget = false,
            });
            res.Items.Add(new PluginItem
            {
                Key = "feels", Label = Label(lang, "feels-label"), Short = Label(lang, "feels-short"), Value = feels, Min = imperial ? 0 : -20, Max = imperial ? 100 : 40, Unit = deg,
                Text = Fmt.Num(feels) + "°", Detail = $"{Label(lang, "feels")} {Fmt.Num(feels)}{deg}", Widget = false,
            });
            return res;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return PluginResult.Fail("Unexpected reply from the weather service.");
        }
    }

    private static double Num(JsonElement e, string name, double def = 0)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && double.IsFinite(v.GetDouble()) ? v.GetDouble() : def;

    // ---------- teksten ----------
    internal static string Glyph(int code, bool day) => code switch
    {
        0 => day ? "☀" : "☾",
        1 or 2 => day ? "⛅" : "☁",
        3 => "☁",
        45 or 48 => "≋",
        >= 51 and <= 67 => "☂",
        >= 71 and <= 77 => "❄",
        >= 80 and <= 82 => "☂",
        85 or 86 => "❄",
        >= 95 => "⚡",
        _ => "☁",
    };

    private static readonly Dictionary<int, string[]> Codes = new()
    {
        [0] = new[] { "Clear sky", "Onbewolkt", "Klarer Himmel" },
        [1] = new[] { "Mostly clear", "Overwegend helder", "Überwiegend klar" },
        [2] = new[] { "Partly cloudy", "Half bewolkt", "Teilweise bewölkt" },
        [3] = new[] { "Overcast", "Bewolkt", "Bedeckt" },
        [45] = new[] { "Fog", "Mist", "Nebel" },
        [48] = new[] { "Rime fog", "Rijpmist", "Reifnebel" },
        [51] = new[] { "Light drizzle", "Lichte motregen", "Leichter Nieselregen" },
        [53] = new[] { "Drizzle", "Motregen", "Nieselregen" },
        [55] = new[] { "Heavy drizzle", "Zware motregen", "Starker Nieselregen" },
        [56] = new[] { "Freezing drizzle", "Ijzel-motregen", "Gefrierender Nieselregen" },
        [57] = new[] { "Heavy freezing drizzle", "Zware ijzel-motregen", "Starker gefrierender Nieselregen" },
        [61] = new[] { "Light rain", "Lichte regen", "Leichter Regen" },
        [63] = new[] { "Rain", "Regen", "Regen" },
        [65] = new[] { "Heavy rain", "Zware regen", "Starker Regen" },
        [66] = new[] { "Freezing rain", "Ijzelregen", "Gefrierender Regen" },
        [67] = new[] { "Heavy freezing rain", "Zware ijzelregen", "Starker gefrierender Regen" },
        [71] = new[] { "Light snow", "Lichte sneeuw", "Leichter Schneefall" },
        [73] = new[] { "Snow", "Sneeuw", "Schneefall" },
        [75] = new[] { "Heavy snow", "Zware sneeuw", "Starker Schneefall" },
        [77] = new[] { "Snow grains", "Korrelsneeuw", "Schneegriesel" },
        [80] = new[] { "Light showers", "Lichte buien", "Leichte Schauer" },
        [81] = new[] { "Showers", "Buien", "Schauer" },
        [82] = new[] { "Heavy showers", "Zware buien", "Starke Schauer" },
        [85] = new[] { "Snow showers", "Sneeuwbuien", "Schneeschauer" },
        [86] = new[] { "Heavy snow showers", "Zware sneeuwbuien", "Starke Schneeschauer" },
        [95] = new[] { "Thunderstorm", "Onweer", "Gewitter" },
        [96] = new[] { "Thunderstorm with hail", "Onweer met hagel", "Gewitter mit Hagel" },
        [99] = new[] { "Heavy thunderstorm with hail", "Zwaar onweer met hagel", "Schweres Gewitter mit Hagel" },
    };

    private static int LangIndex(string lang) => lang switch { "nl" => 1, "de" => 2, _ => 0 };

    internal static string Describe(int code, string lang)
        => Codes.TryGetValue(code, out var t) ? t[LangIndex(lang)] : "?";

    internal static string Compass(double deg, string lang)
    {
        string[] en = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        string[] nl = { "N", "NO", "O", "ZO", "Z", "ZW", "W", "NW" };
        string[] de = { "N", "NO", "O", "SO", "S", "SW", "W", "NW" };
        int i = (int)Math.Round(((deg % 360) + 360) % 360 / 45.0) % 8;
        return (lang == "nl" ? nl : lang == "de" ? de : en)[i];
    }

    private static string Label(string lang, string what)
    {
        int i = LangIndex(lang);
        return what switch
        {
            "weather" => new[] { "Weather", "Weer", "Wetter" }[i],
            "humidity" => new[] { "Humidity", "Luchtvochtigheid", "Luftfeuchtigkeit" }[i],
            "humidity-short" => new[] { "Hum", "Vocht", "Feucht" }[i],
            "rain" => new[] { "Chance of rain", "Kans op regen", "Regenwahrscheinlichkeit" }[i],
            "rain-short" => new[] { "Rain", "Regen", "Regen" }[i],
            "wind" => new[] { "Wind", "Wind", "Wind" }[i],
            "wind-short" => new[] { "Wind", "Wind", "Wind" }[i],
            "feels" => new[] { "feels like", "voelt als", "gefühlt" }[i],
            "feels-label" => new[] { "Feels like", "Voelt als", "Gefühlt" }[i],
            "feels-short" => new[] { "Feels", "Voelt", "Gefühlt" }[i],
            _ => what,
        };
    }
}
