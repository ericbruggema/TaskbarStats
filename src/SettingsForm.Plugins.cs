using System.Diagnostics;
using TaskbarStats.Plugins;

namespace TaskbarStats;

// Tabblad Plugins: plugins toevoegen/verwijderen (galerij), goedkeuren wat ze mogen, geheimen, configbestand bewerken en kiezen welke waarden in het widget komen.
public sealed partial class SettingsForm
{
    internal const string HandbookUrl = "https://github.com/ericbruggema/TaskbarStats/blob/main/docs/plugins.md";

    private TabPage PluginsTab()
    {
        var p = Page(Loc.T("Plugins"), out var tab);
        p.Controls.Add(Head(Loc.T("Plugins (optional)")));
        p.Controls.Add(Note(Loc.T("Plugins fetch extra values (Claude usage, weather, your location, website status, your own scripts) and show them in the widget. They run in a separate helper program WITHOUT administrator rights; TaskbarStats itself only reads the results. All plugins are off by default."), 680));
        p.Controls.Add(Note(Loc.T("A plugin only exists after you add it: choose 'Add plugin…', pick one from the gallery (or import a recipe file) and read what it is allowed to do. Plugins you have not added are not shown anywhere. Remove a plugin to delete it again."), 680));
        if (!PluginHost.Installed)
        {
            var warn = Note(Loc.T("The plugin helper is not installed (expected: {0}). Run Setup again and tick 'Optional plugins'.", PluginHost.HostExe), 680);
            warn.ForeColor = Color.Firebrick;
            p.Controls.Add(warn);
        }
        p.Controls.Add(Check(Loc.T("Show plugin values in the widget"), _c.ShowPlugins, v => _c.ShowPlugins = v));

        // ---- helper ----
        var helperRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 2) };
        var helperLbl = new Label { AutoSize = false, Width = 320, Height = 30, TextAlign = ContentAlignment.MiddleLeft };
        var start = Btn(Loc.T("Start helper"), 100);
        var stop = Btn(Loc.T("Stop helper"), 100);
        var log = Btn(Loc.T("Open log"), 90);
        start.Click += (_, _) => { PluginHost.EnsureRunning(); };
        stop.Click += (_, _) => { start.Enabled = stop.Enabled = false; Task.Run(PluginHost.Stop); };
        log.Click += (_, _) => OpenPath(PluginHost.LogPath);
        helperRow.Controls.AddRange(new Control[] { helperLbl, start, stop, log });
        p.Controls.Add(helperRow);

        // ---- toevoegen, geheimen, map, handboek ----
        var statusLabels = new Dictionary<string, Label>();
        var approveBtns = new Dictionary<string, Button>();
        var listHost = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, MinimumSize = new Size(690, 0) };
        string lastIds = "\0";
        // Wat de 2-secondentimer per plugin uit het configbestand afleidt (aan/uit, soort, goedkeuring): alleen opnieuw als er een
        // bestand veranderd is (PluginRegistry.Signature), niet bij elke tik. Zo lezen we geen tien bestanden per 2 seconden.
        string lastCfg = "\0";
        var cfgState = new Dictionary<string, (bool Enabled, bool External, bool Needs)>();

        void buildList()
        {
            bool was = _building;
            _building = true;
            listHost.SuspendLayout();
            foreach (Control c in listHost.Controls.OfType<Control>().ToList()) { listHost.Controls.Remove(c); c.Dispose(); }
            statusLabels.Clear(); approveBtns.Clear(); cfgState.Clear(); lastCfg = "\0";
            var ids = PluginRegistry.Ids();
            lastIds = string.Join("|", ids);
            if (ids.Count == 0) listHost.Controls.Add(Note(Loc.T("No plugins yet. Choose 'Add plugin…' to pick one."), 600));
            foreach (var id in ids) listHost.Controls.Add(PluginBox(id, statusLabels, approveBtns, () => lastIds = "\0"));
            listHost.ResumeLayout();
            _building = was;
        }

        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 2) };
        var add = Btn(Loc.T("Add plugin…"), 120);
        var secrets = Btn(Loc.T("Secrets…"), 100);
        var folder = Btn(Loc.T("Open plugins folder"), 150);
        var help = Btn(Loc.T("Handbook"), 90);
        var examples = Btn(Loc.T("Help and examples…"), 150);
        add.Enabled = PluginHost.Installed;
        add.Click += (_, _) =>
        {
            using var gal = new PluginGalleryForm();
            if (gal.ShowDialog(this) != DialogResult.OK || gal.AddedId is null) { buildList(); return; }
            buildList();
            if (gal.OpenEditor) { using var ed = new PluginConfigForm(gal.AddedId); ed.ShowDialog(this); }
            buildList();
        };
        secrets.Click += (_, _) => { using var s = new PluginSecretsForm(); s.ShowDialog(this); };
        folder.Click += (_, _) => { try { Directory.CreateDirectory(PluginConfig.Dir); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Diag.Swallow(ex); } OpenPath(PluginConfig.Dir); };
        help.Click += (_, _) => OpenPath(HandbookUrl);
        examples.Click += (_, _) => { using var h = new PluginHelpForm(); h.ShowDialog(this); };
        actions.Controls.AddRange(new Control[] { add, secrets, folder, examples, help });
        p.Controls.Add(actions);

        // ---- toegevoegde plugins ----
        p.Controls.Add(listHost);
        buildList();

        // ---- meldingen ----
        p.Controls.Add(Head(Loc.T("Plugin notifications")));
        p.Controls.Add(Note(Loc.T("A warning for the same problem is shown once, not again every few minutes (this is remembered, also after a restart). You can have an unchanged warning repeated, and be told shortly before a limit is reset. Notifications must be on (General tab)."), 680));
        var repeat = Seg(new (string, int)[] { (Loc.T("Only when it changes"), 0), ("30 min", 30), ("1 h", 60), ("4 h", 240) },
            () => _c.PluginAlertRepeatMin, v => _c.PluginAlertRepeatMin = v, 120);
        p.Controls.Add(Row(Loc.T("Repeat an unchanged warning"), repeat, 240));
        var soon = Seg(new (string, int)[] { (Loc.T("Off"), 0), ("2 min", 2), ("5 min", 5), ("10 min", 10), ("15 min", 15) },
            () => _c.PluginResetNotifyMin, v => _c.PluginResetNotifyMin = v, 66);
        p.Controls.Add(Row(Loc.T("Tell me before a limit is reset"), soon, 240));

        // ---- waarden ----
        p.Controls.Add(Head(Loc.T("Values")));
        p.Controls.Add(Note(Loc.T("Choose which values appear in the widget and how. New values show up here after a plugin's first update."), 680));
        var itemsHost = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, MinimumSize = new Size(690, 0) };
        p.Controls.Add(itemsHost);
        string lastSig = "\0";

        void refresh(bool force = false)
        {
            if (!force && !tab.Visible) return;   // een tabblad dat niemand ziet hoeft niets te verversen (geen bestanden lezen, geen proces zoeken)
            bool installed = PluginHost.Installed, running = installed && PluginHost.IsRunning();
            helperLbl.Text = !installed ? Loc.T("Helper: not installed") : running ? Loc.T("Helper: running (without administrator rights)") : Loc.T("Helper: not running");
            start.Enabled = installed && !running;
            stop.Enabled = running;
            string cfgNow = PluginRegistry.Signature();
            if (cfgNow != lastCfg)
            {
                if (string.Join("|", PluginRegistry.Ids()) != lastIds) buildList();
                cfgState.Clear();
                lastCfg = cfgNow;
            }
            var data = PluginFeed.Current;
            foreach (var (pid, lbl) in statusLabels)
            {
                var s = data.Statuses.FirstOrDefault(x => x.Plugin == pid);
                if (!cfgState.TryGetValue(pid, out var cs))
                {
                    string text = PluginConfig.Read(pid);
                    cfgState[pid] = cs = (PluginConfig.IsEnabledIn(text), PluginRegistry.TypeOfText(pid, text) == "external",
                        PluginRegistry.NeedsApproval(pid, text) && !PluginRegistry.IsApproved(pid, text));
                }
                bool enabled = cs.Enabled, external = cs.External, needs = cs.Needs;
                if (approveBtns.TryGetValue(pid, out var ab)) ab.Visible = needs;
                lbl.ForeColor = SystemColors.GrayText;
                if (!enabled) lbl.Text = Loc.T("Off");
                else if (needs) { lbl.Text = Loc.T("Needs your approval before it runs. Choose 'Review and approve…'."); lbl.ForeColor = Color.Firebrick; }
                else if (s is null) lbl.Text = external ? Loc.T("Waiting for your program to write plugins\\{0}.json", pid) : installed ? Loc.T("Starting… waiting for the first update") : Loc.T("Waiting for the plugin helper");
                else if (s.Status == "error") { lbl.Text = Loc.T("Problem: {0}", s.Message) + (data.Values.Any(v => v.Plugin == pid) ? "  " + Loc.T("(showing the last known values)") : ""); lbl.ForeColor = Color.Firebrick; }
                else lbl.Text = Loc.T("OK — updated {0:HH:mm:ss}", s.UpdatedUtc.ToLocalTime()) + (s.Stale ? "  " + Loc.T("(outdated)") : "") + (s.Message.Length > 0 ? "  " + s.Message : "");
            }
            string sig = string.Join("|", data.Values.Select(v => v.Id + (v.Percent is null ? "t" : "p")));
            if (sig != lastSig) { lastSig = sig; FillPluginItems(itemsHost, data.Values); }
        }
        refresh(force: true);
        var timer = new System.Windows.Forms.Timer { Interval = 2000 };
        timer.Tick += (_, _) => { if (!_building) refresh(); };
        timer.Start();
        tab.VisibleChanged += (_, _) => { if (tab.Visible && !_building) refresh(); };   // meteen bijwerken zodra het tabblad opengaat
        tab.Disposed += (_, _) => timer.Dispose();
        return tab;
    }

    /// <summary>Het blok van één toegevoegde plugin: aan/uit, uitleg, toestand en knoppen (bewerken, goedkeuren, verwijderen).</summary>
    private Control PluginBox(string pid, Dictionary<string, Label> statusLabels, Dictionary<string, Button> approveBtns, Action listChanged)
    {
        var box = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0, 8, 0, 0),
            Padding = new Padding(8, 4, 8, 6), BorderStyle = BorderStyle.FixedSingle, MinimumSize = new Size(690, 0),
        };
        string kind = PluginRegistry.TypeOf(pid) switch { "recipe" => Loc.T("recipe"), "external" => Loc.T("own program"), _ => Loc.T("built in") };
        CheckBox? on = null;
        on = Check(PluginCatalog.TitleOf(pid) + "   (" + kind + ")", PluginConfig.IsEnabled(pid), v =>
        {
            if (v && PluginRegistry.NeedsApproval(pid, PluginConfig.Read(pid)) && !PluginRegistry.IsApproved(pid, PluginConfig.Read(pid)) && !ApprovePlugin(pid))
            {
                bool was = _building; _building = true; on!.Checked = false; _building = was;
                return;
            }
            try { PluginConfig.SetEnabled(pid, v); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Diag.Swallow(ex); }
            if (v) PluginHost.EnsureRunning();
        });
        on.Font = new Font(Font, FontStyle.Bold);
        box.Controls.Add(on);
        box.Controls.Add(Note(PluginRegistry.DescriptionOf(pid), 660));
        var st = new Label { AutoSize = true, MaximumSize = new Size(660, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(3, 2, 3, 4) };
        statusLabels[pid] = st;
        box.Controls.Add(st);
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        var edit = Btn(Loc.T("Edit config…"), 110);
        edit.Click += (_, _) => { using var dlg = new PluginConfigForm(pid); dlg.ShowDialog(this); on.Checked = PluginConfig.IsEnabled(pid); };
        var approve = Btn(Loc.T("Review and approve…"), 160);
        approve.Visible = false;
        approve.Click += (_, _) => { ApprovePlugin(pid); };
        approveBtns[pid] = approve;
        var remove = Btn(Loc.T("Remove…"), 90);
        remove.Click += (_, _) =>
        {
            var ask = MessageBox.Show(this, Loc.T("Remove the plugin '{0}'? Its configuration and values are deleted. Saved secrets are kept.", PluginCatalog.TitleOf(pid)),
                Loc.T("Remove plugin"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (ask != DialogResult.Yes) return;
            PluginRegistry.Remove(pid);
            listChanged();
        };
        row.Controls.AddRange(new Control[] { edit, approve, remove });
        box.Controls.Add(row);
        return box;
    }

    /// <summary>Laat zien wat de plugin mag en legt de goedkeuring vast. Geeft true als de gebruiker het goedkeurt.</summary>
    private bool ApprovePlugin(string id)
    {
        string text = PluginConfig.Read(id);
        using var dlg = new PluginApproveForm(PluginCatalog.TitleOf(id), text);
        if (dlg.ShowDialog(this) != DialogResult.OK) return false;
        try { PluginRegistry.Approve(id, text); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Diag.Swallow(ex); return false; }
        PluginHost.EnsureRunning();
        return true;
    }

    private void FillPluginItems(FlowLayoutPanel host, IReadOnlyList<PluginValue> values)
    {
        bool was = _building;
        _building = true;
        host.SuspendLayout();
        foreach (Control c in host.Controls.OfType<Control>().ToList()) { host.Controls.Remove(c); c.Dispose(); }
        if (values.Count == 0)
            host.Controls.Add(Note(Loc.T("No values yet. Turn on a plugin above."), 600));
        string? lastPlugin = null;
        foreach (var v in values)
        {
            if (v.Plugin != lastPlugin)
            {
                lastPlugin = v.Plugin;
                host.Controls.Add(new Label { Text = v.PluginTitle, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 8, 0, 2) });
            }
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 0) };
            var vv = v;
            var show = new CheckBox { Text = $"{v.Label}   ({v.Text})", Checked = _c.PluginShown(v), Width = 330, Margin = new Padding(3, 4, 3, 2) };
            show.CheckedChanged += (_, _) => { if (_building) return; _c.PluginCfg(vv.Id).Show = show.Checked; Changed(); };
            row.Controls.Add(show);
            if (v.Percent is not null)
            {
                var styles = new (string, DisplayStyle)[] { (Loc.T("Text"), DisplayStyle.Digital), (Loc.T("Bar"), DisplayStyle.Bar), (Loc.T("Ring"), DisplayStyle.Gauge) };
                row.Controls.Add(Seg(styles, () => _c.PluginStyle(vv), s => _c.PluginCfg(vv.Id).Style = s, 56));
            }
            else row.Controls.Add(new Panel { Width = 190, Height = 1, Margin = new Padding(0) });   // houdt de kleurkiezers netjes onder elkaar
            var sw = new Panel { Width = 26, Height = 22, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(8, 5, 4, 2) };
            void paint() => sw.BackColor = Col(_c.PluginColor(vv), Color.Transparent);
            paint();
            var pick = new Button { Text = Loc.T("Colour…"), AutoSize = true, MinimumSize = new Size(70, 26) };
            pick.Click += (_, _) =>
            {
                using var dlg = new ColorDialog { Color = Col(_c.PluginColor(vv), Color.White), FullOpen = true };
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                _c.PluginCfg(vv.Id).Color = $"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}";
                paint();
                Changed();
            };
            var clear = new Button { Text = "×", Width = 28, Height = 26 };
            clear.Click += (_, _) => { _c.PluginCfg(vv.Id).Color = null; paint(); Changed(); };
            row.Controls.AddRange(new Control[] { sw, pick, clear });
            host.Controls.Add(row);
        }
        host.ResumeLayout();
        _building = was;
    }

    internal static void OpenPath(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException or InvalidOperationException) { Diag.Swallow(ex); }
    }
}

