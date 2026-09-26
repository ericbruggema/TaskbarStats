using LibreHardwareMonitor.Hardware;

namespace TaskbarStats;

/// <summary>Detailpagina's van het fullscreen-scherm (CPU, GPU, geheugen, netwerk, schijven, systeem, programma's).</summary>
public sealed partial class FullscreenRenderer
{
    // ---------- Details ----------
    private void DrawDetail(Graphics g)
    {
        var R = new RectangleF(M, 76, CW - 2 * M, CH - 76 - M);
        switch (_v.Detail)
        {
            case "cpu": DetailCpu(g, R); break;
            case "gpu": DetailGpu(g, R); break;
            case "mem": DetailMem(g, R); break;
            case "net": DetailNet(g, R); break;
            case "disk": DetailDisk(g, R); break;
            case "sys": DetailSys(g, R); break;
            case "proc": DetailProcs(g, R); break;
            case "spec": DetailSpecs(g, R); break;
        }
    }

    private void DetailCpu(Graphics g, RectangleF R)
    {
        var m = _snap;
        Card(g, null, R, "CPU — " + Loc.T("details"), Trunc(CpuName(), 70));
        float gw = 1240;
        var cores = m.CpuCores;
        int ccols = cores.Length <= 8 ? 4 : cores.Length <= 24 ? 6 : 8;
        int crows = cores.Length == 0 ? 0 : (cores.Length + ccols - 1) / ccols;
        float cellH = crows == 0 ? 0 : Math.Min(130f, 400f / crows);
        float cpuGh = Math.Max(260f, R.Bottom - 16 - (R.Y + 50) - crows * cellH - 24);
        Graph(g, new RectangleF(R.X + 16, R.Y + 50, gw, cpuGh), new[] { (_c.History.Cpu, Accent) }, 100, Pct);

        if (cores.Length > 0)
        {
            int cols = ccols;
            int rows = crows;
            float y0 = R.Y + 50 + cpuGh + 24, cw = gw / cols, ch = cellH;
            for (int i = 0; i < cores.Length; i++)
            {
                float cx0 = R.X + 16 + (i % cols) * cw, cy0 = y0 + (i / cols) * ch;
                var cell = new RectangleF(cx0 + 3, cy0 + 3, cw - 6, ch - 6);
                using (var path = Rounded(cell, 8))
                    g.FillPath(GdiCache.Brush(Color.FromArgb(22, 255, 255, 255)), path);
                T(g, Loc.T("Core") + $" {i}", _fs, Dim, cell.X + 10, cell.Y + 6);
                TR(g, $"{cores[i]:0}%", _fb, Thr(cores[i]), cell.Right - 10, cell.Y + 5);
                if (i < _c.History.Cores.Count)
                    Graph(g, new RectangleF(cell.X + 8, cell.Y + 28, cell.Width - 16, cell.Height - 36), new[] { (_c.History.Cores[i], Accent) }, 100, Pct, false);
            }
        }

        float rx = R.X + gw + 40, rw = R.Right - 16 - rx;
        T(g, Loc.T("Total"), _fs, Dim, rx, R.Y + 50);
        Gauge(g, rx + 70, R.Y + 150, 56, m.CpuPercent, Thr(m.CpuPercent), $"{m.CpuPercent:0}%");
        StatRows(g, rx + 160, R.Y + 96, rw - 160, _c.History.Cpu, Pct);
        float y = R.Y + 236;
        KeyValue(g, rx, y, rw, Loc.T("Cores"), $"{cores.Length}"); y += 24;
        if (m.CpuMHz is double f) { KeyValue(g, rx, y, rw, Loc.T("Clock speed"), $"{f / 1000:0.00} GHz"); y += 24; }
        if (m.CpuTempC is double t) { KeyValue(g, rx, y, rw, Loc.T("Temperature"), $"{t:0}°C"); y += 24; }
        y += 12;
        T(g, Loc.T("Sensors"), _fs, Dim, rx, y);
        var cpuRows = Rows(_snap.Sensors.Where(x => x.HwType == HardwareType.Cpu && x.Type != SensorType.Load));
        float usedH;
        if (cpuRows.Count == 0) { T(g, SensorHint() ?? (IsAdmin() ? Loc.T("Temperature, power and clocks of this processor are not supported by the sensor library.") : Loc.T("No CPU sensors available (administrator rights needed).")), _fs, Dim, rx, y + 22); usedH = 26; }
        else usedH = SensorList(g, rx, y + 22, rw, 9, cpuRows);
        y += 22 + usedH + 14;
        T(g, Loc.T("Top programs (CPU)"), _fs, Dim, rx, y);
        ProcList(g, rx, y + 22, rw, 8, true);
    }

