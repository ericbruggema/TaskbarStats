
namespace TaskbarStats;

/// <summary>Overzichtspagina van het fullscreen-scherm: de tegels.</summary>
public sealed partial class FullscreenRenderer
{
    // ---------- Overzicht ----------
    private void DrawOverview(Graphics g)
    {
        float top = 76;
        var visible = Tiles.Order(_c.Cfg.FullOrder).Where(id => Tiles.FullOn(_c.Cfg, id));
        var cells = Tiles.FullCells(visible, CW, CH, top, M);
        if (cells.Count == 0)
        {
            T(g, Loc.T("No components enabled — turn them on in Settings (Fullscreen tab)."), _f, Dim, M + 8, top + 20);
            return;
        }
        foreach (var (ids, r) in cells) DrawCell(g, ids, r);
    }

    private void DrawCell(Graphics g, string[] c, RectangleF r)
    {
        if (c.Length == 2)   // batterij + systeem boven elkaar
        {
            TileBattery(g, new RectangleF(r.X, r.Y, r.Width, 190));
            TileSystem(g, new RectangleF(r.X, r.Y + 190 + M, r.Width, Math.Max(120, r.Height - 190 - M)));
            return;
        }
        switch (c[0])
        {
            case "cpu": TileCpu(g, r); break;
            case "gpu": TileGpu(g, r); break;
            case "mem": TileMem(g, r); break;
            case "net": TileNet(g, r); break;
            case "disk": TileDisk(g, r); break;
            case "batt": TileBattery(g, r); break;
            case "sys": TileSystem(g, r); break;
            case "proc": TileProcs(g, r); break;
        }
    }

    private void TileCpu(Graphics g, RectangleF r)
    {
        var m = _snap;
        string sub = $"{m.CpuCores.Length} {Loc.T("cores")}" + (m.CpuMHz is double f ? $"  ·  {f / 1000:0.00} GHz" : "") + (m.CpuTempC is double t ? $"  ·  {t:0}°C" : "") + (CpuPower() is string pw ? $"  ·  {pw}" : "");
        Card(g, "cpu", r, "CPU", sub);
        T(g, Trunc(CpuName(), 60), _fs, Dim, r.X + 16, r.Y + 36);
        Gauge(g, r.X + 96, r.Y + 140, 62, m.CpuPercent, Thr(m.CpuPercent), $"{m.CpuPercent:0}%");
        StatRows(g, r.X + 16, r.Y + 224, 170, _c.History.Cpu, Pct);

        var cores = m.CpuCores;
        if (cores.Length > 0)
        {
            float x0 = r.X + 232, w = r.Right - 16 - x0, y0 = r.Y + 62;
            int cols = cores.Length <= 8 ? 4 : cores.Length <= 24 ? 6 : 8;
            int rows = (cores.Length + cols - 1) / cols;
            float cw = w / cols, ch = Math.Min(48, 230f / rows);
            for (int i = 0; i < cores.Length; i++)
            {
                float x = x0 + (i % cols) * cw, y = y0 + (i / cols) * ch;
                var cell = new RectangleF(x + 2, y + 2, cw - 4, ch - 4);
                using (var path = Rounded(cell, 6))
                    g.FillPath(GdiCache.Brush(Color.FromArgb(24, 255, 255, 255)), path);
                Bar(g, cell.X + 6, cell.Bottom - 8, cell.Width - 12, cores[i], Thr(cores[i]), 4);
                T(g, $"C{i}", _fs, Dim, cell.X + 8, cell.Y + 3);
                TR(g, $"{cores[i]:0}%", _fb, TextCol, cell.Right - 8, cell.Y + 3);
            }
        }
        Graph(g, new RectangleF(r.X + 16, r.Y + 322, r.Width - 32, Math.Max(60, r.Height - 322 - 12)), new[] { (_c.History.Cpu, Accent) }, 100, Pct);
    }

