using System.Diagnostics;
using System.Drawing.Text;
using System.Text.Json;

namespace TaskbarStats;

/// <summary>Wat het instellingenvenster van het widget nodig heeft (toepassen, acties, gegevens).</summary>
public sealed class SettingsHost
{
    public required Action Apply { get; init; }
    public required Action ResetDash { get; init; }
    public required Action ResetWidget { get; init; }
    public required Action ShowLog { get; init; }
    public required Func<List<DriveSpace>> Drives { get; init; }
}

/// <summary>
/// Instellingenvenster met tabbladen. Elke wijziging wordt direct toegepast (en opgeslagen) via <c>apply</c>,
/// zodat je het effect meteen op het widget, het dashboard en het fullscreen-scherm ziet.
/// Bedieningselementen die door een andere keuze geen effect hebben, worden uitgeschakeld (<see cref="Dep"/>).
/// </summary>
public sealed partial class SettingsForm : Form
{
    private readonly AppSettings _c;
    private readonly Action _apply;
    private readonly SettingsHost _h;
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly List<Action> _deps = new();
    private bool _building;
    private string _snapshot = "";

    private static readonly Color SegOn = Color.FromArgb(0, 103, 192), SegOff = Color.FromArgb(240, 240, 240);
    private const int ColW = 210;   // breedte van één vinkje in een kolomrooster

    public SettingsForm(AppSettings cfg, SettingsHost host)
    {
        AppIcon.Apply(this);
        _c = cfg;
        _h = host;
        _apply = host.Apply;
        Text = Loc.Pick("TaskbarStats — instellingen", "TaskbarStats — settings");
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(780, 640);
        MinimumSize = new Size(640, 480);
        Font = new Font("Segoe UI", 9f);
        Controls.Add(_tabs);
        Build(0);
        // Is er buiten dit venster iets gewijzigd (menu, sneltoets, taal)? Dan tonen we de nieuwe waarden.
        Activated += (_, _) =>
        {
            if (Snap() != _snapshot) { Text = Loc.Pick("TaskbarStats — instellingen", "TaskbarStats — settings"); Build(_tabs.SelectedIndex); }
        };
    }

    private string Snap() => JsonSerializer.Serialize(_c);

    // ---------- opbouw ----------
    private void Build(int select)
    {
        _building = true;
        SuspendLayout();
        _deps.Clear();
        _tabs.TabPages.Clear();
        _tabs.TabPages.Add(WidgetTab());
        _tabs.TabPages.Add(ColorsTab());
        _tabs.TabPages.Add(DashTab());
        _tabs.TabPages.Add(FullTab());
        _tabs.TabPages.Add(ThemesTab());
        _tabs.TabPages.Add(GeneralTab());
        _tabs.SelectedIndex = Math.Clamp(select, 0, _tabs.TabPages.Count - 1);
        ResumeLayout();
        RunDeps();
        _snapshot = Snap();
        _building = false;
    }

    private void RunDeps() { foreach (var d in _deps) d(); }

    private void Changed()
    {
        if (_building) return;
        RunDeps();
        _apply();
        _snapshot = Snap();
    }

    /// <summary>Registreert een regel die bedieningselementen in- of uitschakelt afhankelijk van andere instellingen.</summary>
    private void Dep(Action rule) { _deps.Add(rule); rule(); }

