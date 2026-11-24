using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TaskbarStats.Plugins;

internal sealed class ExprException : Exception
{
    public ExprException(string message) : base(message) { }
}

/// <summary>
/// De kleine formuletaal van de recepten (plugins zonder code). Een formule kan alleen rekenen met de gegevens die het recept ophaalt:
/// getallen, tekst, waar/onwaar, lijsten en objecten. Er is geen toegang tot bestanden, netwerk of opdrachten, er zijn geen lussen of
/// zelfgedefinieerde functies, en lengte, diepte en rekenstappen zijn begrensd. Een formule kan dus niets doen behalve een waarde uitrekenen.
///
/// Syntaxis: getallen (<c>12.5</c>), tekst (<c>'ok'</c> of <c>"ok"</c>), <c>true false null</c>, <c>+ - * / %</c>, <c>== != &lt; &lt;= &gt; &gt;=</c>,
/// <c>&amp;&amp; || !</c>, <c>a ? b : c</c>, haakjes, <c>naam</c>, <c>a.b</c>, <c>a[0]</c>, <c>a["sleutel met spaties"]</c> en functies <c>f(x, y)</c>.
/// Ontbrekende gegevens geven <c>null</c> (geen fout): <c>a.b.c</c> is <c>null</c> als <c>b</c> ontbreekt.
/// </summary>
internal sealed class Expr
{
    public const int MaxLength = 2000, MaxSteps = 20_000, MaxDepth = 40;

    private readonly Func<ExprEnv, object?> _eval;
    public string Source { get; }

    private Expr(string source, Func<ExprEnv, object?> eval) { Source = source; _eval = eval; }

    public static Expr Parse(string source)
    {
        if (source.Length > MaxLength) throw new ExprException($"the formula is too long (max {MaxLength} characters)");
        var p = new Parser(source);
        var f = p.ParseAll();
        return new Expr(source, f);
    }

    public object? Eval(ExprEnv env)
    {
        env.Steps = 0;
        return _eval(env);
    }

    // ---------- waarden ----------
    public static bool Truthy(object? v) => v switch
    {
        null => false,
        bool b => b,
        double d => d != 0,
        string s => s.Length > 0,
        System.Collections.ICollection c => c.Count > 0,
        _ => true,
    };

