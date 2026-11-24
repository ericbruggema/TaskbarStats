using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TaskbarStats;

/// <summary>
/// Bewaking van de UI-thread: een vastlopend widget ("reageert niet") laat nu een spoor achter. <see cref="UiMark"/> onthoudt in welke
/// stap van de tik de UI-thread zit (en logt een stap die lang duurde), <see cref="Slow"/> logt een trage aanroep, en
/// <see cref="UiWatchdog"/> merkt op een eigen thread dat Windows het venster als vastgelopen aanmerkt en legt dan vast waar het zat,
/// hoe druk het systeem was en welke programma's de processor gebruikten.
/// </summary>
internal static partial class Diag
{
    private static readonly Dictionary<string, int> SlowSeen = new();
    private static volatile string _uiStep = "";
    private static long _uiStepAt;
    private const long SlowStepMs = 400;

    /// <summary>De stap waar de UI-thread nu in zit ("" = in het berichtenlus, dus niet bezig).</summary>
    internal static string UiStep => _uiStep;

    /// <summary>Hoe lang de UI-thread al in <see cref="UiStep"/> zit.</summary>
    internal static long UiStepMs => _uiStep.Length == 0 ? 0 : (Stopwatch.GetTimestamp() - Interlocked.Read(ref _uiStepAt)) * 1000 / Stopwatch.Frequency;

    /// <summary>
    /// Op de UI-thread: begin van een volgende stap (of "" aan het eind). Duurde de vorige stap langer dan 400 ms, dan wordt dat
    /// gelogd: zo is te zien welke aanroep de tik ophoudt.
    /// </summary>
    public static void UiMark(string step)
    {
        long now = Stopwatch.GetTimestamp();
        string prev = _uiStep;
        if (prev.Length > 0)
        {
            long ms = (now - Interlocked.Read(ref _uiStepAt)) * 1000 / Stopwatch.Frequency;
            if (ms >= SlowStepMs) Slow("ui:" + prev, ms);
        }
        Interlocked.Exchange(ref _uiStepAt, now);
        _uiStep = step;
    }

    /// <summary>Legt een trage aanroep vast (per soort de eerste 10 keer, daarna elke 50e, zodat het logbestand niet volloopt).</summary>
    public static void Slow(string what, long ms)
    {
        int n;
        lock (Gate)
        {
            SlowSeen.TryGetValue(what, out n);
            SlowSeen[what] = ++n;
        }
        if (n <= 10 || n % 50 == 0) Write("WARN", "slow", $"{what} duurde {ms} ms" + (n > 10 ? $" (keer {n})" : ""));
    }

    /// <summary>Alleen voor tests.</summary>
    internal static void ResetWatchForTests() { lock (Gate) SlowSeen.Clear(); _uiStep = ""; }
}

