using System.Drawing.Drawing2D;

namespace TaskbarStats;

// Widget: de waarden van de (optionele) plugins. De plugin-host schrijft ze naar plugins\*.json, de app leest ze via PluginFeed.
internal sealed partial class WidgetRenderer
{
    /// <summary>Tekent alle gekozen plugin-waarden achter elkaar en geeft de breedte terug (0 = niets te tekenen).</summary>
    private int DrawPlugins(Graphics g, int x)
    {
        if (!_cfg.ShowPlugins) return 0;
        int start = x, gap = Gap;
        bool any = false;
        foreach (var v in _snap.Plugins.Values)
        {
            if (!_cfg.PluginShown(v)) continue;
            x += DrawPluginItem(g, x, v) + gap;
            any = true;
        }
        return any ? x - start - gap : 0;
    }

    /// <summary>Kleur van een plugin-waarde: verouderd = vervaagd, anders eigen keuze, dan niveau (waarschuwing/kritiek), dan de tip van de plugin.</summary>
    private Color PluginColor(PluginValue v, Color fallback)
    {
        if (v.Stale) return Color.FromArgb(120, EffectiveText());
        var own = _cfg.PluginColor(v);
        if (own is not null) return C(own, fallback);
        return v.Level switch
        {
            "crit" => C(_cfg.CritColor, Color.Red),
            "warn" => C(_cfg.WarnColor, Color.Orange),
            _ => v.Color.Length > 0 ? C(v.Color, fallback) : fallback,
        };
    }

    private int DrawPluginItem(Graphics g, int x, PluginValue v)
    {
        var style = _cfg.PluginStyle(v);
        string label = v.WidgetLabel;
        if (v.Percent is double pct && style is DisplayStyle.Bar or DisplayStyle.Gauge)
        {
            Color acc = PluginColor(v, EffectiveAccent());
            return style == DisplayStyle.Gauge ? DrawGauge(g, x, label, pct, null, acc) : DrawBar(g, x, label, pct, null, acc);
        }
        string txt = v.Stale ? v.Text + " ?" : v.Text;
        Color col = PluginColor(v, EffectiveText());
        return label.Length == 0 ? DrawDigital(g, x, txt, txt, col) : DrawTextCell(g, x, label, txt, txt, col);
    }
}