    private void DetailGpu(Graphics g, RectangleF R)
    {
        var gpus = Gpus();
        var m = _snap;
        Card(g, null, R, "GPU — " + Loc.T("details"), m.GpuTempC is double tt ? $"{tt:0}°C" : null);
        if (gpus.Count == 0) { T(g, Loc.T("No GPU data"), _f, Dim, R.X + 16, R.Y + 56); return; }
        float sh = (R.Height - 50) / gpus.Count;
        float col = (R.Width - 64) / 3;
        for (int i = 0; i < gpus.Count; i++)
        {
            var (luid, v) = (gpus[i].Key, gpus[i].Value);
            string gpuName = Metrics.GpuName(luid);
            float y0 = R.Y + 48 + i * sh, hh = sh - 20;
            T(g, gpuName, _fh, TextCol, R.X + 16, y0);
            TR(g, $"{v:0}%", _fbig, Thr(v), R.Right - 16, y0 - 8);
            float gh = hh - 50;
            float x1 = R.X + 16, x2 = R.X + 32 + col, x3 = R.X + 48 + 2 * col;

            T(g, Loc.T("Load"), _fs, Dim, x1, y0 + 34);
            if (_c.History.GpuPer.TryGetValue(luid, out var ring))
            {
                Graph(g, new RectangleF(x1, y0 + 54, col - 190, gh), new[] { (ring, Accent) }, 100, Pct);
                StatRows(g, x1 + col - 170, y0 + 60, 170, ring, Pct);
            }
            long ded = Metrics.GpuDedicatedBytes(luid);
            T(g, Loc.T("Video memory (VRAM)"), _fs, Dim, x2, y0 + 34);
            if (ded > 0 && _c.History.VramPer.TryGetValue(luid, out var vr))
            {
                Graph(g, new RectangleF(x2, y0 + 54, col - 190, gh), new[] { (vr, Green) }, ded, SizeStr);
                var s2 = Stat(vr);
                KeyValue(g, x2 + col - 170, y0 + 60, 170, Loc.T("Used"), SizeStr(s2.cur));
                KeyValue(g, x2 + col - 170, y0 + 84, 170, Loc.T("Total"), SizeStr(ded));
                KeyValue(g, x2 + col - 170, y0 + 108, 170, "Max", SizeStr(s2.max));
            }
            else T(g, Loc.T("No VRAM data"), _f, Dim, x2, y0 + 60);

            T(g, Loc.T("Sensors"), _fs, Dim, x3, y0 + 34);
            var rows = Rows(GpuSensors(gpuName).Where(x => x.Type != SensorType.Load));
            if (rows.Count == 0) T(g, SensorHint() ?? Loc.T("No sensors for this card."), _fs, Dim, x3, y0 + 60);
            else SensorList(g, x3, y0 + 58, col - 8, Math.Max(4, (int)((gh - 4) / 24)), rows);
        }
    }