    private void TileGpu(Graphics g, RectangleF r)
    {
        var gpus = Gpus();
        var m = _snap;
        Card(g, "gpu", r, "GPU", m.GpuTempC is double t ? $"{t:0}°C" : null);
        if (gpus.Count == 0) { T(g, Loc.T("No GPU data"), _f, Dim, r.X + 16, r.Y + 50); return; }
        float sh = (r.Height - 52) / gpus.Count;
        for (int i = 0; i < gpus.Count; i++)
        {
            var (luid, v) = (gpus[i].Key, gpus[i].Value);
            float y0 = r.Y + 44 + i * sh;
            T(g, Trunc(Metrics.GpuName(luid), 32), _fb, TextCol, r.X + 16, y0);
            TR(g, $"{v:0}%", _fbig, Thr(v), r.Right - 16, y0 - 10);
            Bar(g, r.X + 16, y0 + 30, r.Width - 32, v, Thr(v), 8);
            long ded = Metrics.GpuDedicatedBytes(luid);
            float gy = y0 + 46;
            if (ded > 0 && m.VramUsedPerLuid.TryGetValue(luid, out var used))
            {
                T(g, $"VRAM  {SizeStr(used)} / {SizeStr(ded)}", _fs, Dim, r.X + 16, y0 + 46);
                Bar(g, r.X + 190, y0 + 50, r.Width - 206, 100.0 * used / ded, Green, 6);
                gy = y0 + 66;
            }
            string info = GpuInfo(Metrics.GpuName(luid));
            if (info != "") { T(g, info, _fs, Dim, r.X + 16, gy); gy += 20; }
            if (_c.History.GpuPer.TryGetValue(luid, out var ring))
                Graph(g, new RectangleF(r.X + 16, gy, r.Width - 32, Math.Max(50, y0 + sh - gy - 14)), new[] { (ring, Accent) }, 100, Pct, false);
        }
    }

    private void TileMem(Graphics g, RectangleF r)
    {
        var m = _snap;
        Card(g, "mem", r, Loc.T("Memory"), $"{SizeStr(m.MemTotalBytes)}");
        Gauge(g, r.X + 82, r.Y + 122, 50, m.MemPercent, Thr(m.MemPercent), $"{m.MemPercent:0}%");
        T(g, $"{SizeStr(m.MemUsedBytes)}", _fbig, TextCol, r.X + 150, r.Y + 84);
        T(g, $"{Loc.T("in use of")} {SizeStr(m.MemTotalBytes)}", _f, Dim, r.X + 152, r.Y + 126);
        T(g, $"{Loc.T("Free")}  {SizeStr(m.MemTotalBytes - m.MemUsedBytes)}", _f, Dim, r.X + 152, r.Y + 150);
        Graph(g, new RectangleF(r.X + 16, r.Y + 190, r.Width - 32, 140), new[] { (_c.History.Mem, Green) }, 100, Pct);
        T(g, Loc.T("Top memory"), _fs, Dim, r.X + 16, r.Y + 348);
        ProcList(g, r.X + 16, r.Y + 370, r.Width - 32, 5, false);
    }

