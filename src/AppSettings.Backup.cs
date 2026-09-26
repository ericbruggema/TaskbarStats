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
    public string ToJson()
    {
        if (!SeasonalActive) return JsonSerializer.Serialize(this, JsonOpts);
        // feestthema actief: bewaar het eigen uiterlijk (en alle andere wijzigingen van vandaag)
        var copy = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, JsonOpts), JsonOpts)!;
        RestoreOriginalInto(copy);
        return JsonSerializer.Serialize(copy, JsonOpts);
    }

    /// <summary>De instellingen voor een exportbestand: als <see cref="ToJson"/>, met bovenaan een vermelding van het programma (<c>_generator</c>; bij importeren genegeerd).</summary>
    public string ToExportJson()
    {
        var old = System.Text.Json.Nodes.JsonNode.Parse(ToJson())!.AsObject();
        var node = new System.Text.Json.Nodes.JsonObject { ["_generator"] = AppInfo.Signature };
        foreach (var kv in old) node[kv.Key] = kv.Value?.DeepClone();
        return node.ToJsonString(JsonOpts);
    }

    /// <summary>
    /// Neemt alle instellingen van <paramref name="other"/> over in dit object (het object zelf blijft bestaan, want widget,
    /// dashboard en fullscreen houden er een verwijzing naar). <see cref="FilePath"/> blijft ongewijzigd.
    /// </summary>
    public void CopyFrom(AppSettings other)
    {
        ForgetSeasonal();
        foreach (var p in typeof(AppSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.CanRead || !p.CanWrite || p.SetMethod is not { IsPublic: true }) continue;
            if (p.GetCustomAttribute<JsonIgnoreAttribute>() is not null) continue;
            if (p.GetIndexParameters().Length > 0) continue;
            p.SetValue(this, p.GetValue(other));
        }
    }
}
