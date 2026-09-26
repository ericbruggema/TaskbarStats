using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TaskbarStats;

/// <summary>Uitkomst van de FPS-meting van het voorgrondprogramma.</summary>
public sealed record FpsReading(int ProcessId, string Process, double Fps, double Low1Percent);

/// <summary>
/// EXPERIMENTEEL: framerate van het voorgrondprogramma, gemeten zoals PresentMon: een realtime ETW-sessie luistert naar de
/// Present-gebeurtenissen van DXGI (DirectX 10/11/12), Direct3D 9 en DxgKrnl (o.a. Vulkan/OpenGL) en telt ze per proces.
/// Het is alleen actief als de gebruiker het aanzet en vraagt administratorrechten (de app draait al als administrator).
/// Er wordt niets van de inhoud gelezen: alleen het proces-id en het tijdstip van elke gebeurtenis.
/// </summary>
public sealed class FpsMonitor : IDisposable
{
    private const string SessionName = "TaskbarStats-FPS";
    private static readonly Guid Dxgi = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");      // Microsoft-Windows-DXGI, Present::Start = 42
    private static readonly Guid D3D9 = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");      // Microsoft-Windows-D3D9, Present::Start = 1
    private static readonly Guid DxgKrnl = new("802EC45A-1E99-4B83-9920-87C98277BA9D");   // Microsoft-Windows-DxgKrnl, Present = 184

    private sealed class Frames
    {
        public readonly object Lock = new();
        public readonly long[] Dxgi = new long[1024], Krnl = new long[1024];
        public int DxgiN, KrnlN;
        public long LastSeen;
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, Frames> _procs = new();
    private Thread? _thread;
    private ulong _session;
    private ulong _trace = ulong.MaxValue;
    private volatile bool _on;
    private bool _cleaned;
    private string? _error;
    private long _cleanAt;
    private int _fgPid; private string _fgName = ""; private long _fgAt;

    /// <summary>Waarom er niets wordt gemeten (geen rechten, sessie niet te starten), voor de uitleg in de instellingen.</summary>
    public string? Error => _error;
    public bool Enabled => _on;

    public void Configure(bool on)
    {
        if (on == _on)
        {
            if (!on && !_cleaned) { _cleaned = true; StopByName(); }   // een sessie die een eerdere run (gecrasht of afgebroken) heeft laten staan
            return;
        }
        _on = on;
        if (on) Start(); else Stop();
    }

