namespace TaskbarStats;

// Instellingen voor de mini-grafiek in het widget (uitbreiding van het gedeelte "Weergave" op tabblad Widget).
public sealed partial class SettingsForm
{
    private bool AnyGraph() =>
        _c.ShowCpu && !_c.CpuPerCore && _c.CpuStyle == DisplayStyle.Graph
        || _c.ShowGpu && _c.GpuStyle == DisplayStyle.Graph
        || _c.ShowMem && _c.MemStyle == DisplayStyle.Graph
        || _c.ShowCpuTemp && !(_c.TempMerge && _c.ShowCpu) && _c.CpuTempStyle == DisplayStyle.Graph
        || _c.ShowGpuTemp && !(_c.TempMerge && _c.ShowGpu) && _c.GpuTempStyle == DisplayStyle.Graph
        || (_c.ShowNetUp || _c.ShowNetDown) && _c.NetStyle == TextGraphStyle.Graph
        || _c.ShowPing && _c.PingStyle == TextGraphStyle.Graph;

    // Geeft de geavanceerde rijen (lengte, periode, schaal) terug; die staan op het subtabblad Geavanceerd.
    private List<Control> GraphSettings(FlowLayoutPanel p)
    {
        var adv = new List<Control>();
        var two = new (string, TextGraphStyle)[] { (Loc.S("digital"), TextGraphStyle.Digital), (Loc.Pick("Grafiek", "Graph"), TextGraphStyle.Graph) };
        var netStyle = Seg(two, () => _c.NetStyle, v => _c.NetStyle = v);
        var pingStyle = Seg(two, () => _c.PingStyle, v => _c.PingStyle = v);
        var secs = Seg(new (string, int)[] { ("30 s", 30), ("60 s", 60), ("120 s", 120) }, () => _c.GraphSeconds, v => _c.GraphSeconds = v, 56);
        var netMax = Seg(new (string, int)[]
        {
            (Loc.Pick("Automatisch", "Automatic"), 0), ("1 MB/s", 1), ("10 MB/s", 10), ("100 MB/s", 100),
        }, () => _c.NetGraphMaxMBps, v => _c.NetGraphMaxMBps = v, 56);
        p.Controls.Add(Row(Loc.Pick("Netwerk", "Network"), netStyle));
        adv.Add(Why(() => AnyGraph() ? null : Loc.Pick("Nog geen onderdeel heeft de stijl Grafiek (subtab Weergave). Je kunt dit alvast instellen; het werkt zodra een onderdeel een grafiek toont.", "No item uses the Graph style yet (Display subtab). You can set this already; it applies as soon as an item shows a graph.")));
        adv.Add(Row(Loc.Pick("Netwerkgrafiek: schaal", "Network graph: scale"), netMax));
        p.Controls.Add(Row("Ping", pingStyle));
        var len = Seg(new (string, GraphLen)[]
        {
            (Loc.Pick("Kort", "Short"), GraphLen.Short), (Loc.Pick("Middel", "Medium"), GraphLen.Medium), (Loc.Pick("Lang", "Long"), GraphLen.Long),
        }, () => _c.GraphLength, v => _c.GraphLength = v, 76);
        adv.Add(Row(Loc.Pick("Lengte van de grafiek", "Graph length"), len));
        adv.Add(Row(Loc.Pick("Periode: de laatste", "Period: the last"), secs));
        return adv;
    }
}
