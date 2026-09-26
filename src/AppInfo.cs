namespace TaskbarStats;

/// <summary>Naam en link van het programma, voor de vermelding in alles wat de gebruiker kopieert of exporteert.</summary>
public static class AppInfo
{
    public const string Url = "https://github.com/ericbruggema/TaskbarStats";

    /// <summary>"TaskbarStats 1.6.1 - https://github.com/..."</summary>
    public static string Signature => "TaskbarStats " + AboutForm.Version + " - " + Url;
}
