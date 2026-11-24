namespace TaskbarStats;

/// <summary>
/// Hulp en voorbeelden voor plugins, in de app zelf: hoe het werkt, complete recepten (antwoord, recept, resultaat) en formules met hun uitkomst.
/// Alles kan gekopieerd worden; opent de editor dit venster, dan kan een voorbeeld ook direct in de editor worden gezet.
/// De inhoud staat in <see cref="PluginHelp"/> (en wordt daar in tests echt uitgevoerd).
/// </summary>
internal sealed class PluginHelpForm : Form
{
    private readonly Action<string>? _insert;
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };

    /// <summary>Opent een bepaald tabblad (0 = hoe het werkt, 1 = receptvoorbeelden, 2 = formules, 3 = eigen programma); voor de documentatie-afbeeldingen.</summary>
    internal void SelectTab(int index) { if (index >= 0 && index < _tabs.TabPages.Count) _tabs.SelectedIndex = index; }

    private static TextBox Box(bool mono, int height = 0) => new()
    {
        Multiline = true, ReadOnly = true, WordWrap = !mono, ScrollBars = mono ? ScrollBars.Both : ScrollBars.Vertical,
        Font = mono ? new Font("Consolas", 9.5f) : new Font("Segoe UI", 9.5f), BackColor = SystemColors.Window,
        Dock = DockStyle.Fill, Height = height, TabStop = false,
    };

    private static Label Cap(string text) => new() { Text = text, AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold), Margin = new Padding(0, 8, 0, 2) };

    private static void Copy(string text)
    {
        try { Clipboard.SetText(text); }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or ThreadStateException) { Diag.Swallow(ex); }
    }

    /// <param name="insert">Gezet door de editor: zet tekst op de plek van de cursor. Zonder editor is er alleen kopiëren.</param>
    public PluginHelpForm(Action<string>? insert = null)
    {
        AppIcon.Apply(this);
        _insert = insert;
        Text = Loc.T("Plugin help and examples");
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(980, 700);
        MinimumSize = new Size(700, 520);
        Font = new Font("Segoe UI", 9f);

        var tabs = _tabs;
        tabs.TabPages.Add(HowTab());
        tabs.TabPages.Add(RecipesTab());
        tabs.TabPages.Add(FormulasTab());
        tabs.TabPages.Add(OwnTab());

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6), FlowDirection = FlowDirection.RightToLeft };
        var close = new Button { Text = Loc.T("Close"), AutoSize = true, MinimumSize = new Size(90, 30), DialogResult = DialogResult.Cancel };
        var book = new Button { Text = Loc.T("Open the handbook"), AutoSize = true, MinimumSize = new Size(150, 30) };
        book.Click += (_, _) => SettingsForm.OpenPath(SettingsForm.HandbookUrl);
        bar.Controls.AddRange(new Control[] { close, book });

        Controls.Add(tabs);
        Controls.Add(bar);
        CancelButton = close;
    }

    private TabPage HowTab()
    {
        var page = new TabPage(Loc.T("How it works")) { Padding = new Padding(10) };
        var t = Box(false);
        t.Text = PluginHelp.HowItWorks();
        t.Select(0, 0);
        page.Controls.Add(t);
        return page;
    }

    private TabPage RecipesTab()
    {
        var page = new TabPage(Loc.T("Recipe examples")) { Padding = new Padding(8) };
        var examples = PluginHelp.Recipes();
        var list = new ListBox { Dock = DockStyle.Left, Width = 280, IntegralHeight = false };
        foreach (var e in examples) list.Items.Add(e.Title);

        var explain = new Label { AutoSize = true, MaximumSize = new Size(600, 0), Margin = new Padding(0, 0, 0, 4) };
        var reply = Box(true, 80); var recipe = Box(true); var result = Box(false, 40);
        var copy = new Button { Text = Loc.T("Copy the recipe"), AutoSize = true, MinimumSize = new Size(130, 30) };
        var put = new Button { Text = Loc.T("Put the values in the editor"), AutoSize = true, MinimumSize = new Size(180, 30), Visible = _insert is not null };

        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, Padding = new Padding(12, 0, 0, 0) };
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 6, 0, 0) };
        buttons.Controls.AddRange(new Control[] { copy, put });
        grid.Controls.Add(explain, 0, 0);
        grid.Controls.Add(Cap(Loc.T("The reply of the web address (made-up data)")), 0, 1);
        grid.Controls.Add(reply, 0, 2);
        grid.Controls.Add(Cap(Loc.T("The recipe (the plugin's configuration file)")), 0, 3);
        grid.Controls.Add(recipe, 0, 4);
        grid.Controls.Add(Cap(Loc.T("What you see in the widget")), 0, 5);
        grid.Controls.Add(result, 0, 6);
        grid.Controls.Add(buttons, 0, 7);

        void show()
        {
            if (list.SelectedIndex < 0) return;
            var e = examples[list.SelectedIndex];
            explain.Text = e.Explain;
            reply.Text = e.Reply;
            recipe.Text = e.Recipe.Replace("\n", "\r\n");
            result.Text = e.Result;
        }
        list.SelectedIndexChanged += (_, _) => show();
        copy.Click += (_, _) => { if (list.SelectedIndex >= 0) Copy(examples[list.SelectedIndex].Recipe.Replace("\n", "\r\n")); };
        put.Click += (_, _) =>
        {
            if (list.SelectedIndex < 0) return;
            string r = examples[list.SelectedIndex].Recipe.Replace("\r\n", "\n");
            int i = r.IndexOf("\n[", StringComparison.Ordinal);
            _insert?.Invoke((i >= 0 ? r[(i + 1)..] : r).Replace("\n", "\r\n"));
        };
        page.Controls.Add(grid);
        page.Controls.Add(list);
        if (list.Items.Count > 0) list.SelectedIndex = 0;
        return page;
    }

    private TabPage FormulasTab()
    {
        var page = new TabPage(Loc.T("Formulas")) { Padding = new Padding(8) };
        var intro = new Label
        {
            Text = Loc.T("Formulas only calculate. Here are examples on this reply; the right column is what each one gives. Missing data is never an error: it simply shows as ?."),
            Dock = DockStyle.Top, Height = 38,
        };
        var sample = Box(true, 76);
        sample.Dock = DockStyle.Top;
        sample.Text = PluginHelp.SampleJson.Replace("\n", "\r\n");
        var view = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, Font = new Font("Segoe UI", 9.5f) };
        view.Columns.Add(Loc.T("Typed in"), 80);
        view.Columns.Add(Loc.T("Formula"), 330);
        view.Columns.Add(Loc.T("Gives"), 110);
        view.Columns.Add(Loc.T("What it does"), 330);
        // de laatste kolom vult de rest van de breedte, zodat de uitleg niet wordt afgekapt
        void fit() => view.Columns[3].Width = Math.Max(200, view.ClientSize.Width - view.Columns[0].Width - view.Columns[1].Width - view.Columns[2].Width - 4);
        view.Columns[1].Width = 290;
        view.Columns[2].Width = 96;
        view.Resize += (_, _) => fit();
        fit();
        var items = PluginHelp.Formulas();
        foreach (var e in items) view.Items.Add(new ListViewItem(new[] { e.Where == "value" ? "value =" : "format =", e.Text, e.Gives, e.Meaning }));

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
        var copy = new Button { Text = Loc.T("Copy the formula"), AutoSize = true, MinimumSize = new Size(130, 30) };
        var put = new Button { Text = Loc.T("Put it in the editor"), AutoSize = true, MinimumSize = new Size(150, 30), Visible = _insert is not null };
        copy.Click += (_, _) => { if (view.SelectedIndices.Count > 0) Copy(items[view.SelectedIndices[0]].Text); };
        put.Click += (_, _) => { if (view.SelectedIndices.Count > 0) _insert?.Invoke(items[view.SelectedIndices[0]].Text); };
        bar.Controls.AddRange(new Control[] { copy, put });

        page.Controls.Add(view);
        page.Controls.Add(bar);
        page.Controls.Add(sample);
        page.Controls.Add(intro);
        return page;
    }

    private TabPage OwnTab()
    {
        var page = new TabPage(Loc.T("Your own program")) { Padding = new Padding(8) };
        var intro = new Label
        {
            Text = Loc.T("Prefer your own script or program? Add the template 'Own script writes the values'. Your program writes a small file like this one to the plugins folder (plugins\\mytool.json, with 'type = external' in plugins\\mytool.ini) and TaskbarStats shows it. Write it again every time the values change; a file that is not renewed within 'ttl' seconds is shown as outdated."),
            Dock = DockStyle.Top, Height = 66,
        };
        var json = Box(true);
        json.Text = PluginHelp.OwnProgramJson.Replace("\n", "\r\n");
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
        var copy = new Button { Text = Loc.T("Copy the example"), AutoSize = true, MinimumSize = new Size(130, 30) };
        copy.Click += (_, _) => Copy(json.Text);
        bar.Controls.Add(copy);
        page.Controls.Add(json);
        page.Controls.Add(bar);
        page.Controls.Add(intro);
        return page;
    }
}
