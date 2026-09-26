namespace TaskbarStats;

/// <summary>Instellingen voor de waardeopmaak in het widget en de extra widget-onderdelen (tabblad Widget).</summary>
public sealed partial class SettingsForm
{
    private bool FmtItemOn(string id) => id switch
    {
        "cpufreq" => _c.ShowCpuFreq,
        "diskbusy" => _c.ShowDiskBusy,
        "disktemp" => _c.ShowDiskTemp,
        "mobotemp" => _c.ShowMoboTemp,
        "procs" => _c.ShowProcs,
        "fps" => _c.ShowFps,
        _ => true,
    };

    /// <summary>Vinkjes voor de extra onderdelen, onder de bestaande vinkjes bij "Onderdelen".</summary>
    private Control FmtItemGrid() => Grid(
        Check(Loc.T("CPU clock speed"), _c.ShowCpuFreq, v => _c.ShowCpuFreq = v, ColW),
        Check(Loc.T("Disk active (%)"), _c.ShowDiskBusy, v => _c.ShowDiskBusy = v, ColW),
        Check(Loc.T("Disk temperature"), _c.ShowDiskTemp, v => _c.ShowDiskTemp = v, ColW),
        Check(Loc.T("Motherboard temperature"), _c.ShowMoboTemp, v => _c.ShowMoboTemp = v, ColW),
        Check(Loc.T("Running processes"), _c.ShowProcs, v => _c.ShowProcs = v, ColW),
        Check(Loc.T("FPS (experimental)"), _c.ShowFps, v => _c.ShowFps = v, ColW));

    /// <summary>Sectie "Waarden": eenheden, decimalen en wat de cellen tonen. Alleen het widget; dashboard, fullscreen en tooltip blijven zoals ze zijn.</summary>
    private void AddValueSettings(Control p)
    {
        p.Controls.Add(Head(Loc.T("Values")));
        p.Controls.Add(Note(Loc.T("Applies to the widget only; the dashboard, full screen and tooltip show values as before.")));

        var unit = Seg(new (string, RateUnitKind)[]
        {
            (Loc.T("Bytes (MB/s)"), RateUnitKind.Bytes), (Loc.T("Bits (Mb/s)"), RateUnitKind.Bits),
        }, () => _c.NetUnit, v => _c.NetUnit = v, 110);
        p.Controls.Add(Row(Loc.T("Network unit"), unit));

        var scale = Seg(new (string, RateScale)[]
        {
            (Loc.T("Automatic"), RateScale.Auto), ("KB/s", RateScale.Kilo), ("MB/s", RateScale.Mega),
        }, () => _c.RateScaleMode, v => _c.RateScaleMode = v, 90);
        p.Controls.Add(Row(Loc.T("Speed unit"), scale));

        var mem = Seg(new (string, MemShowMode)[]
        {
            (Loc.T("Percentage"), MemShowMode.Percent), (Loc.T("Used (GB)"), MemShowMode.Used),
            (Loc.T("Available (GB)"), MemShowMode.Available),
        }, () => _c.MemShow, v => _c.MemShow = v, 110);
        p.Controls.Add(Row(Loc.T("Memory (digital)"), mem));

        var busyStyle = Seg(new (string, DisplayStyle)[] { (Loc.T("Digital"), DisplayStyle.Digital), (Loc.T("Gauge"), DisplayStyle.Gauge), (Loc.T("Bar"), DisplayStyle.Bar) },
                            () => _c.DiskBusyStyle, v => _c.DiskBusyStyle = v);
        p.Controls.Add(Row(Loc.T("Disk active: style"), busyStyle));

        var diskLabels = Seg(new (string, DiskLabelMode)[]
        {
            (Loc.T("R / W"), DiskLabelMode.Letters), (Loc.T("Arrows (↓ read, ↑ write)"), DiskLabelMode.Arrows),
        }, () => _c.DiskLabels, v => _c.DiskLabels = v, 100);
        p.Controls.Add(Row(Loc.T("Disk read/write shown as"), diskLabels));

        var procMode = Seg(new (string, ProcMode)[]
        {
            (Loc.T("All"), ProcMode.Total), (Loc.T("Apps"), ProcMode.Apps), (Loc.T("Background"), ProcMode.Background), (Loc.T("Apps / background"), ProcMode.Both),
        }, () => _c.ProcMode, v => _c.ProcMode = v, 100);
        p.Controls.Add(Row(Loc.T("Processes: show"), procMode));
        p.Controls.Add(Note(Loc.T("Apps are processes with a visible window (like Apps in Task Manager); the rest are background processes.")));
        p.Controls.Add(Note(Loc.T("FPS is experimental: it measures the frame rate of the app in front by listening to Windows graphics events (like PresentMon). It needs administrator rights, uses a little CPU while on and only shows a value while an app is drawing frames (games, video players, browsers).")));

        var shortV = Check(Loc.T("Short values (fewer decimals)"), _c.ShortValues, v => _c.ShortValues = v, ColW * 2);
        var hideUnit = Check(Loc.T("Hide the unit (MB/s, GB, GHz)"), _c.HideUnit, v => _c.HideUnit = v, ColW * 2);
        var hidePct = Check(Loc.T("Hide the % sign"), _c.HidePercent, v => _c.HidePercent = v, ColW * 2);
        var swap = Check(Loc.T("Swap upload and download (download on top)"), _c.SwapNet, v => _c.SwapNet = v, ColW * 2);
        p.Controls.Add(Grid(shortV, hideUnit, hidePct, swap));

    }
}