    /// <summary>Getal uit een waarde: getal blijft, tekst wordt (tolerant) gelezen ("12,5%", " 7 "), waar/onwaar = 1/0; anders null.</summary>
    public static double? ToNum(object? v)
    {
        switch (v)
        {
            case double d: return double.IsFinite(d) ? d : null;
            case bool b: return b ? 1 : 0;
            case string s:
                s = s.Trim().TrimEnd('%').Trim().Replace(',', '.');
                return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) ? n : null;
            default: return null;
        }
    }

    /// <summary>Tekst uit een waarde (zonder opmaak): getallen compact (12,5 / 130), null = leeg.</summary>
    public static string ToText(object? v) => v switch
    {
        null => "",
        string s => s,
        bool b => b ? "true" : "false",
        double d => ScriptPlugin.Compact(d),
        System.Collections.IList l => $"[{l.Count} items]",
        System.Collections.IDictionary => "{...}",
        _ => v.ToString() ?? "",
    };

    // ---------- JSON -> gewone waarden ----------
    public static object? FromJson(JsonElement e, int depth = 0, int[]? budget = null)
    {
        budget ??= new[] { 200_000 };
        if (--budget[0] < 0) throw new ExprException("the data is too large");
        if (depth > 60) throw new ExprException("the data is nested too deeply");
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                var d = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var p in e.EnumerateObject()) d[p.Name] = FromJson(p.Value, depth + 1, budget);
                return d;
            case JsonValueKind.Array:
                var l = new List<object?>();
                foreach (var x in e.EnumerateArray()) l.Add(FromJson(x, depth + 1, budget));
                return l;
            case JsonValueKind.String: return e.GetString();
            case JsonValueKind.Number: return e.TryGetDouble(out var n) && double.IsFinite(n) ? n : null;
            case JsonValueKind.True: return true;
            case JsonValueKind.False: return false;
            default: return null;
        }
    }

    // ---------- parser ----------
    private sealed class Parser
    {
        private readonly string _s;
        private int _i;
        private int _depth;
        private int _nodes;

        public Parser(string s) { _s = s; }

        public Func<ExprEnv, object?> ParseAll()
        {
            var f = Ternary();
            Ws();
            if (_i < _s.Length) throw Err($"unexpected '{_s[_i]}'");
            return f;
        }

        private ExprException Err(string m) => new($"{m} (position {_i + 1})");

        private void Ws() { while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++; }

        private bool Eat(string tok)
        {
            Ws();
            if (string.CompareOrdinal(_s, _i, tok, 0, tok.Length) != 0) return false;
            _i += tok.Length;
            return true;
        }

        private bool Peek(string tok) { Ws(); return string.CompareOrdinal(_s, _i, tok, 0, tok.Length) == 0; }

        private Func<ExprEnv, object?> Node(Func<ExprEnv, object?> f)
        {
            if (++_nodes > 600) throw Err("the formula is too complex");
            return e =>
            {
                if (++e.Steps > MaxSteps) throw new ExprException("the formula is too complex (too many steps)");
                return f(e);
            };
        }

        private Func<ExprEnv, object?> Ternary()
        {
            if (++_depth > MaxDepth) throw Err("the formula is nested too deeply");
            var c = Or();
            if (Eat("?"))
            {
                var a = Ternary();
                if (!Eat(":")) throw Err("expected ':' in a ? b : c");
                var b = Ternary();
                _depth--;
                return Node(e => Truthy(c(e)) ? a(e) : b(e));
            }
            _depth--;
            return c;
        }

        private Func<ExprEnv, object?> Or()
        {
            var l = And();
            while (Eat("||"))
            {
                var a = l; var r = And();
                l = Node(e => Truthy(a(e)) || Truthy(r(e)));
            }
            return l;
        }

        private Func<ExprEnv, object?> And()
        {
            var l = Equality();
            while (Eat("&&"))
            {
                var a = l; var r = Equality();
                l = Node(e => Truthy(a(e)) && Truthy(r(e)));
            }
            return l;
        }

        private Func<ExprEnv, object?> Equality()
        {
            var l = Compare();
            while (true)
            {
                bool eq = Peek("==");
                if (eq || Peek("!=")) { _i += 2; var a = l; var r = Compare(); bool want = eq; l = Node(e => Equal(a(e), r(e)) == want); }
                else return l;
            }
        }

        private Func<ExprEnv, object?> Compare()
        {
            var l = Additive();
            while (true)
            {
                Ws();
                string? op = Peek("<=") ? "<=" : Peek(">=") ? ">=" : Peek("<") ? "<" : Peek(">") ? ">" : null;
                if (op is null) return l;
                _i += op.Length;
                var a = l; var r = Additive(); string o = op;
                l = Node(e => Cmp(a(e), r(e), o));
            }
        }

        private Func<ExprEnv, object?> Additive()
        {
            var l = Multiplicative();
            while (true)
            {
                Ws();
                if (_i < _s.Length && (_s[_i] == '+' || _s[_i] == '-'))
                {
                    char op = _s[_i++];
                    var a = l; var r = Multiplicative();
                    l = Node(e => Add(a(e), r(e), op));
                }
                else return l;
            }
        }

        private Func<ExprEnv, object?> Multiplicative()
        {
            var l = Unary();
            while (true)
            {
                Ws();
                if (_i < _s.Length && (_s[_i] == '*' || _s[_i] == '/' || _s[_i] == '%'))
                {
                    char op = _s[_i++];
                    var a = l; var r = Unary();
                    l = Node(e => Arith(a(e), r(e), op));
                }
                else return l;
            }
        }

        private Func<ExprEnv, object?> Unary()
        {
            Ws();
            if (_i < _s.Length && _s[_i] == '!' && !(_i + 1 < _s.Length && _s[_i + 1] == '='))
            {
                _i++;
                if (++_depth > MaxDepth) throw Err("the formula is nested too deeply");
                var x = Unary(); _depth--;
                return Node(e => !Truthy(x(e)));
            }
            if (_i < _s.Length && _s[_i] == '-')
            {
                _i++;
                if (++_depth > MaxDepth) throw Err("the formula is nested too deeply");
                var x = Unary(); _depth--;
                return Node(e => ToNum(x(e)) is double d ? -d : null);
            }
            return Postfix();
        }

        private Func<ExprEnv, object?> Postfix()
        {
            var cur = Primary();
            while (true)
            {
                Ws();
                if (_i < _s.Length && _s[_i] == '.' && _i + 1 < _s.Length && (char.IsLetter(_s[_i + 1]) || _s[_i + 1] == '_'))
                {
                    _i++;
                    string name = Ident();
                    var a = cur;
                    cur = Node(e => Member(a(e), name));
                }
                else if (_i < _s.Length && _s[_i] == '[')
                {
                    _i++;
                    var idx = Ternary();
                    if (!Eat("]")) throw Err("expected ']'");
                    var a = cur;
                    cur = Node(e => Index(a(e), idx(e)));
                }
                else return cur;
            }
        }

        private string Ident()
        {
            int s = _i;
            while (_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || _s[_i] == '_')) _i++;
            return _s[s.._i];
        }

        private Func<ExprEnv, object?> Primary()
        {
            Ws();
            if (_i >= _s.Length) throw Err("the formula ends too early");
            char c = _s[_i];
            if (c == '(')
            {
                _i++;
                var x = Ternary();
                if (!Eat(")")) throw Err("expected ')'");
                return x;
            }
            if (char.IsDigit(c) || (c == '.' && _i + 1 < _s.Length && char.IsDigit(_s[_i + 1])))
            {
                int s = _i;
                while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.')) _i++;
                if (_i < _s.Length && (_s[_i] == 'e' || _s[_i] == 'E') && _i + 1 < _s.Length && (char.IsDigit(_s[_i + 1]) || _s[_i + 1] is '+' or '-'))
                {
                    _i += 2;
                    while (_i < _s.Length && char.IsDigit(_s[_i])) _i++;
                }
                if (!double.TryParse(_s[s.._i], NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) throw Err("invalid number");
                object boxed = n;
                return Node(_ => boxed);
            }
            if (c == '\'' || c == '"')
            {
                var sb = new StringBuilder();
                _i++;
                while (true)
                {
                    if (_i >= _s.Length) throw Err("text is not closed");
                    char ch = _s[_i++];
                    if (ch == c) break;
                    if (ch == '\\' && _i < _s.Length)
                    {
                        // \\ \' \" \n \t zijn escapes; een andere backslash blijft staan (handig voor patronen zoals '\d+')
                        char n = _s[_i];
                        if (n is '\\' or '\'' or '"') { sb.Append(n); _i++; }
                        else if (n == 'n') { sb.Append('\n'); _i++; }
                        else if (n == 't') { sb.Append('\t'); _i++; }
                        else sb.Append('\\');
                    }
                    else sb.Append(ch);
                }
                string lit = sb.ToString();
                return Node(_ => lit);
            }
            if (char.IsLetter(c) || c == '_')
            {
                string name = Ident();
                if (name == "true") return Node(_ => true);
                if (name == "false") return Node(_ => false);
                if (name == "null") return Node(_ => null);
                Ws();
                if (_i < _s.Length && _s[_i] == '(')
                {
                    _i++;
                    var args = new List<Func<ExprEnv, object?>>();
                    if (!Eat(")"))
                    {
                        do args.Add(Ternary()); while (Eat(","));
                        if (!Eat(")")) throw Err("expected ')' after the arguments");
                    }
                    if (!Funcs.Table.TryGetValue(name, out var fn)) throw Err($"unknown function '{name}'");
                    if (args.Count < fn.Min || args.Count > fn.Max) throw Err($"'{name}' needs {(fn.Min == fn.Max ? fn.Min.ToString() : fn.Min + " to " + fn.Max)} argument(s)");
                    var argv = args.ToArray();
                    return Node(e =>
                    {
                        var vals = new object?[argv.Length];
                        for (int i = 0; i < argv.Length; i++) vals[i] = argv[i](e);
                        return fn.Run(e, vals);
                    });
                }
                return Node(e => e.Lookup(name));
            }
            throw Err($"unexpected '{c}'");
        }
    }

    // ---------- bewerkingen ----------
    internal static object? Member(object? o, string name)
        => o is IDictionary<string, object?> d && d.TryGetValue(name, out var v) ? v : null;

    internal static object? Index(object? o, object? i)
    {
        if (o is IList<object?> l && ToNum(i) is double n && n >= 0 && n < l.Count && n == Math.Floor(n)) return l[(int)n];
        if (o is IDictionary<string, object?> d && i is string s && d.TryGetValue(s, out var v)) return v;
        return null;
    }

    internal static bool Equal(object? a, object? b)
    {
        if (a is null || b is null) return a is null && b is null;
        if (a is string sa && b is string sb) return string.Equals(sa, sb, StringComparison.OrdinalIgnoreCase);
        if (a is bool ba && b is bool bb) return ba == bb;
        if ((a is double || b is double) && ToNum(a) is double x && ToNum(b) is double y) return x == y;
        return ToText(a) == ToText(b);
    }

    private static object? Cmp(object? a, object? b, string op)
    {
        if (ToNum(a) is double x && ToNum(b) is double y)
            return op switch { "<" => x < y, "<=" => x <= y, ">" => x > y, _ => x >= y };
        if (a is string sa && b is string sb)
        {
            int c = string.Compare(sa, sb, StringComparison.OrdinalIgnoreCase);
            return op switch { "<" => c < 0, "<=" => c <= 0, ">" => c > 0, _ => c >= 0 };
        }
        return false;   // met null of onvergelijkbare soorten is niets "groter" of "kleiner"
    }

    private static object? Add(object? a, object? b, char op)
    {
        if (op == '+' && (a is string || b is string)) return ToText(a) + ToText(b);
        return Arith(a, b, op);
    }

    private static object? Arith(object? a, object? b, char op)
    {
        if (ToNum(a) is not double x || ToNum(b) is not double y) return null;
        double r = op switch
        {
            '+' => x + y, '-' => x - y, '*' => x * y,
            '/' => y == 0 ? double.NaN : x / y,
            _ => y == 0 ? double.NaN : x % y,
        };
        return double.IsFinite(r) ? r : null;
    }
}

