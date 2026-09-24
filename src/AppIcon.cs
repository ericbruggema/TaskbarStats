namespace TaskbarStats;

/// <summary>Het programma-icoon (ingebed, meerdere formaten) voor venster-, taakbalk- en systeemvak-pictogrammen.</summary>
public static class AppIcon
{
    private static Icon? _small, _large;

    private static Icon Load(Size size)
    {
        try
        {
            using var s = typeof(AppIcon).Assembly.GetManifestResourceStream("app.ico");
            if (s is not null) return new Icon(s, size);
        }
        catch { }
        return SystemIcons.Application;
    }

    /// <summary>Klein formaat (systeemvak, titelbalk).</summary>
    public static Icon Small => _small ??= Load(SystemInformation.SmallIconSize);

    /// <summary>Groot formaat (taakbalkknop, Alt+Tab).</summary>
    public static Icon Large => _large ??= Load(SystemInformation.IconSize);

    /// <summary>Zet het icoon op een gewoon venster.</summary>
    public static void Apply(Form f)
    {
        try { f.Icon = Large; } catch { }
    }
}