/// <summary>Eenvoudige editor voor het configbestand van één plugin (platte tekst met uitleg in commentaarregels), met een testknop die het resultaat toont.</summary>
internal sealed class PluginConfigForm : Form
{
    private readonly string _id;
    private readonly TextBox _text = new()
    {
        Multiline = true, AcceptsReturn = true, AcceptsTab = true, WordWrap = false, ScrollBars = ScrollBars.Both,
        Font = new Font("Consolas", 10f), Dock = DockStyle.Fill,
    };
    private readonly Label _msg = new() { AutoSize = true, ForeColor = Color.Firebrick, Margin = new Padding(8, 8, 3, 3) };
    private readonly Label _result = new() { Dock = DockStyle.Bottom, Height = 96, Padding = new Padding(8, 4, 8, 4), AutoEllipsis = false, BorderStyle = BorderStyle.FixedSingle };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private bool _testing;
    private PluginHelpForm? _help;

    public PluginConfigForm(string id)
    {
        AppIcon.Apply(this);
        _id = id;
        Text = Loc.T("Edit config — {0}", PluginCatalog.TitleOf(id));
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(820, 700);
        MinimumSize = new Size(520, 420);
        Font = new Font("Segoe UI", 9f);
        _text.Text = PluginConfig.MaskSecrets(PluginConfig.WithTokenSection(id, PluginConfig.Read(id)));
        _text.Select(0, 0);
        _result.Text = Loc.T("Press 'Save and test' to run the plugin now and see the result here.");
        _result.ForeColor = SystemColors.GrayText;

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6), FlowDirection = FlowDirection.LeftToRight };
        var save = new Button { Text = Loc.T("Save"), AutoSize = true, MinimumSize = new Size(90, 30) };
        var test = new Button { Text = Loc.T("Save and test"), AutoSize = true, MinimumSize = new Size(120, 30) };
        var cancel = new Button { Text = Loc.T("Cancel"), AutoSize = true, MinimumSize = new Size(90, 30), DialogResult = DialogResult.Cancel };
        var reset = new Button { Text = Loc.T("Default"), AutoSize = true, MinimumSize = new Size(90, 30), Visible = PluginCatalog.IsBuiltIn(id) };
        var notepad = new Button { Text = Loc.T("Open in Notepad"), AutoSize = true, MinimumSize = new Size(130, 30) };
        var help = new Button { Text = Loc.T("Help and examples…"), AutoSize = true, MinimumSize = new Size(150, 30), Visible = !PluginCatalog.IsBuiltIn(id) };
        help.Click += (_, _) =>
        {
            if (_help is { IsDisposed: false }) { _help.Activate(); return; }
            // zonder het venster te blokkeren: je kunt een voorbeeld kopiëren of direct op de plek van de cursor in de editor zetten
            _help = new PluginHelpForm(text => { _text.Focus(); _text.SelectedText = text; });
            _help.Show(this);
        };
        FormClosed += (_, _) => _help?.Dispose();
        save.Click += (_, _) => Save(false);
        test.Click += (_, _) => Save(true);
        reset.Click += (_, _) => { _text.Text = PluginConfig.DefaultText(id).Replace("\r\n", "\n").Replace("\n", "\r\n"); _text.Select(0, 0); _msg.Text = ""; };
        notepad.Click += (_, _) =>
        {
            if (!File.Exists(PluginConfig.PathOf(id))) { var err = TrySave(); if (err is not null) { _msg.Text = err; return; } }
            SettingsForm.OpenPath(PluginConfig.PathOf(id));
        };
        bar.Controls.AddRange(new Control[] { save, test, cancel, reset, notepad, help, _msg });
        Controls.Add(_text);
        Controls.Add(_result);
        Controls.Add(bar);
        AcceptButton = null;
        CancelButton = cancel;
        _timer.Tick += (_, _) => ShowResult();
        _timer.Start();
        FormClosed += (_, _) => _timer.Dispose();
    }

    private string? TrySave()
    {
        var err = PluginConfig.Validate(_text.Text);
        if (err is not null) return err;
        try { PluginConfig.Write(_id, PluginConfig.RestoreSecrets(_text.Text, PluginConfig.Read(_id))); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ex.Message; }
        return null;
    }

    private void Save(bool test)
    {
        var err = TrySave();
        if (err is not null) { _msg.Text = err; return; }
        _msg.Text = "";
        string saved = PluginConfig.Read(_id);
        bool enabled = PluginConfig.IsEnabledIn(saved);
        // Recepten en het script-plugin draaien pas na goedkeuring van wat ze mogen; een nieuwe of gewijzigde lijst vraagt dus opnieuw.
        if (enabled && PluginRegistry.NeedsApproval(_id, saved) && !PluginRegistry.IsApproved(_id, saved))
        {
            using var dlg = new PluginApproveForm(PluginCatalog.TitleOf(_id), saved);
            if (dlg.ShowDialog(this) == DialogResult.OK) { try { PluginRegistry.Approve(_id, saved); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Diag.Swallow(ex); } }
            else _msg.Text = Loc.T("Saved, but not approved: the plugin will not run until you approve it.");
        }
        if (enabled) PluginHost.EnsureRunning();
        if (!test) { DialogResult = DialogResult.OK; Close(); return; }
        _testing = true;
        _testStart = DateTime.UtcNow;
        ShowResult();
    }

    private DateTime _testStart;

    /// <summary>Het laatste resultaat van de plugin (de host draait hem meteen na het opslaan): toestand en de eerste waarden.</summary>
    private void ShowResult()
    {
        if (!_testing) return;
        string text = PluginConfig.Read(_id);
        if (!PluginConfig.IsEnabledIn(text)) { Set(Loc.T("The plugin is switched off. Set 'enabled = true' to test it."), SystemColors.GrayText); return; }
        if (!PluginHost.Installed) { Set(Loc.T("The plugin helper is not installed."), Color.Firebrick); return; }
        var data = PluginFeed.Current;
        var st = data.Statuses.FirstOrDefault(s => s.Plugin == _id);
        if (st is null || st.UpdatedUtc < _testStart.AddSeconds(-2)) { Set(Loc.T("Running… waiting for the result."), SystemColors.GrayText); return; }
        var vals = data.Values.Where(v => v.Plugin == _id).Take(6).Select(v => $"{v.Label}:  {v.Text}").ToList();
        if (st.Status == "error") Set(Loc.T("Problem: {0}", st.Message) + (vals.Count > 0 ? "\n" + string.Join("\n", vals) : ""), Color.Firebrick);
        else Set(Loc.T("OK — updated {0:HH:mm:ss}", st.UpdatedUtc.ToLocalTime()) + (st.Message.Length > 0 ? "\n" + st.Message : "") + "\n" + string.Join("\n", vals), Color.ForestGreen);
    }

    private void Set(string text, Color c) { if (_result.Text != text) _result.Text = text; _result.ForeColor = c; }
}

