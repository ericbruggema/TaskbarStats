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
        _ => true,
    };

    /// <summary>Vinkjes voor de extra onderdelen, onder de bestaande vinkjes bij "Onderdelen".</summary>
    private Control FmtItemGrid() => Grid(
        Check(Loc.Pick("CPU-klokfrequentie", "CPU clock speed"), _c.ShowCpuFreq, v => _c.ShowCpuFreq = v, ColW),
        Check(Loc.Pick("Schijf actief (%)", "Disk active (%)"), _c.ShowDiskBusy, v => _c.ShowDiskBusy = v, ColW),
        Check(Loc.Pick("Schijftemperatuur", "Disk temperature"), _c.ShowDiskTemp, v => _c.ShowDiskTemp = v, ColW),
        Check(Loc.Pick("Hoofdbordtemperatuur", "Motherboard temperature"), _c.ShowMoboTemp, v => _c.ShowMoboTemp = v, ColW));

    /// <summary>Sectie "Waarden": eenheden, decimalen en wat de cellen tonen. Alleen het widget; dashboard, fullscreen en tooltip blijven zoals ze zijn.</summary>
    private void AddValueSettings(Control p)
    {
        p.Controls.Add(Head(Loc.Pick("Waarden", "Values")));
        p.Controls.Add(Note(Loc.Pick("Geldt alleen voor het widget; dashboard, fullscreen en tooltip tonen de waarden zoals altijd.",
                                     "Applies to the widget only; the dashboard, full screen and tooltip show values as before.")));

        var unit = Seg(new (string, RateUnitKind)[]
        {
            (Loc.Pick("Bytes (MB/s)", "Bytes (MB/s)"), RateUnitKind.Bytes), (Loc.Pick("Bits (Mb/s)", "Bits (Mb/s)"), RateUnitKind.Bits),
        }, () => _c.NetUnit, v => _c.NetUnit = v, 110);
        p.Controls.Add(Row(Loc.Pick("Netwerk-eenheid", "Network unit"), unit));

        var scale = Seg(new (string, RateScale)[]
        {
            (Loc.Pick("Automatisch", "Automatic"), RateScale.Auto), ("KB/s", RateScale.Kilo), ("MB/s", RateScale.Mega),
        }, () => _c.RateScaleMode, v => _c.RateScaleMode = v, 90);
        p.Controls.Add(Row(Loc.Pick("Snelheidseenheid", "Speed unit"), scale));

        var mem = Seg(new (string, MemShowMode)[]
        {
            (Loc.Pick("Percentage", "Percentage"), MemShowMode.Percent), (Loc.Pick("Gebruikt (GB)", "Used (GB)"), MemShowMode.Used),
            (Loc.Pick("Beschikbaar (GB)", "Available (GB)"), MemShowMode.Available),
        }, () => _c.MemShow, v => _c.MemShow = v, 110);
        p.Controls.Add(Row(Loc.Pick("Geheugen (digitaal)", "Memory (digital)"), mem));

        var busyStyle = Seg(new (string, DisplayStyle)[] { (Loc.S("digital"), DisplayStyle.Digital), (Loc.S("gauge"), DisplayStyle.Gauge), (Loc.S("bar"), DisplayStyle.Bar) },
                            () => _c.DiskBusyStyle, v => _c.DiskBusyStyle = v);
        p.Controls.Add(Row(Loc.Pick("Schijf actief: stijl", "Disk active: style"), busyStyle));

        var shortV = Check(Loc.Pick("Korte waarden (minder decimalen)", "Short values (fewer decimals)"), _c.ShortValues, v => _c.ShortValues = v, ColW * 2);
        var hideUnit = Check(Loc.Pick("Eenheid weglaten (MB/s, GB, GHz)", "Hide the unit (MB/s, GB, GHz)"), _c.HideUnit, v => _c.HideUnit = v, ColW * 2);
        var hidePct = Check(Loc.Pick("%-teken weglaten", "Hide the % sign"), _c.HidePercent, v => _c.HidePercent = v, ColW * 2);
        var swap = Check(Loc.Pick("Upload en download omwisselen (download boven)", "Swap upload and download (download on top)"), _c.SwapNet, v => _c.SwapNet = v, ColW * 2);
        p.Controls.Add(Grid(shortV, hideUnit, hidePct, swap));

    }
}
