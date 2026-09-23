namespace TaskbarStats;

/// <summary>Eenvoudige tweetalige teksten (Nederlands/Engels) voor het menu.</summary>
public static class Loc
{
    public static string Lang = "nl";

    private static readonly Dictionary<string, (string nl, string en)> T = new()
    {
        ["components"]   = ("Onderdelen", "Components"),
        ["cpu"]          = ("CPU", "CPU"),
        ["gpu"]          = ("GPU", "GPU"),
        ["memory"]       = ("Geheugen", "Memory"),
        ["upload"]       = ("Upload", "Upload"),
        ["download"]     = ("Download", "Download"),
        ["cpuTemp"]      = ("CPU-temperatuur", "CPU temperature"),
        ["gpuTemp"]      = ("GPU-temperatuur", "GPU temperature"),
        ["display"]      = ("Weergave", "Display"),
        ["digital"]      = ("Digitaal", "Digital"),
        ["gauge"]        = ("Meter", "Gauge"),
        ["bar"]          = ("Balk", "Bar"),
        ["perCore"]      = ("Per core (balkjes)", "Per core (bars)"),
        ["colors"]       = ("Kleuren", "Colors"),
        ["textColor"]    = ("Tekstkleur…", "Text color…"),
        ["background"]   = ("Achtergrond…", "Background…"),
        ["meterColor"]   = ("Meter/balk-kleur…", "Meter/bar color…"),
        ["warnColor"]    = ("Waarschuwingskleur…", "Warning color…"),
        ["critColor"]    = ("Kritiek-kleur…", "Critical color…"),
        ["borderColor"]  = ("Randkleur", "Border color"),
        ["noBorder"]     = ("Geen rand", "No border"),
        ["thresholds"]   = ("Drempels (waarschuwing/kritiek)", "Thresholds (warning/critical)"),
        ["transparent"]  = ("Transparante achtergrond", "Transparent background"),
        ["netAdapter"]   = ("Netwerkadapter", "Network adapter"),
        ["allAdapters"]  = ("Alle adapters", "All adapters"),
        ["gpuSource"]    = ("GPU-bron", "GPU source"),
        ["auto"]         = ("Automatisch (drukste)", "Automatic (busiest)"),
        ["startup"]      = ("Met Windows meestarten", "Start with Windows"),
        ["resetPos"]     = ("Reset positie", "Reset position"),
        ["exit"]         = ("Afsluiten", "Exit"),
        ["interval"]     = ("Ververssnelheid", "Update interval"),
        ["language"]     = ("Taal", "Language"),
        ["dutch"]        = ("Nederlands", "Dutch"),
        ["english"]      = ("Engels", "English"),
        ["disks"]        = ("Schijven", "Disks"),
        ["diskIo"]       = ("Schijf lezen/schrijven", "Disk read/write"),
        ["diskSpace"]    = ("Schijfruimte in widget", "Disk space in widget"),
        ["off"]          = ("Uit", "Off"),
        ["total"]        = ("Totaal", "Total"),
        ["eachDisk"]     = ("Alle schijven apart", "Each disk separately"),
        ["freeOf"]       = ("vrij van", "free of"),
        ["usedWord"]     = ("gebruikt", "used"),
    };

    /// <summary>Kies de Nederlandse of Engelse tekst.</summary>
    public static string Pick(string nl, string en) => Lang == "en" ? en : nl;

    public static string S(string key)
        => T.TryGetValue(key, out var v) ? (Lang == "en" ? v.en : v.nl) : key;
}
