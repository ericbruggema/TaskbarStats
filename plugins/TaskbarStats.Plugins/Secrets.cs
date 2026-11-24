using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace TaskbarStats.Plugins;

// Dit bestand wordt ook in de app zelf gecompileerd (zie TaskbarStats.csproj): de app bewaart geheimen, de host leest ze.

/// <summary>
/// Geheimen voor recepten (API-sleutels, tokens), bewaard in <c>plugins\secrets.dat</c>, versleuteld met de Windows-gegevensbescherming (DPAPI,
/// alleen te lezen door DIT Windows-account op DEZE pc). In een recept staat alleen <c>{secret:naam}</c>, nooit de waarde zelf.
/// De app toont een bewaard geheim nooit terug; je kunt het alleen vervangen of verwijderen.
/// </summary>
internal static class Secrets
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TaskbarStats.Plugins.v1");
    private static readonly Regex NameRe = new("^[A-Za-z0-9_-]{1,40}$", RegexOptions.CultureInvariant);

    public static string FilePath(string dataDir) => Path.Combine(dataDir, "plugins", "secrets.dat");
    public static bool ValidName(string name) => NameRe.IsMatch(name ?? "");

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob { public int cbData; public IntPtr pbData; }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DataBlob pDataIn, string? szDataDescr, ref DataBlob pOptionalEntropy, IntPtr pvReserved, IntPtr pPrompt, int dwFlags, out DataBlob pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DataBlob pDataIn, IntPtr ppszDataDescr, ref DataBlob pOptionalEntropy, IntPtr pvReserved, IntPtr pPrompt, int dwFlags, out DataBlob pDataOut);

    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr h);

    private const int UiForbidden = 0x1;

    private static byte[] Run(byte[] input, bool protect)
    {
        var inBlob = new DataBlob { cbData = input.Length, pbData = Marshal.AllocHGlobal(Math.Max(1, input.Length)) };
        var entBlob = new DataBlob { cbData = Entropy.Length, pbData = Marshal.AllocHGlobal(Entropy.Length) };
        try
        {
            Marshal.Copy(input, 0, inBlob.pbData, input.Length);
            Marshal.Copy(Entropy, 0, entBlob.pbData, Entropy.Length);
            DataBlob outBlob;
            bool ok = protect
                ? CryptProtectData(ref inBlob, null, ref entBlob, IntPtr.Zero, IntPtr.Zero, UiForbidden, out outBlob)
                : CryptUnprotectData(ref inBlob, IntPtr.Zero, ref entBlob, IntPtr.Zero, IntPtr.Zero, UiForbidden, out outBlob);
            if (!ok) throw new InvalidOperationException("Windows data protection failed (error " + Marshal.GetLastWin32Error() + ")");
            try
            {
                var res = new byte[outBlob.cbData];
                Marshal.Copy(outBlob.pbData, res, 0, res.Length);
                return res;
            }
            finally { LocalFree(outBlob.pbData); }
        }
        finally { Marshal.FreeHGlobal(inBlob.pbData); Marshal.FreeHGlobal(entBlob.pbData); }
    }

    private static Dictionary<string, string> LoadRaw(string dataDir)
    {
        var res = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            string path = FilePath(dataDir);
            if (!File.Exists(path)) return res;
            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                int tab = line.IndexOf('\t');
                if (tab > 0 && ValidName(line[..tab])) res[line[..tab]] = line[(tab + 1)..].Trim();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return res;
    }

    private static void SaveRaw(string dataDir, Dictionary<string, string> raw)
    {
        string path = FilePath(dataDir);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllLines(tmp, raw.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase).Select(k => k.Key + "\t" + k.Value), new UTF8Encoding(false));
        File.Move(tmp, path, true);
    }

    /// <summary>De namen van de bewaarde geheimen (zonder te ontsleutelen).</summary>
    public static List<string> Names(string dataDir) => LoadRaw(dataDir).Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();

    public static void Set(string dataDir, string name, string value)
    {
        if (!ValidName(name)) throw new ArgumentException("a name may contain letters, digits, - and _ (max 40)");
        if (string.IsNullOrEmpty(value)) throw new ArgumentException("the value is empty");
        var raw = LoadRaw(dataDir);
        raw[name] = Convert.ToBase64String(Run(Encoding.UTF8.GetBytes(value), true));
        SaveRaw(dataDir, raw);
    }

    public static bool Remove(string dataDir, string name)
    {
        var raw = LoadRaw(dataDir);
        if (!raw.Remove(name)) return false;
        SaveRaw(dataDir, raw);
        return true;
    }

    /// <summary>
    /// De geheimen ontsleuteld (alleen voor de host). Met <paramref name="only"/> alleen die namen: een recept krijgt zo alleen de geheimen die het
    /// zelf noemt (en die de gebruiker heeft goedgekeurd) en we ontsleutelen niet de hele kluis bij elke update. Een geheim dat niet te ontsleutelen is
    /// (ander account) wordt overgeslagen.
    /// </summary>
    public static Dictionary<string, string> LoadAll(string dataDir, IEnumerable<string>? only = null)
    {
        var res = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var wanted = only is null ? null : new HashSet<string>(only, StringComparer.OrdinalIgnoreCase);
        foreach (var kv in LoadRaw(dataDir))
        {
            if (wanted is not null && !wanted.Contains(kv.Key)) continue;
            try { res[kv.Key] = Encoding.UTF8.GetString(Run(Convert.FromBase64String(kv.Value), false)); }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException) { }
        }
        return res;
    }
}
