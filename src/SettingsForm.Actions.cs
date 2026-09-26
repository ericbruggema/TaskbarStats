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
    // ---------- back-up: exporteren, importeren, terugzetten ----------
    private void BackupSection(FlowLayoutPanel p)
    {
        p.Controls.Add(Head(Loc.T("Backup and reset")));
        var export = Btn(Loc.T("Export settings…"), 170);
        export.Click += (_, _) =>
        {
            using var dlg = new SaveFileDialog { Filter = "JSON|*.json", FileName = "TaskbarStats-settings.json", Title = Loc.T("Export settings…") };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try { File.WriteAllText(dlg.FileName, _c.ToJson()); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
        var import = Btn(Loc.T("Import settings…"), 170);
        import.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog { Filter = "JSON|*.json", Title = Loc.T("Import settings…") };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            AppSettings? other = null;
            try { other = AppSettings.TryParse(File.ReadAllText(dlg.FileName)); }
            catch (Exception ex) { Diag.Swallow(ex); }
            if (other is null) { MessageBox.Show(this, Loc.T("This is not a TaskbarStats settings file."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (MessageBox.Show(this, Loc.T("Replace all your current settings with the ones in this file?"), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            ReplaceSettings(other);
        };
        var reset = Btn(Loc.T("Reset all settings…"), 170);
        reset.Click += (_, _) =>
        {
            if (MessageBox.Show(this, Loc.T("Reset every setting to its default? Your themes and usage log are kept."), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            var fresh = new AppSettings { WelcomeShown = true };
            ReplaceSettings(fresh);
        };
        var line = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0) };
        line.Controls.AddRange(new Control[] { export, import, reset });
        p.Controls.Add(line);
        p.Controls.Add(Note(Loc.T("The file contains only your settings (no personal data). Themes are separate files in the data folder.")));

        p.Controls.Add(Head(Loc.T("Data folder")));
        var open = Btn(Loc.T("Open data folder"), 170);
        open.Click += (_, _) =>
        {
            try { Directory.CreateDirectory(AppPaths.DataDir); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppPaths.DataDir) { UseShellExecute = true }); }
            catch (Exception ex) { Diag.Swallow(ex); }
        };
        p.Controls.Add(open);
        p.Controls.Add(Note(AppPaths.Portable
            ? Loc.T("Portable mode: everything is stored in the data folder next to the program.")
            : Loc.T("For portable use (for example on a USB stick), create an empty file named portable.txt next to TaskbarStats.exe and restart; the data then moves to a data folder there.")));
    }

    /// <summary>Zet alle instellingen op die van <paramref name="other"/>, past ze toe en bouwt dit venster opnieuw op.</summary>
    private void ReplaceSettings(AppSettings other)
    {
        _c.CopyFrom(other);
        Loc.Lang = string.IsNullOrEmpty(_c.Language) ? Loc.Detect() : _c.Language;
        _apply();
        BeginInvoke(new Action(() =>
        {
            Text = Loc.T("TaskbarStats — settings");
            Build(_tabs.SelectedIndex);
        }));
    }
}
