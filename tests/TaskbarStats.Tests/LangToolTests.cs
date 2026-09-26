using System.Diagnostics;
using System.Text.RegularExpressions;
using Xunit;

namespace TaskbarStats.Tests;

/// <summary>Draait tools\LangTool echt (dotnet run) tegen de repo en tegen een bewust kapotgemaakte kopie.</summary>
public class LangToolTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "tslang-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    private static (int code, string output) RunLangTool(string root, string repo)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            WorkingDirectory = repo,
        };
        foreach (var a in new[] { "run", "--project", Path.Combine(repo, "tools", "LangTool", "LangTool.csproj"), "-c", "Release", "--verbosity", "quiet", "--", "check", "--strict", "--root", root })
            psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var so = p.StandardOutput.ReadToEndAsync();
        var se = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(240_000)) { try { p.Kill(true); } catch { } throw new TimeoutException("LangTool duurde te lang"); }
        return (p.ExitCode, so.Result + se.Result);
    }

    private string CopyRoot()
    {
        string repo = RepoRoot.Find();
        Directory.CreateDirectory(_tmp);
        File.Copy(Path.Combine(repo, "TaskbarStats.csproj"), Path.Combine(_tmp, "TaskbarStats.csproj"));
        CopyDir(Path.Combine(repo, "lang"), Path.Combine(_tmp, "lang"));
        CopyDir(Path.Combine(repo, "src"), Path.Combine(_tmp, "src"));
        return _tmp;
    }

    private static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
    }

    [Fact]
    public void Repo_slaagt_met_strict()
    {
        string repo = RepoRoot.Find();
        var (code, output) = RunLangTool(repo, repo);
        Assert.True(code == 0, "LangTool check --strict gaf exitcode " + code + Environment.NewLine + output);
    }

    [Fact]
    public void Kapotte_plaatsaanduiding_in_vertaling_geeft_exitcode_1()
    {
        string root = CopyRoot();
        string nl = Path.Combine(root, "lang", "nl.json");
        var lines = File.ReadAllLines(nl).ToList();
        // Zoek een regel waarvan sleutel en waarde {0} hebben en maak er in de waarde {1} van.
        var rx = new Regex(@"^(\s*""[^""]*\{0\}[^""]*"":\s*"")(.*\{0\}.*)(""\s*,?\s*)$");
        int i = lines.FindIndex(l => rx.IsMatch(l) && !l.Contains('|'));
        Assert.True(i >= 0, "geen geschikte regel met {0} in nl.json gevonden");
        var m = rx.Match(lines[i]);
        lines[i] = m.Groups[1].Value + m.Groups[2].Value.Replace("{0}", "{1}") + m.Groups[3].Value;
        File.WriteAllLines(nl, lines);

        var (code, output) = RunLangTool(root, RepoRoot.Find());
        Assert.True(code == 1, "verwacht exitcode 1, kreeg " + code + Environment.NewLine + output);
    }

    [Fact]
    public void Dubbele_sleutel_geeft_exitcode_1()
    {
        string root = CopyRoot();
        string nl = Path.Combine(root, "lang", "nl.json");
        var lines = File.ReadAllLines(nl).ToList();
        int i = lines.FindIndex(l => l.TrimStart().StartsWith("\"") && !l.StartsWith("  \"_") && l.TrimEnd().EndsWith(","));
        Assert.True(i >= 0);
        lines.Insert(i + 1, lines[i]);   // dezelfde regel nog een keer
        File.WriteAllLines(nl, lines);

        var (code, output) = RunLangTool(root, RepoRoot.Find());
        Assert.True(code == 1, "verwacht exitcode 1, kreeg " + code + Environment.NewLine + output);
        Assert.Contains("LANG111", output);
    }
}
