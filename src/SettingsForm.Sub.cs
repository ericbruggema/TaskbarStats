namespace TaskbarStats;

// Hulp voor subtabbladen (het tabblad Widget is verdeeld in Onderdelen, Bronnen, Weergave, Waarden en Uiterlijk).
public sealed partial class SettingsForm
{
    private int _dashSub, _fullSub, _generalSub;
    private readonly List<Action> _subRestore = new();

    /// <summary>Subtabbladen in een tabblad; het gekozen subtabblad blijft onthouden als het venster opnieuw wordt opgebouwd.</summary>
    private TabControl Subs(TabPage tab, Func<int> get, Action<int> set)
    {
        var sub = new TabControl { Dock = DockStyle.Fill };
        tab.Controls.Clear();
        tab.Controls.Add(sub);
        _subRestore.Add(() =>
        {
            sub.SelectedIndex = Math.Clamp(get(), 0, Math.Max(0, sub.TabPages.Count - 1));
            sub.SelectedIndexChanged += (_, _) => { if (!_building) set(sub.SelectedIndex); };
        });
        return sub;
    }
    private void RestoreSubs() { foreach (var a in _subRestore) a(); _subRestore.Clear(); }

    /// <summary>Grijze uitleg onder een bedieningselement: zichtbaar zolang de functie een reden geeft waarom het niets doet.</summary>
    private Label Why(Func<string?> reason)
    {
        var l = new Label { AutoSize = true, MaximumSize = new Size(640, 0), ForeColor = Color.FromArgb(176, 96, 0), Margin = new Padding(3, 0, 0, 6), Visible = false };
        Dep(() =>
        {
            string? r = reason();
            l.Visible = r != null;
            if (r != null) l.Text = "\u2192 " + r;
        });
        return l;
    }

    private int _widgetSub;   // onthoudt het gekozen subtabblad als het venster opnieuw wordt opgebouwd

    private static FlowLayoutPanel SubPage(TabControl host, string title)
    {
        var page = new TabPage(title) { Padding = new Padding(6), UseVisualStyleBackColor = true };
        var fp = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        page.Controls.Add(fp);
        host.TabPages.Add(page);
        return fp;
    }
}