/// <summary>
/// Kijkt op een eigen thread of Windows het widgetvenster als vastgelopen ziet (<c>IsHungAppWindow</c>: de UI-thread haalt al ruim 5 s
/// geen berichten meer op; dezelfde grens waarop Windows "reageert niet" toont). Bij een vastloper komt er een regel in diag.log en een
/// crashrapport met de laatste stap van de UI-thread, de belasting van het systeem en de programma's die de meeste processortijd
/// gebruikten; als het venster weer reageert staat daar hoe lang het duurde.
/// </summary>
internal sealed class UiWatchdog : IDisposable
{
    [DllImport("user32.dll")] private static extern bool IsHungAppWindow(IntPtr hwnd);
    [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
    [DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr process, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }
    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    private readonly Func<bool> _isHung;
    private readonly int _pollMs, _topWindowMs;
    private readonly ManualResetEventSlim _stop = new(false);
    private readonly Thread _thread;
    private long _lastIdle, _lastBusy;
    private double _systemCpu = -1;

    /// <summary>Start de bewaking voor het venster met deze handle.</summary>
    public static UiWatchdog Start(IntPtr hwnd) => new(() => IsHungAppWindow(hwnd), 1000, 1000);

    /// <summary>Voor tests: eigen "vastgelopen"-test en korte tijden.</summary>
    internal UiWatchdog(Func<bool> isHung, int pollMs, int topWindowMs)
    {
        _isHung = isHung; _pollMs = pollMs; _topWindowMs = topWindowMs;
        _thread = new Thread(Run) { IsBackground = true, Name = "TaskbarStats UI watchdog", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    public void Dispose() => _stop.Set();

    private void Run()
    {
        long hungAt = 0;
        bool reportedLong = false;
        while (!_stop.Wait(_pollMs))
        {
            try
            {
                SampleSystemCpu();
                bool hung = _isHung();
                if (hung && hungAt == 0)
                {
                    hungAt = Stopwatch.GetTimestamp();
                    reportedLong = false;
                    OnHung();
                }
                else if (hung && !reportedLong && Elapsed(hungAt) > 30_000)
                {
                    reportedLong = true;
                    Diag.Info("ui-hang", $"De UI-thread reageert nog steeds niet ({Elapsed(hungAt) / 1000} s); laatste stap '{Diag.UiStep}'.");
                }
                else if (!hung && hungAt != 0)
                {
                    Diag.Info("ui-hang", $"De UI-thread reageert weer, na ruim {Elapsed(hungAt) / 1000 + 5} s (Windows meldt pas na 5 s)." + (Diag.UiStep.Length > 0 ? $" Nu in stap '{Diag.UiStep}'." : ""));
                    hungAt = 0;
                }
            }
            catch (Exception ex) { Diag.Swallow(ex); }
        }
    }

    private static long Elapsed(long since) => (Stopwatch.GetTimestamp() - since) * 1000 / Stopwatch.Frequency;

    private void SampleSystemCpu()
    {
        if (!GetSystemTimes(out long idle, out long kernel, out long user)) return;
        long busy = kernel + user - idle;   // kernel bevat de idle-tijd
        long di = idle - _lastIdle, db = busy - _lastBusy;
        if (_lastIdle != 0 && di + db > 0) _systemCpu = 100.0 * db / (di + db);
        _lastIdle = idle; _lastBusy = busy;
    }

    /// <summary>Het venster is net vastgelopen: leg vast waar het zit en wat er op het systeem speelt.</summary>
    private void OnHung()
    {
        string step = Diag.UiStep, line = step.Length == 0
            ? "de UI-thread zit niet in een van onze stappen (berichtenlus, een menu of een venster)"
            : $"de UI-thread zit al {Diag.UiStepMs} ms in stap '{step}'";
        string env = Conditions();
        Diag.Warn("ui-hang", "Windows ziet het widget als vastgelopen: " + line + ". " + env);
        string top = TopCpu();   // duurt een moment (meet over een korte tijd), de UI-thread merkt daar niets van
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("The widget did not respond for at least 5 seconds (Windows marks the window as hung).");
        sb.AppendLine(line + ".");
        sb.AppendLine(env);
        sb.AppendLine("Busiest programs just after the freeze started (CPU of one core in %):");
        sb.AppendLine(top);
        Diag.SaveCrashReport("UI thread not responding (the app kept running)", null, sb.ToString());
    }

    private string Conditions()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(_systemCpu >= 0 ? $"Systeem-CPU {_systemCpu:0}%" : "Systeem-CPU onbekend");
        var ms = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (GlobalMemoryStatusEx(ref ms)) sb.Append($", geheugen {ms.MemoryLoad}% in gebruik ({ms.AvailPhys >> 20} MB vrij)");
        try
        {
            using var p = Process.GetCurrentProcess();
            sb.Append($", app: {p.WorkingSet64 >> 20} MB, {p.Threads.Count} threads, {p.HandleCount} handles, GDI {GetGuiResources(p.Handle, 0)}, USER {GetGuiResources(p.Handle, 1)}, prioriteit {p.PriorityClass}");
        }
        catch (Exception ex) { Diag.Swallow(ex); }
        return sb.ToString() + ".";
    }

    /// <summary>De vijf programma's die de meeste processortijd gebruikten in een korte meetperiode (per programmanaam samengeteld).</summary>
    private string TopCpu()
    {
        try
        {
            var first = Snapshot();
            if (_stop.Wait(_topWindowMs)) return "(stopped)";
            var second = Snapshot();
            double window = _topWindowMs / 1000.0;
            var rows = second.Select(kv => (Name: kv.Key, Cpu: first.TryGetValue(kv.Key, out var t0) ? (kv.Value - t0).TotalSeconds / window * 100 : 0))
                             .Where(r => r.Cpu >= 1).OrderByDescending(r => r.Cpu).Take(5).ToList();
            return rows.Count == 0 ? "(no program above 1%)" : string.Join(System.Environment.NewLine, rows.Select(r => $"  {r.Name}: {r.Cpu:0}%"));
        }
        catch (Exception ex) { Diag.Swallow(ex); return "(not available)"; }
    }

    private static Dictionary<string, TimeSpan> Snapshot()
    {
        var d = new Dictionary<string, TimeSpan>();
        foreach (var p in Process.GetProcesses())
        {
            try { d[p.ProcessName] = d.TryGetValue(p.ProcessName, out var t) ? t + p.TotalProcessorTime : p.TotalProcessorTime; }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { /* proces weg of niet toegankelijk */ }
            finally { p.Dispose(); }
        }
        return d;
    }
}
