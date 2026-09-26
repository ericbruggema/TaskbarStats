using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskbarStats;

/// <summary>Een thema: alles wat uiterlijk en indeling bepaalt (geen posities, taal of andere persoonlijke keuzes).</summary>
public sealed partial class ThemeData
{
    public string Name { get; set; } = "";
    public string? Author { get; set; }

    // Widget
    public bool ShowCpu { get; set; } = true;
    public bool ShowGpu { get; set; } = true;
    public bool ShowMem { get; set; } = true;
    public bool ShowNetUp { get; set; } = true;
    public bool ShowNetDown { get; set; } = true;
    public bool ShowBattery { get; set; } = true;
    public bool ShowDisk { get; set; }
    public bool ShowCpuTemp { get; set; }
    public bool ShowGpuTemp { get; set; }
    public DisplayStyle CpuStyle { get; set; }
    public DisplayStyle GpuStyle { get; set; }
    public DisplayStyle MemStyle { get; set; }
    public DisplayStyle CpuTempStyle { get; set; }
    public DisplayStyle GpuTempStyle { get; set; }
    public bool TempMerge { get; set; }
    public int GraphSeconds { get; set; } = 60;
    public GraphLen GraphLength { get; set; } = GraphLen.Medium;
    public int GraphWidthPx { get; set; } = 46;
    public Dictionary<string, GraphSize>? GraphSizes { get; set; }
    public TextGraphStyle NetStyle { get; set; }
    public TextGraphStyle PingStyle { get; set; }
    public bool CpuPerCore { get; set; }
    public BatteryPercentMode BatteryPercent { get; set; } = BatteryPercentMode.Inside;
    public bool LabelsAbove { get; set; }
    public bool Compact { get; set; }
    public bool AutoHeight { get; set; } = true;
    public int WidgetHeight { get; set; } = 44;
    public string FontFamily { get; set; } = "Segoe UI";
    public int FontSize { get; set; } = 9;
    public bool TransparentBackground { get; set; }

    // Kleuren
    public string TextColor { get; set; } = "#FFFFFF";
    public string BackgroundColor { get; set; } = "#141414";
    public string AccentColor { get; set; } = "#0A84FF";
    public string WarnColor { get; set; } = "#FF9500";
    public string CritColor { get; set; } = "#FF3B30";
    public string BorderColor { get; set; } = "#FF00FF";
    public int WarnThreshold { get; set; } = 85;
    public int CritThreshold { get; set; } = 95;

    // Dashboard en fullscreen
    public int DashOpacity { get; set; } = 90;
    public int DashScale { get; set; } = 100;
    public int DashColumns { get; set; } = 2;
    public bool DashFront { get; set; }
    public List<string>? WidgetOrder { get; set; }
    public List<string>? DashOrder { get; set; }
    public List<string>? DashHidden { get; set; }
    public List<string>? FullOrder { get; set; }
    public List<string>? FullHidden { get; set; }

    /// <summary>Meegeleverde achtergrond (naam uit <see cref="BgPatterns.Names"/>) voor widget, dashboard en fullscreen; leeg = geen. Eigen bestanden horen niet in een thema.</summary>
    public string? Background { get; set; }

    /// <summary>Vermelding van het programma in een geëxporteerd thema (leeg in de gewone bestanden); wordt bij het toepassen genegeerd.</summary>
    [JsonPropertyName("_generator")] public string? Generator { get; set; }
    public int BackgroundOpacity { get; set; } = 60;

