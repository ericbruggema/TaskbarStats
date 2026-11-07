using System.Text;

namespace TaskbarStats;

/// <summary>Widget: tooltipregels en meldingen voor de waarden van de (optionele) plugins.</summary>
public sealed partial class WidgetForm
{
    private void AppendPluginTooltip(StringBuilder sb)
    {
        if (!_cfg.ShowPlugins) return;
        var data = _snap.Plugins;
        bool header = false;
        void head() { if (!header) { header = true; sb.AppendLine(); } }

        foreach (var st in data.Statuses.Where(s => s.Status == "error"))
        {
            head();
            sb.AppendLine($"{st.Title}  ⚠ {(st.Message.Length > 0 ? st.Message : Loc.T("not available"))}");
        }
        foreach (var v in data.Values.Where(_cfg.PluginShown))
        {
            head();
            string text = v.Text + (v.Stale ? "  " + Loc.T("(outdated)") : "");
            string detail = v.Detail.Trim();
            if (detail.Length == 0 || detail == v.Text) sb.AppendLine($"{v.Label}  {text}");
            else if (!detail.Contains('\n')) sb.AppendLine($"{v.Label}  {text}  ·  {detail}");
            else
            {
                sb.AppendLine($"{v.Label}  {text}");
                foreach (var line in detail.Replace("\r", "").Split('\n')) sb.AppendLine($"   {line}");
            }
        }
    }

    /// <summary>Een melding als een onderdeel van een plugin op "kritiek" springt en de plugin dat wil (bijvoorbeeld: een site is onbereikbaar,
    /// of de Claude-limiet is bijna op). Hooguit eens per half uur per onderdeel.</summary>
    private void PluginAlerts()
    {
        if (!_cfg.ShowPlugins) return;
        foreach (var v in _snap.Plugins.Values)
        {
            if (!v.Notify || v.Level != "crit" || v.Stale || !_cfg.PluginShown(v)) continue;
            Alert("plugin:" + v.Id, 30 * 60_000L, v.Label, AlertText(v));
        }
    }

    internal static string AlertText(PluginValue v)
    {
        var lines = v.Detail.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var bad = lines.Where(l => l.StartsWith("✕")).ToList();
        if (bad.Count > 0) return string.Join("\n", bad);
        return lines.Length > 0 ? lines[0] : v.Text;
    }
}
