using System.Diagnostics;

namespace TaskbarStats;

// Tabblad Plugins: plugins aan/uit, hun configbestand bewerken, en kiezen welke waarden in het widget komen.
public sealed partial class SettingsForm
{
    private TabPage PluginsTab()
    {
        var p = Page(Loc.T("Plugins"), out var tab);
        p.Controls.Add(Head(Loc.T("Plugins (optional)")));
        p.Controls.Add(Note(Loc.T("Plugins fetch extra values (Claude usage, weather, your location, website status, your own scripts) and show them in the widget. They run in a separate helper program WITHOUT administrator rights; TaskbarStats itself only reads the results. All plugins are off by default."), 680));
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

        // ---- plugins ----
        var statusLabels = new Dictionary<string, Label>();
        foreach (var id in PluginCatalog.Ids)
        {
            string pid = id;
            var box = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0, 8, 0, 0),
                Padding = new Padding(8, 4, 8, 6), BorderStyle = BorderStyle.FixedSingle, MinimumSize = new Size(690, 0),
            };
            var on = Check(PluginCatalog.TitleOf(pid), PluginConfig.IsEnabled(pid), v =>
            {
                try { PluginConfig.SetEnabled(pid, v); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Diag.Swallow(ex); }
                if (v) PluginHost.EnsureRunning();
            });
            on.Font = new Font(Font, FontStyle.Bold);
            box.Controls.Add(on);
            box.Controls.Add(Note(PluginCatalog.DescriptionOf(pid), 660));
            var st = new Label { AutoSize = true, MaximumSize = new Size(660, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(3, 2, 3, 4) };
            statusLabels[pid] = st;
            box.Controls.Add(st);
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            var edit = Btn(Loc.T("Edit config…"), 110);
            edit.Click += (_, _) => { using var dlg = new PluginConfigForm(pid); dlg.ShowDialog(this); on.Checked = PluginConfig.IsEnabled(pid); };
            row.Controls.Add(edit);
            box.Controls.Add(row);
            p.Controls.Add(box);
        }

        // ---- waarden ----
        p.Controls.Add(Head(Loc.T("Values")));
        p.Controls.Add(Note(Loc.T("Choose which values appear in the widget and how. New values show up here after a plugin's first update."), 680));
        var itemsHost = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, MinimumSize = new Size(690, 0) };
        p.Controls.Add(itemsHost);
        string lastSig = "\0";

        void refresh()
        {
            bool installed = PluginHost.Installed, running = installed && PluginHost.IsRunning();
            helperLbl.Text = !installed ? Loc.T("Helper: not installed") : running ? Loc.T("Helper: running (without administrator rights)") : Loc.T("Helper: not running");
            start.Enabled = installed && !running;
            stop.Enabled = running;
            var data = PluginFeed.Current;
            foreach (var pid in PluginCatalog.Ids)
            {
                var lbl = statusLabels[pid];
                var s = data.Statuses.FirstOrDefault(x => x.Plugin == pid);
                bool enabled = PluginConfig.IsEnabled(pid);
                lbl.ForeColor = SystemColors.GrayText;
                if (!enabled) lbl.Text = Loc.T("Off");
                else if (s is null) lbl.Text = installed ? Loc.T("Starting… waiting for the first update") : Loc.T("Waiting for the plugin helper");
                else if (s.Status == "error") { lbl.Text = Loc.T("Problem: {0}", s.Message) + (data.Values.Any(v => v.Plugin == pid) ? "  " + Loc.T("(showing the last known values)") : ""); lbl.ForeColor = Color.Firebrick; }
                else lbl.Text = Loc.T("OK — updated {0:HH:mm:ss}", s.UpdatedUtc.ToLocalTime()) + (s.Stale ? "  " + Loc.T("(outdated)") : "") + (s.Message.Length > 0 ? "  " + s.Message : "");
            }
            string sig = string.Join("|", data.Values.Select(v => v.Id + (v.Percent is null ? "t" : "p")));
            if (sig != lastSig) { lastSig = sig; FillPluginItems(itemsHost, data.Values); }
        }
        refresh();
        var timer = new System.Windows.Forms.Timer { Interval = 2000 };
        timer.Tick += (_, _) => { if (!_building) refresh(); };
        timer.Start();
        tab.Disposed += (_, _) => timer.Dispose();
        return tab;
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

/// <summary>Eenvoudige editor voor het configbestand van één plugin (platte tekst met uitleg in commentaarregels).</summary>
internal sealed class PluginConfigForm : Form
{
    private readonly string _id;
    private readonly TextBox _text = new()
    {
        Multiline = true, AcceptsReturn = true, AcceptsTab = true, WordWrap = false, ScrollBars = ScrollBars.Both,
        Font = new Font("Consolas", 10f), Dock = DockStyle.Fill,
    };
    private readonly Label _msg = new() { AutoSize = true, ForeColor = Color.Firebrick, Margin = new Padding(8, 8, 3, 3) };

    public PluginConfigForm(string id)
    {
        AppIcon.Apply(this);
        _id = id;
        Text = Loc.T("Edit config — {0}", PluginCatalog.TitleOf(id));
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(820, 640);
        MinimumSize = new Size(520, 360);
        Font = new Font("Segoe UI", 9f);
        _text.Text = PluginConfig.Read(id).Replace("\r\n", "\n").Replace("\n", "\r\n");
        _text.Select(0, 0);

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6), FlowDirection = FlowDirection.LeftToRight };
        var save = new Button { Text = Loc.T("Save"), AutoSize = true, MinimumSize = new Size(90, 30) };
        var cancel = new Button { Text = Loc.T("Cancel"), AutoSize = true, MinimumSize = new Size(90, 30), DialogResult = DialogResult.Cancel };
        var reset = new Button { Text = Loc.T("Default"), AutoSize = true, MinimumSize = new Size(90, 30) };
        var notepad = new Button { Text = Loc.T("Open in Notepad"), AutoSize = true, MinimumSize = new Size(130, 30) };
        save.Click += (_, _) => Save();
        reset.Click += (_, _) => { _text.Text = PluginConfig.DefaultText(id).Replace("\r\n", "\n").Replace("\n", "\r\n"); _text.Select(0, 0); _msg.Text = ""; };
        notepad.Click += (_, _) =>
        {
            if (!File.Exists(PluginConfig.PathOf(id))) { var err = TrySave(); if (err is not null) { _msg.Text = err; return; } }
            SettingsForm.OpenPath(PluginConfig.PathOf(id));
        };
        bar.Controls.AddRange(new Control[] { save, cancel, reset, notepad, _msg });
        Controls.Add(_text);
        Controls.Add(bar);
        AcceptButton = null;
        CancelButton = cancel;
    }

    private string? TrySave()
    {
        var err = PluginConfig.Validate(_text.Text);
        if (err is not null) return err;
        try { PluginConfig.Write(_id, _text.Text); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ex.Message; }
        return null;
    }

    private void Save()
    {
        var err = TrySave();
        if (err is not null) { _msg.Text = err; return; }
        if (PluginConfig.IsEnabledIn(_text.Text)) PluginHost.EnsureRunning();
        DialogResult = DialogResult.OK;
        Close();
    }
}