    public static ThemeData Capture(AppSettings c, string name)
    {
        var hidden = Tiles.All.Where(id => !Tiles.DashOn(c, id)).ToList();
        return new ThemeData
        {
            Name = name,
            ShowCpu = c.ShowCpu, ShowGpu = c.ShowGpu, ShowMem = c.ShowMem, ShowNetUp = c.ShowNetUp, ShowNetDown = c.ShowNetDown,
            ShowBattery = c.ShowBattery, ShowDisk = c.ShowDisk, ShowCpuTemp = c.ShowCpuTemp, ShowGpuTemp = c.ShowGpuTemp,
            ShowCpuFreq = c.ShowCpuFreq, ShowDiskBusy = c.ShowDiskBusy, DiskBusyStyle = c.DiskBusyStyle, ShowDiskTemp = c.ShowDiskTemp, ShowMoboTemp = c.ShowMoboTemp,
            CpuStyle = c.CpuStyle, GpuStyle = c.GpuStyle, MemStyle = c.MemStyle, CpuPerCore = c.CpuPerCore,
            CpuTempStyle = c.CpuTempStyle, GpuTempStyle = c.GpuTempStyle, TempMerge = c.TempMerge,
            GraphSeconds = c.GraphSeconds, GraphLength = c.GraphLength, GraphWidthPx = c.GraphWidthPx, GraphSizes = c.GraphSizes.ToDictionary(k => k.Key, k => new GraphSize { Len = k.Value.Len, Px = k.Value.Px }), NetStyle = c.NetStyle, PingStyle = c.PingStyle,
            BatteryPercent = c.BatteryPercent, LabelsAbove = c.LabelsAbove, Compact = c.Compact, AutoHeight = c.AutoHeight,
            WidgetHeight = c.WidgetHeight, FontFamily = c.FontFamily, FontSize = c.FontSize, TransparentBackground = c.TransparentBackground,
            TextColor = c.TextColor, BackgroundColor = c.BackgroundColor, AccentColor = c.AccentColor, WarnColor = c.WarnColor,
            CritColor = c.CritColor, BorderColor = c.BorderColor, WarnThreshold = c.WarnThreshold, CritThreshold = c.CritThreshold,
            DashOpacity = c.DashOpacity, DashScale = c.DashScale, DashColumns = c.DashColumns, DashFront = c.DashFront,
            WidgetOrder = c.WidgetOrder is null ? null : new List<string>(c.WidgetOrder),
            DashOrder = c.DashOrder is null ? null : new List<string>(c.DashOrder),
            DashHidden = hidden.Count > 0 ? hidden : null,
            FullOrder = c.FullOrder is null ? null : new List<string>(c.FullOrder),
            FullHidden = c.FullHidden is null ? null : new List<string>(c.FullHidden),
            Background = BgPatterns.IsBuiltIn(c.DashBgImage) ? BgPatterns.NameOf(c.DashBgImage!) : null,
            BackgroundOpacity = BgPatterns.IsBuiltIn(c.DashBgImage) ? c.DashBgOpacity : 60,
        };
    }

    public void ApplyTo(AppSettings c)
    {
        c.ShowCpu = ShowCpu; c.ShowGpu = ShowGpu; c.ShowMem = ShowMem; c.ShowNetUp = ShowNetUp; c.ShowNetDown = ShowNetDown;
        c.ShowBattery = ShowBattery; c.ShowDisk = ShowDisk; c.ShowCpuTemp = ShowCpuTemp; c.ShowGpuTemp = ShowGpuTemp;
        c.ShowCpuFreq = ShowCpuFreq; c.ShowDiskBusy = ShowDiskBusy; c.DiskBusyStyle = DiskBusyStyle; c.ShowDiskTemp = ShowDiskTemp; c.ShowMoboTemp = ShowMoboTemp;
        c.CpuStyle = CpuStyle; c.GpuStyle = GpuStyle; c.MemStyle = MemStyle; c.CpuPerCore = CpuPerCore;
        c.CpuTempStyle = CpuTempStyle; c.GpuTempStyle = GpuTempStyle; c.TempMerge = TempMerge;
        c.GraphSeconds = GraphSeconds <= 30 ? 30 : GraphSeconds >= 120 ? 120 : 60; c.GraphLength = GraphLength; c.GraphWidthPx = Math.Clamp(GraphWidthPx, 16, 160);
        c.GraphSizes = (GraphSizes ?? new()).Where(k => AppSettings.GraphKeys.Contains(k.Key)).ToDictionary(k => k.Key, k => new GraphSize { Len = k.Value.Len, Px = Math.Clamp(k.Value.Px, 16, 160) });
        c.NetStyle = NetStyle; c.PingStyle = PingStyle;
        c.BatteryPercent = BatteryPercent; c.Compact = Compact; c.LabelsAbove = LabelsAbove || Compact;   // compact hoort bij labels boven
        c.AutoHeight = AutoHeight;
        c.WidgetHeight = Math.Clamp(WidgetHeight, 24, 96);
        c.FontFamily = string.IsNullOrWhiteSpace(FontFamily) ? "Segoe UI" : FontFamily;
        c.FontSize = Math.Clamp(FontSize, 6, 16);
        c.TransparentBackground = TransparentBackground;
        c.TextColor = TextColor; c.BackgroundColor = BackgroundColor; c.AccentColor = AccentColor; c.WarnColor = WarnColor;
        c.CritColor = CritColor; c.BorderColor = BorderColor ?? ""; c.WarnThreshold = WarnThreshold; c.CritThreshold = CritThreshold;
        c.DashOpacity = Math.Clamp(DashOpacity, 10, 100);
        c.DashScale = Math.Clamp(DashScale, 30, 400);
        c.DashColumns = Math.Clamp(DashColumns, 1, 4);
        c.DashFront = DashFront;
        c.WidgetOrder = WidgetOrder is null ? null : Tiles.WidgetOrder(WidgetOrder);
        c.DashOrder = DashOrder is null ? null : Tiles.Order(DashOrder);
        foreach (var id in Tiles.All) Tiles.SetDashOn(c, id, !(DashHidden?.Contains(id) ?? false));
        c.FullOrder = FullOrder is null ? null : Tiles.Order(FullOrder);
        var fh = FullHidden?.Where(Tiles.All.Contains).ToList();
        c.FullHidden = fh is { Count: > 0 } ? fh : null;

        // Achtergrond: een meegeleverd patroon vervangt wat er stond; zonder patroon verdwijnt alleen een eerder patroon (eigen afbeeldingen blijven).
        string bg = (Background ?? "").Trim().ToLowerInvariant();
        if (BgPatterns.Names.Contains(bg))
        {
            int op = Math.Clamp(BackgroundOpacity, 5, 100);
            c.WidgetBgImage = c.DashBgImage = c.FullBgImage = BgPatterns.Path(bg);
            c.WidgetBgMode = c.DashBgMode = c.FullBgMode = BgMode.Fill;
            c.WidgetBgOpacity = c.DashBgOpacity = c.FullBgOpacity = op;
        }
        else
        {
            if (BgPatterns.IsBuiltIn(c.WidgetBgImage)) c.WidgetBgImage = null;
            if (BgPatterns.IsBuiltIn(c.DashBgImage)) c.DashBgImage = null;
            if (BgPatterns.IsBuiltIn(c.FullBgImage)) c.FullBgImage = null;
        }
    }
}

