namespace TaskbarStats;

// Achtergrondafbeelding per venster (persoonlijk pad: hoort niet in thema's).
public sealed partial class AppSettings
{
    public string? WidgetBgImage { get; set; } = null;
    public BgMode WidgetBgMode { get; set; } = BgMode.Fill;
    public int WidgetBgOpacity { get; set; } = 100;   // procent

    public string? DashBgImage { get; set; } = null;
    public BgMode DashBgMode { get; set; } = BgMode.Fill;
    public int DashBgOpacity { get; set; } = 100;

    public string? FullBgImage { get; set; } = null;
    public BgMode FullBgMode { get; set; } = BgMode.Fill;
    public int FullBgOpacity { get; set; } = 100;
}