/// <summary>Zegt in gewone woorden wat een plugin mag en vraagt of dat goed is. Verandert die lijst later, dan komt dit scherm opnieuw.</summary>
internal sealed class PluginApproveForm : Form
{
    public PluginApproveForm(string title, string iniText)
    {
        AppIcon.Apply(this);
        Text = Loc.T("Review and approve — {0}", title);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(640, 400);
        Font = new Font("Segoe UI", 9f);

        var caps = PluginRegistry.DescribeCaps(iniText);
        var head = new Label { Text = Loc.T("Allow '{0}' to do the following?", title), Font = new Font(Font.FontFamily, 11f, FontStyle.Bold), AutoSize = false, Left = 14, Top = 12, Width = 612, Height = 26 };
        var intro = new Label
        {
            Text = caps.Count == 0 ? Loc.T("This plugin only works with data it already has. It does not connect to anything and does not read or run anything.")
                                   : Loc.T("It runs without administrator rights, as you, and it can only do the things listed here. Nothing else."),
            AutoSize = false, Left = 14, Top = 42, Width = 612, Height = 38,
        };
        var list = new ListView { Left = 14, Top = 84, Width = 612, Height = 220, View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable, BorderStyle = BorderStyle.FixedSingle };
        list.Columns.Add(Loc.T("What it does"), 300);
        list.Columns.Add(Loc.T("Which"), 290);
        foreach (var (kind, what, danger) in caps)
        {
            var it = new ListViewItem(new[] { kind, what });
            if (danger) it.ForeColor = Color.Firebrick;
            list.Items.Add(it);
        }
        var note = new Label
        {
            Text = Loc.T("Only approve recipes from a source you trust. If you later change the recipe so it can do more (another address, a file, a command), you will be asked again; until then it does not run."),
            AutoSize = false, Left = 14, Top = 312, Width = 612, Height = 40, ForeColor = SystemColors.GrayText,
        };
        var ok = new Button { Text = Loc.T("Allow"), DialogResult = DialogResult.OK, Left = 436, Top = 360, Width = 90, Height = 30 };
        var no = new Button { Text = Loc.T("Don't allow"), DialogResult = DialogResult.Cancel, Left = 536, Top = 360, Width = 90, Height = 30 };
        Controls.AddRange(new Control[] { head, intro, list, note, ok, no });
        AcceptButton = no;   // de veilige keuze is de standaard
        CancelButton = no;
    }
}

