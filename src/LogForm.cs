using System.Text;

namespace TaskbarStats;

/// <summary>Venster met het verbruikslog: per dag en per adapter ontvangen/verzonden.</summary>
public sealed class LogForm : Form
{
    private readonly UsageTracker _usage;
    private readonly ComboBox _range = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    private readonly DataGridView _grid = new();
    private readonly Label _summary = new() { AutoSize = true };
    private static readonly int[] Ranges = { 7, 30, 90, 365 };

    public LogForm(UsageTracker usage)
    {
        _usage = usage;
        Text = Loc.Pick("Netwerkverbruik – log", "Network usage – log");
        Size = new Size(760, 520);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(560, 300);

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(8, 8, 8, 0), WrapContents = false };
        top.Controls.Add(new Label { Text = Loc.Pick("Periode:", "Period:"), AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        foreach (var d in Ranges) _range.Items.Add(Loc.Pick($"Laatste {d} dagen", $"Last {d} days"));
        _range.SelectedIndex = 1;
        _range.SelectedIndexChanged += (_, _) => Fill();
        top.Controls.Add(_range);

        var export = new Button { Text = Loc.Pick("Exporteer CSV…", "Export CSV…"), AutoSize = true };
        export.Click += (_, _) => ExportCsv();
        var clear = new Button { Text = Loc.Pick("Log wissen…", "Clear log…"), AutoSize = true };
        clear.Click += (_, _) => ClearLog();
        top.Controls.Add(export);
        top.Controls.Add(clear);

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.BackgroundColor = SystemColors.Window;
        _grid.Columns.Add("day", Loc.Pick("Datum", "Date"));
        _grid.Columns.Add("adapter", Loc.Pick("Adapter", "Adapter"));
        _grid.Columns.Add("down", Loc.Pick("Ontvangen", "Received"));
        _grid.Columns.Add("up", Loc.Pick("Verzonden", "Sent"));
        _grid.Columns.Add("total", Loc.Pick("Totaal", "Total"));
        _grid.Columns["adapter"]!.FillWeight = 200;
        foreach (var n in new[] { "down", "up", "total" })
            _grid.Columns[n]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 32, Padding = new Padding(8, 8, 8, 0) };
        bottom.Controls.Add(_summary);

        Controls.Add(_grid);
        Controls.Add(bottom);
        Controls.Add(top);
        Fill();
    }

    private int Days => Ranges[Math.Max(0, _range.SelectedIndex)];

    private void Fill()
    {
        _grid.Rows.Clear();
        long down = 0, up = 0;
        foreach (var (day, per) in _usage.Days(Days))
        {
            long d = per.Values.Sum(u => u.Down), u2 = per.Values.Sum(u => u.Up);
            down += d; up += u2;

            int i = _grid.Rows.Add(day.ToString("yyyy-MM-dd (ddd)"), Loc.Pick("Alle adapters", "All adapters"),
                                   Metrics.FormatBytes(d), Metrics.FormatBytes(u2), Metrics.FormatBytes(d + u2));
            _grid.Rows[i].DefaultCellStyle.Font = new Font(_grid.Font, FontStyle.Bold);
            _grid.Rows[i].DefaultCellStyle.BackColor = Color.FromArgb(235, 240, 248);

            foreach (var (name, u) in per.OrderByDescending(k => k.Value.Total))
                _grid.Rows.Add("", name, Metrics.FormatBytes(u.Down), Metrics.FormatBytes(u.Up), Metrics.FormatBytes(u.Total));
        }
        _summary.Text = Loc.Pick($"Totaal in deze periode:  ↓ {Metrics.FormatBytes(down)}   ↑ {Metrics.FormatBytes(up)}   (samen {Metrics.FormatBytes(down + up)})",
                                 $"Total in this period:  ↓ {Metrics.FormatBytes(down)}   ↑ {Metrics.FormatBytes(up)}   (combined {Metrics.FormatBytes(down + up)})");
    }

    private void ExportCsv()
    {
        using var dlg = new SaveFileDialog { Filter = "CSV|*.csv", FileName = $"netwerkverbruik-{DateTime.Now:yyyyMMdd}.csv" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var sb = new StringBuilder("date,adapter,received_bytes,sent_bytes\n");
        foreach (var (day, per) in _usage.Days(Days))
            foreach (var (name, u) in per)
                sb.Append(day.ToString("yyyy-MM-dd")).Append(",\"").Append(name.Replace("\"", "\"\""))
                  .Append("\",").Append(u.Down).Append(',').Append(u.Up).Append('\n');
        try { File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true)); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Text); }
    }

    private void ClearLog()
    {
        var r = MessageBox.Show(this,
            Loc.Pick("Alle opgeslagen verbruiksgegevens wissen?", "Delete all stored usage data?"),
            Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (r != DialogResult.Yes) return;
        _usage.Clear();
        Fill();
    }
}
