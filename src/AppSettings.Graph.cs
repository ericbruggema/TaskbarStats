namespace TaskbarStats;

/// <summary>Weergave voor onderdelen die alleen cijfers of een grafiek kennen (netwerk, ping).</summary>
public enum TextGraphStyle { Digital, Graph }

/// <summary>Breedte van een grafiekje in het widget.</summary>
public enum GraphLen { Tiny, Short, Medium, Long, Custom }

/// <summary>Eigen lengte van één grafiek: <see cref="Len"/> = null volgt de algemene lengte; <see cref="Px"/> geldt bij <see cref="GraphLen.Custom"/>.</summary>
public sealed class GraphSize
{
    public GraphLen? Len { get; set; }
    public int Px { get; set; } = 46;
}

// Instellingen voor de mini-geschiedenisgrafiek in het taakbalk-widget.
public sealed partial class AppSettings
{
    public int GraphSeconds { get; set; } = 60;                    // hoeveel seconden de grafiek toont (30/60/120)
    public GraphLen GraphLength { get; set; } = GraphLen.Medium;    // breedte van het grafiekje: kort / middel / lang
    public int GraphWidthPx { get; set; } = 46;                     // eigen breedte in px (bij GraphLength = Custom)
    /// <summary>Breedte van het grafiekje in px (bij 100% schaling).</summary>
    public int GraphWidthBase => WidthOf(GraphLength, GraphWidthPx);

    /// <summary>Eigen lengte per grafiek (sleutels: zie <see cref="GraphKeys"/>); een onderdeel zonder regel volgt de algemene lengte hierboven.</summary>
    public Dictionary<string, GraphSize> GraphSizes { get; set; } = new();

    /// <summary>De grafiek-onderdelen van het widget waarvoor een eigen lengte kan worden gekozen.</summary>
    public static readonly string[] GraphKeys = { "cpu", "gpu", "mem", "cputemp", "gputemp", "net", "ping" };

    /// <summary>Breedte in px (bij 100% schaling) van de grafiek van één onderdeel: de eigen keuze, anders de algemene lengte.</summary>
    public int GraphWidthFor(string key)
        => GraphSizes.TryGetValue(key, out var sz) && sz.Len is GraphLen l ? WidthOf(l, sz.Px) : GraphWidthBase;

    internal static int WidthOf(GraphLen len, int customPx) => len switch
    {
        GraphLen.Tiny => 24, GraphLen.Short => 34, GraphLen.Long => 60, GraphLen.Custom => Math.Clamp(customPx, 16, 160), _ => 46,
    };
    public TextGraphStyle NetStyle { get; set; } = TextGraphStyle.Digital;
    public TextGraphStyle PingStyle { get; set; } = TextGraphStyle.Digital;
    public int NetGraphMaxMBps { get; set; } = 0;                  // schaal van de netwerkgrafiek in MB/s; 0 = automatisch
}
