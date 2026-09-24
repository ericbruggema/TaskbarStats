using System.Diagnostics;
using System.Drawing.Text;

namespace TaskbarStats;

/// <summary>
/// Instellingenvenster met tabbladen. Elke wijziging wordt direct toegepast (en opgeslagen) via <c>apply</c>,
/// zodat je het effect meteen op het widget, het dashboard en het fullscreen-scherm ziet.
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly AppSettings _c;
    private readonly Action _apply;
    private readonly Action _resetDash;
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private bool _building;

    public SettingsForm(AppSettings cfg, Action apply, Action resetDash)
    {
        _c = cfg;
        _apply = apply;
        _resetDash = resetDash;
        Text = Loc.Pick("TaskbarStats — instellingen", "TaskbarStats — settings");
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(760, 680);
        MinimumSize = new Size(640, 520);
        Font = new Font("Segoe UI", 9f);
        Controls.Add(_tabs);
        Build(0);
    }

    // ---------- opbouw ----------
    private void Build(int select)
    {
        _building = true;
        SuspendLayout();
        _tabs.TabPages.Clear();
        _tabs.TabPages.Add(WidgetTab());
        _tabs.TabPages.Add(ColorsTab());
        _tabs.TabPages.Add(DashTab());
        _tabs.TabPages.Add(FullTab());
        _tabs.TabPages.Add(ThemesTab());
        _tabs.SelectedIndex = Math.Clamp(select, 0, _tabs.TabPages.Count - 1);
        ResumeLayout();
        _building = false;
    }

    private void Changed()
    {
        if (_building) return;
        _apply();
    }

    private static FlowLayoutPanel Page(string title, out TabPage tab)
    {
        tab = new TabPage(title) { Padding = new Padding(10), UseVisualStyleBackColor = true };
        var p = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true,
        };
        tab.Controls.Add(p);
        return p;
    }

    private static Label Head(string text) => new()
    {
        Text = text, AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), Margin = new Padding(0, 12, 0, 4),
    };

    private CheckBox Check(string text, bool val, Action<bool> set)
    {
        var cb = new CheckBox { Text = text, Checked = val, AutoSize = true, Margin = new Padding(3, 2, 3, 2) };
        cb.CheckedChanged += (_, _) => { if (_building) return; set(cb.Checked); Changed(); };
        return cb;
    }

    private Control Row(string label, Control input, int labelWidth = 200)
    {
        var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 2, 0, 2) };
        row.Controls.Add(new Label { Text = label, Width = labelWidth, TextAlign = ContentAlignment.MiddleLeft, Height = 26 });
        row.Controls.Add(input);
        return row;
    }

    private ComboBox Combo<T>(IReadOnlyList<(string label, T value)> items, Func<T> get, Action<T> set, int width = 220) where T : notnull
    {
        var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = width };
        foreach (var it in items) cb.Items.Add(it.label);
        int idx = 0;
        for (int i = 0; i < items.Count; i++) if (EqualityComparer<T>.Default.Equals(items[i].value, get())) idx = i;
        cb.SelectedIndex = idx;
        cb.SelectedIndexChanged += (_, _) => { if (_building || cb.SelectedIndex < 0) return; set(items[cb.SelectedIndex].value); Changed(); };
        return cb;
    }

    private Control Slider(int min, int max, int step, Func<int> get, Action<int> set, string unit)
    {
        var host = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        var tb = new TrackBar { Minimum = min, Maximum = max, TickFrequency = step, SmallChange = step, LargeChange = step * 2, Width = 240, Value = Math.Clamp(get(), min, max) };
        var lb = new Label { Text = tb.Value + unit, Width = 60, TextAlign = ContentAlignment.MiddleLeft, Height = 40 };
        tb.ValueChanged += (_, _) =>
        {
            lb.Text = tb.Value + unit;
            if (_building) return;
            set(tb.Value);
            Changed();
        };
        host.Controls.Add(tb);
        host.Controls.Add(lb);
        return host;
    }

    private Control Num(int min, int max, Func<int> get, Action<int> set)
    {
        var n = new NumericUpDown { Minimum = min, Maximum = max, Width = 80, Value = Math.Clamp(get(), min, max) };
        n.ValueChanged += (_, _) => { if (_building) return; set((int)n.Value); Changed(); };
        return n;
    }

    private static Color Col(string? hex, Color fallback)
    {
        try { return string.IsNullOrWhiteSpace(hex) ? fallback : ColorTranslator.FromHtml(hex); } catch { return fallback; }
    }

    private Control ColorRow(string label, Func<string> get, Action<string> set, bool allowNone = false)
    {
        var host = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        var sw = new Panel { Width = 60, Height = 24, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(3, 2, 8, 2) };
        void paint() => sw.BackColor = Col(get(), Color.Transparent);
        paint();
        var pick = new Button { Text = Loc.Pick("Kies…", "Pick…"), AutoSize = true };
        pick.Click += (_, _) =>
        {
            using var dlg = new ColorDialog { Color = Col(get(), Color.White), FullOpen = true };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            set($"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}");
            paint();
            Changed();
        };
        host.Controls.Add(sw);
        host.Controls.Add(pick);
        if (allowNone)
        {
            var none = new Button { Text = Loc.Pick("Geen", "None"), AutoSize = true };
            none.Click += (_, _) => { set(""); paint(); Changed(); };
            host.Controls.Add(none);
        }
        return Row(label, host);
    }

    // ---------- tabblad Widget ----------
    private TabPage WidgetTab()
    {
        var p = Page(Loc.Pick("Widget", "Widget"), out var tab);

        p.Controls.Add(Head(Loc.S("components")));
        p.Controls.Add(Check(Loc.S("cpu"), _c.ShowCpu, v => _c.ShowCpu = v));
        p.Controls.Add(Check(Loc.S("gpu"), _c.ShowGpu, v => _c.ShowGpu = v));
        p.Controls.Add(Check(Loc.S("memory"), _c.ShowMem, v => _c.ShowMem = v));
        p.Controls.Add(Check(Loc.S("upload"), _c.ShowNetUp, v => _c.ShowNetUp = v));
        p.Controls.Add(Check(Loc.S("download"), _c.ShowNetDown, v => _c.ShowNetDown = v));
        p.Controls.Add(Check(Loc.Pick("Batterij", "Battery"), _c.ShowBattery, v => _c.ShowBattery = v));
        p.Controls.Add(Check(Loc.S("diskIo"), _c.ShowDisk, v => _c.ShowDisk = v));
        p.Controls.Add(Check(Loc.S("cpuTemp"), _c.ShowCpuTemp, v => _c.ShowCpuTemp = v));
        p.Controls.Add(Check(Loc.S("gpuTemp"), _c.ShowGpuTemp, v => _c.ShowGpuTemp = v));

        p.Controls.Add(Head(Loc.S("display")));
        var styles = new (string, DisplayStyle)[] { (Loc.S("digital"), DisplayStyle.Digital), (Loc.S("gauge"), DisplayStyle.Gauge), (Loc.S("bar"), DisplayStyle.Bar) };
        p.Controls.Add(Row(Loc.S("cpu"), Combo(styles, () => _c.CpuStyle, v => _c.CpuStyle = v)));
        p.Controls.Add(Row(Loc.S("gpu"), Combo(styles, () => _c.GpuStyle, v => _c.GpuStyle = v)));
        p.Controls.Add(Row(Loc.S("memory"), Combo(styles, () => _c.MemStyle, v => _c.MemStyle = v)));
        p.Controls.Add(Check(Loc.S("perCore"), _c.CpuPerCore, v => _c.CpuPerCore = v));
        p.Controls.Add(Row(Loc.Pick("Batterij: percentage", "Battery: percentage"), Combo(new (string, BatteryPercentMode)[]
        {
            (Loc.Pick("In de batterij", "Inside the battery"), BatteryPercentMode.Inside),
            (Loc.Pick("Naast de batterij", "Next to the battery"), BatteryPercentMode.Beside),
            (Loc.Pick("Uit", "Off"), BatteryPercentMode.Off),
        }, () => _c.BatteryPercent, v => _c.BatteryPercent = v)));

        var labels = Check(Loc.Pick("Labels boven", "Labels above"), _c.LabelsAbove, v => _c.LabelsAbove = v);
        p.Controls.Add(labels);
        p.Controls.Add(Check(Loc.Pick("Compact (kort en klein)", "Compact (short and small)"), _c.Compact, v =>
        {
            _c.Compact = v;
            if (v) { _c.LabelsAbove = true; _building = true; labels.Checked = true; _building = false; }
        }));
        p.Controls.Add(Check(Loc.S("transparent"), _c.TransparentBackground, v => _c.TransparentBackground = v));

        p.Controls.Add(Head(Loc.Pick("Afmetingen en lettertype", "Size and font")));
        var heights = new List<(string, int)> { (Loc.Pick("Automatisch (taakbalk)", "Automatic (taskbar)"), 0) };
        foreach (int h in new[] { 32, 36, 40, 44, 48, 56, 64 }) heights.Add(($"{h} px", h));
        p.Controls.Add(Row(Loc.Pick("Hoogte", "Height"), Combo(heights, () => _c.AutoHeight ? 0 : _c.WidgetHeight, v =>
        {
            _c.AutoHeight = v == 0;
            if (v > 0) _c.WidgetHeight = v;
        })));

        var fonts = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
        using (var installed = new InstalledFontCollection())
            foreach (var f in installed.Families) fonts.Items.Add(f.Name);
        int fi = fonts.FindStringExact(_c.FontFamily);
        if (fi < 0) { fonts.Items.Insert(0, _c.FontFamily); fi = 0; }
        fonts.SelectedIndex = fi;
        fonts.SelectedIndexChanged += (_, _) => { if (_building || fonts.SelectedItem is not string s) return; _c.FontFamily = s; Changed(); };
        p.Controls.Add(Row(Loc.Pick("Lettertype", "Font"), fonts));
        p.Controls.Add(Row(Loc.Pick("Lettergrootte (pt)", "Font size (pt)"), Num(6, 16, () => _c.FontSize, v => _c.FontSize = v)));

        var intervals = new (string, int)[] { ("0,1 s", 100), ("0,2 s", 200), ("0,25 s", 250), ("0,5 s", 500), ("1 s", 1000), ("2 s", 2000) };
        p.Controls.Add(Row(Loc.S("interval"), Combo(intervals, () => _c.RefreshMs, v => _c.RefreshMs = v)));
        return tab;
    }

    // ---------- tabblad Kleuren ----------
    private TabPage ColorsTab()
    {
        var p = Page(Loc.S("colors"), out var tab);
        p.Controls.Add(Head(Loc.S("colors")));
        p.Controls.Add(ColorRow(Loc.Pick("Tekst", "Text"), () => _c.TextColor, v => _c.TextColor = v));
        p.Controls.Add(ColorRow(Loc.Pick("Achtergrond", "Background"), () => _c.BackgroundColor, v => _c.BackgroundColor = v));
        p.Controls.Add(ColorRow(Loc.Pick("Meter/balk", "Meter/bar"), () => _c.AccentColor, v => _c.AccentColor = v));
        p.Controls.Add(ColorRow(Loc.Pick("Waarschuwing", "Warning"), () => _c.WarnColor, v => _c.WarnColor = v));
        p.Controls.Add(ColorRow(Loc.Pick("Kritiek", "Critical"), () => _c.CritColor, v => _c.CritColor = v));
        p.Controls.Add(ColorRow(Loc.S("borderColor"), () => _c.BorderColor, v => _c.BorderColor = v, allowNone: true));
        p.Controls.Add(Head(Loc.S("thresholds")));
        p.Controls.Add(Row(Loc.Pick("Waarschuwing vanaf (%)", "Warning from (%)"), Num(1, 99, () => _c.WarnThreshold, v => _c.WarnThreshold = v)));
        p.Controls.Add(Row(Loc.Pick("Kritiek vanaf (%)", "Critical from (%)"), Num(1, 100, () => _c.CritThreshold, v => _c.CritThreshold = v)));
        return tab;
    }

    // ---------- tabblad Dashboard ----------
    private TabPage DashTab()
    {
        var p = Page("Dashboard", out var tab);
        p.Controls.Add(Head(Loc.Pick("Bureaublad-dashboard", "Desktop dashboard")));
        p.Controls.Add(Check(Loc.Pick("Dashboard tonen", "Show dashboard"), _c.ShowDashboard, v => _c.ShowDashboard = v));
        p.Controls.Add(Row(Loc.Pick("Positie", "Layer"), Combo(new (string, bool)[]
        {
            (Loc.Pick("Op de achtergrond (onder alle vensters)", "In the background (below all windows)"), false),
            (Loc.Pick("Op de voorgrond (altijd bovenop)", "In front (always on top)"), true),
        }, () => _c.DashFront, v => _c.DashFront = v, 300)));
        p.Controls.Add(Check(Loc.Pick("Klik-door (Ctrl+Alt+D)", "Click-through (Ctrl+Alt+D)"), _c.DashClickThrough, v => _c.DashClickThrough = v));
        p.Controls.Add(Check(Loc.Pick("Positie vergrendelen", "Lock position"), _c.DashLocked, v => _c.DashLocked = v));
        p.Controls.Add(Row(Loc.Pick("Doorzichtigheid", "Opacity"), Slider(20, 100, 10, () => _c.DashOpacity, v => _c.DashOpacity = v, "%")));
        p.Controls.Add(Row(Loc.Pick("Schaal", "Scale"), Slider(50, 300, 25, () => _c.DashScale, v => _c.DashScale = v, "%")));
        p.Controls.Add(Row(Loc.Pick("Kolommen", "Columns"), Combo(new (string, int)[] { ("1", 1), ("2", 2), ("3", 3), ("4", 4) }, () => _c.DashColumns, v => _c.DashColumns = v, 80)));
        p.Controls.Add(Head(Loc.Pick("Onderdelen en volgorde", "Components and order")));
        p.Controls.Add(new Label { Text = Loc.Pick("Vink aan of uit; verplaats met de knoppen.", "Tick to show or hide; move with the buttons."), AutoSize = true });
        p.Controls.Add(TileEditor(() => _c.DashOrder, v => _c.DashOrder = v, id => Tiles.DashOn(_c, id), (id, on) => Tiles.SetDashOn(_c, id, on)));
        var reset = new Button { Text = Loc.Pick("Reset positie dashboard", "Reset dashboard position"), AutoSize = true, Margin = new Padding(3, 10, 3, 3) };
        reset.Click += (_, _) => _resetDash();
        p.Controls.Add(reset);
        return tab;
    }

    // ---------- tabblad Fullscreen ----------
    private TabPage FullTab()
    {
        var p = Page("Fullscreen", out var tab);
        p.Controls.Add(Head(Loc.Pick("Fullscreen dashboard (Ctrl+Alt+F)", "Fullscreen dashboard (Ctrl+Alt+F)")));
        var screens = new List<(string, string?)> { (Loc.Pick("Automatisch (waar het widget staat)", "Automatic (where the widget is)"), null) };
        foreach (var sc in Screen.AllScreens)
            screens.Add(($"{sc.DeviceName.TrimStart('\\', '.')}  {sc.Bounds.Width}×{sc.Bounds.Height}{(sc.Primary ? " *" : "")}", sc.DeviceName));
        var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
        foreach (var s in screens) cb.Items.Add(s.Item1);
        cb.SelectedIndex = Math.Max(0, screens.FindIndex(s => s.Item2 == _c.FullMonitor));
        cb.SelectedIndexChanged += (_, _) => { if (_building || cb.SelectedIndex < 0) return; _c.FullMonitor = screens[cb.SelectedIndex].Item2; Changed(); };
        p.Controls.Add(Row(Loc.Pick("Scherm", "Display"), cb));
        p.Controls.Add(Head(Loc.Pick("Onderdelen en volgorde", "Components and order")));
        p.Controls.Add(new Label { Text = Loc.Pick("Het fullscreen-scherm past zich aan; open het (Ctrl+Alt+F) om het effect te zien.", "The fullscreen screen adapts; open it (Ctrl+Alt+F) to see the effect."), AutoSize = true });
        p.Controls.Add(TileEditor(() => _c.FullOrder, v => _c.FullOrder = v, id => Tiles.FullOn(_c, id), (id, on) => Tiles.SetFullOn(_c, id, on)));
        return tab;
    }

    /// <summary>Lijst met vinkjes (aan/uit) en knoppen omhoog/omlaag voor de volgorde van de hoofdonderdelen.</summary>
    private Control TileEditor(Func<List<string>?> getOrder, Action<List<string>?> setOrder, Func<string, bool> on, Action<string, bool> setOn)
    {
        var host = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        var list = new CheckedListBox { Width = 300, Height = 200, CheckOnClick = true, IntegralHeight = false };
        var order = Tiles.Order(getOrder());
        void fill(int sel)
        {
            _building = true;
            list.Items.Clear();
            foreach (var id in order) list.Items.Add(Tiles.Name(id), on(id));
            if (list.Items.Count > 0) list.SelectedIndex = Math.Clamp(sel, 0, list.Items.Count - 1);
            _building = false;
        }
        fill(0);
        list.ItemCheck += (_, e) =>
        {
            if (_building) return;
            setOn(order[e.Index], e.NewValue == CheckState.Checked);
            Changed();
        };
        void move(int d)
        {
            int i = list.SelectedIndex, j = i + d;
            if (i < 0 || j < 0 || j >= order.Count) return;
            (order[i], order[j]) = (order[j], order[i]);
            setOrder(new List<string>(order));
            fill(j);
            Changed();
        }
        var up = new Button { Text = "▲", Width = 36, Height = 32 };
        var down = new Button { Text = "▼", Width = 36, Height = 32 };
        up.Click += (_, _) => move(-1);
        down.Click += (_, _) => move(1);
        var reset = new Button { Text = Loc.Pick("Standaard", "Default"), Width = 80, Height = 30 };
        reset.Click += (_, _) =>
        {
            order = Tiles.Order(null);
            setOrder(null);
            foreach (var id in Tiles.All) setOn(id, true);
            fill(0);
            Changed();
        };
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        buttons.Controls.Add(up);
        buttons.Controls.Add(down);
        buttons.Controls.Add(reset);
        host.Controls.Add(list);
        host.Controls.Add(buttons);
        return host;
    }

    // ---------- tabblad Thema's ----------
    private TabPage ThemesTab()
    {
        var p = Page(Loc.Pick("Thema's", "Themes"), out var tab);
        p.Controls.Add(Head(Loc.Pick("Uiterlijk en indeling opslaan en laden", "Save and load looks and layout")));
        p.Controls.Add(new Label
        {
            Text = Loc.Pick("Een thema bevat stijl, kleuren, lettertype, hoogte, dashboard en fullscreen-indeling (geen posities of taal).",
                            "A theme contains style, colors, font, height, dashboard and fullscreen layout (not positions or language)."),
            AutoSize = true, MaximumSize = new Size(640, 0),
        });

        var list = new ListBox { Width = 400, Height = 240, IntegralHeight = false };
        var entries = new List<(ThemeData t, bool builtIn, string? file)>();
        void reload()
        {
            entries.Clear();
            foreach (var t in ThemeStore.BuiltIn()) entries.Add((t, true, null));
            foreach (var t in ThemeStore.User(_c)) entries.Add((t, false, ThemeStore.FileFor(_c, t.Name)));
            list.Items.Clear();
            foreach (var e in entries) list.Items.Add(e.t.Name + (e.builtIn ? "   " + Loc.Pick("(meegeleverd)", "(built-in)") : ""));
        }
        reload();

        void applySelected()
        {
            if (list.SelectedIndex < 0) return;
            entries[list.SelectedIndex].t.ApplyTo(_c);
            _apply();
            Build(4);   // alle tabbladen tonen de nieuwe waarden
        }

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        Button Btn(string text, Action click)
        {
            var b = new Button { Text = text, Width = 170, Height = 30 };
            b.Click += (_, _) => click();
            buttons.Controls.Add(b);
            return b;
        }
        Btn(Loc.Pick("Toepassen", "Apply"), applySelected);
        Btn(Loc.Pick("Huidige opslaan als…", "Save current as…"), () =>
        {
            var name = Ask(Loc.Pick("Naam van het thema:", "Theme name:"), "");
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim();
            if (ThemeStore.BuiltIn().Any(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(this, Loc.Pick("Die naam is van een meegeleverd thema. Kies een andere naam.", "That name belongs to a built-in theme. Choose another name."), Text);
                return;
            }
            var file = ThemeStore.FileFor(_c, name);
            if (File.Exists(file) && MessageBox.Show(this, Loc.Pick("Bestaand thema overschrijven?", "Overwrite the existing theme?"), Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            ThemeStore.Write(file, ThemeData.Capture(_c, name));
            reload();
        });
        Btn(Loc.Pick("Verwijderen", "Delete"), () =>
        {
            if (list.SelectedIndex < 0 || entries[list.SelectedIndex] is not { builtIn: false, file: { } f }) return;
            if (MessageBox.Show(this, Loc.Pick("Dit thema verwijderen?", "Delete this theme?"), Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            try { File.Delete(f); } catch { }
            reload();
        });
        Btn(Loc.Pick("Importeren…", "Import…"), () =>
        {
            using var dlg = new OpenFileDialog { Filter = "TaskbarStats-thema (*.json)|*.json", Multiselect = true };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            foreach (var path in dlg.FileNames)
            {
                var t = ThemeStore.Read(path);
                if (t is null) { MessageBox.Show(this, Loc.Pick("Geen geldig thema: ", "Not a valid theme: ") + Path.GetFileName(path), Text); continue; }
                if (string.IsNullOrWhiteSpace(t.Name)) t.Name = Path.GetFileNameWithoutExtension(path);
                if (ThemeStore.BuiltIn().Any(b => b.Name.Equals(t.Name, StringComparison.OrdinalIgnoreCase))) t.Name += " (2)";
                ThemeStore.Write(ThemeStore.FileFor(_c, t.Name), t);
            }
            reload();
        });
        Btn(Loc.Pick("Exporteren…", "Export…"), () =>
        {
            if (list.SelectedIndex < 0) return;
            var t = entries[list.SelectedIndex].t;
            using var dlg = new SaveFileDialog { Filter = "TaskbarStats-thema (*.json)|*.json", FileName = t.Name + ".json" };
            if (dlg.ShowDialog(this) == DialogResult.OK) ThemeStore.Write(dlg.FileName, t);
        });
        Btn(Loc.Pick("Themamap openen", "Open themes folder"), () =>
        {
            try
            {
                Directory.CreateDirectory(ThemeStore.Dir(_c));
                Process.Start(new ProcessStartInfo(ThemeStore.Dir(_c)) { UseShellExecute = true });
            }
            catch { }
        });
        list.DoubleClick += (_, _) => applySelected();

        var host = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 8, 0, 0) };
        host.Controls.Add(list);
        host.Controls.Add(buttons);
        p.Controls.Add(host);
        return tab;
    }

    private string? Ask(string prompt, string initial)
    {
        using var f = new Form
        {
            Text = Text, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false, MinimizeBox = false, ClientSize = new Size(360, 110), Font = Font,
        };
        var lb = new Label { Text = prompt, Left = 12, Top = 12, AutoSize = true };
        var tb = new TextBox { Left = 12, Top = 36, Width = 336, Text = initial };
        var ok = new Button { Text = "OK", Left = 190, Top = 72, Width = 75, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = Loc.Pick("Annuleren", "Cancel"), Left = 272, Top = 72, Width = 76, DialogResult = DialogResult.Cancel };
        f.Controls.AddRange(new Control[] { lb, tb, ok, cancel });
        f.AcceptButton = ok;
        f.CancelButton = cancel;
        return f.ShowDialog(this) == DialogResult.OK ? tb.Text : null;
    }
}
