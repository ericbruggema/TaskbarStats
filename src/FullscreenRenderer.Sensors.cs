using System.Text.RegularExpressions;
using LibreHardwareMonitor.Hardware;

namespace TaskbarStats;

/// <summary>Sensorlijsten (LibreHardwareMonitor) van het fullscreen-scherm.</summary>
public sealed partial class FullscreenRenderer
{
    // ---------- Sensoren (LibreHardwareMonitor) ----------
    private sealed record SRow(string Name, string Value, SensorType Type, double Raw);
    private static readonly Regex NumSuffix = new(@"^(.*?)\s*#?(\d+)(\s*\(.*\))?$", RegexOptions.Compiled);

    private static bool IsGpuHw(HardwareType t) => t is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;
    private static string Fmt(SensorType t, double v) => SensorInfo.FormatValue(t, v);
    private Color TempColor(double v) => v >= 90 ? Col(Cfg.CritColor, Color.Red) : v >= 75 ? Col(Cfg.WarnColor, Color.Orange) : TextCol;

    private static int Prio(SensorType t) => t switch
    {
        SensorType.Power => 0, SensorType.Temperature => 1, SensorType.Clock => 2, SensorType.Voltage => 3, SensorType.Current => 4,
        SensorType.Fan => 5, SensorType.Control => 6, SensorType.Level => 7, SensorType.Load => 8, _ => 9,
    };

    /// <summary>Sensoren die "niet ondersteund" melden (bijv. 255 °C, -1 FPS) horen niet in de lijst.</summary>
    private static bool PlausibleValue(SensorInfo s)
    {
        double v = s.Value ?? 0;
        return s.Type switch
        {
            SensorType.Temperature => v > -30 && v < 150,
            SensorType.Fan or SensorType.Clock or SensorType.Data or SensorType.SmallData or SensorType.Throughput or SensorType.Factor => v >= 0,
            _ => true,
        };
    }

    /// <summary>Sensoren met een oplopend nummer ("Core #1..#16", "Fan #2") worden één regel met bereik.</summary>
    private static List<SRow> Rows(IEnumerable<SensorInfo> sensors)
    {
        var rows = new List<SRow>();
        var groups = sensors.Where(s => s.Value.HasValue && PlausibleValue(s)).GroupBy(s =>
        {
            var m = NumSuffix.Match(s.Name);
            string key = m.Success && m.Groups[1].Value.Length > 0 ? m.Groups[1].Value + m.Groups[3].Value : s.Name;
            return (key, s.Type, s.Hardware);
        });
        foreach (var grp in groups)
        {
            var list = grp.ToList();
            if (list.Count > 1 && NumSuffix.IsMatch(list[0].Name))
            {
                double mn = list.Min(x => x.Value!.Value), mx = list.Max(x => x.Value!.Value);
                string val = Math.Abs(mx - mn) < 1e-9 ? Fmt(grp.Key.Type, mx) : $"{Fmt(grp.Key.Type, mn)} – {Fmt(grp.Key.Type, mx)}";
                rows.Add(new SRow($"{grp.Key.Item1} (×{list.Count})", val, grp.Key.Type, mx));
            }
            else foreach (var s in list) rows.Add(new SRow(s.Name, Fmt(s.Type, s.Value!.Value), s.Type, s.Value!.Value));
        }
        return rows.OrderBy(r => Prio(r.Type)).ThenBy(r => r.Name).ToList();
    }

    private float SensorList(Graphics g, float x, float y, float w, int maxRows, List<SRow> rows)
    {
        int shown = Math.Min(maxRows, rows.Count);
        for (int i = 0; i < shown; i++)
        {
            var r = rows[i];
            float yy = y + i * 24;
            T(g, Trunc(r.Name, Math.Max(12, (int)(w / 8.5f))), _f, Dim, x, yy);
            TR(g, r.Value, _f, r.Type == SensorType.Temperature ? TempColor(r.Raw) : TextCol, x + w, yy);
        }
        if (rows.Count > shown) T(g, $"+{rows.Count - shown} …", _fs, Dim, x, y + shown * 24);
        return shown * 24 + (rows.Count > shown ? 20 : 0);
    }

