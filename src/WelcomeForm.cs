namespace TaskbarStats;

/// <summary>Eenmalig welkomstscherm (midden van het scherm): uitleg over het programma en een sluitknop.</summary>
public sealed class WelcomeForm : Form
{
    private readonly Action<string> _setLanguage;
    private readonly CheckBox _upd = new() { AutoSize = true, Location = new Point(22, 558) };
    private readonly Label _title = new();
    private readonly RichTextBox _body = new();
    private readonly Button _close = new();
    private readonly List<(Button b, string code)> _langs = new();
    private readonly Font _regular = new("Segoe UI", 10f);
    private readonly Font _bold = new("Segoe UI", 10.5f, FontStyle.Bold);

    public WelcomeForm(Action<string> setLanguage, bool checkUpdates = false, Action<bool>? setCheckUpdates = null)
    {
        AppIcon.Apply(this);
        _setLanguage = setLanguage;

        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        ShowInTaskbar = true;
        ClientSize = new Size(660, 640);

        _title.Font = new Font("Segoe UI", 18, FontStyle.Bold);
        _title.AutoSize = true;
        _title.Location = new Point(20, 14);

        _body.ReadOnly = true;
        _body.BorderStyle = BorderStyle.None;
        _body.BackColor = SystemColors.Control;
        _body.ScrollBars = RichTextBoxScrollBars.Vertical;
        _body.TabStop = false;
        _body.BulletIndent = 16;
        _body.Location = new Point(22, 64);
        _body.Size = new Size(616, 486);

        int lx = 20;
        foreach (var l in Loc.Available())
        {
            var b = new Button { Text = l.Name, AutoSize = true, Location = new Point(lx, 594) };
            b.Click += (_, _) => Apply(l.Code);
            _langs.Add((b, l.Code));
            lx = b.Right + 8;
        }

        _close.Size = new Size(130, 34);
        _close.Location = new Point(510, 590);
        _close.Click += (_, _) => Close();
        AcceptButton = _close;
        CancelButton = _close;

        _upd.Checked = checkUpdates;
        _upd.CheckedChanged += (_, _) => setCheckUpdates?.Invoke(_upd.Checked);   // standaard uit: alleen met toestemming
        Controls.AddRange(new Control[] { _title, _body, _upd, _close });
        foreach (var (b, _) in _langs) Controls.Add(b);
        Fill();
        Shown += (_, _) => _close.Focus();
    }

    private void Apply(string code)
    {
        _setLanguage(code);   // zet Loc.Lang en bewaart de keuze
        Fill();
    }

    private void Para(string text, bool bold = false, bool bullet = false)
    {
        _body.SelectionStart = _body.TextLength;
        _body.SelectionLength = 0;
        _body.SelectionBullet = bullet;
        _body.SelectionFont = bold ? _bold : _regular;
        _body.SelectedText = text + "\n";
    }

    private void Heading(string text)
    {
        if (_body.TextLength > 0) Para("");
        Para(text, bold: true);
    }

    private void Fill()
    {
        Text = Loc.T("Welcome to TaskbarStats");
        _title.Text = Text;
        _close.Text = Loc.T("Close");
        _upd.Text = Loc.T("Check for new versions (connects to github.com)");
        foreach (var (b, code) in _langs) b.Enabled = code != Loc.Lang;

        _body.Clear();
        FillBody();
        _body.SelectionStart = 0;
        _body.ScrollToCaret();
    }



    private void FillBody()
    {
        Para(Loc.T("TaskbarStats is a small, lightweight program that shows on your taskbar how hard your computer is working right now. At a glance you can see whether something is slowing down your PC, how much internet you are using and how full your disk or battery is, without having to open Task Manager. It is on your taskbar now, next to the notification area."));

        Heading(Loc.T("What can you see?"));
        Para(Loc.T("Processor (CPU): the load, in total or per core. The tooltip also shows the clock speed."), bullet: true);
        Para(Loc.T("Graphics card (GPU): the load per graphics card, with video memory. If your PC has two (for example an integrated and a dedicated one), the widget automatically follows the busiest card."), bullet: true);
        Para(Loc.T("Memory (RAM): the percentage in use and the amount in GB."), bullet: true);
        Para(Loc.T("Network: upload and download speed, per network adapter or all adapters combined."), bullet: true);

        Para(Loc.T("The values come from the same sources as Windows Task Manager. Also: usage per day, notifications and lots of options (components, numbers, gauge, bar or graph, colours, themes)."));

        Heading(Loc.T("How to use it"));
        Para(Loc.T("Move it: hold the left mouse button and drag the widget. Place it anywhere; use \"Lock position\" to fix it."), bullet: true);
        Para(Loc.T("Settings: right-click the widget and choose \"Settings…\": looks, colors, layout and themes, all with immediate effect. The menu itself stays open while you pick several options."), bullet: true);
        Para(Loc.T("Dashboard: use the menu (Desktop dashboard) to put a large dashboard on your desktop, and press Ctrl+Alt+F for a fullscreen overview with all the details. Click a tile inside for more, press I for all your PC specifications, or click 3 times on an empty spot (or press space) for an automatic tour; Esc goes back."), bullet: true);

        Para(Loc.T(""));
        Para(Loc.T("You can find this explanation again via \"About TaskbarStats\" in the menu (Welcome screen button)."));
    }
}