/// <summary>De omgeving van een formule: variabelen (de opgehaalde gegevens), de klok en een teller voor de rekenstappen.</summary>
internal sealed class ExprEnv
{
    /// <summary>De gegevens van het recept: <c>root</c> (de JSON), <c>text</c>, <c>lines</c>, <c>rows</c>, <c>http_status</c>, <c>elapsed_ms</c>.</summary>
    public IReadOnlyDictionary<string, object?> Vars { get; init; } = new Dictionary<string, object?>();
    /// <summary>Waarden die voorrang hebben (<c>value</c> en <c>text</c> van het onderdeel zelf).</summary>
    public IReadOnlyDictionary<string, object?>? Locals { get; init; }
    public Func<DateTimeOffset> Now { get; init; } = () => DateTimeOffset.Now;
    public int Steps { get; set; }

    /// <summary>Volgorde: de eigen waarden van het onderdeel, <c>now</c>, de sleutels van het JSON-hoofdobject (dus <c>price</c> in plaats van <c>root.price</c>), dan de rest.</summary>
    public object? Lookup(string name)
    {
        if (Locals is not null && Locals.TryGetValue(name, out var l)) return l;
        if (name == "now") return (double)Now().ToUnixTimeSeconds();
        if (Vars.TryGetValue("root", out var r) && r is IDictionary<string, object?> rd && rd.TryGetValue(name, out var rv)) return rv;
        return Vars.TryGetValue(name, out var v) ? v : null;
    }
}

