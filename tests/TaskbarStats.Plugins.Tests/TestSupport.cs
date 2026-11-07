using System.Net;
using System.Text;

namespace TaskbarStats.Plugins.Tests;

/// <summary>HTTP-dubbel: geeft per verzoek een vast antwoord en onthoudt wat er gevraagd is (zonder internet).</summary>
internal sealed class FakeHandler : HttpMessageHandler
{
    public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK);
    public List<string> Urls { get; } = new();
    public List<string> AuthHeaders { get; } = new();
    public List<string> Betas { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Urls.Add(request.RequestUri!.ToString());
        AuthHeaders.Add(request.Headers.Authorization?.ToString() ?? "");
        Betas.Add(string.Join(",", request.Headers.TryGetValues("anthropic-beta", out var v) ? v : Array.Empty<string>()));
        return Task.FromResult(Respond(request));
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK)
        => new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

internal static class T
{
    public static HttpClient Http(FakeHandler h) => new(h);

    public static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "tsplugtest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(d);
        return d;
    }

    public static readonly DateTimeOffset Noon = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    public static PluginContext Ctx(string ini, FakeHandler? h = null, string? dataDir = null, Func<DateTimeOffset>? now = null)
        => new() { Config = IniFile.Parse(ini), Http = Http(h ?? new FakeHandler()), DataDir = dataDir ?? TempDir(), Now = now ?? (() => Noon) };

    /// <summary>Zoekt de map met de meegeleverde standaardconfigs (naast de testuitvoer of in de bronmap).</summary>
    public static string DefaultsDir()
    {
        var local = Path.Combine(AppContext.BaseDirectory, "defaults");
        if (Directory.Exists(local) && Directory.GetFiles(local, "*.ini").Length > 0) return local;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var c = Path.Combine(dir.FullName, "plugins", "TaskbarStats.Plugins", "defaults");
            if (Directory.Exists(c)) return c;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("defaults");
    }
}
