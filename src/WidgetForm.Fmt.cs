using System.Text;

namespace TaskbarStats;

/// <summary>Waardeopmaak in de widget-cellen (eenheden, decimalen, %-teken, geheugen als GB) en de extra widget-onderdelen.</summary>
public sealed partial class WidgetForm
{
    // ---------- Percentages ----------
    private string Pct(double v) => _cfg.HidePercent ? $"{v:0}" : $"{v:0}%";
    private string PctTemplate => _cfg.HidePercent ? "100" : "100%";

    // ---------- Snelheden (alleen widget; Metrics.FormatRate blijft voor dashboard, fullscreen en tooltip) ----------

    /// <summary>Snelheid voor een widget-cel volgens de opmaakopties (zonder pijl/R/W-voorvoegsel).</summary>
    private string WidgetRate(double bytesPerSec, bool bits)
    {
        bool compact = _cfg.Compact;
        double v = bits ? bytesPerSec * 8 : bytesPerSec;
        double k = bits ? 1000.0 : 1024.0;
        int tier = _cfg.RateScaleMode switch
        {
            RateScale.Kilo => 1,
            RateScale.Mega => 2,
            _ => v >= k * k ? 2 : v >= k ? 1 : 0,
        };
        double val = v / Math.Pow(k, tier);
        string fmt = tier == 0 ? "0"
                   : tier == 1 ? (_cfg.ShortValues || compact ? "0" : "0.0")
                   : (_cfg.ShortValues && val >= 10 ? "0" : "0.0");
        string num = val.ToString(fmt);
        if (_cfg.HideUnit) return num;
        return num + " " + RateUnit(tier, bits) + (compact ? "" : "/s");
    }

    private static string RateUnit(int tier, bool bits)
        => tier switch { 2 => bits ? "Mb" : "MB", 1 => bits ? "Kb" : "KB", _ => bits ? "b" : "B" };

    /// <summary>Breedste verwachte snelheid: de vaste breedte van de cel schaalt mee met eenheid, modus en opmaak.</summary>
    private string RateTemplate(bool bits)
    {
        bool compact = _cfg.Compact;
        int tier = _cfg.RateScaleMode == RateScale.Kilo ? 1 : 2;
        int digits = tier == 1 ? (bits ? 7 : 6) : bits && _cfg.RateScaleMode == RateScale.Mega ? 4 : 3;
        bool decimals = tier == 1 ? !(_cfg.ShortValues || compact) : !_cfg.ShortValues;   // "000.0" is de breedste vorm
        string num = new string('0', digits) + (decimals ? ".0" : "");
        if (_cfg.HideUnit) return num;
        return num + " " + RateUnit(tier, bits) + (compact ? "" : "/s");
    }

    // Breedte van de cel: het sjabloon; bij een vaste eenheid groeit hij mee als een uitschieter breder is (bij automatisch blijft hij gelijk aan vroeger).
    private float Reserve(Graphics g, string template, string a, string b)
    {
        float w = g.MeasureString(template, _fontSmall).Width;
        if (_cfg.RateScaleMode == RateScale.Auto) return w;
        foreach (var s in new[] { a, b })
            if (s.Length > template.Length) w = Math.Max(w, g.MeasureString(s, _fontSmall).Width);   // alleen bij meer tekens dan het sjabloon (geen gespring door cijferbreedte)
        return w;
    }

    private int DrawNetwork(Graphics g, int x)
    {
        if (_cfg.NetStyle == TextGraphStyle.Graph) return DrawNetGraph(g, x);
        bool bits = _cfg.NetUnit == RateUnitKind.Bits;
        var b = GdiCache.Brush(EffectiveText());
        string up = _cfg.ShowNetUp ? "↑ " + WidgetRate(_metrics.NetUpBytesPerSec, bits) : "";
        string dn = _cfg.ShowNetDown ? "↓ " + WidgetRate(_metrics.NetDownBytesPerSec, bits) : "";
        string top = _cfg.SwapNet ? dn : up, bottom = _cfg.SwapNet ? up : dn;
        float w = Reserve(g, "↑ " + RateTemplate(bits), up, dn);   // vaste reservering
        float lh = _fontSmall.GetHeight(g);
        float y = (Height - lh * 2) / 2;
        if (top != "") g.DrawString(top, _fontSmall, b, x, y);
        if (bottom != "") g.DrawString(bottom, _fontSmall, b, x, y + lh);
        return (int)Math.Ceiling(w);
    }

