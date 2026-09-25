namespace TaskbarStats;

/// <summary>Eenheid voor snelheden in het widget: bytes (MB/s) of bits (Mb/s).</summary>
public enum RateUnitKind { Bytes, Bits }

/// <summary>Vaste eenheid voor snelheden in het widget, of automatisch de best passende.</summary>
public enum RateScale { Auto, Kilo, Mega }

/// <summary>Wat toont de geheugen-cel (digitale weergave)?</summary>
public enum MemShowMode { Percent, Used, Available }

/// <summary>Opmaak van de waarden in het taakbalk-widget en de extra widget-onderdelen. Heeft geen effect op dashboard, fullscreen en tooltip.</summary>
public sealed partial class AppSettings
{
    // Waarden in de widget-cellen
    public RateUnitKind NetUnit { get; set; } = RateUnitKind.Bytes;   // netwerk: bytes (MB/s) of bits (Mb/s)
    public RateScale RateScaleMode { get; set; } = RateScale.Auto;    // automatisch, of vast KB/s / MB/s
    public bool ShortValues { get; set; } = false;                    // minder decimalen
    public bool HideUnit { get; set; } = false;                       // eenheid weglaten (MB/s, GB, GHz)
    public bool HidePercent { get; set; } = false;                    // het %-teken weglaten
    public bool SwapNet { get; set; } = false;                        // download boven, upload onder
    public MemShowMode MemShow { get; set; } = MemShowMode.Percent;   // geheugen-cel: percentage / gebruikt / beschikbaar

    // Extra widget-onderdelen
    public bool ShowCpuFreq { get; set; } = false;        // klokfrequentie ("3,6 GHz")
    public bool ShowDiskBusy { get; set; } = false;       // schijf-actief (%)
    public DisplayStyle DiskBusyStyle { get; set; } = DisplayStyle.Digital;
    public bool ShowDiskTemp { get; set; } = false;       // heetste schijf (LibreHardwareMonitor)
    public bool ShowMoboTemp { get; set; } = false;       // hoofdbord (LibreHardwareMonitor)
}

/// <summary>De extra onderdelen horen bij een thema (zoals ShowCpuTemp); de waardeopmaak niet (dat is een persoonlijke voorkeur).</summary>
public sealed partial class ThemeData
{
    public bool ShowCpuFreq { get; set; }
    public bool ShowDiskBusy { get; set; }
    public DisplayStyle DiskBusyStyle { get; set; }
    public bool ShowDiskTemp { get; set; }
    public bool ShowMoboTemp { get; set; }
}