/// <summary>Opmaak van teksten met formules erin: <c>"{value:0.0}% gebruikt, nog {until(reset)}"</c>. <c>{{</c> en <c>}}</c> zijn losse accolades.</summary>
internal sealed class Template
{
    private readonly List<(string Literal, Expr? Expr, string? Spec)> _parts = new();
    public string Source { get; }

    private static readonly Regex SpecRe = new(@"^\+?[#0,]*(\.[#0]+)?%?$", RegexOptions.CultureInvariant);

    public static Template Parse(string src)
    {
        var t = new Template(src);
        var lit = new StringBuilder();
        for (int i = 0; i < src.Length; i++)
        {
            char c = src[i];
            if (c == '{' && i + 1 < src.Length && src[i + 1] == '{') { lit.Append('{'); i++; continue; }
            if (c == '}' && i + 1 < src.Length && src[i + 1] == '}') { lit.Append('}'); i++; continue; }
            if (c != '{') { lit.Append(c); continue; }
            // zoek de sluitende accolade (tekst tussen aanhalingstekens telt niet mee)
            int j = i + 1; char q = '\0';
            for (; j < src.Length; j++)
            {
                if (q != '\0') { if (src[j] == '\\') j++; else if (src[j] == q) q = '\0'; continue; }
                if (src[j] is '\'' or '"') q = src[j];
                else if (src[j] == '}') break;
            }
            if (j >= src.Length) throw new ExprException("a '{' is not closed (use {{ for a literal brace)");
            string inner = src[(i + 1)..j];
            if (lit.Length > 0) { t._parts.Add((lit.ToString(), null, null)); lit.Clear(); }
            t._parts.Add(("", ParseInner(inner, out var spec), spec));
            i = j;
        }
        if (lit.Length > 0) t._parts.Add((lit.ToString(), null, null));
        return t;
    }