/// <summary>Meegeleverde thema's plus eigen thema's als losse .json-bestanden in %AppData%\TaskbarStats\themes.</summary>
public static class ThemeStore
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Dir(AppSettings c) => Path.Combine(Path.GetDirectoryName(c.FilePath) ?? ".", "themes");

    /// <summary>Weergavenaam van een meegeleverd thema (de naam zelf blijft Engels en dient als sleutel); eigen thema's blijven zoals ze heten.</summary>
    public static string Label(string name) => name switch
    {
        "Default" => Loc.T("Default@@theme"), "Dark" => Loc.T("Dark@@theme"), "Light" => Loc.T("Light@@theme"),
        "Black and white" => Loc.T("Black and white"), "Love" => Loc.T("Love"), "Ocean" => Loc.T("Ocean"),
        "Sunset" => Loc.T("Sunset"), "Forest" => Loc.T("Forest"), "Minimal" => Loc.T("Minimal"),
        _ => name,
    };

    public static List<ThemeData> BuiltIn() => new()
    {
        new ThemeData { Name = "Default" },
        new ThemeData
        {
            Name = "Dark", TextColor = "#E0E0E0", BackgroundColor = "#000000", AccentColor = "#0A84FF", WarnColor = "#FF9F0A",
            CritColor = "#FF453A", BorderColor = "", DashOpacity = 100,
        },
        new ThemeData
        {
            Name = "Light", TextColor = "#1C1C1E", BackgroundColor = "#F2F2F7", AccentColor = "#0A64D6",
            WarnColor = "#E07B00", CritColor = "#D70015", BorderColor = "#C7C7CC",
        },
        new ThemeData
        {
            // Hoe helderder, hoe drukker: grijs -> lichtgrijs -> wit
            Name = "Black and white", TextColor = "#FFFFFF", BackgroundColor = "#000000", AccentColor = "#7A7A7A", WarnColor = "#C8C8C8",
            CritColor = "#FFFFFF", BorderColor = "#FFFFFF", DashOpacity = 100,
        },
        new ThemeData
        {
            Name = "Love", Background = "hearts", BackgroundOpacity = 85, TextColor = "#FFE4EE", BackgroundColor = "#2B0A1A", AccentColor = "#FF4D8D", WarnColor = "#FFB3C7",
            CritColor = "#FF1744", BorderColor = "#FF69B4", CpuStyle = DisplayStyle.Gauge, GpuStyle = DisplayStyle.Gauge, MemStyle = DisplayStyle.Gauge,
            FontFamily = "Segoe UI Semibold",
        },
        new ThemeData
        {
            // De vier CGA-kleuren van de eerste pc's: zwart, cyaan, magenta, wit (plus geel en rood voor waarschuwingen)
            Name = "CGA", Background = "scanlines", BackgroundOpacity = 100, TextColor = "#FFFFFF", BackgroundColor = "#000000", AccentColor = "#55FFFF", WarnColor = "#FFFF55",
            CritColor = "#FF5555", BorderColor = "#FF55FF", CpuStyle = DisplayStyle.Bar, GpuStyle = DisplayStyle.Bar, MemStyle = DisplayStyle.Bar,
            FontFamily = "Lucida Console", DashOpacity = 100,
        },
        new ThemeData
        {
            Name = "Matrix", Background = "matrix", BackgroundOpacity = 45, TextColor = "#00FF41", BackgroundColor = "#000A00", AccentColor = "#00CC33", WarnColor = "#B6FF00",
            CritColor = "#FF0033", BorderColor = "#00FF41", CpuStyle = DisplayStyle.Bar, GpuStyle = DisplayStyle.Bar, MemStyle = DisplayStyle.Bar,
            FontFamily = "Consolas", Compact = true, LabelsAbove = true, DashOpacity = 95,
        },
        new ThemeData
        {
            Name = "Amber", Background = "scanlines", BackgroundOpacity = 100, TextColor = "#FFB000", BackgroundColor = "#0A0500", AccentColor = "#FF8C00", WarnColor = "#FFD060",
            CritColor = "#FF4500", BorderColor = "#FF8C00", CpuStyle = DisplayStyle.Bar, GpuStyle = DisplayStyle.Bar, MemStyle = DisplayStyle.Bar,
            FontFamily = "Consolas", DashOpacity = 100,
        },
        new ThemeData
        {
            // De groentinten van de eerste Game Boy (donkere variant); hoe lichter, hoe drukker
            Name = "Game Boy", Background = "scanlines", BackgroundOpacity = 100, TextColor = "#9BBC0F", BackgroundColor = "#0F380F", AccentColor = "#8BAC0F", WarnColor = "#CADC9F",
            CritColor = "#E0F8D0", BorderColor = "#306230", CpuStyle = DisplayStyle.Bar, GpuStyle = DisplayStyle.Bar, MemStyle = DisplayStyle.Bar,
            FontFamily = "Lucida Console", DashOpacity = 100,
        },
        new ThemeData
        {
            Name = "Dracula", Background = "stars", BackgroundOpacity = 80, TextColor = "#F8F8F2", BackgroundColor = "#282A36", AccentColor = "#BD93F9", WarnColor = "#FFB86C",
            CritColor = "#FF5555", BorderColor = "#6272A4",
        },
        new ThemeData
        {
            Name = "Ocean", Background = "waves", BackgroundOpacity = 80, TextColor = "#E6F7FF", BackgroundColor = "#06202B", AccentColor = "#00B4D8", WarnColor = "#FFD166",
            CritColor = "#EF476F", BorderColor = "#0077B6", FontFamily = "Segoe UI Semibold",
        },
        new ThemeData
        {
            Name = "Sunset", Background = "sunset", BackgroundOpacity = 70, TextColor = "#FFF1E0", BackgroundColor = "#1A0B2E", AccentColor = "#FF7E5F", WarnColor = "#FEB47B",
            CritColor = "#FF3CAC", BorderColor = "#FEB47B", CpuStyle = DisplayStyle.Gauge, GpuStyle = DisplayStyle.Gauge, MemStyle = DisplayStyle.Gauge,
        },
        new ThemeData
        {
            Name = "Forest", Background = "forest", BackgroundOpacity = 60, TextColor = "#E8F5E9", BackgroundColor = "#10201A", AccentColor = "#66BB6A", WarnColor = "#FFCA28",
            CritColor = "#EF5350", BorderColor = "#2E7D32", CpuStyle = DisplayStyle.Gauge, GpuStyle = DisplayStyle.Gauge, MemStyle = DisplayStyle.Gauge,
        },
        new ThemeData
        {
            Name = "Minimal", ShowGpu = false, ShowNetUp = false, ShowBattery = false, Compact = true, LabelsAbove = true,
            BackgroundColor = "#101010", BorderColor = "", DashColumns = 1, DashOpacity = 80,
            DashHidden = new List<string> { "disk", "batt", "proc", "sys" }, FullHidden = new List<string> { "batt", "sys" },
        },
        new ThemeData
        {
            Name = "Meters", CpuStyle = DisplayStyle.Gauge, GpuStyle = DisplayStyle.Gauge, MemStyle = DisplayStyle.Gauge,
            AccentColor = "#30D158", BackgroundColor = "#0B0F14", BorderColor = "#30D158", FontFamily = "Bahnschrift",
        },
        new ThemeData
        {
            Name = "Neon", Background = "grid", BackgroundOpacity = 55, TextColor = "#E6FBFF", BackgroundColor = "#05060A", AccentColor = "#00E5FF", WarnColor = "#FFD60A",
            CritColor = "#FF2D95", BorderColor = "#FF2D95", CpuStyle = DisplayStyle.Bar, GpuStyle = DisplayStyle.Bar,
            MemStyle = DisplayStyle.Bar, FontFamily = "Consolas", DashOpacity = 95,
        },
    };

    public static List<ThemeData> User(AppSettings c)
    {
        var res = new List<ThemeData>();
        try
        {
            if (!Directory.Exists(Dir(c))) return res;
            foreach (var f in Directory.EnumerateFiles(Dir(c), "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                if (Read(f) is { } t)
                {
                    if (string.IsNullOrWhiteSpace(t.Name)) t.Name = Path.GetFileNameWithoutExtension(f);
                    res.Add(t);
                }
        }
        catch (Exception dex) { Diag.Swallow(dex); }
        return res;
    }

    public static ThemeData? Read(string path)
    {
        try { return JsonSerializer.Deserialize<ThemeData>(File.ReadAllText(path), Opts); } catch { return null; }
    }

    /// <summary>Schrijft een thema om te delen: met de vermelding van het programma en de link erin.</summary>
    public static void Export(string path, ThemeData t)
    {
        var copy = Parse(ToJson(t));
        copy.Generator = AppInfo.Signature;
        Write(path, copy);
    }

    public static void Write(string path, ThemeData t)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(t, Opts));
    }

    /// <summary>Eén regel in de themalijst: een meegeleverd thema (evt. overschreven door een eigen bestand met dezelfde naam) of een eigen thema.</summary>
    public sealed record ThemeEntry(ThemeData T, bool BuiltIn, bool Edited, string? File);

    /// <summary>
    /// Alle thema's zoals de gebruiker ze ziet: de meegeleverde (vervangen door een eigen bestand met dezelfde naam, want aangepaste
    /// standaardthema's worden op schijf bewaard) gevolgd door de overige eigen thema's.
    /// </summary>
    public static List<ThemeEntry> Entries(AppSettings c)
    {
        var mine = User(c);
        var res = new List<ThemeEntry>();
        foreach (var b in BuiltIn())
        {
            var over = mine.FirstOrDefault(m => m.Name.Equals(b.Name, StringComparison.OrdinalIgnoreCase));
            if (over is not null) { over.Name = b.Name; res.Add(new ThemeEntry(over, true, true, FileFor(c, b.Name))); }
            else res.Add(new ThemeEntry(b, true, false, null));
        }
        foreach (var m in mine)
            if (!IsBuiltInName(m.Name)) res.Add(new ThemeEntry(m, false, false, FileFor(c, m.Name)));
        return res;
    }

    public static bool IsBuiltInName(string name) => BuiltIn().Any(b => b.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>De JSON zoals die in de editor komt te staan.</summary>
    public static string ToJson(ThemeData t) => JsonSerializer.Serialize(t, Opts);

    /// <summary>Leest een thema uit JSON-tekst; een fout komt als <see cref="JsonException"/> (met regel en positie) naar boven.</summary>
    public static ThemeData Parse(string json)
        => JsonSerializer.Deserialize<ThemeData>(json, Opts) ?? throw new JsonException("empty");

    public static string FileFor(AppSettings c, string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = string.Concat(name.Select(ch => invalid.Contains(ch) ? '_' : ch)).Trim();
        return Path.Combine(Dir(c), (safe.Length == 0 ? "theme" : safe) + ".json");
    }
}
