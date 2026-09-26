using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TaskbarStats;

/// <summary>Aantal draaiende processen: totaal (zoals Taakbeheer), apps (processen met een zichtbaar venster) en achtergrondprocessen.</summary>
public sealed partial class Metrics
{
    /// <summary>Alle processen (Windows-teller System\Processes).</summary>
    public int ProcessTotal { get; private set; }
    /// <summary>Apps: processen met minstens één zichtbaar hoofdvenster (zoals de groep "Apps" in Taakbeheer).</summary>
    public int ProcessApps { get; private set; }
    public int ThreadTotal { get; private set; }

    private PerformanceCounter? _procCount, _threadCount;
    private long _procAt;

    // Op de sampler-thread; hooguit elke 2 s (de teller is goedkoop, het doorlopen van de vensters kost enkele ms).
    private void UpdateProcesses()
    {
        long now = Environment.TickCount64;
        if (now - _procAt < 2000 && ProcessTotal > 0) return;
        _procAt = now;
        try
        {
            _procCount ??= new PerformanceCounter("System", "Processes");
            _threadCount ??= new PerformanceCounter("System", "Threads");
            ProcessTotal = (int)_procCount.NextValue();
            ThreadTotal = (int)_threadCount.NextValue();
        }
        catch (Exception dex) { Diag.Swallow(dex); ProcessTotal = Process.GetProcesses().Length; }
        ProcessApps = CountApps();
    }

    /// <summary>Aantal verschillende processen met een zichtbaar, niet-verborgen (cloaked) hoofdvenster zonder eigenaar en zonder tool-venster-stijl.</summary>
    internal static int CountApps()
    {
        var pids = new HashSet<uint>();
        EnumWindows((h, _) =>
        {
            try
            {
                if (!IsWindowVisible(h) || GetWindow(h, 4 /* GW_OWNER */) != IntPtr.Zero) return true;
                if ((GetWindowLongPtr(h, -20 /* GWL_EXSTYLE */).ToInt64() & 0x80) != 0) return true;   // WS_EX_TOOLWINDOW
                if (GetWindowTextLength(h) == 0) return true;
                if (DwmGetWindowAttribute(h, 14 /* DWMWA_CLOAKED */, out int cloaked, 4) == 0 && cloaked != 0) return true;
                GetWindowThreadProcessId(h, out uint pid);
                if (pid != 0) pids.Add(pid);
            }
            catch (Exception dex) { Diag.Swallow(dex); }
            return true;
        }, IntPtr.Zero);
        return pids.Count;
    }

    private delegate bool EnumWindowsProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr h, int index);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);

    /// <summary>De experimentele FPS-meting van het voorgrondprogramma (meet alleen als ze aan staat).</summary>
    public FpsMonitor Fps { get; } = new();
}
