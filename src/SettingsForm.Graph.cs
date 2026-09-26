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
        var two = new (string, TextGraphStyle)[] { (Loc.T("Digital"), TextGraphStyle.Digital), (Loc.T("Graph"), TextGraphStyle.Graph) };
        var netStyle = Seg(two, () => _c.NetStyle, v => _c.NetStyle = v);
        var pingStyle = Seg(two, () => _c.PingStyle, v => _c.PingStyle = v);
        var secs = Seg(new (string, int)[] { ("30 s", 30), ("60 s", 60), ("120 s", 120) }, () => _c.GraphSeconds, v => _c.GraphSeconds = v, 56);
        var netMax = Seg(new (string, int)[]
        {
            (Loc.T("Automatic"), 0), ("1 MB/s", 1), ("10 MB/s", 10), ("100 MB/s", 100),
        }, () => _c.NetGraphMaxMBps, v => _c.NetGraphMaxMBps = v, 56);
        p.Controls.Add(Row(Loc.T("Network"), netStyle));
        adv.Add(Why(() => AnyGraph() ? null : Loc.T("No item uses the Graph style yet (Display subtab). You can set this already; it applies as soon as an item shows a graph.")));
        adv.Add(Row(Loc.T("Network graph: scale"), netMax));
        p.Controls.Add(Row("Ping", pingStyle));
        var len = Seg(new (string, GraphLen)[]
        {
            (Loc.T("Tiny"), GraphLen.Tiny), (Loc.T("Short"), GraphLen.Short), (Loc.T("Medium"), GraphLen.Medium),
            (Loc.T("Long"), GraphLen.Long), (Loc.T("Custom"), GraphLen.Custom),
        }, () => _c.GraphLength, v => _c.GraphLength = v, 60);
        adv.Add(Row(Loc.T("Graph length"), len));
        // Eigen breedte: wijzigen kiest automatisch "Aangepast".
        var px = Num(16, 160, () => _c.GraphWidthPx, v =>
        {
            _c.GraphWidthPx = v; _c.GraphLength = GraphLen.Custom;
            bool was = _building; _building = true; len.Controls.OfType<RadioButton>().Last().Checked = true; _building = was;
        });
        var pxRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        pxRow.Controls.Add(px);
        pxRow.Controls.Add(new Label { Text = Loc.T("px (16–160; Tiny 24, Short 34, Medium 46, Long 60)"), AutoSize = true, Margin = new Padding(6, 6, 0, 0) });
        adv.Add(Row(Loc.T("Own width"), pxRow));
        adv.Add(Row(Loc.T("Period: the last"), secs));

        adv.Add(Head(Loc.T("Graph length per item")));
        adv.Add(Note(Loc.T("Every graph can have its own length. \"Default\" follows the general length above; for \"Custom\" fill in the width in pixels.")));
        foreach (var (key, name) in new (string, string)[]
        {
            ("cpu", Loc.T("CPU")), ("gpu", Loc.T("GPU")), ("mem", Loc.T("Memory")), ("cputemp", Loc.T("CPU temperature")),
            ("gputemp", Loc.T("GPU temperature")), ("net", Loc.T("Network")), ("ping", "Ping"),
        })
            adv.Add(GraphSizeRow(key, name));
        return adv;
    }

    // Eén rij: lengtekeuze (Standaard, Tiny…Custom) en een eigen breedte in px; het invullen van de breedte kiest automatisch "Custom".
    private Control GraphSizeRow(string key, string name)
    {
        // -1 = standaard (geen eigen regel), 0..4 = GraphLen
        int Get() => _c.GraphSizes.TryGetValue(key, out var sz) && sz.Len is GraphLen l ? (int)l : -1;
        GraphSize Own() { if (!_c.GraphSizes.TryGetValue(key, out var sz)) _c.GraphSizes[key] = sz = new GraphSize { Px = _c.GraphWidthPx }; return sz; }
        var seg = Seg(new (string, int)[]
        {
            (Loc.T("Default"), -1), (Loc.T("Tiny"), 0), (Loc.T("Short"), 1), (Loc.T("Medium"), 2), (Loc.T("Long"), 3), (Loc.T("Custom"), 4),
        }, Get, v =>
        {
            if (v < 0) _c.GraphSizes.Remove(key);
            else Own().Len = (GraphLen)v;
        }, 60);
        var px = Num(16, 160, () => _c.GraphSizes.TryGetValue(key, out var sz) ? sz.Px : _c.GraphWidthPx, v =>
        {
            var sz = Own(); sz.Px = v; sz.Len = GraphLen.Custom;
            bool was = _building; _building = true; seg.Controls.OfType<RadioButton>().Last().Checked = true; _building = was;
        });
        // Past de breedte-invoer niet naast de knoppen (bijv. in het Nederlands), dan komt hij eronder in plaats van buiten beeld.
        var line = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(548, 0), Margin = new Padding(0) };
        line.Controls.Add(seg);
        line.Controls.Add(px);
        new ToolTip().SetToolTip(px, Loc.T("Own width in pixels (16–160)"));
        return Row(name, line, 134);
    }
}
