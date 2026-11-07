using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace TaskbarStats.Plugins;

/// <summary>
/// Uptime-monitor: controleert websites (HTTP), poorten (TCP) en apparaten (ping) en toont "3/3 up" met de reactietijd.
/// Eén sectie per doel in de config: <c>[Naam]</c> met <c>url =</c>, <c>tcp = host:poort</c> of <c>ping = host</c>.
/// Als iets uitvalt, springt het onderdeel op rood en kan de app een melding tonen.
/// </summary>
internal sealed class UptimePlugin : IPlugin
{
    public string Id => "uptime";
    public string Title => "Uptime monitor";
    public int DefaultIntervalSec => 60;

    internal sealed record Check(string Name, string Kind, string Target, HashSet<int> Expect);
    internal sealed record Outcome(string Name, bool Up, double? Ms, string Error);

    public async Task<PluginResult> RunAsync(PluginContext ctx, CancellationToken ct)
    {
        var cfg = ctx.Config;
        var checks = ReadChecks(cfg);
        if (checks.Count == 0) return PluginResult.Fail("No targets configured. Add a [Name] section with url =, tcp = or ping =.");
        int timeoutMs = cfg.Int("check_timeout", 8, 1, 60) * 1000;
        var outcomes = await Task.WhenAll(checks.Select(c => RunCheckAsync(c, ctx.Http, timeoutMs, ct))).ConfigureAwait(false);
        return Build(outcomes, cfg);
    }

    internal static List<Check> ReadChecks(IniFile cfg)
    {
        var list = new List<Check>();
        foreach (var s in cfg.Sections)
        {
            if (string.IsNullOrWhiteSpace(s.Name)) continue;
            string? url = s.Get("url"), tcp = s.Get("tcp"), ping = s.Get("ping");
            var expect = new HashSet<int>();
            foreach (var part in (s.Get("expect") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (int.TryParse(part, out var code)) expect.Add(code);
            if (!string.IsNullOrWhiteSpace(url)) list.Add(new Check(s.Name, "url", url.Trim(), expect));
            else if (!string.IsNullOrWhiteSpace(tcp)) list.Add(new Check(s.Name, "tcp", tcp.Trim(), expect));
            else if (!string.IsNullOrWhiteSpace(ping)) list.Add(new Check(s.Name, "ping", ping.Trim(), expect));
        }
        return list;
    }

    internal static async Task<Outcome> RunCheckAsync(Check c, HttpClient http, int timeoutMs, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);
        try
        {
            switch (c.Kind)
            {
                case "url":
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, c.Target);
                    using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
                    int code = (int)resp.StatusCode;
                    bool ok = c.Expect.Count > 0 ? c.Expect.Contains(code) : code < 400;
                    return new Outcome(c.Name, ok, sw.Elapsed.TotalMilliseconds, ok ? "" : "HTTP " + code);
                }
                case "tcp":
                {
                    int colon = c.Target.LastIndexOf(':');
                    if (colon <= 0 || !int.TryParse(c.Target[(colon + 1)..], out var port)) return new Outcome(c.Name, false, null, "use host:port");
                    using var tcp = new TcpClient();
                    await tcp.ConnectAsync(c.Target[..colon], port, cts.Token).ConfigureAwait(false);
                    return new Outcome(c.Name, true, sw.Elapsed.TotalMilliseconds, "");
                }
                default:
                {
                    using var ping = new Ping();
                    var r = await ping.SendPingAsync(c.Target, timeoutMs).ConfigureAwait(false);
                    return r.Status == IPStatus.Success
                        ? new Outcome(c.Name, true, r.RoundtripTime, "")
                        : new Outcome(c.Name, false, null, r.Status.ToString());
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new Outcome(c.Name, false, null, "timeout"); }
        catch (Exception ex) when (ex is HttpRequestException or SocketException or PingException or InvalidOperationException or UriFormatException)
        {
            string msg = ex.InnerException?.Message ?? ex.Message;
            return new Outcome(c.Name, false, null, msg.Length > 80 ? msg[..80] : msg);
        }
    }

    internal static PluginResult Build(IReadOnlyList<Outcome> outcomes, IniFile cfg)
    {
        double slow = cfg.Int("slow_ms", 1500, 10, 60000);
        bool notify = cfg.Bool("notify", true);
        int up = outcomes.Count(o => o.Up);
        bool anySlow = outcomes.Any(o => o.Up && o.Ms > slow);
        var res = new PluginResult();

        var lines = new StringBuilder();
        foreach (var o in outcomes)
        {
            if (lines.Length > 0) lines.Append('\n');
            lines.Append(o.Up ? $"✓ {o.Name}  {o.Ms:0} ms" : $"✕ {o.Name}  {o.Error}");
        }
        res.Items.Add(new PluginItem
        {
            Key = "summary", Label = "Uptime", Short = "UP", Value = outcomes.Count == 0 ? 0 : 100.0 * up / outcomes.Count, Min = 0, Max = 100, Unit = "",   // geen "%": "3/3" leest beter als tekst
            Text = $"{up}/{outcomes.Count}", Detail = lines.ToString(),
            Level = up < outcomes.Count ? "crit" : anySlow ? "warn" : "ok", Widget = true, Notify = notify,
        });
        foreach (var o in outcomes)
        {
            res.Items.Add(new PluginItem
            {
                Key = Slug(o.Name), Label = o.Name, Short = o.Name, Value = o.Up ? o.Ms : null, Min = 0, Max = slow * 2, Unit = "ms",
                Text = o.Up ? $"{o.Ms:0} ms" : "DOWN", Detail = o.Up ? $"{o.Name}: up, {o.Ms:0} ms" : $"{o.Name}: DOWN ({o.Error})",
                Level = !o.Up ? "crit" : o.Ms > slow ? "warn" : "ok", Widget = false, Notify = false,
            });
        }
        return res;
    }

    internal static string Slug(string s)
    {
        var sb = new StringBuilder();
        foreach (char ch in s.ToLowerInvariant()) sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        return sb.ToString().Trim('_') is { Length: > 0 } r ? r : "target";
    }
}
