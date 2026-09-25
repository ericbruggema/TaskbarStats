namespace TaskbarStats;

/// <summary>Hoe het widget het Windows-thema volgt.</summary>
public enum WinThemeMode { Off, DarkLight, DarkLightAccent }

public sealed partial class AppSettings
{
    /// <summary>Widget volgt het Windows-thema (gedragsinstelling, hoort niet in een thema). Uit = eigen kleuren.</summary>
    public WinThemeMode WidgetFollowWindows { get; set; } = WinThemeMode.Off;
}