    // ---------- bouwstenen ----------
    private static FlowLayoutPanel Page(string title, out TabPage tab)
    {
        tab = new TabPage(title) { Padding = new Padding(8), UseVisualStyleBackColor = true };
        var p = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true,
        };
        tab.Controls.Add(p);
        return p;
    }

    private static Label Head(string text) => new()
    {
        Text = text, AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), Margin = new Padding(0, 10, 0, 4),
    };

    private static Label Note(string text, int width = 640) => new()
    {
        Text = text, AutoSize = true, MaximumSize = new Size(width, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, 0, 0, 4),
    };

    private CheckBox Check(string text, bool val, Action<bool> set, int width = 0)
    {
        var cb = new CheckBox { Text = text, Checked = val, AutoSize = width == 0, Margin = new Padding(3, 2, 3, 2) };
        if (width > 0) cb.Width = width;
        cb.CheckedChanged += (_, _) => { if (_building) return; set(cb.Checked); Changed(); };
        return cb;
    }

    /// <summary>Vinkjes naast elkaar in een rooster van meerdere kolommen.</summary>
    private static FlowLayoutPanel Grid(params Control[] items)
    {
        var g = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, MaximumSize = new Size(ColW * 3 + 30, 0) };
        g.Controls.AddRange(items);
        return g;
    }

    private static Control Row(string label, Control input, int labelWidth = 170)
    {
        var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 2, 0, 2) };
        row.Controls.Add(new Label { Text = label, Width = labelWidth, TextAlign = ContentAlignment.MiddleLeft, Height = 34 });
        row.Controls.Add(input);
        return row;
    }

    /// <summary>Keuzeknoppen naast elkaar (één is ingedrukt) in plaats van een uitklaplijst.</summary>
    private FlowLayoutPanel Seg<T>(IReadOnlyList<(string label, T value)> items, Func<T> get, Action<T> set, int minWidth = 72) where T : notnull
    {
        var host = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, MaximumSize = new Size(ColW * 3, 0) };
        var buttons = new List<RadioButton>();
        void paint()
        {
            foreach (var b in buttons)
            {
                b.BackColor = b.Checked ? SegOn : SegOff;
                b.ForeColor = !b.Enabled ? SystemColors.GrayText : b.Checked ? Color.White : SystemColors.ControlText;
            }
        }
        for (int i = 0; i < items.Count; i++)
        {
            var (label, value) = items[i];
            var rb = new RadioButton
            {
                Appearance = Appearance.Button, Text = label, AutoSize = true, MinimumSize = new Size(minWidth, 30),
                TextAlign = ContentAlignment.MiddleCenter, FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false,
                Margin = new Padding(0, 2, 4, 2), Padding = new Padding(8, 0, 8, 0), Checked = EqualityComparer<T>.Default.Equals(value, get()),
            };
            rb.FlatAppearance.BorderColor = Color.FromArgb(190, 190, 190);
            rb.FlatAppearance.CheckedBackColor = SegOn;
            rb.CheckedChanged += (_, _) =>
            {
                paint();
                if (_building || !rb.Checked) return;
                set(value);
                Changed();
            };
            rb.EnabledChanged += (_, _) => paint();
            buttons.Add(rb);
            host.Controls.Add(rb);
        }
        if (!buttons.Any(b => b.Checked) && buttons.Count > 0) buttons[0].Checked = true;
        paint();
        return host;
    }

    private Control Slider(int min, int max, int step, Func<int> get, Action<int> set, string unit)
    {
        var host = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        var tb = new TrackBar
        {
            Minimum = min, Maximum = max, TickStyle = TickStyle.None, SmallChange = step, LargeChange = step * 2,
            AutoSize = false, Width = 200, Height = 26, Value = Math.Clamp(get(), min, max), Margin = new Padding(0, 2, 0, 2),
        };
        var lb = new Label { Text = tb.Value + unit, Width = 56, TextAlign = ContentAlignment.MiddleLeft, Height = 28 };
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

    private NumericUpDown Num(int min, int max, Func<int> get, Action<int> set)
    {
        var n = new NumericUpDown { Minimum = min, Maximum = max, Width = 70, Value = Math.Clamp(get(), min, max) };
        n.ValueChanged += (_, _) => { if (_building) return; set((int)n.Value); Changed(); };
        return n;
    }

    private static Color Col(string? hex, Color fallback)
    {
        try { return string.IsNullOrWhiteSpace(hex) ? fallback : ColorTranslator.FromHtml(hex); } catch { return fallback; }
    }

    private Control ColorRow(string label, Func<string> get, Action<string> set, bool allowNone = false)
    {
        var host = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        var sw = new Panel { Width = 60, Height = 24, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(3, 3, 8, 2) };
        void paint() => sw.BackColor = Col(get(), Color.Transparent);
        paint();
        var pick = new Button { Text = Loc.Pick("Kies…", "Pick…"), AutoSize = true, MinimumSize = new Size(70, 28) };
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
            var none = new Button { Text = Loc.Pick("Geen", "None"), AutoSize = true, MinimumSize = new Size(70, 28) };
            none.Click += (_, _) => { set(""); paint(); Changed(); };
            host.Controls.Add(none);
        }
        return Row(label, host);
    }

    private bool WidgetItemOn(string id) => id switch
    {
        "net" => _c.ShowNetUp || _c.ShowNetDown,
        "disk" => _c.ShowDisk,
        "cpu" => _c.ShowCpu,
        "gpu" => _c.ShowGpu,
        "mem" => _c.ShowMem,
        "batt" => _c.ShowBattery,
        "space" => _c.DiskSpace != DiskSpaceMode.Off,
        "cputemp" => _c.ShowCpuTemp,
        "gputemp" => _c.ShowGpuTemp,
        "ping" => _c.ShowPing,
        "cpufreq" or "diskbusy" or "disktemp" or "mobotemp" => FmtItemOn(id),
        _ => true,
    };

    /// <summary>Volgorde van de onderdelen in het taakbalk-widget; onderdelen die uit staan krijgen "(uit)" erachter.</summary>
    private Control WidgetOrderEditor()
    {
        var host = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        var list = new ListBox { Width = 250, Height = 290, IntegralHeight = false };
        var order = Tiles.WidgetOrder(_c.WidgetOrder);
        void fill()
        {
            int sel = list.SelectedIndex;
            bool was = _building;
            _building = true;
            list.Items.Clear();
            foreach (var id in order) list.Items.Add(Tiles.WidgetName(id) + (WidgetItemOn(id) ? "" : "   " + Loc.Pick("(uit)", "(off)")));
            if (list.Items.Count > 0) list.SelectedIndex = Math.Clamp(sel, 0, list.Items.Count - 1);
            _building = was;
        }
        fill();
        Dep(fill);   // ververst de "(uit)"-markering als de vinkjes veranderen
        void move(int d)
        {
            int i = list.SelectedIndex, j = i + d;
            if (i < 0 || j < 0 || j >= order.Count) return;
            (order[i], order[j]) = (order[j], order[i]);
            _c.WidgetOrder = new List<string>(order);
            list.SelectedIndex = j;
            Changed();
            list.SelectedIndex = j;
        }
        var left = new Button { Text = "▲", Width = 44, Height = 32, Margin = new Padding(0, 0, 0, 6) };
        var right = new Button { Text = "▼", Width = 44, Height = 32, Margin = new Padding(0, 0, 0, 6) };
        left.Click += (_, _) => move(-1);
        right.Click += (_, _) => move(1);
        var reset = Btn(Loc.Pick("Standaard", "Default"), 90);
        reset.Click += (_, _) => { order = Tiles.WidgetOrder(null); _c.WidgetOrder = null; Changed(); };
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(8, 0, 0, 0) };
        buttons.Controls.Add(left);
        buttons.Controls.Add(right);
        buttons.Controls.Add(reset);
        host.Controls.Add(list);
        host.Controls.Add(buttons);
        return host;
    }

    // ---------- tabblad Algemeen ----------
    private TabPage GeneralTab()
    {
        var p = Page(Loc.Pick("Algemeen", "General"), out var tab);

        p.Controls.Add(Head(Loc.S("language")));
        p.Controls.Add(Seg(new (string, string)[] { (Loc.S("dutch"), "nl"), (Loc.S("english"), "en") }, () => _c.Language, v =>
        {
            _c.Language = v;
            Loc.Lang = v;
            _apply();
            BeginInvoke(new Action(() =>
            {
                Text = Loc.Pick("TaskbarStats — instellingen", "TaskbarStats — settings");
                Build(_tabs.SelectedIndex);
            }));
        }, 110));

        p.Controls.Add(Head(Loc.Pick("Opstarten en positie", "Startup and position")));
        p.Controls.Add(Check(Loc.S("startup"), StartupManager.IsEnabled(), v => StartupManager.Set(v)));
        p.Controls.Add(Check(Loc.Pick("Positie vergrendelen", "Lock position"), _c.LockPosition, v => _c.LockPosition = v));
        p.Controls.Add(Check(Loc.Pick("Verbergen bij volledig scherm", "Hide in full screen"), _c.HideInFullscreen, v => _c.HideInFullscreen = v));
        var reset = Btn(Loc.S("resetPos"), 170);
        reset.Margin = new Padding(3, 6, 3, 3);
        reset.Click += (_, _) => _h.ResetWidget();
        p.Controls.Add(reset);

        p.Controls.Add(Head(Loc.Pick("Tooltip boven het widget", "Tooltip over the widget")));
        var delays = new (string, int)[]
        {
            (Loc.S("off"), -1), ("2 s", 2000), ("3 s", 3000), ("4 s", 4000), ("5 s", 5000), ("7 s", 7000), ("10 s", 10000),
        };
        p.Controls.Add(Row(Loc.Pick("Verschijnt na", "Appears after"), Seg(delays, () => _c.TooltipDelayMs, v => _c.TooltipDelayMs = v, 52), 130));
        p.Controls.Add(Note(Loc.Pick("Langer wachten geeft je meer tijd om met de muis op het widget te staan en rechts te klikken.",
                                     "A longer delay gives you more time to rest the mouse on the widget and right-click.")));

        p.Controls.Add(Head(Loc.Pick("Meldingen", "Notifications")));
        var notify = Check(Loc.Pick("Meldingen aan", "Notifications on"), _c.Notifications, v => _c.Notifications = v);
        p.Controls.Add(notify);
        var diskFull = Seg(new (string, int)[] { ("80%", 80), ("85%", 85), ("90%", 90), ("95%", 95) }, () => _c.DiskFullPercent, v => _c.DiskFullPercent = v, 56);
        var critSecs = Seg(new (string, int)[] { ("10 s", 10), ("30 s", 30), ("60 s", 60), ("120 s", 120) }, () => _c.CritSeconds, v => _c.CritSeconds = v, 56);
        p.Controls.Add(Row(Loc.Pick("Schijf bijna vol vanaf", "Disk almost full at"), diskFull, 230));
        p.Controls.Add(Row(Loc.Pick("Hoge belasting melden na", "Notify on high load after"), critSecs, 230));
        p.Controls.Add(Note(Loc.Pick("Hoge belasting geldt voor CPU, GPU en geheugen.", "High load applies to CPU, GPU and memory.")));
        Dep(() => { diskFull.Enabled = critSecs.Enabled = _c.Notifications; });

        p.Controls.Add(Head(Loc.Pick("Netwerkverbruik", "Network usage")));
        var limits = new List<(string, int)> { (Loc.S("off"), 0) };
        foreach (int gb in new[] { 5, 10, 25, 50, 100, 250, 500, 1000 }) limits.Add((gb >= 1000 ? "1 TB" : $"{gb} GB", gb));
        p.Controls.Add(Row(Loc.Pick("Maandlimiet", "Monthly limit"), Seg(limits, () => _c.MonthlyLimitGb, v => _c.MonthlyLimitGb = v, 52), 130));
        var log = Btn(Loc.Pick("Verbruikslog bekijken…", "View usage log…"), 170);
        log.Margin = new Padding(3, 6, 3, 3);
        log.Click += (_, _) => _h.ShowLog();
        p.Controls.Add(log);
        return tab;
    }

    private static Button Btn(string text, int minWidth = 110)
        => new() { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(minWidth, 30), Margin = new Padding(0, 0, 6, 6) };

    // ---------- tabblad Widget ----------
    private TabPage WidgetTab()
    {
        var p = Page(Loc.Pick("Widget", "Widget"), out var tab);

        p.Controls.Add(Head(Loc.S("components")));
        p.Controls.Add(Grid(
            Check(Loc.S("cpu"), _c.ShowCpu, v => _c.ShowCpu = v, ColW),
            Check(Loc.S("gpu"), _c.ShowGpu, v => _c.ShowGpu = v, ColW),
            Check(Loc.S("memory"), _c.ShowMem, v => _c.ShowMem = v, ColW),
            Check(Loc.S("upload"), _c.ShowNetUp, v => _c.ShowNetUp = v, ColW),
            Check(Loc.S("download"), _c.ShowNetDown, v => _c.ShowNetDown = v, ColW),
            Check(Loc.Pick("Batterij", "Battery"), _c.ShowBattery, v => _c.ShowBattery = v, ColW),
            Check(Loc.S("diskIo"), _c.ShowDisk, v => _c.ShowDisk = v, ColW),
            Check(Loc.S("cpuTemp"), _c.ShowCpuTemp, v => _c.ShowCpuTemp = v, ColW),
            Check(Loc.S("gpuTemp"), _c.ShowGpuTemp, v => _c.ShowGpuTemp = v, ColW),
            Check(Loc.Pick("Ping (latency)", "Ping (latency)"), _c.ShowPing, v => _c.ShowPing = v, ColW)));
        p.Controls.Add(FmtItemGrid());

        p.Controls.Add(Head(Loc.Pick("Volgorde in het widget (van links naar rechts)", "Order in the widget (left to right)")));
        p.Controls.Add(WidgetOrderEditor());

        // Bronnen: welke GPU, netwerkadapter en schijven het widget toont (hoort bij de vinkjes hierboven).
        p.Controls.Add(Head(Loc.Pick("Bronnen", "Sources")));
        var gpuItems = new List<(string, string)> { (Loc.Pick("Automatisch (drukste)", "Automatic (busiest)"), "") };
        foreach (var l in Metrics.GetGpuLuids().Where(Metrics.IsRealGpu)) gpuItems.Add((Metrics.GpuName(l), l));
        var gpuSrc = Seg(gpuItems, () => _c.GpuLuid ?? "", v => _c.GpuLuid = v == "" ? null : v, 60);
        p.Controls.Add(Row(Loc.S("gpu"), gpuSrc));

        var cpuMode = Seg(new (string, bool)[]
        {
            (Loc.Pick("Zoals Taakbeheer", "Like Task Manager"), false),
            (Loc.Pick("Incl. turbo (hoger)", "Incl. turbo (higher)"), true),
        }, () => _c.CpuUtility, v => _c.CpuUtility = v, 120);
        p.Controls.Add(Row(Loc.Pick("CPU-meting", "CPU measure"), cpuMode));

        var adapters = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 380 };
        adapters.Items.Add(Loc.S("allAdapters"));
        var adapterNames = Metrics.GetNetworkAdapters();
        foreach (var a in adapterNames) adapters.Items.Add(a);
        int ai = _c.NetworkAdapter is null ? 0 : Array.IndexOf(adapterNames, _c.NetworkAdapter) + 1;
        if (ai <= 0 && _c.NetworkAdapter is not null) { adapters.Items.Add(_c.NetworkAdapter); ai = adapters.Items.Count - 1; }
        adapters.SelectedIndex = Math.Max(0, ai);
        adapters.SelectedIndexChanged += (_, _) =>
        {
            if (_building || adapters.SelectedIndex < 0) return;
            _c.NetworkAdapter = adapters.SelectedIndex == 0 ? null : adapters.SelectedItem as string;
            Changed();
        };
        p.Controls.Add(Row(Loc.S("netAdapter"), adapters));

        var spaceMode = Seg(new (string, DiskSpaceMode)[]
        {
            (Loc.S("off"), DiskSpaceMode.Off), (Loc.S("total"), DiskSpaceMode.Total),
            (Loc.Pick("Alle apart", "Each"), DiskSpaceMode.Each), (Loc.Pick("Eén schijf", "One drive"), DiskSpaceMode.Single),
        }, () => _c.DiskSpace, v => _c.DiskSpace = v, 76);
        p.Controls.Add(Row(Loc.S("diskSpace"), spaceMode));
        var driveBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        foreach (var d in _h.Drives()) driveBox.Items.Add(d.Name);
        if (driveBox.FindStringExact(_c.DiskSpaceDrive) < 0) driveBox.Items.Add(_c.DiskSpaceDrive);
        driveBox.SelectedIndex = Math.Max(0, driveBox.FindStringExact(_c.DiskSpaceDrive));
        driveBox.SelectedIndexChanged += (_, _) => { if (_building || driveBox.SelectedItem is not string dn) return; _c.DiskSpaceDrive = dn; Changed(); };
        p.Controls.Add(Row(Loc.Pick("Welke schijf", "Which drive"), driveBox));
        p.Controls.Add(Check(Loc.Pick("Netwerkschijven meenemen (ook in dashboard en fullscreen)", "Include network drives (also in dashboard and fullscreen)"),
                             _c.IncludeNetworkDrives, v => _c.IncludeNetworkDrives = v));
        var pingHost = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 200 };
        pingHost.Items.AddRange(new object[] { "1.1.1.1", "8.8.8.8", "9.9.9.9", "google.com", "cloudflare.com" });
        pingHost.Text = _c.PingHost;
        pingHost.TextChanged += (_, _) =>
        {
            if (_building) return;
            string t = pingHost.Text.Trim();
            if (t.Length == 0) return;
            _c.PingHost = t;
            Changed();
        };
        p.Controls.Add(Row(Loc.Pick("Ping-doel", "Ping target"), pingHost));
        Dep(() =>
        {
            pingHost.Enabled = _c.ShowPing;
            gpuSrc.Enabled = _c.ShowGpu;
            adapters.Enabled = _c.ShowNetUp || _c.ShowNetDown;
            driveBox.Enabled = _c.DiskSpace == DiskSpaceMode.Single;
        });

        p.Controls.Add(Head(Loc.S("display")));
        var styles = new (string, DisplayStyle)[] { (Loc.S("digital"), DisplayStyle.Digital), (Loc.S("gauge"), DisplayStyle.Gauge), (Loc.S("bar"), DisplayStyle.Bar), (Loc.Pick("Grafiek", "Graph"), DisplayStyle.Graph) };
        var cpuStyle = Seg(styles, () => _c.CpuStyle, v => _c.CpuStyle = v);
        var gpuStyle = Seg(styles, () => _c.GpuStyle, v => _c.GpuStyle = v);
        var memStyle = Seg(styles, () => _c.MemStyle, v => _c.MemStyle = v);
        var perCore = Check(Loc.S("perCore"), _c.CpuPerCore, v => _c.CpuPerCore = v);
        var cpuNote = Note(Loc.Pick("Bij \"per core\" toont de CPU altijd balkjes; de stijl hierboven telt dan niet mee.", "With \"per core\" the CPU always shows bars; the style above is not used."));
        p.Controls.Add(Row(Loc.S("cpu"), cpuStyle));
        p.Controls.Add(perCore);
        p.Controls.Add(cpuNote);
        p.Controls.Add(Row(Loc.S("gpu"), gpuStyle));
        p.Controls.Add(Row(Loc.S("memory"), memStyle));
        var cpuTempStyle = Seg(styles, () => _c.CpuTempStyle, v => _c.CpuTempStyle = v);
        var gpuTempStyle = Seg(styles, () => _c.GpuTempStyle, v => _c.GpuTempStyle = v);
        p.Controls.Add(Row(Loc.S("cpuTemp"), cpuTempStyle));
        p.Controls.Add(Row(Loc.S("gpuTemp"), gpuTempStyle));
        var tempMerge = Check(Loc.Pick("Temperatuur klein achter de CPU-/GPU-cel (smaller)", "Temperature small after the CPU/GPU cell (narrower)"), _c.TempMerge, v => _c.TempMerge = v);
        p.Controls.Add(tempMerge);
        Dep(() =>
        {
            cpuTempStyle.Enabled = _c.ShowCpuTemp && !(_c.TempMerge && _c.ShowCpu);
            gpuTempStyle.Enabled = _c.ShowGpuTemp && !(_c.TempMerge && _c.ShowGpu);
            tempMerge.Enabled = _c.ShowCpuTemp || _c.ShowGpuTemp;
        });
        GraphSettings(p);
        var batt = Seg(new (string, BatteryPercentMode)[]
        {
            (Loc.Pick("In de batterij", "Inside"), BatteryPercentMode.Inside),
            (Loc.Pick("Ernaast", "Beside"), BatteryPercentMode.Beside),
            (Loc.Pick("Uit", "Off"), BatteryPercentMode.Off),
        }, () => _c.BatteryPercent, v => _c.BatteryPercent = v);
        p.Controls.Add(Row(Loc.Pick("Batterij: percentage", "Battery: percentage"), batt));

        var labels = Check(Loc.Pick("Labels boven", "Labels above"), _c.LabelsAbove, v => _c.LabelsAbove = v, ColW);
        var compact = Check(Loc.Pick("Compact (kort en klein)", "Compact (short and small)"), _c.Compact, v =>
        {
            _c.Compact = v;
            if (v) { _c.LabelsAbove = true; _building = true; labels.Checked = true; _building = false; }
        }, ColW);
        p.Controls.Add(Grid(labels, compact, Check(Loc.S("transparent"), _c.TransparentBackground, v => _c.TransparentBackground = v, ColW)));

        AddValueSettings(p);

        p.Controls.Add(Head(Loc.Pick("Afmetingen en lettertype", "Size and font")));
        var heights = new List<(string, int)> { (Loc.Pick("Auto", "Auto"), 0) };
        foreach (int h in new[] { 32, 36, 40, 44, 48, 56, 64 }) heights.Add(($"{h}", h));
        p.Controls.Add(Row(Loc.Pick("Hoogte (px)", "Height (px)"), Seg(heights, () => _c.AutoHeight ? 0 : _c.WidgetHeight, v =>
        {
            _c.AutoHeight = v == 0;
            if (v > 0) _c.WidgetHeight = v;
        }, 52)));

        var fonts = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
        using (var installed = new InstalledFontCollection())
            foreach (var f in installed.Families) fonts.Items.Add(f.Name);
        int fi = fonts.FindStringExact(_c.FontFamily);
        if (fi < 0) { fonts.Items.Insert(0, _c.FontFamily); fi = 0; }
        fonts.SelectedIndex = fi;
        fonts.SelectedIndexChanged += (_, _) => { if (_building || fonts.SelectedItem is not string s) return; _c.FontFamily = s; Changed(); };
        var fontRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        fontRow.Controls.Add(fonts);
        fontRow.Controls.Add(new Label { Text = Loc.Pick("  grootte (pt)", "  size (pt)"), AutoSize = false, Width = 90, Height = 28, TextAlign = ContentAlignment.MiddleLeft });
        fontRow.Controls.Add(Num(6, 16, () => _c.FontSize, v => _c.FontSize = v));
        p.Controls.Add(Row(Loc.Pick("Lettertype", "Font"), fontRow));

        var intervals = new (string, int)[] { ("0,1 s", 100), ("0,2 s", 200), ("0,25 s", 250), ("0,5 s", 500), ("1 s", 1000), ("2 s", 2000) };
        p.Controls.Add(Row(Loc.S("interval"), Seg(intervals, () => _c.RefreshMs, v => _c.RefreshMs = v, 56)));

        // Wat geen effect heeft, uitschakelen.
        Dep(() =>
        {
            cpuStyle.Enabled = _c.ShowCpu && !_c.CpuPerCore;
            perCore.Enabled = _c.ShowCpu;
            cpuNote.Visible = _c.ShowCpu && _c.CpuPerCore;
            gpuStyle.Enabled = _c.ShowGpu;
            memStyle.Enabled = _c.ShowMem;
            batt.Enabled = _c.ShowBattery;
            labels.Enabled = !_c.Compact;
        });
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
        var warn = Num(1, 99, () => _c.WarnThreshold, v => _c.WarnThreshold = v);
        var crit = Num(2, 100, () => _c.CritThreshold, v => _c.CritThreshold = v);
        p.Controls.Add(Row(Loc.Pick("Waarschuwing vanaf (%)", "Warning from (%)"), warn));
        p.Controls.Add(Row(Loc.Pick("Kritiek vanaf (%)", "Critical from (%)"), crit));
        // Waarschuwing moet lager blijven dan kritiek.
        Dep(() =>
        {
            warn.Maximum = Math.Max(1, Math.Min(99, _c.CritThreshold - 1));
            crit.Minimum = Math.Min(100, Math.Max(2, _c.WarnThreshold + 1));
        });
        return tab;
    }

    // ---------- tabblad Dashboard ----------
    private TabPage DashTab()
    {
        var p = Page("Dashboard", out var tab);
        var cols = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, RowCount = 1 };
        cols.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        cols.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var left = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 0, 24, 0) };
        var right = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };

        left.Controls.Add(Head(Loc.Pick("Bureaublad-dashboard", "Desktop dashboard")));
        left.Controls.Add(Check(Loc.Pick("Dashboard tonen", "Show dashboard"), _c.ShowDashboard, v => _c.ShowDashboard = v));
        left.Controls.Add(Check(Loc.Pick("Klik-door (Ctrl+Alt+D)", "Click-through (Ctrl+Alt+D)"), _c.DashClickThrough, v => _c.DashClickThrough = v));
        left.Controls.Add(Check(Loc.Pick("Positie vergrendelen", "Lock position"), _c.DashLocked, v => _c.DashLocked = v));
        left.Controls.Add(Head(Loc.Pick("Positie", "Layer")));
        left.Controls.Add(Seg(new (string, bool)[]
        {
            (Loc.Pick("Achtergrond", "Background"), false),
            (Loc.Pick("Voorgrond", "In front"), true),
        }, () => _c.DashFront, v => _c.DashFront = v, 100));
        left.Controls.Add(Head(Loc.Pick("Doorzichtigheid", "Opacity")));
        left.Controls.Add(Slider(20, 100, 10, () => _c.DashOpacity, v => _c.DashOpacity = v, "%"));
        left.Controls.Add(Head(Loc.Pick("Schaal", "Scale")));
        left.Controls.Add(Slider(50, 300, 25, () => _c.DashScale, v => _c.DashScale = v, "%"));
        left.Controls.Add(Head(Loc.Pick("Kolommen", "Columns")));
        left.Controls.Add(Seg(new (string, int)[] { ("1", 1), ("2", 2), ("3", 3), ("4", 4) }, () => _c.DashColumns, v => _c.DashColumns = v, 48));
        var reset = Btn(Loc.Pick("Reset positie dashboard", "Reset dashboard position"), 170);
        reset.Margin = new Padding(0, 12, 0, 0);
        reset.Click += (_, _) => _h.ResetDash();
        left.Controls.Add(reset);

        right.Controls.Add(Head(Loc.Pick("Onderdelen en volgorde", "Components and order")));
        right.Controls.Add(Note(Loc.Pick("Vink aan of uit; verplaats met de knoppen.", "Tick to show or hide; move with the buttons."), 300));
        right.Controls.Add(TileEditor(() => _c.DashOrder, v => _c.DashOrder = v, id => Tiles.DashOn(_c, id), (id, on) => Tiles.SetDashOn(_c, id, on)));

        cols.Controls.Add(left, 0, 0);
        cols.Controls.Add(right, 1, 0);
        p.Controls.Add(cols);
        return tab;
    }

    // ---------- tabblad Fullscreen ----------
    private TabPage FullTab()
    {
        var p = Page("Fullscreen", out var tab);
        var cols = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, RowCount = 1 };
        cols.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        cols.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var left = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 0, 24, 0) };
        var right = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };

        left.Controls.Add(Head(Loc.Pick("Fullscreen dashboard (Ctrl+Alt+F)", "Fullscreen dashboard (Ctrl+Alt+F)")));
        left.Controls.Add(Note(Loc.Pick("Open het scherm met Ctrl+Alt+F om het effect te zien; het past zich aan het aantal onderdelen aan.",
                                        "Open the screen with Ctrl+Alt+F to see the effect; it adapts to the number of components."), 260));
        var screens = new List<(string, string?)> { (Loc.Pick("Automatisch (waar het widget staat)", "Automatic (where the widget is)"), null) };
        foreach (var sc in Screen.AllScreens)
            screens.Add(($"{sc.DeviceName.TrimStart('\\', '.')}  {sc.Bounds.Width}×{sc.Bounds.Height}{(sc.Primary ? " *" : "")}", sc.DeviceName));
        left.Controls.Add(Head(Loc.Pick("Automatische tour", "Automatic tour")));
        left.Controls.Add(Note(Loc.Pick("Klik 3× op een lege plek in het fullscreen-scherm (of druk op de spatiebalk) om alle pagina's af te lopen. Tijd per pagina:",
                                        "Click 3 times on an empty spot in the fullscreen screen (or press the space bar) to step through all pages. Time per page:"), 260));
        left.Controls.Add(Seg(new (string, int)[] { ("5 s", 5), ("10 s", 10), ("15 s", 15), ("20 s", 20), ("30 s", 30), ("60 s", 60) },
                              () => _c.TourSeconds, v => _c.TourSeconds = v, 44));
        left.Controls.Add(Head(Loc.Pick("Scherm", "Display")));
        var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
        foreach (var s in screens) cb.Items.Add(s.Item1);
        cb.SelectedIndex = Math.Max(0, screens.FindIndex(s => s.Item2 == _c.FullMonitor));
        cb.SelectedIndexChanged += (_, _) => { if (_building || cb.SelectedIndex < 0) return; _c.FullMonitor = screens[cb.SelectedIndex].Item2; Changed(); };
        left.Controls.Add(cb);

        // Miniatuur van de indeling, zodat je de volgorde meteen ziet zonder het scherm te openen.
        left.Controls.Add(Head(Loc.Pick("Voorbeeld van de indeling", "Layout preview")));
        var prev = new DoubleBufferedPanel { Width = 288, Height = 162, Margin = new Padding(0, 0, 0, 4) };
        prev.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.FromArgb(18, 18, 20));
            float k = prev.Width / 1920f;
            var visible = Tiles.Order(_c.FullOrder).Where(id => Tiles.FullOn(_c, id));
            foreach (var (ids, r) in Tiles.FullCells(visible, 1920, 1080, 76, 16))
            {
                var rr = new RectangleF(r.X * k, r.Y * k, r.Width * k, r.Height * k);
                using (var br = new SolidBrush(Color.FromArgb(48, 52, 62))) g.FillRectangle(br, rr);
                using (var pen = new Pen(Color.FromArgb(0, 132, 255), 1)) g.DrawRectangle(pen, rr.X, rr.Y, rr.Width, rr.Height);
                using var f = new Font("Segoe UI", 7.5f);
                using var tb = new SolidBrush(Color.White);
                g.DrawString(string.Join(" + ", ids.Select(Tiles.Name)), f, tb, new RectangleF(rr.X + 3, rr.Y + 2, rr.Width - 4, rr.Height - 4));
            }
        };
        Dep(prev.Invalidate);
        left.Controls.Add(prev);

        right.Controls.Add(Head(Loc.Pick("Onderdelen en volgorde", "Components and order")));
        right.Controls.Add(Note(Loc.Pick("Vink aan of uit; verplaats met de knoppen.", "Tick to show or hide; move with the buttons."), 300));
        right.Controls.Add(TileEditor(() => _c.FullOrder, v => _c.FullOrder = v, id => Tiles.FullOn(_c, id), (id, on) => Tiles.SetFullOn(_c, id, on)));

        cols.Controls.Add(left, 0, 0);
        cols.Controls.Add(right, 1, 0);
        p.Controls.Add(cols);
        return tab;
    }

    /// <summary>Lijst met vinkjes (aan/uit) en knoppen omhoog/omlaag voor de volgorde van de hoofdonderdelen.</summary>
    private Control TileEditor(Func<List<string>?> getOrder, Action<List<string>?> setOrder, Func<string, bool> on, Action<string, bool> setOn)
    {
        var host = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        var list = new CheckedListBox { Width = 230, Height = 190, CheckOnClick = true, IntegralHeight = false };
        var order = Tiles.Order(getOrder());
        void fill(int sel)
        {
            bool was = _building;
            _building = true;
            list.Items.Clear();
            foreach (var id in order) list.Items.Add(Tiles.Name(id), on(id));
            if (list.Items.Count > 0) list.SelectedIndex = Math.Clamp(sel, 0, list.Items.Count - 1);
            _building = was;
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
        var up = new Button { Text = "▲", Width = 44, Height = 32, Margin = new Padding(0, 0, 0, 6) };
        var down = new Button { Text = "▼", Width = 44, Height = 32, Margin = new Padding(0, 0, 0, 6) };
        up.Click += (_, _) => move(-1);
        down.Click += (_, _) => move(1);
        var reset = Btn(Loc.Pick("Standaard", "Default"), 90);
        reset.Click += (_, _) =>
        {
            order = Tiles.Order(null);
            setOrder(null);
            foreach (var id in Tiles.All) setOn(id, true);
            fill(0);
            Changed();
        };
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(8, 0, 0, 0) };
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
        var tab = new TabPage(Loc.Pick("Thema's", "Themes")) { Padding = new Padding(10), UseVisualStyleBackColor = true };
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        tab.Controls.Add(grid);

        var intro = Note(Loc.Pick("Een thema bevat stijl, kleuren, lettertype, hoogte, dashboard en fullscreen-indeling (geen posities of taal). Dubbelklik om toe te passen.",
                                  "A theme contains style, colors, font, height, dashboard and fullscreen layout (not positions or language). Double-click to apply."), 700);
        grid.Controls.Add(intro, 0, 0);
        grid.SetColumnSpan(intro, 2);

        var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, Margin = new Padding(0, 4, 10, 0) };
        grid.Controls.Add(list, 0, 1);

        var right = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Margin = new Padding(0, 4, 0, 0) };
        grid.Controls.Add(right, 1, 1);

        var entries = new List<(ThemeData t, bool builtIn, string? file)>();
        var preview = new Panel { Width = 360, Height = 150, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 10, 0, 0) };
        var previewText = new Label { AutoSize = false, Width = 340, Height = 64, Location = new Point(8, 84) };
        var swatches = new FlowLayoutPanel { Location = new Point(8, 8), Size = new Size(340, 70), WrapContents = true };
        preview.Controls.Add(swatches);
        preview.Controls.Add(previewText);

        void showPreview()
        {
            swatches.Controls.Clear();
            if (list.SelectedIndex < 0 || list.SelectedIndex >= entries.Count) { previewText.Text = ""; return; }
            var t = entries[list.SelectedIndex].t;
            foreach (var (name, hex) in new[]
            {
                (Loc.Pick("Achtergrond", "Background"), t.BackgroundColor), (Loc.Pick("Tekst", "Text"), t.TextColor),
                (Loc.Pick("Meter/balk", "Meter/bar"), t.AccentColor), (Loc.Pick("Waarschuwing", "Warning"), t.WarnColor),
                (Loc.Pick("Kritiek", "Critical"), t.CritColor), (Loc.Pick("Rand", "Border"), t.BorderColor),
            })
            {
                var sw = new Panel { Width = 50, Height = 30, BorderStyle = BorderStyle.FixedSingle, BackColor = Col(hex, SystemColors.Control) };
                new ToolTip().SetToolTip(sw, name + " " + hex);
                swatches.Controls.Add(sw);
            }
            string st(DisplayStyle d) => d switch { DisplayStyle.Gauge => Loc.S("gauge"), DisplayStyle.Bar => Loc.S("bar"), DisplayStyle.Graph => Loc.Pick("Grafiek", "Graph"), _ => Loc.S("digital") };
            var hidden = t.DashHidden is { Count: > 0 } h ? string.Join(", ", h.Select(Tiles.Name)) : Loc.Pick("geen", "none");
            previewText.Text = $"{t.FontFamily} {t.FontSize} pt · CPU {st(t.CpuStyle)} · {(t.Compact ? Loc.Pick("compact", "compact") : Loc.Pick("normaal", "normal"))}\r\n"
                             + Loc.Pick("Dashboard: ", "Dashboard: ") + t.DashColumns + Loc.Pick(" kolommen, uit: ", " columns, off: ") + hidden;
        }

        void reload(string? select = null)
        {
            entries.Clear();
            foreach (var t in ThemeStore.BuiltIn()) entries.Add((t, true, null));
            foreach (var t in ThemeStore.User(_c)) entries.Add((t, false, ThemeStore.FileFor(_c, t.Name)));
            list.Items.Clear();
            foreach (var e in entries) list.Items.Add(e.t.Name + (e.builtIn ? "   " + Loc.Pick("(meegeleverd)", "(built-in)") : ""));
            int i = select is null ? 0 : Math.Max(0, entries.FindIndex(e => e.t.Name == select));
            if (list.Items.Count > 0) list.SelectedIndex = i;
            showPreview();
        }
        list.SelectedIndexChanged += (_, _) => showPreview();

        void applySelected()
        {
            if (list.SelectedIndex < 0) return;
            entries[list.SelectedIndex].t.ApplyTo(_c);
            _apply();
            Build(4);   // alle tabbladen tonen de nieuwe waarden
        }

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, MaximumSize = new Size(380, 0) };
        Button Add(string text, Action click)
        {
            var b = Btn(text, 116);
            b.Click += (_, _) => click();
            buttons.Controls.Add(b);
            return b;
        }
        Add(Loc.Pick("Toepassen", "Apply"), applySelected);
        Add(Loc.Pick("Opslaan als…", "Save as…"), () =>
        {
            var name = Ask(Loc.Pick("Naam van het thema (je huidige instellingen worden opgeslagen):", "Theme name (your current settings are saved):"), "");
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
            reload(name);
        });
        Add(Loc.Pick("Verwijderen", "Delete"), () =>
        {
            if (list.SelectedIndex < 0 || entries[list.SelectedIndex] is not { builtIn: false, file: { } f }) return;
            if (MessageBox.Show(this, Loc.Pick("Dit thema verwijderen?", "Delete this theme?"), Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            try { File.Delete(f); } catch { }
            reload();
        });
        Add(Loc.Pick("Importeren…", "Import…"), () =>
        {
            using var dlg = new OpenFileDialog { Filter = "TaskbarStats-thema (*.json)|*.json", Multiselect = true };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            string? last = null;
            foreach (var path in dlg.FileNames)
            {
                var t = ThemeStore.Read(path);
                if (t is null) { MessageBox.Show(this, Loc.Pick("Geen geldig thema: ", "Not a valid theme: ") + Path.GetFileName(path), Text); continue; }
                if (string.IsNullOrWhiteSpace(t.Name)) t.Name = Path.GetFileNameWithoutExtension(path);
                // Nooit een meegeleverd of bestaand eigen thema stilzwijgend overschrijven.
                string baseName = t.Name;
                for (int n = 2; ThemeStore.BuiltIn().Any(b => b.Name.Equals(t.Name, StringComparison.OrdinalIgnoreCase)) || File.Exists(ThemeStore.FileFor(_c, t.Name)); n++)
                    t.Name = $"{baseName} ({n})";
                ThemeStore.Write(ThemeStore.FileFor(_c, t.Name), t);
                last = t.Name;
            }
            reload(last);
        });
        Add(Loc.Pick("Exporteren…", "Export…"), () =>
        {
            if (list.SelectedIndex < 0) return;
            var t = entries[list.SelectedIndex].t;
            using var dlg = new SaveFileDialog { Filter = "TaskbarStats-thema (*.json)|*.json", FileName = t.Name + ".json" };
            if (dlg.ShowDialog(this) == DialogResult.OK) ThemeStore.Write(dlg.FileName, t);
        });
        Add(Loc.Pick("Themamap openen", "Open folder"), () =>
        {
            try
            {
                Directory.CreateDirectory(ThemeStore.Dir(_c));
                Process.Start(new ProcessStartInfo(ThemeStore.Dir(_c)) { UseShellExecute = true });
            }
            catch { }
        });
        list.DoubleClick += (_, _) => applySelected();

        right.Controls.Add(buttons);
        right.Controls.Add(preview);
        reload();
        return tab;
    }

    private string? Ask(string prompt, string initial)
    {
        using var f = new Form
        {
            Text = Text, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false, MinimizeBox = false, ClientSize = new Size(420, 116), Font = Font,
        };
        var lb = new Label { Text = prompt, Left = 12, Top = 12, Width = 396, Height = 20 };
        var tb = new TextBox { Left = 12, Top = 40, Width = 396, Text = initial };
        var ok = new Button { Text = "OK", Left = 240, Top = 76, Width = 80, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = Loc.Pick("Annuleren", "Cancel"), Left = 328, Top = 76, Width = 80, DialogResult = DialogResult.Cancel };
        f.Controls.AddRange(new Control[] { lb, tb, ok, cancel });
        f.AcceptButton = ok;
        f.CancelButton = cancel;
        return f.ShowDialog(this) == DialogResult.OK ? tb.Text : null;
    }
}

internal sealed class DoubleBufferedPanel : Panel
{
    public DoubleBufferedPanel() { DoubleBuffered = true; BorderStyle = BorderStyle.FixedSingle; }
}
