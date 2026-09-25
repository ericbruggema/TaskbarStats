namespace TaskbarStats;

public sealed partial class AppSettings
{
    /// <summary>Widget blijft tegen het systeemvak staan en volgt het als dat verschuift (verslepen zet dit uit).</summary>
    public bool StickToTray { get; set; } = false;
}