    private void DetailMem(Graphics g, RectangleF R)
    {
        var m = _snap;
        Card(g, null, R, Loc.T("Memory") + " — details", SizeStr(m.MemTotalBytes));
        float gw = 1240;
        Graph(g, new RectangleF(R.X + 16, R.Y + 50, gw, 420), new[] { (_c.History.Mem, Green) }, 100, Pct);
        float rx = R.X + gw + 40, rw = R.Right - 16 - rx;
        Gauge(g, rx + 70, R.Y + 120, 56, m.MemPercent, Thr(m.MemPercent), $"{m.MemPercent:0}%");
        StatRows(g, rx + 160, R.Y + 70, rw - 160, _c.History.Mem, Pct);
        float y = R.Y + 200;
        KeyValue(g, rx, y, rw, Loc.T("In use"), SizeStr(m.MemUsedBytes)); y += 24;
        KeyValue(g, rx, y, rw, Loc.T("Available"), SizeStr(m.MemTotalBytes - m.MemUsedBytes)); y += 24;
        KeyValue(g, rx, y, rw, Loc.T("Total"), SizeStr(m.MemTotalBytes)); y += 40;
        T(g, Loc.T("Top memory"), _fs, Dim, rx, y);
        ProcList(g, rx, y + 22, rw, 12, false);

        // onderaan: gebruik in GB als tweede grafiek
        T(g, Loc.T("Top memory — all"), _fs, Dim, R.X + 16, R.Y + 500);
        float py = R.Y + 522;
        for (int i = 0; i < Math.Min(12, _procs.TopMem.Count); i++)
        {
            var (name, mem) = _procs.TopMem[i];
            double pct = 100.0 * mem / Math.Max(1, m.MemTotalBytes);
            T(g, Trunc(name, 30), _f, TextCol, R.X + 16, py + i * 34);
            Bar(g, R.X + 300, py + i * 34 + 6, 800, pct * 4, Green, 10);
            TR(g, $"{SizeStr(mem)}  ({pct:0.0}%)", _f, Dim, R.X + gw - 8, py + i * 34);
        }
    }

