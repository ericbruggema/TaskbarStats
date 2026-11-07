using System.Globalization;
using System.Text.Json;
using Windows.Devices.Geolocation;

namespace TaskbarStats.Plugins;

/// <summary>Een plaatsbepaling: waar ben ik, hoe nauwkeurig en waaruit blijkt dat (GPS, wifi, IP-adres, opgegeven).</summary>
internal sealed record GeoFix(double Lat, double Lon, double? AccuracyM, string Source, string? Place, string? Country, string? CountryCode, double? SpeedMps);

/// <summary>
/// Gedeelde plaatsbepaling voor de plugins weer en locatie: de Windows-locatieservice (GPS/wifi), het IP-adres, een plaatsnaam of
/// vaste coördinaten. Wat niet kan (geen toestemming, geen GPS) valt terug op het IP-adres. Resultaten worden even onthouden.
/// </summary>
internal static class Geo
{
    // Adressen zijn aanpasbaar zodat tests geen internet nodig hebben.
    public static string IpUrl = "https://ipwho.is/";
    public static string SearchUrl = "https://geocoding-api.open-meteo.com/v1/search";
    public static string ReverseUrl = "https://api.bigdatacloud.net/data/reverse-geocode-client";

    private static readonly Dictionary<string, (DateTimeOffset At, GeoFix Fix)> Cache = new();
    private static readonly object L = new();

    public static void ClearCache() { lock (L) Cache.Clear(); }

    /// <summary>"nl", "en", "de": de taal van Windows, voor plaatsnamen en weerbeschrijvingen.</summary>
    public static string SystemLanguage()
    {
        try
        {
            var l = Windows.System.UserProfile.GlobalizationPreferences.Languages.FirstOrDefault() ?? "en";
            var two = l.Split('-')[0].ToLowerInvariant();
            return two.Length == 2 ? two : "en";
        }
        catch (Exception) { return "en"; }
    }

    /// <param name="mode">auto | gps | ip | "lat,lon" | plaatsnaam</param>
    /// <param name="cacheMinutes">hoe lang een eerdere uitkomst geldig blijft</param>
    public static async Task<(GeoFix? Fix, string Note)> ResolveAsync(string mode, HttpClient http, string lang, bool reverse, int cacheMinutes, int accuracyM, Func<DateTimeOffset> now, CancellationToken ct)
    {
        mode = (mode ?? "auto").Trim();
        string key = mode.ToLowerInvariant() + "|" + lang + "|" + reverse;
        lock (L)
            if (Cache.TryGetValue(key, out var c) && now() - c.At < TimeSpan.FromMinutes(cacheMinutes)) return (c.Fix, "");

        GeoFix? fix = null;
        string note = "";
        string m = mode.ToLowerInvariant();
        if (m is "auto" or "gps" or "windows")
        {
            (fix, note) = await TryWindowsAsync(accuracyM, ct).ConfigureAwait(false);
            if (fix is null && m != "gps")   // auto: terugvallen op het IP-adres
            {
                var ipFix = await TryIpAsync(http, ct).ConfigureAwait(false);
                if (ipFix is not null) { fix = ipFix; note = note == "" ? "" : note + " Using the IP address instead."; }
            }
            else if (fix is null) note = note == "" ? "No GPS/Wi-Fi location available." : note;
        }
        else if (m == "ip") fix = await TryIpAsync(http, ct).ConfigureAwait(false);
        else if (TryParseCoords(mode, out var la, out var lo)) fix = new GeoFix(la, lo, null, "fixed", null, null, null, null);
        else fix = await SearchAsync(mode, http, lang, ct).ConfigureAwait(false);

        if (fix is null) return (null, note == "" ? $"Could not determine the location for '{mode}'." : note);
        if (reverse && fix.Place is null)
        {
            var named = await ReverseAsync(fix, http, lang, ct).ConfigureAwait(false);
            if (named is not null) fix = named;
        }
        lock (L) Cache[key] = (now(), fix);
        return (fix, note);
    }

