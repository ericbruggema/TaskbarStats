namespace TaskbarStats;

// Hulp voor subtabbladen (het tabblad Widget is verdeeld in Onderdelen, Bronnen, Weergave, Waarden en Uiterlijk).
public sealed partial class SettingsForm
{
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
