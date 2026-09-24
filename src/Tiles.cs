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
}