    private void TileNet(Graphics g, RectangleF r)
    {
        var m = _snap;
        Card(g, "net", r, Loc.T("Network"));
        T(g, $"↓ {Rate(m.NetDownBytesPerSec)}", _fbig, Accent, r.X + 16, r.Y + 44);
        T(g, $"↑ {Rate(m.NetUpBytesPerSec)}", _fh, Green, r.X + 16, r.Y + 90);
        Graph(g, new RectangleF(r.X + 210, r.Y + 44, r.Width - 226, 92), new[] { (_c.History.NetDown, Accent), (_c.History.NetUp, Green) }, 0, Rate, false);

        float y = r.Y + 150;
        foreach (var (label, u) in new[]
        {
            (Loc.T("Session"), _c.Usage.Session(null)), (Loc.T("Today"), _c.Usage.Today(null)),
            (Loc.T("Yesterday"), _c.Usage.Yesterday(null)), (Loc.T("7 days"), _c.Usage.Week(null)),
            (Loc.T("This month"), _c.Usage.Month(null)),
        })
        {
            T(g, label, _f, Dim, r.X + 16, y);
            TR(g, $"↓ {Metrics.FormatBytes(u.Down)}    ↑ {Metrics.FormatBytes(u.Up)}", _f, TextCol, r.Right - 16, y);
            y += 24;
        }
        if (Cfg.MonthlyLimitGb > 0)
        {
            var mu = _c.Usage.Month(Cfg.NetworkAdapter);
            double pct = 100.0 * mu.Total / (Cfg.MonthlyLimitGb * 1073741824.0);
            T(g, $"{Loc.T("Limit")}  {Metrics.FormatBytes(mu.Total)} / {Cfg.MonthlyLimitGb} GB", _fs, Dim, r.X + 16, y + 4);
            Bar(g, r.X + 16, y + 26, r.Width - 32, pct, Thr(pct), 7);
            y += 44;
        }
        y += 6;
        foreach (var name in ActiveAdapters(8))
        {
            if (y > r.Bottom - 46) break;
            m.NetPerAdapter.TryGetValue(name, out var rt);
            T(g, TruncMid(name, 30), _fs, Dim, r.X + 16, y);
            TR(g, $"↓ {Rate(rt.down)}  ↑ {Rate(rt.up)}", _fs, TextCol, r.Right - 16, y);
            if (_c.History.NetPer.TryGetValue(name, out var pr))
                Graph(g, new RectangleF(r.X + 16, y + 20, r.Width - 32, 22), new[] { (pr.down, Accent), (pr.up, Green) }, 0, Rate, false);
            y += 48;
        }
    }

    private void TileDisk(Graphics g, RectangleF r)
    {
        var m = _snap;
        var drives = _c.Drives();
        Card(g, "disk", r, Loc.T("Disks"));
        float y = r.Y + 44;
        foreach (var d in drives.Take(6))
        {
            T(g, d.Display, _fb, TextCol, r.X + 16, y);
            TR(g, $"{SizeStr(d.Free)} {Loc.T("free of")} {SizeStr(d.Total)}  ({d.UsedPercent:0}%)", _fs, Dim, r.Right - 16, y + 2);
            Bar(g, r.X + 16, y + 24, r.Width - 32, d.UsedPercent, Thr(d.UsedPercent), 8);
            y += 44;
        }
        if (drives.Count > 6) { T(g, $"+{drives.Count - 6} {Loc.T("more — click for all")}", _fs, Dim, r.X + 16, y - 8); y += 14; }
        T(g, $"R  {Rate(m.DiskReadBytesPerSec)}", _fb, Accent, r.X + 16, y + 4);
        TR(g, $"W  {Rate(m.DiskWriteBytesPerSec)}", _fb, Orange, r.Right - 16, y + 4);
        float gh = 96;
        Graph(g, new RectangleF(r.X + 16, y + 32, r.Width - 32, gh), new[] { (_c.History.DiskRead, Accent), (_c.History.DiskWrite, Orange) }, 0, Rate, false);
        y += 32 + gh + 12;
        foreach (var (name, rt) in m.DiskPerDisk.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (y > r.Bottom - 26) break;
            T(g, Trunc(name, 22), _fs, Dim, r.X + 16, y);
            TR(g, $"R {Rate(rt.read)}   W {Rate(rt.write)}", _fs, TextCol, r.Right - 16, y);
            y += 21;
        }
        foreach (var (name, text) in StorageLines())
        {
            if (y > r.Bottom - 26) break;
            T(g, TruncMid(name, 26), _fs, Dim, r.X + 16, y);
            TR(g, text, _fs, TextCol, r.Right - 16, y);
            y += 21;
        }
    }

    private void TileBattery(Graphics g, RectangleF r)
    {
        var m = _snap;
        Card(g, "bat", r, Loc.T("Battery"));
        if (!m.BatteryPresent) { T(g, Loc.T("No battery (desktop PC)"), _f, Dim, r.X + 16, r.Y + 56); return; }
        DrawBatteryBig(g, r.X + 34, r.Y + 50, 46, 100, m);
        T(g, $"{m.BatteryPercent:0}%", _fhuge, TextCol, r.X + 110, r.Y + 46);
        T(g, BatteryState(m), _f, Dim, r.X + 112, r.Y + 116);
    }

