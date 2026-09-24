namespace TaskbarStats;

/// <summary>De hoofdonderdelen van het bureaublad-dashboard en het fullscreen-scherm: ids, namen, volgorde en zichtbaarheid.</summary>
public static class Tiles
{
    public static readonly string[] All = { "cpu", "gpu", "mem", "net", "disk", "batt", "proc", "sys" };

    public static string Name(string id) => id switch
    {
        "cpu" => "CPU",
        "gpu" => "GPU",
        "mem" => Loc.S("memory"),
        "net" => Loc.Pick("Netwerk", "Network"),
        "disk" => Loc.S("disks"),
        "batt" => Loc.Pick("Batterij", "Battery"),
        "proc" => Loc.Pick("Zwaarste programma's", "Top programs"),
        "sys" => Loc.Pick("Systeem", "System"),
        _ => id,
    };

    /// <summary>Onderdelen van het taakbalk-widget in de standaardvolgorde (net = upload/download, space = schijfruimte).</summary>
    public static readonly string[] WidgetAll = { "net", "ping", "disk", "cpu", "gpu", "mem", "batt", "space", "cputemp", "gputemp" };

    public static string WidgetName(string id) => id switch
    {
        "net" => Loc.Pick("Netwerk (upload/download)", "Network (upload/download)"),
        "ping" => "Ping",
        "disk" => Loc.S("diskIo"),
        "cpu" => "CPU",
        "gpu" => "GPU",
        "mem" => Loc.S("memory"),
        "batt" => Loc.Pick("Batterij", "Battery"),
        "space" => Loc.S("diskSpace"),
        "cputemp" => Loc.S("cpuTemp"),
        "gputemp" => Loc.S("gpuTemp"),
        _ => id,
    };

    public static List<string> WidgetOrder(List<string>? saved)
    {
        var res = new List<string>();
        foreach (var id in saved ?? new()) if (WidgetAll.Contains(id) && !res.Contains(id)) res.Add(id);
        foreach (var id in WidgetAll) if (!res.Contains(id)) res.Add(id);
        return res;
    }

    /// <summary>Opgeslagen volgorde, aangevuld met ontbrekende en ontdaan van onbekende/dubbele ids.</summary>
    public static List<string> Order(List<string>? saved)
    {
        var res = new List<string>();
        foreach (var id in saved ?? new()) if (All.Contains(id) && !res.Contains(id)) res.Add(id);
        foreach (var id in All) if (!res.Contains(id)) res.Add(id);
        return res;
    }

    public static bool DashOn(AppSettings c, string id) => id switch
    {
        "cpu" => c.DashCpu, "gpu" => c.DashGpu, "mem" => c.DashMem, "net" => c.DashNet,
        "disk" => c.DashDisks, "batt" => c.DashBattery, "proc" => c.DashProcs, "sys" => c.DashSystem, _ => false,
    };

    public static void SetDashOn(AppSettings c, string id, bool on)
    {
        switch (id)
        {
            case "cpu": c.DashCpu = on; break; case "gpu": c.DashGpu = on; break; case "mem": c.DashMem = on; break;
            case "net": c.DashNet = on; break; case "disk": c.DashDisks = on; break; case "batt": c.DashBattery = on; break;
            case "proc": c.DashProcs = on; break; case "sys": c.DashSystem = on; break;
        }
    }

    public static bool FullOn(AppSettings c, string id) => !(c.FullHidden?.Contains(id) ?? false);

    public static void SetFullOn(AppSettings c, string id, bool on)
    {
        var h = c.FullHidden ?? new();
        h.Remove(id);
        if (!on) h.Add(id);
        c.FullHidden = h.Count == 0 ? null : h;
    }

    /// <summary>
    /// Indeling van het fullscreen-overzicht: rijen van 4 kolomeenheden (CPU is 2 breed; batterij en systeem delen een cel).
    /// Wordt gebruikt door het scherm zelf en door het voorbeeld in de instellingen.
    /// </summary>
    public static List<(string[] ids, RectangleF r)> FullCells(IEnumerable<string> visible, float width, float height, float top, float margin)
    {
        var ids = visible.ToList();
        var cells = new List<string[]>();
        bool bsDone = false;
        foreach (var id in ids)
        {
            if (id is "batt" or "sys")
            {
                if (bsDone) continue;
                bsDone = true;
                cells.Add(ids.Where(x => x is "batt" or "sys").ToArray());
            }
            else cells.Add(new[] { id });
        }
        var result = new List<(string[], RectangleF)>();
        if (cells.Count == 0) return result;

        static int Span(string[] c) => c[0] == "cpu" ? 2 : 1;
        var rows = new List<List<string[]>>();
        var left = new List<string[]>(cells);
        while (left.Count > 0)
        {
            var row = new List<string[]>(); int used = 0;
            foreach (var c in left.ToList())
                if (used + Span(c) <= 4) { row.Add(c); used += Span(c); left.Remove(c); }
            rows.Add(row);
        }
        float avail = height - margin - top;
        float firstRow = 500f * avail / (1080f - 16 - 76);
        float[] heights = rows.Count == 1 ? new[] { avail }
            : rows.Count == 2 ? new[] { firstRow, avail - firstRow - margin }
            : Enumerable.Repeat((avail - (rows.Count - 1) * margin) / rows.Count, rows.Count).ToArray();

        float y = top;
        for (int ri = 0; ri < rows.Count; ri++)
        {
            var row = rows[ri];
            int units = row.Sum(Span);
            float unit = (width - (row.Count + 1) * margin) / units;
            float x = margin;
            foreach (var c in row)
            {
                float w = Span(c) * unit;
                result.Add((c, new RectangleF(x, y, w, heights[ri])));
                x += w + margin;
            }
            y += heights[ri] + margin;
        }
        return result;
    }
}
