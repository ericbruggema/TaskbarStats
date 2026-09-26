using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskbarStats;

// Instellingen exporteren, importeren en terugzetten.
public sealed partial class AppSettings
{
    /// <summary>Leest instellingen uit JSON-tekst; null als het geen geldig instellingenbestand is.</summary>
    public static AppSettings? TryParse(string json)
    {
        try
        {
            var s = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts);
            return s;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>De instellingen als JSON-tekst (zoals in settings.json).</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOpts);

    /// <summary>
    /// Neemt alle instellingen van <paramref name="other"/> over in dit object (het object zelf blijft bestaan, want widget,
    /// dashboard en fullscreen houden er een verwijzing naar). <see cref="FilePath"/> blijft ongewijzigd.
    /// </summary>
    public void CopyFrom(AppSettings other)
    {
        foreach (var p in typeof(AppSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.CanRead || !p.CanWrite || p.SetMethod is not { IsPublic: true }) continue;
            if (p.GetCustomAttribute<JsonIgnoreAttribute>() is not null) continue;
            if (p.GetIndexParameters().Length > 0) continue;
            p.SetValue(this, p.GetValue(other));
        }
    }
}