    private void DetailNet(Graphics g, RectangleF R)
    {
        var m = _snap;
        Card(g, null, R, Loc.T("Network — details"));
        float gw = 1240;
        T(g, $"↓ {Rate(m.NetDownBytesPerSec)}", _fbig, Accent, R.X + 16, R.Y + 44);
        T(g, $"↑ {Rate(m.NetUpBytesPerSec)}", _fbig, Green, R.X + 330, R.Y + 44);
        if (Cfg.ShowPing)
        {
            var ps = _c.Metrics.Ping.Stats();
            var pc = ps.Last is null ? TextCol : ps.Last < 0 || ps.Last >= 250 ? Col(Cfg.CritColor, Color.Red) : ps.Last >= 100 ? Col(Cfg.WarnColor, Color.Orange) : TextCol;
            T(g, $"Ping {ps.LastText}", _fbig, pc, R.X + 660, R.Y + 44);
            var dp = ps.Details.Split("  ·  ");
            T(g, $"{ps.Host}  ·  {dp[0]}", _fs, Dim, R.X + 900, R.Y + 46);
            if (dp.Length > 2) T(g, $"{dp[1]}  ·  {dp[2]}", _fs, Dim, R.X + 900, R.Y + 68);
        }

        var names = m.NetPerAdapter.Keys.Union(_c.Usage.KnownAdapters()).Distinct()
            .OrderByDescending(n => { m.NetPerAdapter.TryGetValue(n, out var r0); return r0.down + r0.up + _c.Usage.Month(n).Total; })
            .ThenBy(n => n).ToList();
        var active = ActiveAdapters(8);
        int mrows = (active.Count + 1) / 2;
        const float miniH = 120;
        float gridH = active.Count == 0 ? 0 : 26 + mrows * (miniH + 8);
        float tableH = (Math.Min(names.Count, 10) + 1) * 26 + 24;
        float netGh = Math.Max(200f, R.Bottom - (R.Y + 96) - gridH - tableH - 60);
        Graph(g, new RectangleF(R.X + 16, R.Y + 96, gw, netGh), new[] { (_c.History.NetDown, Accent), (_c.History.NetUp, Green) }, 0, Rate);

        // kleine grafiekjes per actieve adapter (2 per rij)
        float gy = R.Y + 96 + netGh + 30;
        if (active.Count > 0)
        {
            T(g, Loc.T("Per adapter"), _fs, Dim, R.X + 16, gy - 4);
            gy += 22;
            float cellW = (gw - 8) / 2;
            for (int i = 0; i < active.Count; i++)
            {
                string n = active[i];
                float cx0 = R.X + 16 + (i % 2) * (cellW + 8), cy0 = gy + (i / 2) * (miniH + 8);
                var cell = new RectangleF(cx0, cy0, cellW, miniH);
                using (var path = Rounded(cell, 8))
                    g.FillPath(GdiCache.Brush(Color.FromArgb(22, 255, 255, 255)), path);
                m.NetPerAdapter.TryGetValue(n, out var rt);
                T(g, TruncMid(n, 34), _fs, Dim, cell.X + 10, cell.Y + 6);
                TR(g, $"↓ {Rate(rt.down)}   ↑ {Rate(rt.up)}", _fb, TextCol, cell.Right - 10, cell.Y + 5);
                if (_c.History.NetPer.TryGetValue(n, out var pr))
                    Graph(g, new RectangleF(cell.X + 10, cell.Y + 28, cell.Width - 20, cell.Height - 36), new[] { (pr.down, Accent), (pr.up, Green) }, 0, Rate, false);
            }
            gy += mrows * (miniH + 8);
        }

        // tabel met alle adapters
        float ty = gy + 8;
        string[] heads = { Loc.T("Adapter"), Loc.T("Now ↓"), Loc.T("Now ↑"), Loc.T("Session"), Loc.T("Today"), Loc.T("Yesterday"), Loc.T("7 days"), Loc.T("Month") };
        float[] xs = { 0, 330, 450, 570, 720, 870, 1020, 1150 };
        for (int i = 0; i < heads.Length; i++)
        {
            if (i == 0) T(g, heads[i], _fs, Dim, R.X + 16 + xs[i], ty);
            else TR(g, heads[i], _fs, Dim, R.X + 16 + xs[i] + 110, ty);
        }
        float y = ty + 26;
        foreach (var n in names)
        {
            if (y > R.Bottom - 30) break;
            m.NetPerAdapter.TryGetValue(n, out var rt);
            T(g, TruncMid(n, 40), _f, TextCol, R.X + 16, y);
            var cells = new[] { Rate(rt.down), Rate(rt.up), Metrics.FormatBytes(_c.Usage.Session(n).Total), Metrics.FormatBytes(_c.Usage.Today(n).Total),
                                Metrics.FormatBytes(_c.Usage.Yesterday(n).Total), Metrics.FormatBytes(_c.Usage.Week(n).Total), Metrics.FormatBytes(_c.Usage.Month(n).Total) };
            for (int i = 0; i < cells.Length; i++) TR(g, cells[i], _f, TextCol, R.X + 16 + xs[i + 1] + 110, y);
            y += 26;
        }

        float rx = R.X + gw + 40, rw = R.Right - 16 - rx;
        T(g, Loc.T("Download speed"), _fs, Dim, rx, R.Y + 50);
        StatRows(g, rx, R.Y + 74, rw, _c.History.NetDown, Rate);
        T(g, Loc.T("Upload speed"), _fs, Dim, rx, R.Y + 190);
        StatRows(g, rx, R.Y + 214, rw, _c.History.NetUp, Rate);
        float ry = R.Y + 330;
        T(g, Loc.T("Total usage (all adapters)"), _fs, Dim, rx, ry);
        ry += 24;
        foreach (var (label, u) in new[]
        {
            (Loc.T("Session"), _c.Usage.Session(null)), (Loc.T("Today"), _c.Usage.Today(null)),
            (Loc.T("Yesterday"), _c.Usage.Yesterday(null)), (Loc.T("7 days"), _c.Usage.Week(null)),
            (Loc.T("This month"), _c.Usage.Month(null)),
        })
        {
            T(g, label, _f, Dim, rx, ry);
            TR(g, $"↓ {Metrics.FormatBytes(u.Down)}   ↑ {Metrics.FormatBytes(u.Up)}", _f, TextCol, rx + rw, ry);
            ry += 26;
        }
        if (Cfg.MonthlyLimitGb > 0)
        {
            var mu = _c.Usage.Month(Cfg.NetworkAdapter);
            double pct = 100.0 * mu.Total / (Cfg.MonthlyLimitGb * 1073741824.0);
            T(g, $"{Loc.T("Monthly limit")}  {Metrics.FormatBytes(mu.Total)} / {Cfg.MonthlyLimitGb} GB", _fs, Dim, rx, ry + 8);
            Bar(g, rx, ry + 32, rw, pct, Thr(pct), 10);
        }
    }

