namespace TaskbarStats;

/// <summary>Specificaties zoals de specificatiepagina ze toont (uit <see cref="HardwareInfo"/>, of door een test opgegeven).</summary>
public sealed record SpecData(IReadOnlyList<SpecBlock> Blocks, bool Loading, string LastError);

/// <summary>
/// Weergavetoestand van het fullscreen-scherm: welke pagina, welk grafiekvenster, scrollstand en tour.
/// Het venster (<see cref="FullscreenForm"/>) beheert en wijzigt dit; <see cref="FullscreenRenderer"/> leest het bij het tekenen
/// (alleen <see cref="SpecScroll"/> en <see cref="SpecMax"/> schrijft de renderer terug: de lengte van de lijst is pas bij het tekenen bekend).
/// </summary>
public sealed class FullscreenView
{
    /// <summary>null = overzicht, anders "cpu", "gpu", "mem", "net", "disk", "sys", "proc" of "spec".</summary>
    public string? Detail { get; set; }
    /// <summary>Sleutel van het klikbare vlak onder de muis (voor de markering).</summary>
    public string? Hover { get; set; }
    /// <summary>Grafiekvenster in seconden: 60, 300 of 3600.</summary>
    public int Win { get; set; } = 300;
    public float SpecScroll { get; set; }
    public float SpecMax { get; set; }
    /// <summary>Tick (Environment.TickCount64) waarop het scherm openging (voor "Loading sensors…").</summary>
    public long OpenedAt { get; set; }

    // Tour
    public bool Tour { get; set; }
    public int TourIdx { get; set; }
    public int TourCount { get; set; }
    public long TourAt { get; set; }

    /// <summary>Duur van één tourpagina in milliseconden (uit de instelling, begrensd op 3–300 s).</summary>
    public static int TourMillis(AppSettings cfg) => Math.Clamp(cfg.TourSeconds, 3, 300) * 1000;
}
