using System.Text;
using System.Text.RegularExpressions;
using TaskbarStats.Plugins;

namespace TaskbarStats;

/// <summary>Een sjabloon in de galerij (Instellingen > Plugins > Plugin toevoegen): een meegeleverde plugin of een recept.</summary>
public sealed record PluginTemplate(string Id, string Name, string Description, string Needs, string Text, bool BuiltIn);

/// <summary>
/// Welke plugins er bestaan. Een plugin bestaat pas als hij is <b>toegevoegd</b> (geregistreerd): zijn configbestand
/// <c>plugins\&lt;id&gt;.ini</c> staat dan in de datamap. Zonder dat bestand is hij nergens te zien: niet in de instellingen, niet in het widget.
/// Toevoegen gaat via de galerij (meegeleverde plugins en recepten) of door een recept uit een bestand te importeren; verwijderen haalt
/// het configbestand en de waarden weer weg. Recepten en het script-plugin draaien pas nadat de gebruiker heeft goedgekeurd wat ze mogen
/// (adressen, bestanden, opdrachten, geheimen; zie <see cref="Caps"/>).
/// </summary>
public static class PluginRegistry
{
    private static readonly Regex IdRe = new("^[A-Za-z0-9_-]{1,40}$", RegexOptions.CultureInvariant);
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase) { "approved", "secrets", "host", "cache", "templates", "defaults" };

    private static string PluginsDir(string? dataDir) => Path.Combine(dataDir ?? AppPaths.DataDir, "plugins");
    private static string IniPath(string id, string? dataDir) => Path.Combine(PluginsDir(dataDir), id + ".ini");

    public static string TemplatesDir => Path.Combine(AppPaths.ExeDir ?? AppContext.BaseDirectory, "plugins", "templates");

    public static bool ValidId(string id) => IdRe.IsMatch(id ?? "") && !Reserved.Contains(id!);

    /// <summary>Soort: <c>builtin</c> (meegeleverde code), <c>recipe</c>, <c>external</c> (eigen programma schrijft het JSON-bestand) of leeg (geen plugin).</summary>
    internal static string TypeOfText(string id, string text)
    {
        if (PluginCatalog.IsBuiltIn(id)) return "builtin";
        string t = IniFile.Parse(text).Str("type", "").ToLowerInvariant();
        return t is "recipe" or "external" ? t : "";
    }

    public static string TypeOf(string id, string? dataDir = null)
    {
        try { return File.Exists(IniPath(id, dataDir)) ? TypeOfText(id, File.ReadAllText(IniPath(id, dataDir), Encoding.UTF8)) : ""; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Diag.Swallow(ex); return ""; }
    }

    public static bool IsRegistered(string id, string? dataDir = null) => ValidId(id) && TypeOf(id, dataDir) != "";

    /// <summary>De toegevoegde plugins: eerst de meegeleverde in vaste volgorde, daarna de rest op naam.</summary>
    public static List<string> Ids(string? dataDir = null) => Entries(dataDir).Select(e => e.Id).ToList();

    /// <summary>De toegevoegde plugins MET hun configtekst (elk bestand wordt maar één keer gelezen), in dezelfde volgorde als <see cref="Ids"/>.</summary>
    internal static List<(string Id, string Text)> Entries(string? dataDir = null)
    {
        var res = new List<(string Id, string Text)>();
        try
        {
            string dir = PluginsDir(dataDir);
            if (!Directory.Exists(dir)) return res;
            foreach (var f in Directory.EnumerateFiles(dir, "*.ini"))
            {
                string id = Path.GetFileNameWithoutExtension(f);
                if (!ValidId(id)) continue;
                try
                {
                    string text = File.ReadAllText(f, Encoding.UTF8);
                    if (TypeOfText(id, text) != "") res.Add((id, text));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Diag.Swallow(ex); }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Diag.Swallow(ex); }
        return res.OrderBy(e => PluginCatalog.OrderOf(e.Id)).ThenBy(e => e.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Een goedkope vingerafdruk van de configbestanden en de goedkeuringen (naam, tijdstip, grootte): verandert hij niet, dan hoeft niets opnieuw
    /// gelezen of uitgerekend te worden. Eén mapaanroep, zonder de bestanden te openen.
    /// </summary>
    internal static string Signature(string? dataDir = null)
    {
        try
        {
            var dir = new DirectoryInfo(PluginsDir(dataDir));
            if (!dir.Exists) return "";
            var sb = new StringBuilder();
            foreach (var f in dir.EnumerateFiles().Where(f => f.Extension.Equals(".ini", StringComparison.OrdinalIgnoreCase) || f.Name.Equals("approved.json", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
                sb.Append(f.Name).Append(':').Append(f.LastWriteTimeUtc.Ticks).Append(':').Append(f.Length).Append(';');
            return sb.ToString();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Diag.Swallow(ex); return ""; }
    }

    /// <summary>Naam uit het configbestand (<c>title =</c>), of null.</summary>
    internal static string? IniTitle(string id)
    {
        try
        {
            if (!File.Exists(IniPath(id, null))) return null;
            string t = IniFile.Parse(File.ReadAllText(IniPath(id, null), Encoding.UTF8)).Str("title", "");
            return t.Length > 0 ? t : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Diag.Swallow(ex); return null; }
    }

    public static string DescriptionOf(string id)
    {
        if (PluginCatalog.IsBuiltIn(id)) return PluginCatalog.DescriptionOf(id);
        try
        {
            var ini = IniFile.Parse(File.ReadAllText(IniPath(id, null), Encoding.UTF8));
            string d = ini.Str("description", "");
            if (d.Length > 0) return d;
            if (ini.Str("type", "").Equals("external", StringComparison.OrdinalIgnoreCase))
                return Loc.T("Your own program or script writes the values (plugins\\{0}.json). TaskbarStats starts nothing for this plugin.", id);
            var caps = Caps.Compute(ini);
            if (caps.Hosts.Count > 0) return Loc.T("Recipe. Contacts: {0}.", string.Join(", ", caps.Hosts));
            if (caps.Commands.Count > 0) return Loc.T("Recipe. Runs a command on this pc.");
            if (caps.Files.Count > 0) return Loc.T("Recipe. Reads a file on this pc.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Diag.Swallow(ex); }
        return Loc.T("Recipe.");
    }

    // ---------- galerij ----------
    private static string Meta(IniFile ini, string key) => ini.Str("template_" + key, "");

    /// <summary>Alle sjablonen: de meegeleverde plugins en de recepten uit <c>plugins\templates</c> naast de app.</summary>
    public static List<PluginTemplate> Templates()
    {
        var res = new List<PluginTemplate>();
        foreach (var id in PluginCatalog.Ids)
            res.Add(new PluginTemplate(id, PluginCatalog.TitleOf(id), PluginCatalog.DescriptionOf(id), id == "claude" ? Loc.T("Signed in with Claude Code: run 'claude' in a terminal and type /login (once).") : "", PluginConfig.DefaultText(id), true));
        try
        {
            if (Directory.Exists(TemplatesDir))
            {
                var recipes = new List<PluginTemplate>();
                foreach (var f in Directory.EnumerateFiles(TemplatesDir, "*.ini"))
                {
                    string id = Path.GetFileNameWithoutExtension(f);
                    if (!ValidId(id)) continue;
                    string text = File.ReadAllText(f, Encoding.UTF8);
                    var ini = IniFile.Parse(text);
                    string name = Meta(ini, "name");
                    if (name.Length == 0) continue;
                    recipes.Add(new PluginTemplate(id, name, Meta(ini, "desc"), Meta(ini, "needs"), text, false));
                }
                res.AddRange(recipes.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Diag.Swallow(ex); }
        return res;
    }

    /// <summary>Haalt de <c>template_*</c>-regels weg (alleen voor de galerij) en zet <c>enabled = false</c>: een nieuwe plugin begint uit.</summary>
    public static string PrepareForAdd(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n')
            .Where(l => !l.TrimStart().StartsWith("template_", StringComparison.OrdinalIgnoreCase)).ToList();
        while (lines.Count > 0 && lines[0].Trim().Length == 0) lines.RemoveAt(0);
        return PluginConfig.WithEnabled(string.Join("\n", lines), false).Replace("\r\n", "\n").Replace("\n", "\r\n") + "\r\n";
    }

    /// <summary>Een vrije, geldige id uit een gewenste naam (onzin eruit, bij een bestaande id komt er -2, -3 achter).</summary>
    public static string UniqueId(string wish, string? dataDir = null)
    {
        string b = Regex.Replace((wish ?? "").Trim().ToLowerInvariant(), "[^a-z0-9_-]+", "-").Trim('-');
        if (b.Length == 0) b = "plugin";
        if (b.Length > 34) b = b[..34];
        string id = b; int n = 2;
        while (Reserved.Contains(id) || File.Exists(IniPath(id, dataDir))) id = b + "-" + n++;
        return id;
    }

    /// <summary>null = mag toegevoegd worden; anders de reden (voor import van een bestand).</summary>
    public static string? CheckImport(string id, string text)
    {
        var err = PluginConfig.Validate(text);
        if (err is not null) return err;
        if (TypeOfText(id, text) == "")
            return Loc.T("This file is not a plugin: it needs 'type = recipe' (or 'type = external') above the first [section].");
        return null;
    }

    /// <summary>Voegt een plugin toe (schrijft het configbestand). Geeft de uiteindelijke id terug. Bestaat de id al, dan is dat een fout (de aanroeper kiest een vrije id).</summary>
    public static string Add(string id, string text, string? dataDir = null)
    {
        if (!ValidId(id)) throw new ArgumentException("invalid id");
        string path = IniPath(id, dataDir);
        if (File.Exists(path)) throw new IOException("already added");
        Directory.CreateDirectory(PluginsDir(dataDir));
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, PrepareForAdd(text), new UTF8Encoding(false));
        File.Move(tmp, path, false);
        return id;
    }

    /// <summary>Verwijdert een plugin: configbestand, waarden en goedkeuring (geheimen blijven staan: andere recepten kunnen ze gebruiken).</summary>
    public static void Remove(string id, string? dataDir = null)
    {
        if (!ValidId(id)) return;
        foreach (var path in new[] { IniPath(id, dataDir), Path.Combine(PluginsDir(dataDir), id + ".json"), IniPath(id, dataDir) + ".tmp" })
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Diag.Swallow(ex); }
        }
        try { Approvals.Revoke(dataDir ?? AppPaths.DataDir, id); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Diag.Swallow(ex); }
    }

    // ---------- rechten en goedkeuring ----------
    public static bool NeedsApproval(string id, string text) => Caps.NeedsApproval(id, IniFile.Parse(text));

    internal static CapSet CapsOf(string text) => Caps.Compute(IniFile.Parse(text));

    public static bool IsApproved(string id, string text, string? dataDir = null)
        => !NeedsApproval(id, text) || Approvals.IsApproved(dataDir ?? AppPaths.DataDir, id, CapsOf(text).Hash());

    public static void Approve(string id, string text, string? dataDir = null)
        => Approvals.Approve(dataDir ?? AppPaths.DataDir, id, CapsOf(text).Hash());

    /// <summary>De lijst voor het goedkeuringsscherm: wat de plugin mag, in gewone woorden (met de technische waarde erbij).</summary>
    internal static List<(string Kind, string What, bool Danger)> DescribeCaps(string text)
    {
        var c = CapsOf(text);
        var res = new List<(string, string, bool)>();
        foreach (var h in c.Hosts) res.Add((Loc.T("Connects to the internet address"), h, false));
        foreach (var f in c.Files) res.Add((Loc.T("Reads the file"), f, false));
        foreach (var cmd in c.Commands) res.Add((Loc.T("RUNS this command on your pc (as you, without administrator rights)"), cmd, true));
        foreach (var s in c.Secrets) res.Add((Loc.T("Uses the saved secret"), s, false));
        foreach (var e in c.EnvVars) res.Add((Loc.T("Reads the environment variable"), e, false));
        return res;
    }
}
