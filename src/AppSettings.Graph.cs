namespace TaskbarStats;

/// <summary>Weergave voor onderdelen die alleen cijfers of een grafiek kennen (netwerk, ping).</summary>
public enum TextGraphStyle { Digital, Graph }

// Instellingen voor de mini-geschiedenisgrafiek in het taakbalk-widget.
public sealed partial class AppSettings
{
    public int GraphSeconds { get; set; } = 60;                    // hoeveel seconden de grafiek toont (30/60/120)
    public TextGraphStyle NetStyle { get; set; } = TextGraphStyle.Digital;
    public TextGraphStyle PingStyle { get; set; } = TextGraphStyle.Digital;
    public int NetGraphMaxMBps { get; set; } = 0;                  // schaal van de netwerkgrafiek in MB/s; 0 = automatisch
}
