namespace TaskbarStats;

// Tabblad Algemeen: muis, extra meldingen en updatecontrole (aangeroepen vanuit GeneralTab).
public sealed partial class SettingsForm
{
    private static IReadOnlyList<(string, MouseAction)> MouseChoices() => new (string, MouseAction)[]
    {
        (Loc.Pick("Geen", "None"), MouseAction.None),
        (Loc.Pick("Taakbeheer", "Task Manager"), MouseAction.TaskManager),
        (Loc.Pick("Dashboard", "Dashboard"), MouseAction.Dashboard),
        (Loc.Pick("Fullscreen", "Fullscreen"), MouseAction.Fullscreen),
        (Loc.Pick("Instellingen", "Settings"), MouseAction.Settings),
        (Loc.Pick("Verbruikslog", "Usage log"), MouseAction.UsageLog),
        (Loc.Pick("Kopieer info", "Copy info"), MouseAction.CopyInfo),
        (Loc.Pick("Menu", "Menu"), MouseAction.Menu),
    };

    private void MouseSection(FlowLayoutPanel p)
    {
        p.Controls.Add(Head(Loc.Pick("Muis", "Mouse")));
        p.Controls.Add(new Label { Text = Loc.Pick("Dubbelklik op het widget:", "Double-click the widget:"), AutoSize = true, Margin = new Padding(3, 4, 0, 0) });
        p.Controls.Add(Seg(MouseChoices(), () => _c.DoubleClickAction, v => _c.DoubleClickAction = v, 60));
        p.Controls.Add(new Label { Text = Loc.Pick("Middelste muisknop:", "Middle mouse button:"), AutoSize = true, Margin = new Padding(3, 8, 0, 0) });
        p.Controls.Add(Seg(MouseChoices(), () => _c.MiddleClickAction, v => _c.MiddleClickAction = v, 60));
        p.Controls.Add(Check(Loc.Pick("Widget klik-door (Ctrl+Alt+W)", "Widget click-through (Ctrl+Alt+W)"),
            _c.WidgetClickThrough, v => _c.WidgetClickThrough = v));
        p.Controls.Add(Note(Loc.Pick("Bij klik-door valt elke klik dwars door het widget heen; slepen, tooltip en menu werken dan niet. " +
                                     "Zet het uit met Ctrl+Alt+W of via het pictogram bij de klok (rechtermuisknop).",
                                     "With click-through every click passes through the widget; dragging, tooltip and menu stop working. " +
                                     "Turn it off with Ctrl+Alt+W or from the tray icon (right-click).")));
    }

    private void ExtraAlertsSection(FlowLayoutPanel p)
    {
        p.Controls.Add(Head(Loc.Pick("Meer meldingen (0 = uit)", "More notifications (0 = off)")));
        Control numRow(string label, int max, string unit, Func<int> get, Action<int> set)
        {
            var host = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            var n = Num(0, max, get, set);
            n.Width = 80;
            host.Controls.Add(n);
            host.Controls.Add(new Label { Text = unit, AutoSize = true, Margin = new Padding(6, 6, 0, 0) });
            return Row(label, host, 230);
        }
        p.Controls.Add(numRow(Loc.Pick("CPU-temperatuur vanaf", "CPU temperature from"), 120, "°C", () => _c.AlertCpuTempC, v => _c.AlertCpuTempC = v));
        p.Controls.Add(numRow(Loc.Pick("GPU-temperatuur vanaf", "GPU temperature from"), 120, "°C", () => _c.AlertGpuTempC, v => _c.AlertGpuTempC = v));
        p.Controls.Add(numRow(Loc.Pick("Schijftemperatuur vanaf", "Drive temperature from"), 120, "°C", () => _c.AlertDiskTempC, v => _c.AlertDiskTempC = v));
        p.Controls.Add(numRow(Loc.Pick("Geheugengebruik vanaf", "Memory use from"), 100, "%", () => _c.AlertMemPercent, v => _c.AlertMemPercent = v));
        p.Controls.Add(numRow(Loc.Pick("Netwerkverbruik per dag vanaf", "Network use per day from"), 5000, "GB", () => _c.AlertDayNetGb, v => _c.AlertDayNetGb = v));
        p.Controls.Add(Note(Loc.Pick("Temperatuurmeldingen zetten het meten van temperaturen aan. Het dagverbruik geldt voor de gekozen netwerkadapter (of alle adapters).",
                                     "Temperature notifications turn on temperature measuring. Daily use applies to the selected network adapter (or all adapters).")));
    }

    private void UpdateSection(FlowLayoutPanel p)
    {
        p.Controls.Add(Head(Loc.Pick("Updates", "Updates")));
        p.Controls.Add(Check(Loc.Pick("Controleer op nieuwe versies (maakt verbinding met github.com)", "Check for new versions (connects to github.com)"),
            _c.CheckUpdates, v => _c.CheckUpdates = v));
        var status = new Label { AutoSize = true, MaximumSize = new Size(600, 0), Margin = new Padding(8, 8, 0, 0) };
        var now = Btn(Loc.Pick("Nu controleren", "Check now"), 130);
        now.Click += async (_, _) =>
        {
            now.Enabled = false;
            status.Text = Loc.Pick("Bezig…", "Checking…");
            try
            {
                var r = await UpdateChecker.RunAsync(_c, AboutForm.Version, true);
                status.Text = r.Status switch
                {
                    UpdateStatus.Newer => Loc.Pick($"Nieuwe versie {r.Info!.Tag.TrimStart('v', 'V')} beschikbaar.",
                                                   $"New version {r.Info!.Tag.TrimStart('v', 'V')} available."),
                    UpdateStatus.UpToDate => Loc.Pick("Je hebt de nieuwste versie.", "You have the latest version."),
                    _ => Loc.Pick("Controleren mislukt: ", "Check failed: ") + r.Error,
                };
                if (r.Status == UpdateStatus.Newer && _c.CheckUpdates) _apply();   // menu bijwerken
            }
            catch (Exception ex) { status.Text = Loc.Pick("Controleren mislukt: ", "Check failed: ") + ex.Message; }
            now.Enabled = true;
        };
        var line = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        line.Controls.Add(now);
        line.Controls.Add(status);
        p.Controls.Add(line);
        p.Controls.Add(Note(Loc.Pick("Alleen een melding en een menu-item; er wordt nooit iets automatisch gedownload of geïnstalleerd. Hoogstens 1× per 24 uur.",
                                     "Only a notification and a menu item; nothing is ever downloaded or installed automatically. At most once per 24 hours.")));
    }
}
