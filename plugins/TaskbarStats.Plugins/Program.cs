using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace TaskbarStats.Plugins;

internal static class Program
{
    public const string Version = "1.6.8";

    [DllImport("kernel32.dll")] private static extern bool AttachConsole(int pid);

    internal static IReadOnlyList<IPlugin> AllPlugins() => new IPlugin[]
    {
        new ClaudePlugin(), new WeatherPlugin(), new LocationPlugin(), new UptimePlugin(), new ScriptPlugin(),
    };

    internal static HttpClient CreateHttp()
    {
        var http = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }) { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("TaskbarStats-Plugins", Version));
        return http;
    }

    private static string? Arg(string[] a, string name)
    {
        for (int i = 0; i < a.Length - 1; i++) if (string.Equals(a[i], name, StringComparison.OrdinalIgnoreCase)) return a[i + 1];
        return null;
    }

    private static bool Flag(string[] a, string name) => a.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));

    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        string dataDir = Arg(args, "--data") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskbarStats");
        Directory.CreateDirectory(Path.Combine(dataDir, "plugins"));
        HostLog.Path = Path.Combine(dataDir, "plugins", "host.log");

        var plugins = AllPlugins();
        using var http = CreateHttp();
        var runner = new PluginRunner(dataDir, plugins, http, Arg(args, "--defaults"));

        // Eenmalig draaien (diagnose en tests): resultaat als JSON naar --out of naar de console.
        string? once = Arg(args, "--once");
        if (once is not null)
        {
            var p = plugins.FirstOrDefault(x => string.Equals(x.Id, once, StringComparison.OrdinalIgnoreCase));
            if (p is null) return Report(args, $"unknown plugin '{once}'", 1);
            var r = await runner.RunPluginAsync(p, true, CancellationToken.None);
            var json = r is null ? "" : OutputFile.Render(p.Id, p.Title, r, r.Error ? "error" : "ok", 0, DateTimeOffset.Now);
            return Report(args, json, r is { Error: true } ? 2 : 0);
        }

        // Eén host per datamap.
        string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dataDir.ToLowerInvariant())))[..16];
        using var mutex = new Mutex(true, "Local\\TaskbarStats_Plugins_" + id, out bool first);
        if (!first) return 0;
        using var stop = new EventWaitHandle(false, EventResetMode.ManualReset, "Local\\TaskbarStats_Plugins_Stop_" + id);

        HostLog.Write("INFO", $"host {Version} started, pid {Environment.ProcessId}, elevated={IsElevated()}, data={dataDir}");
        bool watchApp = Flag(args, "--watch-app");
        using var cts = new CancellationTokenSource();
        var idleSince = DateTime.UtcNow;
        var appGoneSince = (DateTime?)null;
        long lastCheck = 0;

        while (!stop.WaitOne(0))
        {
            try { runner.Tick(cts.Token); }
            catch (Exception ex) { HostLog.Write("ERROR", "tick: " + Fmt.Scrub(ex.Message)); }

            if (Environment.TickCount64 - lastCheck > 15_000)
            {
                lastCheck = Environment.TickCount64;
                if (runner.AnyEnabled()) idleSince = DateTime.UtcNow;
                else if (watchApp && DateTime.UtcNow - idleSince > TimeSpan.FromMinutes(2)) { HostLog.Write("INFO", "no plugin enabled, exiting"); break; }
                if (watchApp)
                {
                    bool appUp = Process.GetProcessesByName("TaskbarStats").Length > 0;
                    if (appUp) appGoneSince = null;
                    else if ((appGoneSince ??= DateTime.UtcNow) is var gone && DateTime.UtcNow - gone > TimeSpan.FromMinutes(3)) { HostLog.Write("INFO", "TaskbarStats not running, exiting"); break; }
                }
            }
            stop.WaitOne(1500);
        }
        cts.Cancel();
        HostLog.Write("INFO", "host stopped");
        return 0;
    }

    private static int Report(string[] args, string text, int code)
    {
        var outFile = Arg(args, "--out");
        if (outFile is not null) File.WriteAllText(outFile, text, new UTF8Encoding(false));
        else
        {
            AttachConsole(-1);
            using var o = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
            o.WriteLine(text);
        }
        return code;
    }

    internal static bool IsElevated()
    {
        try
        {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch (Exception) { return false; }
    }
}
