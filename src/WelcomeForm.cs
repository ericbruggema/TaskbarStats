namespace TaskbarStats;

/// <summary>Eenmalig welkomstscherm (midden van het scherm): uitleg over het programma en een sluitknop.</summary>
public sealed class WelcomeForm : Form
{
    private readonly Action<string> _setLanguage;
    private readonly CheckBox _upd = new() { AutoSize = true, Location = new Point(22, 558) };
    private readonly Label _title = new();
    private readonly RichTextBox _body = new();
    private readonly Button _close = new();
    private readonly Button _nl = new() { Text = "Nederlands", AutoSize = true };
    private readonly Button _en = new() { Text = "English", AutoSize = true };
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

        _nl.Location = new Point(20, 594);
        _en.Location = new Point(_nl.Right + 8, 594);
        _nl.Click += (_, _) => Apply("nl");
        _en.Click += (_, _) => Apply("en");

        _close.Size = new Size(130, 34);
        _close.Location = new Point(510, 590);
        _close.Click += (_, _) => Close();
        AcceptButton = _close;
        CancelButton = _close;

        _upd.Checked = checkUpdates;
        _upd.CheckedChanged += (_, _) => setCheckUpdates?.Invoke(_upd.Checked);   // standaard uit: alleen met toestemming
        Controls.AddRange(new Control[] { _title, _body, _upd, _nl, _en, _close });
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
        Text = Loc.Pick("Welkom bij TaskbarStats", "Welcome to TaskbarStats");
        _title.Text = Text;
        _close.Text = Loc.Pick("Sluiten", "Close");
        _upd.Text = Loc.Pick("Controleer op nieuwe versies (maakt verbinding met github.com)", "Check for new versions (connects to github.com)");
        _nl.Enabled = Loc.Lang == "en";
        _en.Enabled = Loc.Lang != "en";

        _body.Clear();
        if (Loc.Lang == "en") FillEn(); else FillNl();
        _body.SelectionStart = 0;
        _body.ScrollToCaret();
    }

    private void FillNl()
    {
        Para("TaskbarStats is een klein, lichtgewicht programma dat op je taakbalk laat zien hoe zwaar je computer het op dit " +
             "moment heeft. Zo zie je in één oogopslag of er iets is dat je pc vertraagt, hoeveel internet je gebruikt en hoe vol " +
             "je schijf of accu is, zonder steeds Taakbeheer te hoeven openen. Het staat nu op je taakbalk, naast het systeemvak.");

        Heading("Wat kun je zien?");
        Para("Processor (CPU): de belasting, in totaal of per kern. In de tooltip zie je ook de klokfrequentie.", bullet: true);
        Para("Videokaart (GPU): de belasting per videokaart, met het videogeheugen. Heeft je pc er twee (bijvoorbeeld een ingebouwde " +
             "en een losse), dan volgt het widget automatisch de kaart die het drukst is.", bullet: true);
        Para("Geheugen (RAM): hoeveel procent er in gebruik is en hoeveel GB.", bullet: true);
        Para("Netwerk: de snelheid van upload en download, per netwerkadapter of van alle adapters samen.", bullet: true);

        Para("De waarden komen uit dezelfde bronnen als Taakbeheer van Windows. Ook: verbruik per dag, meldingen en veel aanpasbaar (onderdelen, cijfers, meter, balk of grafiek, kleuren, thema's).");

        Heading("Zo gebruik je het");
        Para("Verplaatsen: houd de linkermuisknop ingedrukt en sleep het widget. Je kunt het overal neerzetten; met " +
             "\"Positie vergrendelen\" zet je het vast.", bullet: true);
        Para("Instellingen: klik met de rechtermuisknop op het widget en kies \"Instellingen…\": uiterlijk, kleuren, indeling en thema's, allemaal met direct effect. Het menu zelf blijft open terwijl je meerdere dingen kiest.", bullet: true);
        Para("Dashboard: zet via het menu (Bureaublad-dashboard) een groot dashboard op je bureaublad, en druk op Ctrl+Alt+F voor een " +
             "fullscreen overzicht met alle details. Klik daarin op een tegel voor meer, druk op I voor alle specificaties van je pc, of klik 3× op een lege plek (of druk op spatie) voor een automatische tour; Esc gaat terug.", bullet: true);

        Para("");
        Para("Deze uitleg vind je later terug via \"Over TaskbarStats\" in het menu (knop Welkomstscherm).");
    }

    private void FillEn()
    {
        Para("TaskbarStats is a small, lightweight program that shows on your taskbar how hard your computer is working right " +
             "now. At a glance you can see whether something is slowing down your PC, how much internet you are using and how " +
             "full your disk or battery is, without having to open Task Manager. It is on your taskbar now, next to the " +
             "notification area.");

        Heading("What can you see?");
        Para("Processor (CPU): the load, in total or per core. The tooltip also shows the clock speed.", bullet: true);
        Para("Graphics card (GPU): the load per graphics card, with video memory. If your PC has two (for example an integrated " +
             "and a dedicated one), the widget automatically follows the busiest card.", bullet: true);
        Para("Memory (RAM): the percentage in use and the amount in GB.", bullet: true);
        Para("Network: upload and download speed, per network adapter or all adapters combined.", bullet: true);

        Para("The values come from the same sources as Windows Task Manager. Also: usage per day, notifications and lots of options (components, numbers, gauge, bar or graph, colours, themes).");

        Heading("How to use it");
        Para("Move it: hold the left mouse button and drag the widget. Place it anywhere; use \"Lock position\" to fix it.", bullet: true);
        Para("Settings: right-click the widget and choose \"Settings…\": looks, colors, layout and themes, all with immediate effect. The menu itself stays open while you pick several options.", bullet: true);
        Para("Dashboard: use the menu (Desktop dashboard) to put a large dashboard on your desktop, and press Ctrl+Alt+F for a " +
             "fullscreen overview with all the details. Click a tile inside for more, press I for all your PC specifications, or click 3 times on an empty spot (or press space) for an automatic tour; Esc goes back.", bullet: true);

        Para("");
        Para("You can find this explanation again via \"About TaskbarStats\" in the menu (Welcome screen button).");
    }
}
