namespace TaskbarStats;

// Tabblad Algemeen: muis, extra meldingen en updatecontrole (aangeroepen vanuit GeneralTab).
public sealed partial class SettingsForm
{
    private static IReadOnlyList<(string, MouseAction)> MouseChoices() => new (string, MouseAction)[]
    {
        (Loc.T("None"), MouseAction.None),
        (Loc.T("Task Manager"), MouseAction.TaskManager),
        (Loc.T("Dashboard"), MouseAction.Dashboard),
        (Loc.T("Fullscreen"), MouseAction.Fullscreen),
        (Loc.T("Settings"), MouseAction.Settings),
        (Loc.T("Usage log"), MouseAction.UsageLog),
        (Loc.T("Copy info"), MouseAction.CopyInfo),
        (Loc.T("Menu"), MouseAction.Menu),
    };

    private void MouseSection(FlowLayoutPanel p)
    {
        p.Controls.Add(Head(Loc.T("Mouse")));
        p.Controls.Add(new Label { Text = Loc.T("Double-click the widget:"), AutoSize = true, Margin = new Padding(3, 4, 0, 0) });
        p.Controls.Add(Seg(MouseChoices(), () => _c.DoubleClickAction, v => _c.DoubleClickAction = v, 60));
        p.Controls.Add(new Label { Text = Loc.T("Middle mouse button:"), AutoSize = true, Margin = new Padding(3, 8, 0, 0) });
        p.Controls.Add(Seg(MouseChoices(), () => _c.MiddleClickAction, v => _c.MiddleClickAction = v, 60));
        p.Controls.Add(Check(Loc.T("Widget click-through (Ctrl+Alt+W)"),
            _c.WidgetClickThrough, v => _c.WidgetClickThrough = v));
        p.Controls.Add(Note(Loc.T("With click-through every click passes through the widget; dragging, tooltip and menu stop working. Turn it off with Ctrl+Alt+W or from the tray icon (right-click).")));
    }

    private void ExtraAlertsSection(FlowLayoutPanel p)
    {
        p.Controls.Add(Head(Loc.T("More notifications (0 = off)")));
        Control numRow(string label, int max, string unit, Func<int> get, Action<int> set)
        {
            var host = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            var n = Num(0, max, get, set);
            n.Width = 80;
            host.Controls.Add(n);
            host.Controls.Add(new Label { Text = unit, AutoSize = true, Margin = new Padding(6, 6, 0, 0) });
            return Row(label, host, 230);
        }
        p.Controls.Add(numRow(Loc.T("CPU temperature from"), 120, "°C", () => _c.AlertCpuTempC, v => _c.AlertCpuTempC = v));
        p.Controls.Add(numRow(Loc.T("GPU temperature from"), 120, "°C", () => _c.AlertGpuTempC, v => _c.AlertGpuTempC = v));
        p.Controls.Add(numRow(Loc.T("Drive temperature from"), 120, "°C", () => _c.AlertDiskTempC, v => _c.AlertDiskTempC = v));
        p.Controls.Add(numRow(Loc.T("Memory use from"), 100, "%", () => _c.AlertMemPercent, v => _c.AlertMemPercent = v));
        p.Controls.Add(numRow(Loc.T("Network use per day from"), 5000, "GB", () => _c.AlertDayNetGb, v => _c.AlertDayNetGb = v));
        p.Controls.Add(Note(Loc.T("Temperature notifications turn on temperature measuring. Daily use applies to the selected network adapter (or all adapters).")));
    }

    private void UpdateSection(FlowLayoutPanel p)
    {
        p.Controls.Add(Head(Loc.T("Updates")));
        p.Controls.Add(Check(Loc.T("Check for new versions (connects to github.com)"),
            _c.CheckUpdates, v => _c.CheckUpdates = v));
        var status = new Label { AutoSize = true, MaximumSize = new Size(600, 0), Margin = new Padding(8, 8, 0, 0) };
        var now = Btn(Loc.T("Check now"), 130);
        now.Click += async (_, _) =>
        {
            now.Enabled = false;
            status.Text = Loc.T("Checking…");
            try
            {
                var r = await UpdateChecker.RunAsync(_c, AboutForm.Version, true);
                status.Text = r.Status switch
                {
                    UpdateStatus.Newer => Loc.T("New version {0} available.", r.Info!.Tag.TrimStart('v', 'V')),
                    UpdateStatus.UpToDate => Loc.T("You have the latest version."),
                    _ => Loc.T("Check failed: ") + r.Error,
                };
                if (r.Status == UpdateStatus.Newer && _c.CheckUpdates) _apply();   // menu bijwerken
            }
            catch (Exception ex) { status.Text = Loc.T("Check failed: ") + ex.Message; }
            now.Enabled = true;
        };
        var line = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        line.Controls.Add(now);
        line.Controls.Add(status);
        p.Controls.Add(line);
        p.Controls.Add(Note(Loc.T("Only a notification and a menu item; nothing is ever downloaded or installed automatically. At most once per 24 hours.")));
    }
}