    private void DetailDisk(Graphics g, RectangleF R)
    {
        var m = _snap;
        var drives = _c.Drives();
        Card(g, null, R, Loc.T("Disks") + " — details");
        float gw = 1240;
        T(g, $"R  {Rate(m.DiskReadBytesPerSec)}", _fbig, Accent, R.X + 16, R.Y + 44);
        T(g, $"W  {Rate(m.DiskWriteBytesPerSec)}", _fbig, Orange, R.X + 380, R.Y + 44);
        float diskGh = Math.Max(240f, R.Bottom - (R.Y + 96) - (drives.Count * 34 + 84));
        Graph(g, new RectangleF(R.X + 16, R.Y + 96, gw, diskGh), new[] { (_c.History.DiskRead, Accent), (_c.History.DiskWrite, Orange) }, 0, Rate);

        float y = R.Y + 96 + diskGh + 24;
        T(g, Loc.T("Drives"), _fs, Dim, R.X + 16, y);
        y += 24;
        foreach (var d in drives)
        {
            T(g, d.Display, _fb, TextCol, R.X + 16, y);
            Bar(g, R.X + 90, y + 6, 640, d.UsedPercent, Thr(d.UsedPercent), 12);
            TR(g, $"{SizeStr(d.Used)} {Loc.T("used")}  ·  {SizeStr(d.Free)} {Loc.T("free")}  ·  {SizeStr(d.Total)}  ({d.UsedPercent:0}%)", _f, Dim, R.X + gw + 8, y);
            y += 34;
        }

        float rx = R.X + gw + 40, rw = R.Right - 16 - rx;
        T(g, Loc.T("Read"), _fs, Dim, rx, R.Y + 50);
        StatRows(g, rx, R.Y + 74, rw, _c.History.DiskRead, Rate);
        T(g, Loc.T("Write"), _fs, Dim, rx, R.Y + 190);
        StatRows(g, rx, R.Y + 214, rw, _c.History.DiskWrite, Rate);
        float ry = R.Y + 330;
        T(g, Loc.T("Physical disks"), _fs, Dim, rx, ry);
        ry += 24;
        foreach (var (name, rt) in m.DiskPerDisk.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            T(g, Trunc(name, 22), _f, TextCol, rx, ry);
            TR(g, $"R {Rate(rt.read)}   W {Rate(rt.write)}", _f, Dim, rx + rw, ry);
            ry += 26;
        }
        var stLines = StorageLines();
        ry += 14;
        T(g, Loc.T("Temperature and health"), _fs, Dim, rx, ry);
        ry += 24;
        if (stLines.Count == 0) T(g, SensorHint() ?? Loc.T("No SMART data available."), _fs, Dim, rx, ry);
        foreach (var (name, text) in stLines)
        {
            T(g, TruncMid(name, 30), _f, TextCol, rx, ry);
            TR(g, text, _f, Dim, rx + rw, ry);
            ry += 26;
        }
        var stRows = Rows(_snap.Sensors.Where(x => x.HwType == HardwareType.Storage && x.Type != SensorType.Load));
        if (stRows.Count > 0)
        {
            ry += 10;
            T(g, Loc.T("All disk sensors"), _fs, Dim, rx, ry);
            SensorList(g, rx, ry + 22, rw, Math.Max(3, (int)((R.Bottom - 20 - (ry + 22)) / 24)), stRows);
        }
    }

