namespace TaskbarStats;

// Instellingen voor de mini-grafiek in het widget (uitbreiding van het gedeelte "Weergave" op tabblad Widget).
public sealed partial class SettingsForm
{
    private void GraphSettings(FlowLayoutPanel p)
    {
        var two = new (string, TextGraphStyle)[] { (Loc.S("digital"), TextGraphStyle.Digital), (Loc.Pick("Grafiek", "Graph"), TextGraphStyle.Graph) };
        var netStyle = Seg(two, () => _c.NetStyle, v => _c.NetStyle = v);
        var pingStyle = Seg(two, () => _c.PingStyle, v => _c.PingStyle = v);
        var secs = Seg(new (string, int)[] { ("30 s", 30), ("60 s", 60), ("120 s", 120) }, () => _c.GraphSeconds, v => _c.GraphSeconds = v, 56);
        var netMax = Seg(new (string, int)[]
        {
            (Loc.Pick("Automatisch", "Automatic"), 0), ("1 MB/s", 1), ("10 MB/s", 10), ("100 MB/s", 100),
        }, () => _c.NetGraphMaxMBps, v => _c.NetGraphMaxMBps = v, 56);
        p.Controls.Add(Row(Loc.Pick("Netwerk", "Network"), netStyle));
        p.Controls.Add(Row(Loc.Pick("Netwerkgrafiek: schaal", "Network graph: scale"), netMax));
        p.Controls.Add(Row("Ping", pingStyle));
        var len = Seg(new (string, GraphLen)[]
        {
            (Loc.Pick("Kort", "Short"), GraphLen.Short), (Loc.Pick("Middel", "Medium"), GraphLen.Medium), (Loc.Pick("Lang", "Long"), GraphLen.Long),
        }, () => _c.GraphLength, v => _c.GraphLength = v, 76);
        p.Controls.Add(Row(Loc.Pick("Lengte van de grafiek", "Graph length"), len));
        p.Controls.Add(Row(Loc.Pick("Periode: de laatste", "Period: the last"), secs));
        Dep(() =>
        {
            netStyle.Enabled = _c.ShowNetUp || _c.ShowNetDown;
            pingStyle.Enabled = _c.ShowPing;
            netMax.Enabled = netStyle.Enabled && _c.NetStyle == TextGraphStyle.Graph;
            bool cpu = _c.ShowCpu && !_c.CpuPerCore && _c.CpuStyle == DisplayStyle.Graph;
            bool gpu = _c.ShowGpu && _c.GpuStyle == DisplayStyle.Graph;
            bool mem = _c.ShowMem && _c.MemStyle == DisplayStyle.Graph;
            bool ct = _c.ShowCpuTemp && !(_c.TempMerge && _c.ShowCpu) && _c.CpuTempStyle == DisplayStyle.Graph;
            bool gt = _c.ShowGpuTemp && !(_c.TempMerge && _c.ShowGpu) && _c.GpuTempStyle == DisplayStyle.Graph;
            bool net = netStyle.Enabled && _c.NetStyle == TextGraphStyle.Graph;
            bool ping = _c.ShowPing && _c.PingStyle == TextGraphStyle.Graph;
            secs.Enabled = len.Enabled = cpu || gpu || mem || ct || gt || net || ping;
        });
    }
}
