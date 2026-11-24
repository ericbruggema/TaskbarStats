using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TaskbarStats;

/// <summary>
/// Vraagt Verkenner dingen die synchroon via een andere proces lopen (volledig-scherm-melding, automatisch verbergen van de taakbalk)
/// op een EIGEN thread, en bewaart alleen het laatste antwoord. Deze aanroepen (<c>SHQueryUserNotificationState</c>,
/// <c>SHAppBarMessage</c>) wachten tot Verkenner antwoordt; stond dat op de UI-thread (elke 200 ms), dan hing het widget zodra
/// Verkenner het druk had of de processor vol zat, en meldde Windows "reageert niet". Nu wacht alleen deze thread.
/// </summary>
internal sealed class ShellWatcher : IDisposable
{
    [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);

    private readonly Func<bool> _fullscreen, _autoHide;
    private readonly int _intervalMs;
    private readonly ManualResetEventSlim _stop = new(false);
    private volatile bool _fullscreenNow, _autoHiddenNow;

    /// <summary>Draait er nu een app op volledig scherm (of een presentatie)? Laatst bekende antwoord, nooit wachtend.</summary>
    public bool FullscreenApp => _fullscreenNow;

    /// <summary>Is de taakbalk nu weggeschoven (automatisch verbergen)? Laatst bekende antwoord, nooit wachtend.</summary>
    public bool TaskbarAutoHidden => _autoHiddenNow;

    /// <param name="fullscreen">De vraag naar volledig scherm; wordt op de watcher-thread uitgevoerd en mag langzaam zijn (bevat zelf de test of de instelling aan staat).</param>
    /// <param name="autoHide">De vraag naar de automatisch verborgen taakbalk (idem).</param>
    public ShellWatcher(Func<bool> fullscreen, Func<bool> autoHide, int intervalMs = 200)
    {
        _fullscreen = fullscreen; _autoHide = autoHide; _intervalMs = intervalMs;
        new Thread(Run) { IsBackground = true, Name = "TaskbarStats shell watcher", Priority = ThreadPriority.BelowNormal }.Start();
    }

    public void Dispose() => _stop.Set();   // de thread is een achtergrondthread: hij hoeft niet te wachten op een vastzittende aanroep

    private void Run()
    {
        while (!_stop.Wait(_intervalMs))
        {
            _fullscreenNow = Ask("shell: volledig scherm", _fullscreen);
            _autoHiddenNow = Ask("shell: taakbalk verbergen", _autoHide);
        }
    }

    private static bool Ask(string what, Func<bool> query)
    {
        long t = Stopwatch.GetTimestamp();
        try { return query(); }
        catch (Exception ex) { Diag.Swallow(ex); return false; }
        finally
        {
            long ms = (Stopwatch.GetTimestamp() - t) * 1000 / Stopwatch.Frequency;
            if (ms >= 400) Diag.Slow(what, ms);   // Verkenner antwoordde traag: precies het soort wachten dat het widget eerst liet vastlopen
        }
    }

    /// <summary>QUNS_BUSY (2), QUNS_RUNNING_D3D_FULL_SCREEN (3), QUNS_PRESENTATION_MODE (4).</summary>
    public static bool QueryFullscreen()
    {
        try { return SHQueryUserNotificationState(out int st) == 0 && st is 2 or 3 or 4; }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }
}