    private void DetailSys(Graphics g, RectangleF R)
    {
        var m = _snap;
        Card(g, null, R, Loc.T("Battery and system — details"));
        float half = (R.Width - 48) / 2;

        // batterij
        float x = R.X + 16, y = R.Y + 56;
        T(g, Loc.T("Battery"), _fs, Dim, x, y);
        if (m.BatteryPresent)
        {
            DrawBatteryBig(g, x + 20, y + 40, 90, 190, m);
            T(g, $"{m.BatteryPercent:0}%", _fhuge, TextCol, x + 150, y + 36);
            T(g, BatteryState(m), _fh, Dim, x + 152, y + 110);
            KeyValue(g, x + 152, y + 160, 420, Loc.T("AC power"), m.BatteryOnAc ? Loc.T("connected") : Loc.T("not connected"));
            KeyValue(g, x + 152, y + 186, 420, Loc.T("Charging"), m.BatteryCharging ? Loc.T("yes") : Loc.T("no"));
        }
        else T(g, Loc.T("No battery present (desktop PC)"), _f, Dim, x, y + 30);

        // systeem
        float sx = R.X + 32 + half, sy = R.Y + 56;
        T(g, Loc.T("System"), _fs, Dim, sx, sy);
        T(g, Now().ToString("HH:mm:ss"), _fhuge, TextCol, sx, sy + 26);
        var up = TimeSpan.FromMilliseconds(Ticks());
        float yy = sy + 110;
        void kv(string k, string v) { KeyValue(g, sx, yy, half - 16, k, v); yy += 26; }
        kv(Loc.T("Computer"), Environment.MachineName);
        kv(Loc.T("User"), Environment.UserName);
        kv("OS", System.Runtime.InteropServices.RuntimeInformation.OSDescription);
        kv(Loc.T("Booted"), (Now() - up).ToString("yyyy-MM-dd HH:mm"));
        kv("Uptime", $"{(int)up.TotalDays} {Loc.T("d")} {up.Hours} {Loc.T("h")} {up.Minutes} m");
        kv("CPU", Trunc(CpuName(), 46));
        kv(Loc.T("Cores"), $"{m.CpuCores.Length} ({Environment.ProcessorCount} threads)");
        kv(Loc.T("Memory@@ram"), SizeStr(m.MemTotalBytes));
        if (m.CpuTempC is double c) kv(Loc.T("CPU temperature"), $"{c:0}°C");
        if (m.GpuTempC is double t) kv(Loc.T("GPU temperature"), $"{t:0}°C");

        // videokaarten en schermen onderaan links
        float by = R.Y + 340;
        T(g, Loc.T("Graphics cards"), _fs, Dim, x, by);
        by += 24;
        foreach (var kv2 in Gpus())
        {
            long ded = Metrics.GpuDedicatedBytes(kv2.Key);
            KeyValue(g, x, by, half - 16, Trunc(Metrics.GpuName(kv2.Key), 40), ded > 0 ? SizeStr(ded) + " VRAM" : "");
            by += 26;
        }
        by += 16;
        T(g, Loc.T("Displays"), _fs, Dim, x, by);
        by += 24;
        foreach (var sc in Screen.AllScreens)
        {
            KeyValue(g, x, by, half - 16, sc.DeviceName.TrimStart('\\', '.') + (sc.Primary ? " *" : ""), $"{sc.Bounds.Width} × {sc.Bounds.Height}");
            by += 26;
        }

        // Hoofdbord, ventilatoren, geheugen, batterij en overige sensoren (LibreHardwareMonitor)
        float sy2 = R.Y + 470;
        T(g, Loc.T("Motherboard, fans and other sensors"), _fs, Dim, sx, sy2);
        var others = Rows(_snap.Sensors.Where(x => x.HwType != HardwareType.Cpu && !IsGpuHw(x.HwType) && x.HwType != HardwareType.Storage));
        if (others.Count == 0) T(g, SensorHint() ?? Loc.T("No additional sensors found."), _fs, Dim, sx, sy2 + 24);
        else
        {
            int perCol = Math.Max(3, (int)((R.Bottom - 16 - (sy2 + 24)) / 24));
            float cw2 = (half - 40) / 2;
            for (int ci = 0; ci < 2; ci++)
            {
                var slice = others.Skip(ci * perCol).Take(perCol).ToList();
                if (slice.Count == 0) break;
                SensorList(g, sx + ci * (cw2 + 24), sy2 + 24, cw2, perCol, slice);
            }
        }
    }

    private void DetailProcs(Graphics g, RectangleF R)
    {
        Card(g, null, R, Loc.T("Top programs — details"));
        float half = (R.Width - 64) / 2;
        T(g, Loc.T("Most CPU"), _fh, TextCol, R.X + 16, R.Y + 50);
        ProcList(g, R.X + 16, R.Y + 90, half, 12, true);
        T(g, Loc.T("Most memory"), _fh, TextCol, R.X + 48 + half, R.Y + 50);
        ProcList(g, R.X + 48 + half, R.Y + 90, half, 12, false);
        T(g, Loc.T("Grouped per program (all processes with the same name added together). Refreshes every 2 seconds."),
          _fs, Dim, R.X + 16, R.Bottom - 34);
    }
}
