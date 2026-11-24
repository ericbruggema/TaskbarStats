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

    private PluginAlertMemory? _pluginAlerts;

    /// <summary>Plugin-meldingen volgens <see cref="PluginAlertRules"/>: één keer per toestand (ook na een herstart onthouden), en kort voor een limiet die vrijkomt.</summary>
    private void PluginAlerts()
    {
        if (!_cfg.ShowPlugins) return;
        _pluginAlerts ??= new PluginAlertMemory();
        foreach (var a in PluginAlertRules.Evaluate(_snap.Plugins.Values, _cfg, _pluginAlerts, DateTime.UtcNow))
            ShowBalloon(a.Title, a.Text, a.Warning ? ToolTipIcon.Warning : ToolTipIcon.Info);
    }

    internal static string AlertText(PluginValue v) => PluginAlertRules.AlertText(v);
}
