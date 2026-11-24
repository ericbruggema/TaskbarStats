using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TaskbarStats.Plugins;

// Dit bestand wordt ook in de app zelf gecompileerd (zie TaskbarStats.csproj): de app toont wat een plugin mag en legt de goedkeuring
// vast, de host controleert die goedkeuring vóór hij een recept uitvoert. Het mag dus nergens van de host-onderdelen afhangen.

/// <summary>
/// Wat een recept (of het script-plugin) kan doen, afgeleid uit zijn configbestand: met welke adressen het praat, welke bestanden en
/// omgevingsvariabelen het leest, welke opdrachten het start en welke geheimen het gebruikt. De gebruiker keurt precies deze lijst goed;
/// verandert ze (bijvoorbeeld een ander adres), dan is opnieuw goedkeuren nodig.
/// </summary>
internal sealed class CapSet
{
    public List<string> Hosts { get; } = new();
    public List<string> Files { get; } = new();
    public List<string> Commands { get; } = new();
    public List<string> Secrets { get; } = new();
    public List<string> EnvVars { get; } = new();

    public bool IsEmpty => Hosts.Count + Files.Count + Commands.Count + Secrets.Count + EnvVars.Count == 0;

    /// <summary>Vaste, gesorteerde regels ("host:example.com"); de grondslag van de controlesom en van de lijst in het scherm.</summary>
    public IEnumerable<string> Lines()
    {
        foreach (var x in Hosts) yield return "host:" + x;
        foreach (var x in Files) yield return "file:" + x;
        foreach (var x in Commands) yield return "cmd:" + x;
        foreach (var x in Secrets) yield return "secret:" + x;
        foreach (var x in EnvVars) yield return "env:" + x;
    }

    public string Hash()
    {
        var sb = new StringBuilder("v1\n");
        foreach (var l in Lines()) sb.Append(l).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))[..32];
    }
}

internal static class Caps
{
    /// <summary><c>{secret:naam}</c>, <c>{file:pad#veld}</c>, <c>{env:NAAM}</c> in een waarde.</summary>
    internal static readonly Regex TokenRe = new(@"\{(secret|file|env):([^{}]+)\}", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static CapSet Compute(IniFile ini)
    {
        var set = new CapSet();
        void Scan(IniFile.Section s)
        {
            foreach (var (key, value) in s.Entries)
            {
                string k = key.ToLowerInvariant();
                if (k == "url")
                {
                    string probe = TokenRe.Replace(value, "x").Trim();
                    string host = Uri.TryCreate(probe, UriKind.Absolute, out var u) ? u.Host : "(invalid address)";
                    Add(set.Hosts, host.ToLowerInvariant());
                }
                else if (k == "file") Add(set.Files, value.Trim());
                else if (k == "cmd") Add(set.Commands, value.Trim());
                foreach (Match m in TokenRe.Matches(value))
                {
                    string kind = m.Groups[1].Value.ToLowerInvariant(), arg = m.Groups[2].Value.Trim();
                    if (kind == "secret") Add(set.Secrets, arg);
                    else if (kind == "env") Add(set.EnvVars, arg);
                    else Add(set.Files, arg.Split('#')[0].Trim());
                }
            }
        }
        Scan(ini.Root);
        foreach (var s in ini.Sections) Scan(s);
        set.Hosts.Sort(StringComparer.Ordinal); set.Files.Sort(StringComparer.Ordinal); set.Commands.Sort(StringComparer.Ordinal);
        set.Secrets.Sort(StringComparer.Ordinal); set.EnvVars.Sort(StringComparer.Ordinal);
        return set;
    }

    private static void Add(List<string> l, string v) { if (v.Length > 0 && !l.Contains(v)) l.Add(v); }

    /// <summary>Alleen deze soorten plugin vragen om goedkeuring: de recepten en het script-plugin (de rest praat alleen met vaste adressen).</summary>
    public static bool NeedsApproval(string id, IniFile ini)
        => id == "script" || string.Equals(ini.Str("type", ""), "recipe", StringComparison.OrdinalIgnoreCase);
}

/// <summary>De goedgekeurde plugins: <c>plugins\approved.json</c> met per id de controlesom van de toegestane lijst.</summary>
internal static class Approvals
{
    public static string FilePath(string dataDir) => Path.Combine(dataDir, "plugins", "approved.json");

    public static Dictionary<string, string> Load(string dataDir)
    {
        var res = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            string path = FilePath(dataDir);
            if (!File.Exists(path)) return res;
            using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
                foreach (var p in doc.RootElement.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.String) res[p.Name] = p.Value.GetString() ?? "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        return res;
    }

    public static bool IsApproved(string dataDir, string id, string hash)
        => Load(dataDir).TryGetValue(id, out var h) && string.Equals(h, hash, StringComparison.Ordinal);

    public static void Approve(string dataDir, string id, string hash)
    {
        var all = Load(dataDir);
        all[id] = hash;
        Save(dataDir, all);
    }

    public static void Revoke(string dataDir, string id)
    {
        var all = Load(dataDir);
        if (all.Remove(id)) Save(dataDir, all);
    }

    private static void Save(string dataDir, Dictionary<string, string> all)
    {
        string path = FilePath(dataDir);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            foreach (var kv in all.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)) w.WriteString(kv.Key, kv.Value);
            w.WriteEndObject();
        }
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, ms.ToArray());
        File.Move(tmp, path, true);
    }
}