    internal static bool TryParseCoords(string s, out double lat, out double lon)
    {
        lat = lon = 0;
        var parts = s.Split(new[] { ',', ';' }, StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return false;
        return double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out lat)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out lon)
            && Math.Abs(lat) <= 90 && Math.Abs(lon) <= 180;
    }

    // ---------- Windows-locatieservice ----------
    private static async Task<(GeoFix?, string)> TryWindowsAsync(int accuracyM, CancellationToken ct)
    {
        try
        {
            var gl = new Geolocator { DesiredAccuracyInMeters = (uint)Math.Clamp(accuracyM, 1, 5000) };
            var op = gl.GetGeopositionAsync(TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(20));
            using var reg = ct.Register(() => op.Cancel());
            var pos = await op;
            var c = pos.Coordinate;
            string src = c.PositionSource switch
            {
                PositionSource.Satellite => "GPS",
                PositionSource.WiFi => "Wi-Fi",
                PositionSource.Cellular => "Cellular",
                PositionSource.IPAddress => "IP address",
                _ => "Windows location",
            };
            double? speed = c.Speed is double sp && double.IsFinite(sp) ? sp : null;
            return (new GeoFix(c.Point.Position.Latitude, c.Point.Position.Longitude, c.Accuracy, src, null, null, null, speed), "");
        }
        catch (UnauthorizedAccessException)
        {
            return (null, "Windows location access is off: Settings > Privacy & security > Location (also 'Let desktop apps access your location').");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return (null, "Windows location unavailable (" + ex.GetType().Name + ")."); }
    }

    // ---------- IP-adres ----------
    private static async Task<GeoFix?> TryIpAsync(HttpClient http, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(await http.GetStringAsync(IpUrl, ct).ConfigureAwait(false));
            var r = doc.RootElement;
            if (r.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.False) return null;
            if (!r.TryGetProperty("latitude", out var la) || !r.TryGetProperty("longitude", out var lo)) return null;
            string? city = r.TryGetProperty("city", out var c) ? c.GetString() : null;
            string? country = r.TryGetProperty("country", out var co) ? co.GetString() : null;
            string? cc = r.TryGetProperty("country_code", out var ccp) ? ccp.GetString() : null;
            return new GeoFix(la.GetDouble(), lo.GetDouble(), 25_000, "IP address", string.IsNullOrWhiteSpace(city) ? null : city, country, cc, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException) { return null; }
    }

    // ---------- plaatsnaam -> coördinaten ----------
    private static async Task<GeoFix?> SearchAsync(string name, HttpClient http, string lang, CancellationToken ct)
    {
        try
        {
            string url = $"{SearchUrl}?name={Uri.EscapeDataString(name)}&count=1&language={lang}&format=json";
            using var doc = JsonDocument.Parse(await http.GetStringAsync(url, ct).ConfigureAwait(false));
            if (!doc.RootElement.TryGetProperty("results", out var rs) || rs.GetArrayLength() == 0) return null;
            var r = rs[0];
            return new GeoFix(r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble(), null, "place name",
                r.TryGetProperty("name", out var n) ? n.GetString() : name,
                r.TryGetProperty("country", out var c) ? c.GetString() : null,
                r.TryGetProperty("country_code", out var cc) ? cc.GetString() : null, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException) { return null; }
    }

    // ---------- coördinaten -> plaatsnaam ----------
    private static async Task<GeoFix?> ReverseAsync(GeoFix f, HttpClient http, string lang, CancellationToken ct)
    {
        try
        {
            string url = $"{ReverseUrl}?latitude={f.Lat.ToString(CultureInfo.InvariantCulture)}&longitude={f.Lon.ToString(CultureInfo.InvariantCulture)}&localityLanguage={lang}";
            using var doc = JsonDocument.Parse(await http.GetStringAsync(url, ct).ConfigureAwait(false));
            var r = doc.RootElement;
            string? city = Str(r, "city");
            if (string.IsNullOrWhiteSpace(city)) city = Str(r, "locality");
            if (string.IsNullOrWhiteSpace(city)) city = Str(r, "principalSubdivision");
            return f with { Place = string.IsNullOrWhiteSpace(city) ? null : city, Country = Str(r, "countryName"), CountryCode = Str(r, "countryCode") };
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException) { return null; }
    }

    private static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371.0;
        double dLat = (lat2 - lat1) * Math.PI / 180, dLon = (lon2 - lon1) * Math.PI / 180;
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                 + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * R * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }
}
