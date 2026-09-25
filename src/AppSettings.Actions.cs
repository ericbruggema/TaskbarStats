namespace TaskbarStats;

/// <summary>Wat een muisklik op het widget doet.</summary>
public enum MouseAction { None, TaskManager, Dashboard, Fullscreen, Settings, UsageLog, CopyInfo, Menu }

// Instellingen voor muisacties, extra meldingen en de (opt-in) updatecontrole.
// Alle standaardwaarden laten het gedrag van eerdere versies ongemoeid.
public sealed partial class AppSettings
{
    // Muis
    public MouseAction DoubleClickAction { get; set; } = MouseAction.TaskManager;
    public MouseAction MiddleClickAction { get; set; } = MouseAction.CopyInfo;
    public bool WidgetClickThrough { get; set; } = false;   // klikken vallen door het widget heen (Ctrl+Alt+W of het pictogram bij de klok)

    // Extra meldingen (0 = uit)
    public int AlertCpuTempC { get; set; } = 0;
    public int AlertGpuTempC { get; set; } = 0;
    public int AlertDayNetGb { get; set; } = 0;      // netwerkverbruik vandaag (gekozen adapter of alle adapters)
    public int AlertMemPercent { get; set; } = 0;

    // Updatecontrole: alleen als de gebruiker het aanzet; nooit automatisch downloaden of installeren.
    public bool CheckUpdates { get; set; } = false;
    public DateTime? LastUpdateCheck { get; set; } = null;   // UTC van de laatste geslaagde controle
    public string? UpdateLatestTag { get; set; } = null;     // laatst gevonden versie (zodat het menu-item ook na herstart blijft)
    public string? UpdateLatestUrl { get; set; } = null;
    public string? UpdateNotifiedTag { get; set; } = null;   // voor welke versie is al een melding getoond
}