    private Template(string src) { Source = src; }

    /// <summary>Eerst proberen als één formule (dan is <c>a ? 1 : 0</c> een ternary); lukt dat niet en staat er achter de laatste ':' een getalopmaak, dan is dat de opmaak.</summary>
    private static Expr ParseInner(string inner, out string? spec)
    {
        spec = null;
        try { return Expr.Parse(inner); }
        catch (ExprException)
        {
            int c = inner.LastIndexOf(':');
            if (c > 0)
            {
                string s = inner[(c + 1)..].Trim();
                if (s.Length > 0 && SpecRe.IsMatch(s) && s.IndexOfAny(new[] { '0', '#' }) >= 0)
                {
                    var e = Expr.Parse(inner[..c]);
                    spec = s;
                    return e;
                }
            }
            throw;   // de oorspronkelijke fout, met zijn stack (niet 'throw first': dat wist de stack)
        }
    }

    public string Render(ExprEnv env)
    {
        var sb = new StringBuilder();
        foreach (var (lit, ex, spec) in _parts)
        {
            if (ex is null) { sb.Append(lit); continue; }
            var v = ex.Eval(env);
            if (spec is not null && Expr.ToNum(v) is double d) sb.Append(FormatNumber(d, spec));
            else sb.Append(Expr.ToText(v));
        }
        return sb.ToString();
    }

    /// <summary><c>0</c> <c>0.0</c> <c>+0.0</c> <c>#,##0</c> <c>0%</c> (een % vermenigvuldigt met 100, afronden naar boven bij .5).</summary>
    public static string FormatNumber(double d, string spec)
    {
        bool plus = spec.StartsWith('+');
        string sp = spec.TrimStart('+');
        bool pct = sp.EndsWith('%');
        if (pct) { sp = sp[..^1]; d *= 100; }
        int dot = sp.IndexOf('.');
        int decimals = dot < 0 ? 0 : sp.Length - dot - 1;
        double r = Math.Round(d, Math.Min(decimals, 10), MidpointRounding.AwayFromZero);
        string s = r.ToString(sp.Length == 0 ? "0" : sp, CultureInfo.InvariantCulture);
        if (s.StartsWith('-') && s.Trim('-', '0', '.', ',') == "") s = s[1..];   // "-0" -> "0"
        if (plus && !s.StartsWith('-') && r >= 0) s = "+" + s;
        return pct ? s + "%" : s;
    }
}

/// <summary>De functies van de formuletaal.</summary>
internal static class Funcs
{
    internal sealed record Fn(int Min, int Max, Func<ExprEnv, object?[], object?> Run);

    private static readonly Dictionary<string, Regex> RegexCache = new();

