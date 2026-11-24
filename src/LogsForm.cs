namespace TaskbarStats;

/// <summary>
/// Logs en crashrapporten terugkijken: links alle bestanden (crashrapporten, diag.log, log van de plugin-host), rechts de inhoud.
/// Kopiëren voor een bug-melding (persoonlijke namen eruit), de map openen, alles als zip bewaren en rapporten wissen.
/// De bestanden zelf staan in de datamap en zijn ook zonder het programma te openen.
/// </summary>
internal sealed class LogsForm : Form
{
    private static LogsForm? _open;

    private readonly ListView _list = new()
    {
        Dock = DockStyle.Left, Width = 360, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, Font = new Font("Segoe UI", 9f),
    };

    private readonly TextBox _text = new()
    {
        Multiline = true, ReadOnly = true, WordWrap = false, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill,
        Font = new Font("Consolas", 9.5f), BackColor = SystemColors.Window,
    };

    private readonly Button _copy, _clear;
    private List<Diag.LogFile> _files = new();
    private int _current = -1;   // de getoonde regel; SelectedIndices is vóór het aanmaken van het venster nog leeg

    /// <summary>Opent het venster. Met een eigenaar (vanuit een ander venster) als dialoog; zonder (vanuit het widget) los en maar één keer.</summary>
    internal static void Open(IWin32Window? owner)
    {
        if (owner is not null) { using var f = new LogsForm(); f.ShowDialog(owner); return; }
        if (_open is { IsDisposed: false }) { _open.WindowState = FormWindowState.Normal; _open.Activate(); _open.Refresh2(); return; }
        _open = new LogsForm();
        _open.FormClosed += (_, _) => _open = null;
        _open.Show();
    }

    public LogsForm()
    {
        AppIcon.Apply(this);
        Text = Loc.T("Logs and crash reports");
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1040, 660);
        MinimumSize = new Size(760, 440);
        Font = new Font("Segoe UI", 9f);

        var intro = new Label
        {
            Text = Loc.T("Every error and crash is saved on this computer as a plain text file; nothing is ever sent anywhere. Send a file, or the zip, when you report a problem."),
            Dock = DockStyle.Top, Height = 44, Padding = new Padding(8, 8, 8, 0),
        };
        var where = new Label { Text = Diag.DisplayDir, Dock = DockStyle.Top, Height = 22, Padding = new Padding(8, 0, 8, 0), ForeColor = SystemColors.GrayText, AutoEllipsis = true };

        _list.Columns.Add(Loc.T("File"), 190);
        _list.Columns.Add(Loc.T("Changed"), 100);
        _list.Columns.Add(Loc.T("Size"), 56);
        _list.SelectedIndexChanged += (_, _) => { if (_list.SelectedIndices.Count > 0) Show(_list.SelectedIndices[0]); };

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6), FlowDirection = FlowDirection.RightToLeft };
        var close = new Button { Text = Loc.T("Close"), AutoSize = true, MinimumSize = new Size(90, 30), DialogResult = DialogResult.Cancel };
        _copy = new Button { Text = Loc.T("Copy for a bug report"), AutoSize = true, MinimumSize = new Size(160, 30) };
        var folder = new Button { Text = Loc.T("Open the folder"), AutoSize = true, MinimumSize = new Size(130, 30) };
        var zip = new Button { Text = Loc.T("Save everything as zip…"), AutoSize = true, MinimumSize = new Size(170, 30) };
        _clear = new Button { Text = Loc.T("Delete crash reports…"), AutoSize = true, MinimumSize = new Size(170, 30) };
        var refresh = new Button { Text = Loc.T("Refresh"), AutoSize = true, MinimumSize = new Size(90, 30) };
        bar.Controls.AddRange(new Control[] { close, _clear, zip, folder, _copy, refresh });

        _copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(Diag.Scrub(_text.Text) + Environment.NewLine + Environment.NewLine + AppInfo.Signature); _copy.Text = Loc.T("Copied"); }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or ThreadStateException) { Diag.Swallow(ex); }
        };
        folder.Click += (_, _) =>
        {
            Directory.CreateDirectory(Diag.Dir);
            SettingsForm.OpenPath(Directory.Exists(Diag.CrashDir) ? Diag.CrashDir : Diag.Dir);
        };
        zip.Click += (_, _) => SaveZip();
        _clear.Click += (_, _) =>
        {
            if (MessageBox.Show(this, Loc.T("Delete all saved crash reports? The log files are kept."), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            Diag.ClearCrashReports();
            Refresh2();
        };
        refresh.Click += (_, _) => Refresh2();

        // volgorde van toevoegen bepaalt de plaatsing: eerst vullen, dan de randen
        Controls.Add(_text);
        Controls.Add(_list);
        Controls.Add(bar);
        Controls.Add(where);
        Controls.Add(intro);
        CancelButton = close;
        Refresh2();
    }

    /// <summary>Leest de bestanden opnieuw (en houdt de keuze vast als dat bestand er nog is).</summary>
    internal void Refresh2()
    {
        string? keep = _current >= 0 && _current < _files.Count ? _files[_current].Path : null;
        _current = -1;
        _files = Diag.LogFiles();
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var f in _files)
        {
            string name = f.IsCrash ? f.Name.Replace("crash-", "").Replace(".txt", "") : f.Name;
            _list.Items.Add(new ListViewItem(new[] { name, f.Time.ToString("yyyy-MM-dd HH:mm"), f.Size < 1024 ? f.Size + " B" : (f.Size / 1024) + " KB" }) { ForeColor = f.IsCrash ? Color.Firebrick : SystemColors.WindowText });
        }
        _list.EndUpdate();
        _clear.Enabled = _files.Any(f => f.IsCrash);
        int sel = keep is null ? -1 : _files.FindIndex(f => f.Path == keep);
        if (sel < 0) sel = _files.FindIndex(f => f.IsCrash);   // het nieuwste rapport als er een is
        if (sel < 0 && _files.Count > 0) sel = 0;
        if (sel >= 0) { _list.Items[sel].Selected = true; _list.EnsureVisible(sel); Show(sel); }   // SelectedIndexChanged komt niet vóór het aanmaken van het venster: daarom ook direct
        else _text.Text = Loc.T("No logs yet. Errors and crashes will show up here.");
        _copy.Enabled = sel >= 0;
    }

    internal int FileCount => _files.Count;
    internal string ShownText => _text.Text;
    internal void Pick(int index) { if (index >= 0 && index < _list.Items.Count) _list.Items[index].Selected = true; }

    private void Show(int index)
    {
        if (index < 0 || index >= _files.Count || index == _current) return;
        _current = index;
        _copy.Text = Loc.T("Copy for a bug report");
        var f = _files[index];
        string t = Diag.ReadLog(f.Path);
        _text.Text = t.Length == 0 ? Loc.T("(empty)") : t.Replace("\r\n", "\n").Replace("\n", "\r\n");
        if (!f.IsCrash) { _text.SelectionStart = _text.TextLength; _text.ScrollToCaret(); }   // een log: onderaan staat het nieuwste
        else _text.Select(0, 0);
    }

    private void SaveZip()
    {
        using var dlg = new SaveFileDialog { Filter = "ZIP|*.zip", FileName = $"TaskbarStats-logs-{DateTime.Now:yyyyMMdd}.zip", Title = Loc.T("Save everything as zip…") };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            int n = Diag.ExportBundle(dlg.FileName);
            MessageBox.Show(this, Loc.T("Saved {0} files in the zip. Personal names are left out.", n), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