    private string? SensorHint()
    {
        if (_snap.Sensors.Count > 0) return null;
        return Ticks() - _v.OpenedAt < 10000
            ? Loc.T("Loading sensors…")
            : IsAdmin()
                ? Loc.T("No sensor data available on this computer.")
                : Loc.T("No sensor data — LibreHardwareMonitor needs administrator rights.");
    }

    private static readonly bool AdminRights =
        new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent())
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    private static bool IsAdmin() => AdminRights;

    private string? CpuPower()
    {
        var p = _snap.Sensors.Where(s => s.HwType == HardwareType.Cpu && s.Type == SensorType.Power && s.Value.HasValue).ToList();
        var pick = p.FirstOrDefault(s => s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase)) ?? p.FirstOrDefault();
        return pick is null ? null : Fmt(pick.Type, pick.Value!.Value);
    }

    private List<SensorInfo> GpuSensors(string gpuName)
    {
        var all = _snap.Sensors.Where(s => IsGpuHw(s.HwType) && s.Value.HasValue).ToList();
        var hws = all.Select(s => s.Hardware).Distinct().ToList();
        string? hw = hws.FirstOrDefault(h => h.Contains(gpuName, StringComparison.OrdinalIgnoreCase) || gpuName.Contains(h, StringComparison.OrdinalIgnoreCase))
                     ?? (hws.Count == 1 ? hws[0] : null);
        return hw is null ? new List<SensorInfo>() : all.Where(s => s.Hardware == hw).ToList();
    }

    private string GpuInfo(string gpuName)
    {
        var mine = GpuSensors(gpuName);
        if (mine.Count == 0) return "";
        var parts = new List<string>();
        var temp = mine.FirstOrDefault(s => s.Type == SensorType.Temperature && s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                   ?? mine.FirstOrDefault(s => s.Type == SensorType.Temperature);
        if (temp is not null) parts.Add(Fmt(temp.Type, temp.Value!.Value));
        var pw = mine.FirstOrDefault(s => s.Type == SensorType.Power);
        if (pw is not null) parts.Add(Fmt(pw.Type, pw.Value!.Value));
        var ck = mine.FirstOrDefault(s => s.Type == SensorType.Clock && s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase));
        if (ck is not null) parts.Add(Fmt(ck.Type, ck.Value!.Value));
        var fan = mine.FirstOrDefault(s => s.Type == SensorType.Fan);
        if (fan is not null) parts.Add(Fmt(fan.Type, fan.Value!.Value));
        return string.Join("  ·  ", parts);
    }

    /// <summary>Per fysieke schijf: temperatuur en gezondheid (uit SMART via LibreHardwareMonitor).</summary>
    private List<(string name, string text)> StorageLines()
    {
        var res = new List<(string, string)>();
        foreach (var grp in _snap.Sensors.Where(s => s.HwType == HardwareType.Storage && s.Value.HasValue).GroupBy(s => s.Hardware))
        {
            var parts = new List<string>();
            var t = grp.FirstOrDefault(s => s.Type == SensorType.Temperature);
            if (t is not null) parts.Add(Fmt(t.Type, t.Value!.Value));
            var used = grp.FirstOrDefault(s => s.Name.Contains("Percentage Used", StringComparison.OrdinalIgnoreCase));
            var life = grp.FirstOrDefault(s => s.Name.Contains("Remaining Life", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("Life", StringComparison.OrdinalIgnoreCase));
            if (used is not null) parts.Add($"{100 - used.Value!.Value:0}% {Loc.T("health")}");
            else if (life is not null) parts.Add($"{life.Value!.Value:0}% {Loc.T("life")}");
            if (parts.Count > 0) res.Add((grp.Key, string.Join("  ·  ", parts)));
        }
        return res;
    }
}