/// <summary>De galerij: kies een meegeleverde plugin of een recept om toe te voegen, of importeer een recept uit een bestand.</summary>
internal sealed class PluginGalleryForm : Form
{
    public string? AddedId { get; private set; }
    public bool OpenEditor { get; private set; }

    private readonly List<PluginTemplate> _templates = PluginRegistry.Templates();
    private readonly ListBox _list = new() { Dock = DockStyle.Left, Width = 270, IntegralHeight = false };
    private readonly Label _title = new() { AutoSize = true, MaximumSize = new Size(480, 0) };
    private readonly Label _desc = new() { AutoSize = true, MaximumSize = new Size(480, 0), Margin = new Padding(3, 4, 3, 4) };
    private readonly Label _needs = new() { AutoSize = true, MaximumSize = new Size(480, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(3, 4, 3, 4) };
    private readonly Label _perm = new() { AutoSize = true, MaximumSize = new Size(480, 0), Margin = new Padding(3, 4, 3, 4) };
    private readonly TextBox _name = new() { Width = 260 };
    private readonly Label _nameLbl = new() { AutoSize = true, Margin = new Padding(3, 10, 3, 0) };
    private readonly Label _warn = new() { AutoSize = true, MaximumSize = new Size(480, 0), ForeColor = Color.Firebrick, Margin = new Padding(3, 6, 3, 0) };
    private readonly Button _add = new() { AutoSize = true, MinimumSize = new Size(110, 30) };

    public PluginGalleryForm()
    {
        AppIcon.Apply(this);
        Text = Loc.T("Add plugin");
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(840, 560);
        MinimumSize = new Size(700, 460);
        Font = new Font("Segoe UI", 9f);

        foreach (var t in _templates) _list.Items.Add(t.Name + (t.BuiltIn ? "" : "   ·  " + Loc.T("recipe")));
        _list.SelectedIndexChanged += (_, _) => ShowSelected();

        var right = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(12, 8, 8, 8), AutoScroll = true };
        _title.Font = new Font(Font.FontFamily, 12f, FontStyle.Bold);
        _nameLbl.Text = Loc.T("Name (letters, digits, - and _):");
        right.Controls.AddRange(new Control[] { _title, _desc, _needs, _perm, _nameLbl, _name, _warn });

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6), FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = Loc.T("Cancel"), AutoSize = true, MinimumSize = new Size(90, 30), DialogResult = DialogResult.Cancel };
        var import = new Button { Text = Loc.T("Import from file…"), AutoSize = true, MinimumSize = new Size(130, 30) };
        _add.Text = Loc.T("Add");
        _add.Click += (_, _) => AddSelected();
        import.Click += (_, _) => ImportFile();
        bar.Controls.AddRange(new Control[] { cancel, _add, import });

        Controls.Add(right);
        Controls.Add(_list);
        Controls.Add(bar);
        CancelButton = cancel;
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
    }

    /// <summary>Voor de documentatie-afbeeldingen: kies een sjabloon.</summary>
    internal void SelectTemplate(string id)
    {
        int i = _templates.FindIndex(t => t.Id == id);
        if (i >= 0) _list.SelectedIndex = i;
    }

    private PluginTemplate? Selected => _list.SelectedIndex >= 0 && _list.SelectedIndex < _templates.Count ? _templates[_list.SelectedIndex] : null;

    private void ShowSelected()
    {
        var t = Selected;
        if (t is null) return;
        _title.Text = t.Name;
        _desc.Text = t.Description;
        _needs.Text = t.Needs.Length > 0 ? Loc.T("You need: {0}", t.Needs) : "";
        _needs.Visible = t.Needs.Length > 0;
        var caps = t.BuiltIn && t.Id != "script" ? new List<(string Kind, string What, bool Danger)>() : PluginRegistry.DescribeCaps(t.Text);
        if (t.BuiltIn && t.Id != "script") _perm.Text = Loc.T("Built into TaskbarStats. It only contacts the addresses it needs for its job (see the description).");
        else if (caps.Count == 0) _perm.Text = Loc.T("It does not connect to anything and does not read or run anything yet.");
        else _perm.Text = Loc.T("Before it runs you must approve this:") + "\n" + string.Join("\n", caps.Select(c => "•  " + c.Kind + ": " + c.What));
        _name.Text = t.Id;
        _name.Enabled = !t.BuiltIn;   // een meegeleverde plugin heet altijd zoals hij heet
        _nameLbl.Visible = _name.Visible = !t.BuiltIn;
        bool added = t.BuiltIn && PluginRegistry.IsRegistered(t.Id);
        _warn.Text = added ? Loc.T("Already added.") : "";
        _add.Enabled = !added;
    }

    private void AddSelected()
    {
        var t = Selected;
        if (t is null) return;
        try
        {
            string id = t.BuiltIn ? t.Id : PluginRegistry.UniqueId(_name.Text);
            PluginRegistry.Add(id, t.Text);
            AddedId = id;
            OpenEditor = !t.BuiltIn;   // een recept moet je nog invullen (adres, sleutel); een meegeleverde plugin heeft goede standaardwaarden
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { _warn.Text = ex.Message; }
    }

    private void ImportFile()
    {
        using var dlg = new OpenFileDialog { Filter = Loc.T("Plugin configuration (*.ini)|*.ini"), Title = Loc.T("Import a recipe") };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            string text = File.ReadAllText(dlg.FileName, System.Text.Encoding.UTF8);
            string id = PluginRegistry.UniqueId(Path.GetFileNameWithoutExtension(dlg.FileName));
            string? err = PluginRegistry.CheckImport(id, text);
            if (err is not null) { _warn.Text = err; return; }
            var ask = MessageBox.Show(this, Loc.T("Only add recipes from a source you trust. It will not run until you have read and approved what it is allowed to do. Add it?"),
                Loc.T("Import a recipe"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (ask != DialogResult.Yes) return;
            PluginRegistry.Add(id, text);
            AddedId = id;
            OpenEditor = true;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { _warn.Text = ex.Message; }
    }
}

/// <summary>Beheer van de geheimen (API-sleutels) voor recepten: toevoegen, vervangen, verwijderen. Een bewaard geheim wordt nooit teruggetoond.</summary>
internal sealed class PluginSecretsForm : Form
{
    private readonly ListBox _list = new() { Left = 14, Top = 96, Width = 380, Height = 220, IntegralHeight = false };

    public PluginSecretsForm()
    {
        AppIcon.Apply(this);
        Text = Loc.T("Secrets");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(540, 340);
        Font = new Font("Segoe UI", 9f);

        var note = new Label
        {
            Text = Loc.T("Secrets such as an API key or token are stored encrypted for your Windows account on this pc and are never shown again. A recipe uses one by name, for example {secret:homeassistant}. Never type a secret directly into a recipe."),
            AutoSize = false, Left = 14, Top = 12, Width = 512, Height = 76,
        };
        var add = new Button { Text = Loc.T("Add or replace…"), Left = 408, Top = 96, Width = 118, Height = 30 };
        var remove = new Button { Text = Loc.T("Remove"), Left = 408, Top = 132, Width = 118, Height = 30 };
        var close = new Button { Text = Loc.T("Close"), DialogResult = DialogResult.OK, Left = 436, Top = 296, Width = 90, Height = 30 };
        add.Click += (_, _) => AddOrReplace();
        remove.Click += (_, _) =>
        {
            if (_list.SelectedItem is not string name) return;
            if (MessageBox.Show(this, Loc.T("Remove the secret '{0}'? Recipes that use it will stop working.", name), Loc.T("Secrets"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try { Secrets.Remove(AppPaths.DataDir, name); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Diag.Swallow(ex); }
            Fill();
        };
        Controls.AddRange(new Control[] { note, _list, add, remove, close });
        AcceptButton = close;
        Fill();
    }

    private void Fill()
    {
        _list.Items.Clear();
        foreach (var n in Secrets.Names(AppPaths.DataDir)) _list.Items.Add(n);
    }

    private void AddOrReplace()
    {
        using var dlg = new Form
        {
            Text = Loc.T("Add or replace a secret"), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, ClientSize = new Size(420, 190), Font = Font,
        };
        var nameLbl = new Label { Text = Loc.T("Name (letters, digits, - and _):"), Left = 14, Top = 14, AutoSize = true };
        var name = new TextBox { Left = 14, Top = 34, Width = 392, Text = _list.SelectedItem as string ?? "" };
        var valLbl = new Label { Text = Loc.T("Value (it is hidden and cannot be shown again):"), Left = 14, Top = 70, AutoSize = true };
        var val = new TextBox { Left = 14, Top = 90, Width = 392, UseSystemPasswordChar = true };
        var err = new Label { Left = 14, Top = 122, Width = 392, Height = 20, ForeColor = Color.Firebrick };
        var ok = new Button { Text = Loc.T("Save"), Left = 226, Top = 148, Width = 90, Height = 30 };
        var cancel = new Button { Text = Loc.T("Cancel"), DialogResult = DialogResult.Cancel, Left = 322, Top = 148, Width = 84, Height = 30 };
        ok.Click += (_, _) =>
        {
            string n = name.Text.Trim();
            if (!Secrets.ValidName(n)) { err.Text = Loc.T("The name may contain letters, digits, - and _ (at most 40)."); return; }
            if (val.Text.Length == 0) { err.Text = Loc.T("Type the value."); return; }
            try { Secrets.Set(AppPaths.DataDir, n, val.Text); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { err.Text = ex.Message; return; }
            dlg.DialogResult = DialogResult.OK;
        };
        dlg.Controls.AddRange(new Control[] { nameLbl, name, valLbl, val, err, ok, cancel });
        dlg.AcceptButton = ok;
        dlg.CancelButton = cancel;
        if (dlg.ShowDialog(this) == DialogResult.OK) Fill();
    }
}
