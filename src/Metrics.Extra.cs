using System.Diagnostics;
using LibreHardwareMonitor.Hardware;

namespace TaskbarStats;

/// <summary>Extra metingen voor de widget-onderdelen: schijf-actief (PDH) en schijf-/hoofdbordtemperatuur (LibreHardwareMonitor).</summary>
public sealed partial class Metrics
{
    /// <summary>Hoogste "actieve tijd" van de fysieke schijven (0-100), null zolang niet gemeten of niet gevraagd.</summary>
    public double? DiskBusyPercent { get; private set; }
    /// <summary>Temperatuur van de heetste schijf in °C; null zonder waarde (geen admin, geen sensor, niet gevraagd).</summary>
    public double? DiskTempC { get; private set; }
    /// <summary>Temperatuur van het hoofdbord in °C; null zonder waarde.</summary>
    public double? MoboTempC { get; private set; }

    private volatile bool _wantBusy, _wantDiskTemp, _wantMoboTemp;
    private bool _lhmStorage, _lhmMobo, _lhmCpuGpu;         // wat er in LibreHardwareMonitor nu echt open staat
    private PdhWildcard? _busyQuery;
    private long _extraAt;
    /// <summary>Laatste kosten in ms van de extra metingen (voor het meten van de overhead in een testkopie).</summary>
    public double ExtraCostMs { get; private set; }

    /// <summary>Zet de extra metingen aan of uit (UI-thread); het openen van sensoren gebeurt op de sampler-thread.</summary>
    public void SetExtraWanted(bool busy, bool diskTemp, bool moboTemp)
    {
        // Bewust zonder _lhmLock: die is vast zolang de sampler-thread LibreHardwareMonitor opent (kan seconden duren) en de UI-thread mag daar niet op wachten.
        if (_wantDiskTemp != diskTemp || _wantMoboTemp != moboTemp) { _lhmDirty = true; _extraAt = 0; }
        _wantBusy = busy; _wantDiskTemp = diskTemp; _wantMoboTemp = moboTemp;
        if (!diskTemp) DiskTempC = null;
        if (!moboTemp) MoboTempC = null;
        if (!busy) DiskBusyPercent = null;
    }

    // Op de sampler-thread, na UpdateTemperatures.
    private void UpdateExtras()
    {
        if (!(_wantBusy || _wantDiskTemp || _wantMoboTemp)) { if (_busyQuery is not null) { _busyQuery.Dispose(); _busyQuery = null; } return; }
        var sw = Stopwatch.StartNew();

        // Actieve tijd: één PDH-wildcard over alle fysieke schijven (100 - % Idle Time), de drukste telt.
        if (_wantBusy)
        {
            try
            {
                _busyQuery ??= PdhWildcard.TryCreate(@"\PhysicalDisk(*)\% Idle Time");
                double? best = null;
                if (_busyQuery is not null)
                    foreach (var (inst, idle) in _busyQuery.Read())
                    {
                        if (inst == "_Total") continue;
                        double busy = Math.Clamp(100 - idle, 0, 100);
                        best = best is null ? busy : Math.Max(best.Value, busy);
                    }
                DiskBusyPercent = best;
            }
            catch { DiskBusyPercent = null; }
        }
        else if (_busyQuery is not null) { _busyQuery.Dispose(); _busyQuery = null; }

        // Schijf- en hoofdbordtemperatuur: hooguit elke 10 s (SMART/SuperIO uitlezen is duur).
        long now = Environment.TickCount64;
        if ((_wantDiskTemp || _wantMoboTemp) && now - _extraAt >= 10_000)
        {
            _extraAt = now;
            lock (_lhmLock)
            {
                double? disk = null, mobo = null;
                if (_lhm is not null)
                    try
                    {
                        foreach (var hw in _lhm.Hardware)
                        {
                            if (hw.HardwareType == HardwareType.Storage && _wantDiskTemp && _lhmStorage)
                            {
                                try { hw.Update(); } catch { }
                                foreach (var s in hw.Sensors)
                                    if (s.SensorType == SensorType.Temperature && s.Value is float v && v > 0 && v < 150)
                                        disk = disk is null ? v : Math.Max(disk.Value, v);   // de heetste schijf
                            }
                            else if (hw.HardwareType == HardwareType.Motherboard && _wantMoboTemp && _lhmMobo)
                                mobo = MoboTemp(hw);
                        }
                    }
                    catch { }
                DiskTempC = disk;
                MoboTempC = mobo;
            }
        }
        ExtraCostMs = sw.Elapsed.TotalMilliseconds;
    }

    // Hoofdbord: de sensor-chip (SubHardware) heeft sensors als "Motherboard", "System", "Chipset"; "CPU ..." is de processor zelf en telt niet mee.
    private static double? MoboTemp(IHardware board)
    {
        double? best = null;
        void visit(IHardware h)
        {
            try { h.Update(); } catch { }
            foreach (var s in h.Sensors)
            {
                if (s.SensorType != SensorType.Temperature || s.Value is not float v || v < 1 || v > 150) continue;
                string n = s.Name;
                if (n.Contains("Motherboard", StringComparison.OrdinalIgnoreCase) || n.Contains("Mainboard", StringComparison.OrdinalIgnoreCase) ||
                    n.Contains("System", StringComparison.OrdinalIgnoreCase) || n.Contains("Chipset", StringComparison.OrdinalIgnoreCase) ||
                    n.Contains("PCH", StringComparison.OrdinalIgnoreCase) || n.Contains("VRM", StringComparison.OrdinalIgnoreCase))
                    best = best is null ? v : Math.Max(best.Value, v);
            }
            foreach (var sub in h.SubHardware) visit(sub);
        }
        visit(board);
        return best;
    }
}