    private void Start()
    {
        _error = null;
        _thread = new Thread(Run) { IsBackground = true, Name = "TaskbarStats fps", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    private void Stop()
    {
        var t = _thread; _thread = null;
        StopSession();
        _procs.Clear();
        try { t?.Join(2000); } catch (Exception dex) { Diag.Swallow(dex); }
    }

    public void Dispose() { _on = false; Stop(); }

    // ---------- ETW ----------

    private void Run()
    {
        try
        {
            StopByName();   // een sessie die een eerdere (gecrashte) run heeft laten staan
            if (!StartSession()) return;
            var cb = new EventRecordCallback(OnEvent);
            IntPtr logfile = Marshal.AllocHGlobal(LogfileSize);
            try
            {
                for (int i = 0; i < LogfileSize; i += 8) Marshal.WriteInt64(logfile, i, 0);
                IntPtr name = Marshal.StringToHGlobalUni(SessionName);
                Marshal.WriteIntPtr(logfile, 8, name);                                  // LoggerName
                Marshal.WriteInt32(logfile, 28, ProcessTraceModeRealTime | ProcessTraceModeEventRecord);
                Marshal.WriteIntPtr(logfile, EventRecordCallbackOffset, Marshal.GetFunctionPointerForDelegate(cb));
                _trace = OpenTraceW(logfile);
                if (_trace == ulong.MaxValue) { _error = "OpenTrace failed (" + Marshal.GetLastWin32Error() + ")"; return; }
                ulong h = _trace;
                int rc = ProcessTrace(new[] { h }, 1, IntPtr.Zero, IntPtr.Zero);        // blokkeert tot de sessie stopt
                if (rc != 0 && _on) _error = "ProcessTrace: " + rc;
                CloseTrace(h);
                GC.KeepAlive(cb);
            }
            finally { Marshal.FreeHGlobal(logfile); }
        }
        catch (Exception ex) { _error = ex.GetType().Name + ": " + ex.Message; Diag.Swallow(ex); }
        finally { StopSession(); }
    }

    private bool StartSession()
    {
        IntPtr props = AllocProps();
        try
        {
            int rc = StartTraceW(out _session, SessionName, props);
            if (rc != 0) { _error = rc == 5 ? "access denied (administrator needed)" : "StartTrace: " + rc; return false; }
            foreach (var (g, level) in new[] { (Dxgi, (byte)5), (D3D9, (byte)5), (DxgKrnl, (byte)4) })
            {
                var guid = g;
                int e = EnableTraceEx2(_session, ref guid, 1 /* ENABLE_PROVIDER */, level, ulong.MaxValue, 0, 0, IntPtr.Zero);
                if (e != 0) Diag.Warn("FpsMonitor", "enabling provider " + g + " failed (" + e + ")");
            }
            return true;
        }
        finally { Marshal.FreeHGlobal(props); }
    }

    private void StopSession() { StopByName(); }

    private static void StopByName()
    {
        IntPtr props = AllocProps();
        try { ControlTraceW(0, SessionName, props, 1 /* STOP */); }
        catch (Exception dex) { Diag.Swallow(dex); }
        finally { Marshal.FreeHGlobal(props); }
    }

    // EVENT_TRACE_PROPERTIES + ruimte voor de sessienaam en (lege) logbestandsnaam.
    private const int PropsSize = 120, Extra = 2 * 1024;
    private static IntPtr AllocProps()
    {
        IntPtr p = Marshal.AllocHGlobal(PropsSize + Extra);
        for (int i = 0; i < PropsSize + Extra; i += 8) Marshal.WriteInt64(p, i, 0);
        Marshal.WriteInt32(p, 0, PropsSize + Extra);          // Wnode.BufferSize
        Marshal.WriteInt32(p, 44, 0x00020000);                // Wnode.Flags = WNODE_FLAG_TRACED_GUID
        Marshal.WriteInt32(p, 64, 0x00000100);                // LogFileMode = EVENT_TRACE_REAL_TIME_MODE
        Marshal.WriteInt32(p, 68, 1);                          // FlushTimer = 1 s
        Marshal.WriteInt32(p, 48 + 4, 4); Marshal.WriteInt32(p, 48 + 8, 64);   // Minimum/MaximumBuffers
        Marshal.WriteInt32(p, 48 + 0, 64);                    // BufferSize (KB)
        Marshal.WriteInt32(p, 116, PropsSize);                // LoggerNameOffset
        return p;
    }

    // Wnode (48) + BufferSize 4, MinimumBuffers 4, MaximumBuffers 4, MaximumFileSize 4, LogFileMode 4, FlushTimer 4, EnableFlags 4, AgeLimit 4,
    // NumberOfBuffers 4, FreeBuffers 4, EventsLost 4, BuffersWritten 4, LogBuffersLost 4, RealTimeBuffersLost 4, LoggerThreadId 8, LogFileNameOffset 4, LoggerNameOffset 4 = 120

    private void OnEvent(IntPtr rec)
    {
        try
        {
            int pid = Marshal.ReadInt32(rec, 12);
            long ts = Marshal.ReadInt64(rec, 16);
            var g = Marshal.PtrToStructure<Guid>(rec + 24);
            ushort id = (ushort)Marshal.ReadInt16(rec, 40);
            bool dx = (g == Dxgi && id == 42) || (g == D3D9 && id == 1);
            bool kr = g == DxgKrnl && id == 184;
            if (!dx && !kr) return;
            var f = _procs.GetOrAdd(pid, _ => new Frames());
            lock (f.Lock)
            {
                if (dx) { f.Dxgi[f.DxgiN++ & 1023] = ts; }
                else { f.Krnl[f.KrnlN++ & 1023] = ts; }
                f.LastSeen = ts;
            }
        }
        catch { /* nooit een uitzondering terug naar Windows */ }
    }

    /// <summary>Per proces het aantal frames van de laatste seconde (voor diagnose en tests).</summary>
    internal string DebugDump()
    {
        long now = NowTs() - Freq; long freq = Freq;
        var sb = new System.Text.StringBuilder();
        foreach (var (pid, f) in _procs)
        {
            string n = ""; try { n = Process.GetProcessById(pid).ProcessName; } catch (Exception dex) { Diag.Swallow(dex); }
            int dx, kr; lock (f.Lock) { dx = Count(f.Dxgi, f.DxgiN, now - freq, now); kr = Count(f.Krnl, f.KrnlN, now - freq, now); }
            sb.AppendLine($"{pid,6} {n,-24} dxgi/d3d9 {dx,4}  dxgkrnl {kr,4}");
        }
        return sb.ToString();
    }

    // ---------- lezen (sampler-thread) ----------

    /// <summary>De framerate van het voorgrondprogramma, of null zolang er geen frames worden gepresenteerd.</summary>
    public FpsReading? Read()
    {
        if (!_on) return null;
        long now = NowTs() - Freq;   // ETW levert met een kleine vertraging: we tonen de seconde die net voorbij is
        long freq = Freq;
        int fg = ForegroundPid(out string fgName);
        if (fg == 0) return null;

        // dezelfde pid, anders een proces met dezelfde exe-naam (browsers presenteren vanuit een GPU-kindproces)
        int pick = 0; double best = 0;
        foreach (var (pid, f) in _procs)
        {
            if (pid != fg && !SameExe(pid, fgName)) continue;
            double r = Rate(f, now, freq);
            if (pid == fg && r > 0) { pick = pid; best = r; break; }
            if (r > best) { pick = pid; best = r; }
        }
        if (pick == 0 || best <= 0) return null;
        var fr = _procs[pick];
        return new FpsReading(pick, fgName, best, Low1(fr, now, freq));
    }

    private readonly Dictionary<int, (string name, long at)> _names = new();
    private bool SameExe(int pid, string exe)
    {
        long t = Environment.TickCount64;
        if (!_names.TryGetValue(pid, out var e) || t - e.at > 30_000)
        {
            string n = "";
            try { n = Process.GetProcessById(pid).ProcessName; } catch (Exception dex) { Diag.Swallow(dex); }
            _names[pid] = e = (n, t);
            if (_names.Count > 256) _names.Clear();
        }
        return e.name.Equals(exe, StringComparison.OrdinalIgnoreCase);
    }

    private int ForegroundPid(out string name)
    {
        long t = Environment.TickCount64;
        IntPtr h = GetForegroundWindow();
        GetWindowThreadProcessId(h, out uint pid);
        if ((int)pid != _fgPid || t - _fgAt > 5000)
        {
            _fgPid = (int)pid; _fgAt = t; _fgName = "";
            try { if (pid != 0) _fgName = Process.GetProcessById((int)pid).ProcessName; } catch (Exception dex) { Diag.Swallow(dex); }
        }
        name = _fgName;
        if (t - _cleanAt > 15_000) { _cleanAt = t; Clean(); }
        return _fgPid;
    }

    private void Clean()
    {
        long now = NowTs();
        foreach (var (pid, f) in _procs) if (now - f.LastSeen > 20 * Freq) _procs.TryRemove(pid, out _);
    }

    // ETW-tijdstempels zijn FILETIME (100 ns sinds 1601)
    private const long Freq = 10_000_000;
    private static long NowTs() => DateTime.UtcNow.ToFileTimeUtc();

    // Frames per seconde over de laatste seconde; DXGI/D3D9 gaat voor, DxgKrnl alleen als die er niet is.
    private static double Rate(Frames f, long now, long freq)
    {
        lock (f.Lock)
        {
            if (now - f.LastSeen > freq * 2) return 0;
            long from = now - freq;
            int n = Count(f.Dxgi, f.DxgiN, from, now);
            if (n == 0) n = Count(f.Krnl, f.KrnlN, from, now);
            return n;
        }
    }

    private static int Count(long[] ring, int written, long from, long to)
    {
        int n = 0;
        for (int i = 1; i <= Math.Min(written, ring.Length); i++)
        {
            long ts = ring[(written - i) & 1023];
            if (ts < from) break;
            if (ts <= to) n++;
        }
        return n;
    }

    // 1%-low: de framerate van de traagste 1% frames over de laatste ~5 s (0 = te weinig gegevens).
    private static double Low1(Frames f, long now, long freq)
    {
        var deltas = new List<double>();
        lock (f.Lock)
        {
            var ring = f.DxgiN > 0 ? f.Dxgi : f.Krnl;
            int written = ring == f.Dxgi ? f.DxgiN : f.KrnlN;
            long from = now - 5 * freq, prev = 0;
            for (int i = Math.Min(written, ring.Length); i >= 1; i--)
            {
                long ts = ring[(written - i) & 1023];
                if (ts < from || ts > now) continue;
                if (prev != 0) deltas.Add((ts - prev) * 1000.0 / freq);
                prev = ts;
            }
        }
        if (deltas.Count < 30) return 0;
        deltas.Sort();
        double worst = deltas[Math.Min(deltas.Count - 1, (int)(deltas.Count * 0.99))];
        return worst > 0 ? 1000.0 / worst : 0;
    }

    // ---------- Win32 ----------
    private delegate void EventRecordCallback(IntPtr eventRecord);
    private const int ProcessTraceModeRealTime = 0x00000100, ProcessTraceModeEventRecord = 0x10000000;
    // EVENT_TRACE_LOGFILEW (x64): ProcessTraceMode op 28, EventRecordCallback op 424; totale grootte 448
    private const int LogfileSize = 448, EventRecordCallbackOffset = 424;

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern int StartTraceW(out ulong handle, string name, IntPtr props);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern int ControlTraceW(ulong handle, string name, IntPtr props, uint code);
    [DllImport("advapi32.dll")] private static extern int EnableTraceEx2(ulong handle, ref Guid provider, uint control, byte level, ulong matchAny, ulong matchAll, uint timeout, IntPtr parameters);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern ulong OpenTraceW(IntPtr logfile);
    [DllImport("advapi32.dll")] private static extern int ProcessTrace(ulong[] handles, uint count, IntPtr start, IntPtr end);
    [DllImport("advapi32.dll")] private static extern int CloseTrace(ulong handle);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
}