    private static Regex Rx(string pattern)
    {
        lock (RegexCache)
        {
            if (RegexCache.TryGetValue(pattern, out var r)) return r;
            if (pattern.Length > 300) throw new ExprException("the pattern is too long");
            if (RegexCache.Count > 100) RegexCache.Clear();
            try { r = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250)); }
            catch (ArgumentException) { throw new ExprException("invalid pattern: " + pattern); }
            RegexCache[pattern] = r;
            return r;
        }
    }

    private static double? N(object?[] a, int i) => i < a.Length ? Expr.ToNum(a[i]) : null;
    private static string S(object?[] a, int i) => i < a.Length ? Expr.ToText(a[i]) : "";

    /// <summary>Een lijst van getallen: één lijst als enig argument, of losse argumenten (tekst met een getal telt mee, de rest niet).</summary>
    private static List<double> Nums(object?[] a)
    {
        var src = a.Length == 1 && a[0] is IList<object?> l ? l : a;
        var res = new List<double>();
        foreach (var x in src) if (Expr.ToNum(x) is double d) res.Add(d);
        return res;
    }

    /// <summary>Unix-tijd (seconden) uit een getal (seconden of milliseconden) of een tekst (ISO 8601); anders null.</summary>
    internal static double? Time(object? v)
    {
        if (v is string s)
        {
            if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt)) return dt.ToUnixTimeSeconds();
            return Expr.ToNum(s) is double n ? Time(n) : null;
        }
        if (Expr.ToNum(v) is double d) return d > 1e11 ? Math.Floor(d / 1000) : d;
        return null;
    }

    internal static string Bytes(double n)
    {
        string[] u = { "B", "KB", "MB", "GB", "TB", "PB" };
        int i = 0;
        double v = Math.Abs(n);
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        string num = i == 0 ? Math.Round(v).ToString("0", CultureInfo.InvariantCulture) : v < 10 ? v.ToString("0.0", CultureInfo.InvariantCulture) : v.ToString("0", CultureInfo.InvariantCulture);
        return (n < 0 ? "-" : "") + num + " " + u[i];
    }

    public static readonly Dictionary<string, Fn> Table = new(StringComparer.Ordinal)
    {
        // getallen
        ["abs"] = new(1, 1, (_, a) => N(a, 0) is double d ? Math.Abs(d) : null),
        ["round"] = new(1, 2, (_, a) => N(a, 0) is double d ? Math.Round(d, (int)Math.Clamp(N(a, 1) ?? 0, 0, 10), MidpointRounding.AwayFromZero) : null),
        ["floor"] = new(1, 1, (_, a) => N(a, 0) is double d ? Math.Floor(d) : null),
        ["ceil"] = new(1, 1, (_, a) => N(a, 0) is double d ? Math.Ceiling(d) : null),
        ["sqrt"] = new(1, 1, (_, a) => N(a, 0) is double d && d >= 0 ? Math.Sqrt(d) : null),
        ["pow"] = new(2, 2, (_, a) => N(a, 0) is double x && N(a, 1) is double y && double.IsFinite(Math.Pow(x, y)) ? Math.Pow(x, y) : null),
        ["clamp"] = new(3, 3, (_, a) => N(a, 0) is double x && N(a, 1) is double lo && N(a, 2) is double hi ? Math.Clamp(x, Math.Min(lo, hi), Math.Max(lo, hi)) : null),
        ["pct"] = new(2, 2, (_, a) => N(a, 0) is double x && N(a, 1) is double y && y != 0 ? x / y * 100 : null),
        ["num"] = new(1, 1, (_, a) => N(a, 0)),
        ["str"] = new(1, 1, (_, a) => S(a, 0)),
        ["min"] = new(1, 20, (_, a) => { var n = Nums(a); return n.Count == 0 ? null : n.Min(); }),
        ["max"] = new(1, 20, (_, a) => { var n = Nums(a); return n.Count == 0 ? null : n.Max(); }),
        ["sum"] = new(1, 1, (_, a) => Nums(a).Sum()),
        ["avg"] = new(1, 1, (_, a) => { var n = Nums(a); return n.Count == 0 ? null : n.Average(); }),
        // logica en tekst
        ["if"] = new(3, 3, (_, a) => Expr.Truthy(a[0]) ? a[1] : a[2]),
        ["coalesce"] = new(1, 20, (_, a) => a.FirstOrDefault(x => x is not null && !(x is string s && s.Length == 0))),
        ["isnull"] = new(1, 1, (_, a) => a[0] is null),
        ["len"] = new(1, 1, (_, a) => a[0] switch { string s => (double)s.Length, System.Collections.ICollection c => (double)c.Count, null => 0.0, _ => (double)S(a, 0).Length }),
        ["upper"] = new(1, 1, (_, a) => S(a, 0).ToUpperInvariant()),
        ["lower"] = new(1, 1, (_, a) => S(a, 0).ToLowerInvariant()),
        ["trim"] = new(1, 1, (_, a) => S(a, 0).Trim()),
        ["contains"] = new(2, 2, (_, a) => a[0] is IList<object?> l ? l.Any(x => Expr.Equal(x, a[1])) : S(a, 0).Contains(S(a, 1), StringComparison.OrdinalIgnoreCase)),
        ["startswith"] = new(2, 2, (_, a) => S(a, 0).StartsWith(S(a, 1), StringComparison.OrdinalIgnoreCase)),
        ["endswith"] = new(2, 2, (_, a) => S(a, 0).EndsWith(S(a, 1), StringComparison.OrdinalIgnoreCase)),
        ["replace"] = new(3, 3, (_, a) => S(a, 1).Length == 0 ? S(a, 0) : S(a, 0).Replace(S(a, 1), S(a, 2), StringComparison.Ordinal)),
        ["substr"] = new(2, 3, (_, a) =>
        {
            string s = S(a, 0);
            int st = (int)Math.Clamp(N(a, 1) ?? 0, 0, s.Length);
            int len = (int)Math.Clamp(N(a, 2) ?? (s.Length - st), 0, s.Length - st);
            return s.Substring(st, len);
        }),
        ["split"] = new(2, 2, (_, a) => S(a, 1).Length == 0 ? new List<object?> { S(a, 0) } : S(a, 0).Split(S(a, 1)).Cast<object?>().ToList()),
        ["join"] = new(2, 2, (_, a) => a[0] is IList<object?> l ? string.Join(S(a, 1), l.Select(Expr.ToText)) : S(a, 0)),
        ["matches"] = new(2, 2, (_, a) => Rx(S(a, 1)).IsMatch(S(a, 0))),
        ["regex"] = new(2, 3, (_, a) =>
        {
            var m = Rx(S(a, 1)).Match(S(a, 0));
            if (!m.Success) return null;
            int g = (int)(N(a, 2) ?? (m.Groups.Count > 1 ? 1 : 0));
            return g >= 0 && g < m.Groups.Count && m.Groups[g].Success ? m.Groups[g].Value : null;
        }),
        // lijsten
        ["count"] = new(1, 1, (_, a) => a[0] is System.Collections.ICollection c ? (double)c.Count : 0.0),
        ["first"] = new(1, 1, (_, a) => a[0] is IList<object?> { Count: > 0 } l ? l[0] : null),
        ["last"] = new(1, 1, (_, a) => a[0] is IList<object?> { Count: > 0 } l ? l[^1] : null),
        ["pluck"] = new(2, 2, (_, a) => a[0] is IList<object?> l ? l.Select(x => Expr.Member(x, S(a, 1))).ToList() : new List<object?>()),
        ["where"] = new(3, 3, (_, a) => a[0] is IList<object?> l ? l.Where(x => Expr.Equal(Expr.Member(x, S(a, 1)), a[2])).ToList() : new List<object?>()),
        ["json"] = new(1, 1, (_, a) =>
        {
            string s = S(a, 0);
            if (s.Length > 500_000) throw new ExprException("json(): the text is too large");
            try { using var d = JsonDocument.Parse(s); return Expr.FromJson(d.RootElement); }
            catch (JsonException) { return null; }
        }),
        // tijd en grootte
        ["time"] = new(1, 1, (_, a) => Time(a[0])),
        ["duration"] = new(1, 1, (_, a) => N(a, 0) is double d ? Fmt.Duration(TimeSpan.FromSeconds(Math.Min(Math.Abs(d), 3e9) * Math.Sign(d))) : null),
        ["ago"] = new(1, 1, (e, a) => Time(a[0]) is double t ? Fmt.Duration(TimeSpan.FromSeconds(e.Now().ToUnixTimeSeconds() - t)) : null),
        ["until"] = new(1, 1, (e, a) => Time(a[0]) is double t ? Fmt.Duration(TimeSpan.FromSeconds(t - e.Now().ToUnixTimeSeconds())) : null),
        ["clock"] = new(2, 2, (_, a) =>
        {
            if (Time(a[0]) is not double t) return null;
            try { return DateTimeOffset.FromUnixTimeSeconds((long)t).ToLocalTime().ToString(S(a, 1), CultureInfo.InvariantCulture); }
            catch (FormatException) { throw new ExprException("clock(): invalid time format"); }
            catch (ArgumentOutOfRangeException) { return null; }
        }),
        ["bytes"] = new(1, 1, (_, a) => N(a, 0) is double d ? Bytes(d) : null),
    };
}
