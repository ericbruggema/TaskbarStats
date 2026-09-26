using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LangTool;

/// <summary>
/// Beheer van de taalbestanden (lang\*.json) tegen de teksten in de code.
///   check [--strict] [--lang xx] [--quiet]   controleert alles (fouten = exitcode 1; --strict: ook ontbrekende vertalingen)
///   sync [--prune]                           zet ontbrekende sleutels (leeg) in alle taalbestanden, sorteert; --prune verwijdert ongebruikte
///   rename "oud" "nieuw"                     hernoemt een sleutel in de code én in alle taalbestanden
///   new xx "Naam" [--culture xx-XX]          maakt een nieuw taalbestand met alle sleutels leeg
///   status [--md]                            dekking per taal
/// De uitvoer heeft het MSBuild-formaat (bestand(regel,kolom): error LANGnnn: ...), dus fouten verschijnen in de build en de IDE.
/// </summary>
internal static class Program
{
    private static string Root = "";
    private static readonly string[] PrivateFiles = { "Cadence.cs", "MiniForm.cs" };   // bewust buiten de taalbestanden
    private static readonly HashSet<string> Brand = new() { "TaskbarStats" };

    private static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        var a = args.ToList();
        string cmd = a.Count > 0 && !a[0].StartsWith("--") ? a[0] : "check";
        if (a.Count > 0 && !a[0].StartsWith("--")) a.RemoveAt(0);
        bool Flag(string f) => a.Remove(f);
        string? Opt(string f) { int i = a.IndexOf(f); if (i < 0 || i + 1 >= a.Count) return null; var v = a[i + 1]; a.RemoveRange(i, 2); return v; }

        string? root = Opt("--root");
        Root = root ?? FindRoot();
        if (Root == "") { Console.Error.WriteLine("LANG000: TaskbarStats.csproj niet gevonden (gebruik --root)."); return 2; }

