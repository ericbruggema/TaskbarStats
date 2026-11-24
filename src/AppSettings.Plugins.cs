namespace TaskbarStats;

/// <summary>Keuzes van de gebruiker voor één plugin-waarde (sleutel "plugin/sleutel"); null = de standaard van de plugin.</summary>
public sealed class PluginItemCfg
{
    public bool? Show { get; set; }
    public DisplayStyle? Style { get; set; }
    public string? Color { get; set; }
}

// Plugins (optioneel): waarden die de plugin-host in plugins\*.json zet, getoond in het widget.
public sealed partial class AppSettings
{
    /// <summary>Het onderdeel "Plugins" in het widget (alle gekozen plugin-waarden samen op één plek in de volgorde).</summary>
    public bool ShowPlugins { get; set; } = true;
    /// <summary>Tegel "Plugins" in het dashboard (verschijnt alleen als er plugin-waarden zijn).</summary>
    public bool DashPlugins { get; set; } = true;
    public Dictionary<string, PluginItemCfg>? PluginItems { get; set; }

    /// <summary>Een ongewijzigde waarschuwing van een plugin (bijvoorbeeld "limiet bijna op") komt één keer; dit zegt na hoeveel minuten ze toch nog eens
    /// herhaald wordt zolang de toestand gelijk blijft. 0 = nooit herhalen (alleen melden als er iets verandert).</summary>
    public int PluginAlertRepeatMin { get; set; } = 0;
    /// <summary>Meld dat een limiet (of ander terugkerend onderdeel) bijna is teruggezet, zoveel minuten van tevoren; 0 = uit. Alleen als de waarde al op "waarschuwing" of hoger stond.</summary>
    public int PluginResetNotifyMin { get; set; } = 5;

    public bool PluginShown(PluginValue v)
        => PluginItems is not null && PluginItems.TryGetValue(v.Id, out var c) && c.Show is bool b ? b : v.WidgetHint;

    /// <summary>Standaard: een balk voor percentages (zoals CPU), anders tekst.</summary>
    public DisplayStyle PluginStyle(PluginValue v)
    {
        if (PluginItems is not null && PluginItems.TryGetValue(v.Id, out var c) && c.Style is DisplayStyle s) return s;
        return v.Unit == "%" && v.Percent is not null ? DisplayStyle.Bar : DisplayStyle.Digital;
    }

    public string? PluginColor(PluginValue v)
        => PluginItems is not null && PluginItems.TryGetValue(v.Id, out var c) && !string.IsNullOrWhiteSpace(c.Color) ? c.Color : null;

    public PluginItemCfg PluginCfg(string id)
    {
        PluginItems ??= new();
        if (!PluginItems.TryGetValue(id, out var c)) PluginItems[id] = c = new PluginItemCfg();
        return c;
    }

    /// <summary>Gebruikt het widget plugin-waarden? (Dan hoeft de sampler de bestanden niet te lezen als er geen zijn.)</summary>
    public bool AnyPluginShown(IEnumerable<PluginValue> values) => ShowPlugins && values.Any(PluginShown);
}
