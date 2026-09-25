using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskbarStats;

/// <summary>Weergavestijl per onderdeel.</summary>
public enum DisplayStyle { Digital, Gauge, Bar, Graph }

/// <summary>Wat tonen we van de schijfruimte in het widget?</summary>
public enum DiskSpaceMode { Off, Total, Each, Single }

/// <summary>Waar staat het percentage bij de batterij?</summary>
public enum BatteryPercentMode { Inside, Beside, Off }

/// <summary>
/// Instellingen, opgeslagen als settings.json in %AppData%\TaskbarStats
/// (overleeft herbouw/herinstallatie). Wijzigingen via het menu worden direct bewaard.
/// </summary>
public sealed partial class AppSettings
{
    // Welke onderdelen tonen we?
    public bool ShowCpu { get; set; } = true;
    public bool ShowGpu { get; set; } = true;
    public bool ShowMem { get; set; } = true;
    public bool ShowNetUp { get; set; } = true;
    public bool ShowNetDown { get; set; } = true;
    public bool ShowBattery { get; set; } = true;   // verdwijnt vanzelf op een pc zonder accu
    public BatteryPercentMode BatteryPercent { get; set; } = BatteryPercentMode.Inside;
    public bool ShowDisk { get; set; } = false;   // lees/schrijfsnelheid (totaal)
    public bool ShowCpuTemp { get; set; } = false;
    public bool ShowGpuTemp { get; set; } = false;
    public DisplayStyle CpuTempStyle { get; set; } = DisplayStyle.Digital;   // temperatuur als cijfer, meter of balk
    public DisplayStyle GpuTempStyle { get; set; } = DisplayStyle.Digital;
    public bool TempMerge { get; set; } = false;         // temperatuur klein achter de CPU-/GPU-cel i.p.v. een eigen cel (smaller)
    public bool CpuUtility { get; set; } = false;        // false: CPU-% zoals Taakbeheer (tijd); true: "Processor Utility" (telt turbo mee, hoger)
    public bool ShowPing { get; set; } = false;          // ping naar PingHost in het widget (en meten)
    public string PingHost { get; set; } = "1.1.1.1";
    public int TourSeconds { get; set; } = 10;           // fullscreen-tour: seconden per pagina

    // Weergavestijl per onderdeel (digitaal / meter / balk)
    public DisplayStyle CpuStyle { get; set; } = DisplayStyle.Digital;
    public DisplayStyle GpuStyle { get; set; } = DisplayStyle.Digital;
    public DisplayStyle MemStyle { get; set; } = DisplayStyle.Digital;

    // CPU per core (balkjes) i.p.v. één totaalwaarde
    public bool CpuPerCore { get; set; } = false;

    // Netwerkadapter: null = alle adapters samengeteld, anders de instance-naam.
    public string? NetworkAdapter { get; set; } = null;

    // GPU: null = automatisch de drukste GPU volgen, anders een vaste LUID-string.
    public string? GpuLuid { get; set; } = null;

    // Schijfruimte in het widget: uit / totaal / elke schijf apart / één schijf (DiskSpaceDrive, bv. "C:")
    public DiskSpaceMode DiskSpace { get; set; } = DiskSpaceMode.Off;
    public bool IncludeNetworkDrives { get; set; } = false;   // gekoppelde netwerkschijven meenemen (op de achtergrond opgevraagd)
    public string DiskSpaceDrive { get; set; } = "C:";

    // Bureaublad-dashboard (groot, halfdoorzichtig, los van het taakbalk-widget)
    public bool ShowDashboard { get; set; } = false;
    public bool DashFront { get; set; } = false;          // false = op de achtergrond (onder alle vensters)
    public bool DashClickThrough { get; set; } = false;   // kliks gaan erdoorheen (aan/uit met Ctrl+Alt+D)
    public bool DashLocked { get; set; } = false;
    public int DashOpacity { get; set; } = 90;            // procent
    public int DashScale { get; set; } = 100;             // procent
    public int DashColumns { get; set; } = 2;
    public int? DashX { get; set; } = null;
    public int? DashY { get; set; } = null;
    public bool DashCpu { get; set; } = true;
    public bool DashGpu { get; set; } = true;
    public bool DashMem { get; set; } = true;
    public bool DashNet { get; set; } = true;
    public bool DashDisks { get; set; } = true;
    public bool DashBattery { get; set; } = true;
    public bool DashProcs { get; set; } = true;
    public bool DashSystem { get; set; } = true;