    private static string BatteryState(MetricsSnapshot m)
    {
        string state = m.BatteryCharging ? Loc.T("Charging") : m.BatteryOnAc ? Loc.T("Plugged in") : Loc.T("On battery");
        return state + (!m.BatteryOnAc && m.BatteryRemainingSec > 0
            ? $"  ·  {m.BatteryRemainingSec / 3600}{Loc.T("h")} {m.BatteryRemainingSec % 3600 / 60:00}m {Loc.T("left")}" : "");
    }

    private void DrawBatteryBig(Graphics g, float x, float y, float w, float h, MetricsSnapshot m)
    {
        double pct = m.BatteryPercent;
        Color fill = m.BatteryCharging || m.BatteryOnAc ? Green
                   : pct <= 10 ? Col(Cfg.CritColor, Color.Red) : pct <= 20 ? Col(Cfg.WarnColor, Color.Orange) : Accent;
        g.FillRectangle(GdiCache.Brush(Color.FromArgb(142, 142, 147)), x + w / 2 - 9, y - 6, 18, 6);
        using (var path = Rounded(new RectangleF(x, y, w, h), 7))
        {
            g.FillPath(GdiCache.Brush(Color.FromArgb(42, 42, 45)), path);
            g.DrawPath(GdiCache.Pen(Color.FromArgb(142, 142, 147), 2f), path);
        }
        float ih = h - 6, fh = (float)(ih * pct / 100.0);
        if (fh >= 1)
            using (var fp = Rounded(new RectangleF(x + 3, y + 3 + ih - fh, w - 6, fh), 4))
                g.FillPath(GdiCache.Brush(fill), fp);
        if (m.BatteryCharging) WidgetForm.DrawBolt(g, x + w / 2, y + h / 2, h * 0.5f);
        else if (m.BatteryOnAc) WidgetForm.DrawPlug(g, x + w / 2, y + h / 2, h * 0.5f);
    }

    private void TileSystem(Graphics g, RectangleF r)
    {
        var m = _snap;
        Card(g, "sys", r, Loc.T("System"));
        T(g, Now().ToString("HH:mm"), _fhuge, TextCol, r.X + 16, r.Y + 40);
        T(g, Now().ToString("dddd d MMMM"), _f, Dim, r.X + 205, r.Y + 62);
        var up = TimeSpan.FromMilliseconds(Ticks());
        float y = r.Y + 110;
        KeyValue(g, r.X + 16, y, r.Width - 32, "Uptime", $"{(int)up.TotalDays} {Loc.T("d")} {up.Hours} {Loc.T("h")} {up.Minutes} m"); y += 24;
        KeyValue(g, r.X + 16, y, r.Width - 32, Loc.T("Computer"), Environment.MachineName); y += 24;
        KeyValue(g, r.X + 16, y, r.Width - 32, "OS", Trunc(System.Runtime.InteropServices.RuntimeInformation.OSDescription, 34)); y += 24;
        KeyValue(g, r.X + 16, y, r.Width - 32, Loc.T("Memory@@ram"), SizeStr(m.MemTotalBytes)); y += 24;
        if (m.CpuTempC is double c) { KeyValue(g, r.X + 16, y, r.Width - 32, "CPU", $"{c:0}°C"); y += 24; }
        if (m.GpuTempC is double t) KeyValue(g, r.X + 16, y, r.Width - 32, "GPU", $"{t:0}°C");
    }

    private void TileProcs(Graphics g, RectangleF r)
    {
        Card(g, "proc", r, Loc.T("Top programs"));
        T(g, "CPU", _fs, Dim, r.X + 16, r.Y + 42);
        ProcList(g, r.X + 16, r.Y + 62, r.Width - 32, 7, true);
        float y2 = r.Y + 62 + 7 * 24 + 14;
        T(g, "RAM", _fs, Dim, r.X + 16, y2);
        ProcList(g, r.X + 16, y2 + 20, r.Width - 32, 7, false);
    }
}
