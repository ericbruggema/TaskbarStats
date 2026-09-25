namespace TaskbarStats;

/// <summary>Weergave voor onderdelen die alleen cijfers of een grafiek kennen (netwerk, ping).</summary>
public enum TextGraphStyle { Digital, Graph }

/// <summary>Breedte van een grafiekje in het widget.</summary>
public enum GraphLen { Tiny, Short, Medium, Long, Custom }

// Instellingen voor de mini-geschiedenisgrafiek in het taakbalk-widget.
public sealed partial class AppSettings
{
    public int GraphSeconds { get; set; } = 60;                    // hoeveel seconden de grafiek toont (30/60/120)
    public GraphLen GraphLength { get; set; } = GraphLen.Medium;    // breedte van het grafiekje: kort / middel / lang
    public int GraphWidthPx { get; set; } = 46;                     // eigen breedte in px (bij GraphLength = Custom)
    /// <summary>Breedte van het grafiekje in px (bij 100% schaling).</summary>
    public int GraphWidthBase => GraphLength switch
    {
        GraphLen.Tiny => 24, GraphLen.Short => 34, GraphLen.Long => 60, GraphLen.Custom => Math.Clamp(GraphWidthPx, 16, 160), _ => 46,
    };
    public TextGraphStyle NetStyle { get; set; } = TextGraphStyle.Digital;
    public TextGraphStyle PingStyle { get; set; } = TextGraphStyle.Digital;
    public int NetGraphMaxMBps { get; set; } = 0;                  // schaal van de netwerkgrafiek in MB/s; 0 = automatisch
}