    // Volgorde van de onderdelen in het taakbalk-widget (ids uit Tiles.WidgetAll)
    public List<string>? WidgetOrder { get; set; } = null;

    // Volgorde van de hoofdonderdelen (ids uit Tiles.All); ontbrekende ids worden achteraan toegevoegd.
    public List<string>? DashOrder { get; set; } = null;
    public List<string>? FullOrder { get; set; } = null;
    public List<string>? FullHidden { get; set; } = null;   // onderdelen die in het fullscreen-scherm uit staan

    public string? FullMonitor { get; set; } = null;   // null = automatisch (scherm waar het widget staat)

    public bool WelcomeShown { get; set; } = false;   // eenmalig welkomstscherm al getoond?

    // Positie / gedrag
    public bool LockPosition { get; set; } = false;      // sleep uitschakelen
    public bool HideInFullscreen { get; set; } = true;   // verbergen bij volledig-scherm-apps
    public int TooltipDelayMs { get; set; } = 2000;      // wachttijd voor de tooltip boven het widget (minimaal 2 s); -1 = nooit

    // Meldingen
    public bool Notifications { get; set; } = true;
    public int DiskFullPercent { get; set; } = 90;       // melding als een schijf zo vol is
    public int CritSeconds { get; set; } = 30;           // melding als CPU/GPU/MEM zo lang op kritiek staat
    public int MonthlyLimitGb { get; set; } = 0;         // 0 = geen maandlimiet (data)

    // Kleuren
    public string TextColor { get; set; } = "#FFFFFF";
    public string BackgroundColor { get; set; } = "#141414";
    public string AccentColor { get; set; } = "#0A84FF";   // meter-/balkvulling
    public string WarnColor { get; set; } = "#FF9500";   // vanaf WarnThreshold
    public string CritColor { get; set; } = "#FF3B30";   // vanaf CritThreshold
    public int WarnThreshold { get; set; } = 85;
    public int CritThreshold { get; set; } = 95;
    public string BorderColor { get; set; } = "#FF00FF";   // leeg = geen rand

    // Weergave
    public bool TransparentBackground { get; set; } = false;
    public bool AutoHeight { get; set; } = true;      // hoogte volgt de taakbalk
    public int WidgetHeight { get; set; } = 44;       // vaste hoogte (als AutoHeight uit staat)
    public int RefreshMs { get; set; } = 1000;
    public string Language { get; set; } = "nl";   // "nl" of "en"
    public bool LabelsAbove { get; set; } = false;   // label (CPU/GPU/MEM) boven de grafiek i.p.v. ernaast
    public bool Compact { get; set; } = false;       // korte eenheden, kleinere marges en lettertype
    public int FontSize { get; set; } = 9;
    public string FontFamily { get; set; } = "Segoe UI";

    // Positie (zwevend)
    public int TrayGap { get; set; } = 5;
    // null = nog nooit versleept: dan wordt de standaardpositie naast het systeemvak berekend.
    public int? FloatX { get; set; } = null;
    public int? FloatY { get; set; } = null;

    [JsonIgnore] public string FilePath { get; private set; } = "";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static AppSettings Load()
    {
        // TASKBARSTATS_DATA = eigen datamap (voor tests/screenshots, zodat je echte instellingen ongemoeid blijven).
        var custom = Environment.GetEnvironmentVariable("TASKBARSTATS_DATA");
        var dir = !string.IsNullOrWhiteSpace(custom)
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskbarStats");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "settings.json");

        AppSettings s;
        try
        {
            s = File.Exists(path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOpts) ?? new AppSettings()
                : new AppSettings();
        }
        catch { s = new AppSettings(); }
        s.FilePath = path;
        return s;
    }

    private static readonly object _saveLock = new();

    public void Save()
    {
        // Atomair: eerst naar een tijdelijk bestand en dan vervangen, zodat een crash of stroomuitval het bestand nooit half achterlaat.
        lock (_saveLock)
        {
            try
            {
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOpts));
                File.Move(tmp, FilePath, true);
            }
            catch { /* best-effort */ }
        }
    }
}