    private int DrawDisk(Graphics g, int x)
    {
        var b = GdiCache.Brush(EffectiveText());
        string rd = "R " + WidgetRate(_metrics.DiskReadBytesPerSec, false);
        string wr = "W " + WidgetRate(_metrics.DiskWriteBytesPerSec, false);
        float w = Reserve(g, "R " + RateTemplate(false), rd, wr);   // vaste reservering
        float lh = _fontSmall.GetHeight(g);
        float y = (Height - lh * 2) / 2;
        g.DrawString(rd, _fontSmall, b, x, y);
        g.DrawString(wr, _fontSmall, b, x, y + lh);
        return (int)Math.Ceiling(w);
    }

    // ---------- Geheugen ----------

    /// <summary>Geheugen-cel: bij digitaal ook "gebruikt" of "beschikbaar" in GB; meter/balk blijven het percentage tonen.</summary>
    private int DrawMem(Graphics g, int x)
    {
        var m = _metrics;
        if (_cfg.MemStyle != DisplayStyle.Digital || _cfg.MemShow == MemShowMode.Percent || m.MemTotalBytes == 0)
            return DrawMetric(g, x, "MEM", m.MemPercent, _cfg.MemStyle);
        double total = m.MemTotalBytes / 1073741824.0;
        double gb = (_cfg.MemShow == MemShowMode.Used ? m.MemUsedBytes : m.MemTotalBytes - m.MemUsedBytes) / 1073741824.0;
        string fmt = _cfg.ShortValues ? "0" : "0.0";
        string unit = _cfg.HideUnit ? "" : " GB";
        string tpl = new string('0', Math.Max(1, total.ToString("0").Length)) + (_cfg.ShortValues ? "" : ".0") + unit;
        return DrawTextCell(g, x, "MEM", gb.ToString(fmt) + unit, tpl, ThresholdColor(m.MemPercent, EffectiveText()));
    }

    // ---------- Extra onderdelen ----------

    private static readonly string[] ExtraIds = { "cpufreq", "diskbusy", "disktemp", "mobotemp" };

    /// <summary>Tekent een extra onderdeel en geeft de breedte terug (0 = niets getekend: uit of geen waarde).</summary>
    private int DrawExtraItem(Graphics g, int x, string id)
    {
        var textCol = EffectiveText();
        switch (id)
        {
            case "cpufreq" when _cfg.ShowCpuFreq && _metrics.CpuMHz is double mhz && mhz > 0:
                string unit = _cfg.HideUnit ? "" : " GHz";
                return DrawTextCell(g, x, "CLK", $"{mhz / 1000:0.0}{unit}", "0.0" + unit, textCol);
            case "diskbusy" when _cfg.ShowDiskBusy && _metrics.DiskBusyPercent is double busy:
                return DrawMetric(g, x, "DISK", busy, _cfg.DiskBusyStyle);
            case "disktemp" when _cfg.ShowDiskTemp && _metrics.DiskTempC is double dt:
                return DrawTemp(g, x, "DISK", dt, DisplayStyle.Digital, textCol);
            case "mobotemp" when _cfg.ShowMoboTemp && _metrics.MoboTempC is double mt:
                return DrawTemp(g, x, "MB", mt, DisplayStyle.Digital, textCol);
        }
        return 0;
    }

    private void ApplyExtraWanted() => _metrics.SetExtraWanted(_cfg.ShowDiskBusy, _cfg.ShowDiskTemp || _cfg.AlertDiskTempC > 0, _cfg.ShowMoboTemp);

    private void AppendExtraTooltip(StringBuilder sb)
    {
        if (_cfg.ShowDiskBusy && _metrics.DiskBusyPercent is double busy) sb.AppendLine($"{Loc.T("Disk active")}  {busy:0}%");
        if (_cfg.ShowDiskTemp && _metrics.DiskTempC is double dt) sb.AppendLine($"{Loc.T("Disk")}  {dt:0}°C");
        if (_cfg.ShowMoboTemp && _metrics.MoboTempC is double mt) sb.AppendLine($"{Loc.T("Motherboard")}  {mt:0}°C");
    }
}
