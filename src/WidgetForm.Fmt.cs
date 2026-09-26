using System.Text;

namespace TaskbarStats;

/// <summary>Widget: opdracht aan de sampler voor de extra onderdelen en hun tooltipregels (de waardeopmaak zelf zit in WidgetRenderer.Fmt.cs).</summary>
public sealed partial class WidgetForm
{
    private void ApplyExtraWanted()
    {
        _metrics.SetExtraWanted(_cfg.ShowDiskBusy, _cfg.ShowDiskTemp || _cfg.AlertDiskTempC > 0, _cfg.ShowMoboTemp);
        _metrics.Fps.Configure(_cfg.ShowFps);   // experimenteel: alleen een ETW-sessie zolang het aan staat
    }

    private void AppendExtraTooltip(StringBuilder sb)
    {
        if (_cfg.ShowDiskBusy && _snap.DiskBusyPercent is double busy) sb.AppendLine($"{Loc.T("Disk active")}  {busy:0}%");
        if (_cfg.ShowDiskTemp && _snap.DiskTempC is double dt) sb.AppendLine($"{Loc.T("Disk")}  {dt:0}°C");
        if (_cfg.ShowMoboTemp && _snap.MoboTempC is double mt) sb.AppendLine($"{Loc.T("Motherboard")}  {mt:0}°C");
        if (_cfg.ShowProcs && _snap.ProcessTotal > 0)
            sb.AppendLine($"{Loc.T("Processes")}  {_snap.ProcessTotal}  ({Loc.T("apps")} {_snap.ProcessApps}, {Loc.T("background")} {_snap.ProcessBackground}, {Loc.T("threads")} {_snap.ThreadTotal})");
        if (_cfg.ShowFps && _snap.Fps is { } f)
            sb.AppendLine($"FPS  {f.Fps:0}" + (f.Low1Percent > 0 ? $"  (1% low {f.Low1Percent:0})" : "") + (f.Process.Length > 0 ? $"  {f.Process}" : ""));
    }
}
