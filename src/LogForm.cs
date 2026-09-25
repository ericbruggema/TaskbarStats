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
        AppIcon.Apply(this);
        _usage = usage;
        Text = Loc.T("Network usage – log");
        Size = new Size(760, 520);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(560, 300);

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(8, 8, 8, 0), WrapContents = false };
        top.Controls.Add(new Label { Text = Loc.T("Period:"), AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        foreach (var d in Ranges) _range.Items.Add(Loc.T("Last {0} days", d));
        _range.SelectedIndex = 1;
        _range.SelectedIndexChanged += (_, _) => Fill();
        top.Controls.Add(_range);

        var export = new Button { Text = Loc.T("Export CSV…"), AutoSize = true };
        export.Click += (_, _) => ExportCsv();
        var clear = new Button { Text = Loc.T("Clear log…"), AutoSize = true };
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
        _grid.Columns.Add("day", Loc.T("Date"));
        _grid.Columns.Add("adapter", Loc.T("Adapter"));
        _grid.Columns.Add("down", Loc.T("Received"));
        _grid.Columns.Add("up", Loc.T("Sent"));
        _grid.Columns.Add("total", Loc.T("Total"));
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

            int i = _grid.Rows.Add(day.ToString("yyyy-MM-dd (ddd)"), Loc.T("All adapters"),
                                   Metrics.FormatBytes(d), Metrics.FormatBytes(u2), Metrics.FormatBytes(d + u2));
            _grid.Rows[i].DefaultCellStyle.Font = new Font(_grid.Font, FontStyle.Bold);
            _grid.Rows[i].DefaultCellStyle.BackColor = Color.FromArgb(235, 240, 248);

            foreach (var (name, u) in per.OrderByDescending(k => k.Value.Total))
                _grid.Rows.Add("", name, Metrics.FormatBytes(u.Down), Metrics.FormatBytes(u.Up), Metrics.FormatBytes(u.Total));
        }
        _summary.Text = Loc.T("Total in this period:  ↓ {0}   ↑ {1}   (combined {2})", Metrics.FormatBytes(down), Metrics.FormatBytes(up), Metrics.FormatBytes(down + up));
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
            Loc.T("Delete all stored usage data?"),
            Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (r != DialogResult.Yes) return;
        _usage.Clear();
        Fill();
    }
}