        switch (cmd)
        {
            case "check": { bool strict = Flag("--strict"), quiet = Flag("--quiet"); return Check(strict, Opt("--lang"), quiet); }
            case "sync": return Sync(Flag("--prune"));
            case "rename": return a.Count == 2 ? Rename(a[0], a[1]) : Usage();
            case "new": { string? c = Opt("--culture"); return a.Count == 2 ? NewLang(a[0], a[1], c) : Usage(); }
            case "status": return Status(Flag("--md"));
            default: return Usage();
        }
    }

    private static int Usage()
    {
        Console.Error.WriteLine("Gebruik: LangTool check [--strict] [--lang xx] [--quiet] | sync [--prune] | rename \"oud\" \"nieuw\" | new xx \"Naam\" [--culture xx-XX] | status [--md]");
        return 2;
    }

    private static string FindRoot()
    {
        var d = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (d is not null) { if (File.Exists(Path.Combine(d.FullName, "TaskbarStats.csproj"))) return d.FullName; d = d.Parent; }
        d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null) { if (File.Exists(Path.Combine(d.FullName, "TaskbarStats.csproj"))) return d.FullName; d = d.Parent; }
        return "";
    }

    // ------------------------------------------------------------------------------------------------ diagnostiek

    private static int _errors, _warnings;
    private static bool _quiet;
    private static void Diag(bool error, string code, string file, int line, int col, string msg)
    {
        if (error) _errors++; else _warnings++;
        if (_quiet && !error) return;
        string rel = Path.GetRelativePath(Root, file);
        Console.WriteLine($"{rel}({line},{col}): {(error ? "error" : "warning")} {code}: {msg}");
    }

    // ------------------------------------------------------------------------------------------------ code doorzoeken

    private sealed record Use(string Key, string File, int Line, int Col, char Kind);   // Kind: T, N (markering), P (meervoud), L (regel uit een tekstblok)

    private static List<Use> ScanCode()
    {
        var uses = new List<Use>();
        var files = Directory.GetFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories);
        foreach (var f in files)
        {
            bool priv = PrivateFiles.Contains(Path.GetFileName(f));
            var text = File.ReadAllText(f);
            var tree = CSharpSyntaxTree.ParseText(text, path: f);
            var root = tree.GetRoot();
            var lines = text.Split('\n');
            string LineText(SyntaxNode n) { int l = n.GetLocation().GetLineSpan().StartLinePosition.Line; return l < lines.Length ? lines[l] : ""; }
            (int, int) Pos(SyntaxNodeOrToken n) { var p = n.GetLocation()!.GetLineSpan().StartLinePosition; return (p.Line + 1, p.Character + 1); }

            foreach (var node in root.DescendantNodes())
            {
                if (priv) break;

                // Loc.T / Loc.N / Loc.P(...)
                if (node is InvocationExpressionSyntax inv &&
                    inv.Expression is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.Text: "Loc" }, Name.Identifier.Text: var name } &&
                    name is "T" or "N" or "P")
                {
                    var (ln, col) = Pos(inv);
                    if (inv.ArgumentList.Arguments.Count == 0) { Diag(true, "LANG002", f, ln, col, $"Loc.{name} zonder tekst."); continue; }
                    var first = inv.ArgumentList.Arguments[0].Expression;
                    if (first is InterpolatedStringExpressionSyntax)
                    { Diag(true, "LANG003", f, ln, col, "Loc." + name + " met een $\"…\"-tekst: gebruik Loc.T(\"tekst met {0}\", waarde) zodat de tekst vertaald kan worden."); continue; }
                    if (!TryConst(first, out var key))
                    {
                        if (!LineText(inv).Contains("lang-dynamic")) Diag(false, "LANG002", f, ln, col, $"Loc.{name}(…) met een niet-vaste tekst: de tekst wordt niet gevonden. Markeer vaste teksten met Loc.N(\"…\") of zet // lang-dynamic op de regel.");
                        continue;
                    }
                    if (key.Length == 0) continue;   // lege tekst hoeft niet vertaald
                    char kind = name[0];
                    uses.Add(new Use(key, f, ln, col, kind));
                    CheckCall(f, ln, col, key, kind, inv.ArgumentList.Arguments.Skip(1).ToList());
                }

                // Tekstblokken waarvan elke regel een vertaalsleutel is: markeer de declaratie met // lang-lines
                if (node is VariableDeclaratorSyntax vd && vd.Initializer?.Value is LiteralExpressionSyntax { Token: var tok } lit && lit.IsKind(SyntaxKind.StringLiteralExpression) &&
                    (node.Parent?.Parent?.GetLeadingTrivia().ToString().Contains("lang-lines") ?? false))
                {
                    var (ln, col) = Pos(tok);
                    int i = 0;
                    foreach (var l in tok.ValueText.Replace("\r", "").Split('\n')) { if (l.Trim().Length > 0) uses.Add(new Use(l, f, ln + 1 + i, col, 'L')); i++; }
                }
            }
        }
        return uses;
    }

    private static bool TryConst(ExpressionSyntax e, out string s)
    {
        s = "";
        switch (e)
        {
            case LiteralExpressionSyntax l when l.IsKind(SyntaxKind.StringLiteralExpression): s = l.Token.ValueText; return true;
            case ParenthesizedExpressionSyntax p: return TryConst(p.Expression, out s);
            case BinaryExpressionSyntax b when b.IsKind(SyntaxKind.AddExpression):
                if (TryConst(b.Left, out var x) && TryConst(b.Right, out var y)) { s = x + y; return true; }
                return false;
        }
        return false;
    }

    private static void CheckCall(string f, int ln, int col, string key, char kind, List<ArgumentSyntax> extra)
    {
        if (kind == 'N') { CheckKeyShape(f, ln, col, key, true); return; }
        var ph = Placeholders(key);
        if (kind == 'P')
        {
            if (key.Count(c => c == '|') != 1) Diag(true, "LANG006", f, ln, col, "Loc.P: de sleutel moet twee Engelse vormen hebben, gescheiden door één '|' (\"{0} day|{0} days\").");
            CheckKeyShape(f, ln, col, key, true);
            return;   // n is altijd {0}
        }
        CheckKeyShape(f, ln, col, key, extra.Count > 0);
        bool spread = extra.Count == 1 && (extra[0].Expression is ArrayCreationExpressionSyntax or ImplicitArrayCreationExpressionSyntax or CollectionExpressionSyntax);
        if (spread) return;
        int need = ph.Count == 0 ? 0 : ph.Max() + 1;
        if (extra.Count < need) Diag(true, "LANG004", f, ln, col, $"\"{Short(key)}\" heeft {{{need - 1}}} maar krijgt maar {extra.Count} waarde(n).");
        else if (extra.Count > 0 && ph.Count == 0) Diag(false, "LANG005", f, ln, col, $"\"{Short(key)}\" krijgt waarden maar heeft geen {{0}}.");
    }

    private static void CheckKeyShape(string f, int ln, int col, string key, bool formatted)
    {
        if (key.Length == 0) return;
        if (key != key.Trim() && key.Trim().Length > 0) { /* begin/eind-spaties zijn toegestaan; vertalingen moeten ze overnemen */ }
        if (!BracesOk(key, formatted)) Diag(true, "LANG007", f, ln, col, $"Accolades in \"{Short(key)}\" kloppen niet (letterlijke accolades bij Format dubbel: {{{{ en }}}}; zonder waarden juist enkel).");
    }

    private static readonly Regex PhRx = new(@"\{(\d+)(?:,-?\d+)?(?::[^{}]*)?\}", RegexOptions.Compiled);
    private static List<int> Placeholders(string s)
    {
        var t = s.Replace("{{", "").Replace("}}", "");
        return PhRx.Matches(t).Select(m => int.Parse(m.Groups[1].Value)).Distinct().OrderBy(x => x).ToList();
    }
    private static bool BracesOk(string s, bool formatted)
    {
        if (!formatted) return true;
        var t = s.Replace("{{", "").Replace("}}", "");
        t = PhRx.Replace(t, "");
        return !t.Contains('{') && !t.Contains('}');
    }
    private static string Short(string s) => (s.Length > 60 ? s[..57] + "…" : s).Replace("\n", "\\n");

    // ------------------------------------------------------------------------------------------------ hardgecodeerde teksten

    private static void ScanHardcoded()
    {
        foreach (var f in Directory.GetFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            if (PrivateFiles.Contains(Path.GetFileName(f))) continue;
            var text = File.ReadAllText(f);
            var lines = text.Split('\n');
            var root = CSharpSyntaxTree.ParseText(text, path: f).GetRoot();
            foreach (var lit in root.DescendantNodes().OfType<LiteralExpressionSyntax>().Where(l => l.IsKind(SyntaxKind.StringLiteralExpression)))
            {
                string v = lit.Token.ValueText;
                if (v.Count(char.IsLetter) < 3 || Brand.Contains(v)) continue;
                bool ui = false;
                var p = lit.Parent;
                if (p is AssignmentExpressionSyntax { Left: var left } && left.ToString().EndsWith("Text")) ui = true;                       // x.Text = "…" / new() { Text = "…" }
                else if (p is ArgumentSyntax { Parent.Parent: InvocationExpressionSyntax { Expression: var ex } } && ex.ToString().EndsWith("MessageBox.Show")) ui = true;
                else if (p is ArgumentSyntax { Parent.Parent: ObjectCreationExpressionSyntax { Type: var t } } && t.ToString() is "ToolStripMenuItem" or "ToolStripLabel") ui = true;
                if (!ui) continue;
                var pos = lit.GetLocation().GetLineSpan().StartLinePosition;
                if (pos.Line < lines.Length && lines[pos.Line].Contains("nolang")) continue;
                Diag(false, "LANG010", f, pos.Line + 1, pos.Character + 1, $"Tekst \"{Short(v)}\" staat vast in de code: gebruik Loc.T(\"…\") (of zet // nolang op de regel).");
            }
        }
    }

    // ------------------------------------------------------------------------------------------------ taalbestanden

    private sealed class LangFile
    {
        public string Code = "", Path = "";
        public List<(string Key, string Value, int Line)> Entries = new();
        public Dictionary<string, string> Map = new(StringComparer.Ordinal);
        public Dictionary<string, string> Meta => Map.Where(kv => kv.Key.StartsWith('_')).ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    private static string LangDir => Path.Combine(Root, "lang");

    private static LangFile? ReadLang(string path, bool report = true)
    {
        var lf = new LangFile { Code = Path.GetFileNameWithoutExtension(path).ToLowerInvariant(), Path = path };
        var bytes = File.ReadAllBytes(path);
        int Line(long idx) { int n = 1; for (long i = 0; i < idx && i < bytes.Length; i++) if (bytes[i] == (byte)'\n') n++; return n; }
        try
        {
            var r = new Utf8JsonReader(bytes, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (!r.Read() || r.TokenType != JsonTokenType.StartObject) throw new JsonException("verwacht { … }");
            while (r.Read() && r.TokenType == JsonTokenType.PropertyName)
            {
                string k = r.GetString()!; int line = Line(r.TokenStartIndex);
                if (!r.Read() || r.TokenType != JsonTokenType.String) { if (report) Diag(true, "LANG110", path, line, 1, $"Waarde van \"{Short(k)}\" is geen tekst."); r.Skip(); continue; }
                string v = r.GetString()!;
                if (lf.Map.ContainsKey(k)) { if (report) Diag(true, "LANG111", path, line, 1, $"Dubbele sleutel \"{Short(k)}\" (de tweede wint bij het laden, maak er één van)."); }
                lf.Map[k] = v; lf.Entries.Add((k, v, line));
            }
        }
        catch (JsonException ex)
        {
            if (report) Diag(true, "LANG112", path, (int)(ex.LineNumber ?? 1) + 1, 1, "Ongeldige JSON: " + ex.Message);
            return null;
        }
        return lf;
    }

    private static List<LangFile> ReadAll(string? only)
    {
        var res = new List<LangFile>();
        foreach (var p in Directory.GetFiles(LangDir, "*.json").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            if (only is not null && !Path.GetFileNameWithoutExtension(p).Equals(only, StringComparison.OrdinalIgnoreCase)) continue;
            var l = ReadLang(p); if (l is not null) res.Add(l);
        }
        return res;
    }

    private static readonly HashSet<string> Plurals = new() { "one-other", "zero-or-one-other", "one-few-many-other", "other" };
    private static int PluralForms(string rule) => rule switch { "other" => 1, "one-few-many-other" => 3, _ => 2 };

    private static int Check(bool strict, string? only, bool quiet)
    {
        _quiet = quiet;
        var uses = ScanCode();
        ScanHardcoded();
        var keys = uses.Select(u => u.Key).ToHashSet(StringComparer.Ordinal);

        // dezelfde tekst als gewone tekst én als meervoud kan niet: de vertaling zou botsen
        foreach (var g in uses.GroupBy(u => u.Key).Where(g => g.Select(u => u.Kind == 'P').Distinct().Count() > 1))
            foreach (var u in g.Where(u => u.Kind != 'P').Take(1)) Diag(true, "LANG008", u.File, u.Line, u.Col, $"\"{Short(u.Key)}\" wordt zowel met Loc.P als Loc.T gebruikt.");
        var plural = uses.Where(u => u.Kind == 'P').Select(u => u.Key).ToHashSet(StringComparer.Ordinal);

        var langs = ReadAll(only);
        var summary = new List<string>();
        foreach (var l in langs)
        {
            int missing = 0, unused = 0;
            l.Map.TryGetValue("_name", out var nm);
            if (string.IsNullOrWhiteSpace(nm)) Diag(true, "LANG120", l.Path, 1, 1, "\"_name\" (naam van de taal in die taal) ontbreekt.");
            string rule = l.Map.TryGetValue("_plural", out var pr) ? pr : "one-other";
            if (!Plurals.Contains(rule)) Diag(true, "LANG121", l.Path, 1, 1, $"\"_plural\" is onbekend: {rule} (kies uit {string.Join(", ", Plurals)}).");
            if (l.Map.TryGetValue("_culture", out var cu) && cu.Length > 0)
            {
                try { CultureInfo.GetCultureInfo(cu); } catch { Diag(true, "LANG122", l.Path, 1, 1, $"\"_culture\" is geen bekende cultuur: {cu}"); }
            }

            foreach (var (k, v, line) in l.Entries)
            {
                if (k.StartsWith('_')) continue;
                if (!keys.Contains(k)) { unused++; Diag(false, "LANG101", l.Path, line, 1, $"Ongebruikte tekst \"{Short(k)}\" (staat niet meer in de code; `LangTool sync --prune` ruimt op)."); continue; }
                if (v.Length == 0) continue;   // leeg = nog niet vertaald (Engels)
                var pk = Placeholders(k); var pv = Placeholders(v);
                bool isP = plural.Contains(k);
                if (isP)
                {
                    var forms = v.Split('|');
                    if (forms.Length != PluralForms(rule)) Diag(true, "LANG105", l.Path, line, 1, $"Meervoud \"{Short(k)}\": {PluralForms(rule)} vorm(en) verwacht voor _plural={rule}, gevonden {forms.Length} (gescheiden door '|').");
                    foreach (var fm in forms) if (!Placeholders(fm).SequenceEqual(pk.Where(x => true).ToList()) && !Placeholders(fm).All(pk.Contains)) { Diag(true, "LANG102", l.Path, line, 1, $"Plaatsaanduidingen in de vertaling van \"{Short(k)}\" kloppen niet met de Engelse tekst."); break; }
                }
                else if (!pk.SequenceEqual(pv)) Diag(true, "LANG102", l.Path, line, 1, $"Plaatsaanduidingen in de vertaling van \"{Short(k)}\" ({{{string.Join("},{", pv)}}}) kloppen niet met de Engelse tekst ({{{string.Join("},{", pk)}}}).");
                if (!BracesOk(v, pk.Count > 0 || v.Contains("{{"))) Diag(true, "LANG107", l.Path, line, 1, $"Accolades in de vertaling van \"{Short(k)}\" kloppen niet.");
                if (WsLead(k) != WsLead(v) || WsTrail(k) != WsTrail(v)) Diag(true, "LANG103", l.Path, line, 1, $"Spaties/regelovergangen aan begin of eind van \"{Short(k)}\" moeten in de vertaling hetzelfde zijn.");
                if (v.Contains("@@")) Diag(true, "LANG106", l.Path, line, 1, $"De vertaling van \"{Short(k)}\" bevat '@@' (alleen sleutels hebben een context).");
                if (k.Count(c => c == '\n') != v.Count(c => c == '\n') && !isP) Diag(false, "LANG108", l.Path, line, 1, $"Aantal regelovergangen wijkt af bij \"{Short(k)}\".");
            }
            foreach (var k in keys) if (!l.Map.TryGetValue(k, out var v) || v.Length == 0) missing++;
            if (missing > 0)
            {
                Diag(strict, "LANG100", l.Path, 1, 1, $"{missing} tekst(en) nog niet vertaald in {l.Code} (`LangTool sync` zet ze klaar; ontbrekend = Engels).");
                if (!quiet) foreach (var k in keys.Where(k => !l.Map.TryGetValue(k, out var v) || v.Length == 0).OrderBy(k => k, StringComparer.Ordinal).Take(8)) Console.WriteLine($"    - {Short(k)}");
            }
            if (!IsCanonical(l)) Diag(false, "LANG104", l.Path, 1, 1, "Bestand is niet in de vaste volgorde/opmaak (`LangTool sync` herstelt dat).");
            summary.Add($"{l.Code,-6} {keys.Count - missing,4}/{keys.Count} vertaald ({(keys.Count == 0 ? 100 : 100 * (keys.Count - missing) / keys.Count)}%), {unused} ongebruikt");
        }
        // een taalbestand dat als sleutel-bron ontbreekt kan niet: Engels is de code zelf
        Console.WriteLine($"LangTool: {keys.Count} teksten in de code, {langs.Count} taal/talen, {_errors} fout(en), {_warnings} waarschuwing(en).");
        foreach (var s in summary) Console.WriteLine("  " + s);
        return _errors > 0 ? 1 : 0;
    }

    private static string WsLead(string s) => s[..(s.Length - s.TrimStart().Length)];
    private static string WsTrail(string s) => s[s.TrimEnd().Length..];

    // ------------------------------------------------------------------------------------------------ schrijven

    private static readonly UTF8Encoding Utf8 = new(false);

    private static string Render(Dictionary<string, string> meta, IEnumerable<KeyValuePair<string, string>> items)
    {
        var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            foreach (var kv in meta) w.WriteString(kv.Key, kv.Value);
            foreach (var kv in items) w.WriteString(kv.Key, kv.Value);
            w.WriteEndObject();
        }
        return Utf8.GetString(ms.ToArray()).Replace("\r\n", "\n").Replace("\n", "\r\n") + "\r\n";
    }

    private static Dictionary<string, string> OrderedMeta(LangFile l)
    {
        var res = new Dictionary<string, string>();
        foreach (var k in new[] { "_name", "_culture", "_plural" }) if (l.Map.TryGetValue(k, out var v)) res[k] = v;
        foreach (var kv in l.Map.Where(kv => kv.Key.StartsWith('_') && !res.ContainsKey(kv.Key))) res[kv.Key] = kv.Value;
        return res;
    }

    private static string Canonical(LangFile l) => Render(OrderedMeta(l), l.Map.Where(kv => !kv.Key.StartsWith('_')).OrderBy(kv => kv.Key, StringComparer.Ordinal));
    private static bool IsCanonical(LangFile l) => File.ReadAllText(l.Path, Utf8) == Canonical(l);

    private static int Sync(bool prune)
    {
        var uses = ScanCode();
        var keys = uses.Select(u => u.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var l in ReadAll(null))
        {
            int added = 0, removed = 0;
            foreach (var k in keys) if (!l.Map.ContainsKey(k)) { l.Map[k] = ""; added++; }
            if (prune) foreach (var k in l.Map.Keys.Where(k => !k.StartsWith('_') && !keys.Contains(k)).ToList()) { l.Map.Remove(k); removed++; }
            File.WriteAllText(l.Path, Canonical(l), Utf8);
            Console.WriteLine($"{l.Code}: {added} toegevoegd (leeg = nog vertalen), {removed} verwijderd.");
        }
        return 0;
    }

    private static int Rename(string oldKey, string newKey)
    {
        int nl = 0;
        foreach (var l in ReadAll(null))
        {
            if (!l.Map.TryGetValue(oldKey, out var v)) continue;
            l.Map.Remove(oldKey);
            l.Map[newKey] = v;
            File.WriteAllText(l.Path, Canonical(l), Utf8);
            nl++;
        }
        // de letterlijke tekst in de code
        string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        string oldLit = "\"" + Esc(oldKey), newLit = "\"" + Esc(newKey);
        int nc = 0;
        foreach (var f in Directory.GetFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            var t = File.ReadAllText(f, Utf8);
            // alleen als de hele tekst tussen de aanhalingstekens staat (niet als begin van een langere tekst)
            var rx = new Regex(Regex.Escape(oldLit) + "\"");
            if (!rx.IsMatch(t)) continue;
            File.WriteAllText(f, rx.Replace(t, m => newLit + "\"").ToString(), Utf8);
            nc++;
        }
        Console.WriteLine($"Hernoemd in {nl} taalbestand(en) en {nc} codebestand(en). Draai daarna `LangTool check`.");
        return nl + nc == 0 ? 1 : 0;
    }

    private static int NewLang(string code, string name, string? culture)
    {
        code = code.ToLowerInvariant();
        string path = Path.Combine(LangDir, code + ".json");
        if (File.Exists(path)) { Console.Error.WriteLine($"{path} bestaat al."); return 1; }
        var l = new LangFile { Code = code, Path = path };
        l.Map["_name"] = name;
        if (culture is not null) l.Map["_culture"] = culture;
        foreach (var k in ScanCode().Select(u => u.Key).Distinct()) l.Map[k] = "";
        File.WriteAllText(path, Canonical(l), Utf8);
        Console.WriteLine($"{path} gemaakt: vul de lege teksten in (leeg = Engels). Draai `LangTool check`.");
        return 0;
    }

    private static int Status(bool md)
    {
        var keys = ScanCode().Select(u => u.Key).ToHashSet(StringComparer.Ordinal);
        if (md) { Console.WriteLine("| Language | Code | Translated |"); Console.WriteLine("|---|---|---|"); Console.WriteLine($"| English | en | source ({keys.Count} texts) |"); }
        foreach (var l in ReadAll(null))
        {
            int done = keys.Count(k => l.Map.TryGetValue(k, out var v) && v.Length > 0);
            int pct = keys.Count == 0 ? 100 : 100 * done / keys.Count;
            if (md) Console.WriteLine($"| {l.Map.GetValueOrDefault("_name", l.Code)} | {l.Code} | {pct}% ({done}/{keys.Count}) |");
            else Console.WriteLine($"{l.Code,-6} {l.Map.GetValueOrDefault("_name", ""),-14} {pct,3}%  ({done}/{keys.Count})");
        }
        return 0;
    }
}
