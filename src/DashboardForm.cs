using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace TaskbarStats;

/// <summary>
/// Groot bureaublad-dashboard: losse tegels (CPU, GPU, geheugen, netwerk, schijven, batterij, programma's, systeem)
/// met grafiekjes. Halfdoorzichtig, schaalbaar, op de voor- of achtergrond en eventueel klik-door.
/// Tekent zichzelf naar een ARGB-bitmap (UpdateLayeredWindow), net als het taakbalk-widget.
/// </summary>
public sealed partial class DashboardForm : Form
{
    private readonly DashContext _c;
    private AppSettings Cfg => _c.Cfg;
    private readonly ProcessSampler _procs = new() { TopCount = 5 };
    private readonly DashboardRenderer _renderer;
    private readonly System.Windows.Forms.Timer _zTimer = new() { Interval = 400 };   // goedkope controle; herstelt de positie snel na "Bureaublad weergeven"
    private long _lastRender, _lastProc;
    private bool _suppressed, _dragging, _closing, _wantHidden;
    private Point _dragStart;

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00080000 /* LAYERED */ | 0x00000080 /* TOOLWINDOW */ | 0x08000000 /* NOACTIVATE */;
            if (_c?.Cfg.DashClickThrough == true) cp.ExStyle |= 0x00000020;   // TRANSPARENT = klik-door
            return cp;
        }
    }

    public DashboardForm(DashContext c)
    {
        _c = c;
        _renderer = new DashboardRenderer(c.Cfg, c.History, c.Usage, _procs, () => new BgLayer(this, 1024, Render));
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(400, 300);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);

        // Op de voorgrond hoeft niets steeds opnieuw naar boven (topmost blijft topmost); alleen de achtergrond-stand herhalen.
        _zTimer.Tick += (_, _) => { EnsureVisible(); if (!Cfg.DashFront) ApplyZ(true); };
        _zTimer.Start();
        MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right) { _dragging = false; _c.ShowMenu(Cursor.Position); }
            else if (e.Button == MouseButtons.Left) { _dragging = !Cfg.DashLocked; _dragStart = e.Location; }
        };
        MouseMove += (_, e) =>
        {
            if (!_dragging) return;
            Location = new Point(Location.X + e.X - _dragStart.X, Location.Y + e.Y - _dragStart.Y);
            Cfg.DashX = Location.X; Cfg.DashY = Location.Y;
        };
        MouseUp += (_, _) => { if (_dragging) { _dragging = false; Cfg.Save(); } };
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Render();   // bepaalt de grootte
        if (Cfg.DashX is null || Cfg.DashY is null)
        {
            var wa = Screen.PrimaryScreen!.WorkingArea;
            Cfg.DashX = Math.Max(wa.Left, wa.Right - Width - 40);
            Cfg.DashY = wa.Top + 40;
        }
        Location = new Point(Cfg.DashX!.Value, Cfg.DashY!.Value);
        if (!Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(Bounds)))
            Location = new Point(Screen.PrimaryScreen!.WorkingArea.Left + 40, Screen.PrimaryScreen.WorkingArea.Top + 40);
        ApplySettings();
    }

    public void ResetPosition()
    {
        Cfg.DashX = null; Cfg.DashY = null;
        var wa = Screen.PrimaryScreen!.WorkingArea;
        Cfg.DashX = Math.Max(wa.Left, wa.Right - Width - 40);
        Cfg.DashY = wa.Top + 40;
        Location = new Point(Cfg.DashX.Value, Cfg.DashY.Value);
        Cfg.Save();
    }

    /// <summary>Klik-door, voor/achtergrond, doorzichtigheid, schaal e.d. opnieuw toepassen.</summary>
    public void ApplySettings()
    {
        if (!IsHandleCreated) return;
        int ex = GetWindowLong(Handle, -20);
        ex = Cfg.DashClickThrough ? ex | 0x20 : ex & ~0x20;
        SetWindowLong(Handle, -20, ex);
        ApplyZ();
        Render();
    }

    // "Bureaublad weergeven" (Win+D / knop rechts op de taakbalk) minimaliseert of verbergt vensters. Het dashboard
    // hoort juist op het bureaublad te blijven: we herstellen het meteen (en controleren elke seconde).
    private void EnsureVisible()
    {
        if (!IsHandleCreated || _suppressed || _closing || _wantHidden) return;
        if (IsIconic(Handle) || !IsWindowVisible(Handle))
        {
            ShowWindow(Handle, 4);   // SW_SHOWNOACTIVATE
            ApplyZ();
            Render();
        }
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (_suppressed || _closing || _wantHidden || !IsHandleCreated) return;
        bool minimized = m.Msg == 0x0005 /* WM_SIZE */ && m.WParam.ToInt32() == 1 /* SIZE_MINIMIZED */;
        bool hidden = m.Msg == 0x0018 /* WM_SHOWWINDOW */ && m.WParam == IntPtr.Zero && m.LParam == IntPtr.Zero;
        if (minimized || hidden) BeginInvoke(new Action(EnsureVisible));
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _closing = true;
        base.OnFormClosing(e);
    }

    /// <summary>Tonen (na "Dashboard tonen" aan).</summary>
    public void Present()
    {
        _wantHidden = false;
        if (!Visible) Show(); else ApplySettings();
    }

    /// <summary>Bewust verbergen (na "Dashboard tonen" uit): niet automatisch herstellen.</summary>
    public void HideByUser()
    {
        _wantHidden = true;
        Hide();
    }

    public void SetSuppressed(bool suppressed)
    {
        if (suppressed == _suppressed) return;
        _suppressed = suppressed;
        if (!IsHandleCreated) return;
        ShowWindow(Handle, suppressed ? 0 : 4);
        if (!suppressed) { ApplyZ(); Render(); }
    }

    private void ApplyZ(bool periodic = false)
    {
        if (!IsHandleCreated || _suppressed) return;
        const uint flags = 0x0001 | 0x0002 | 0x0010;   // NOSIZE | NOMOVE | NOACTIVATE
        if (Cfg.DashFront) SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, flags);            // TOPMOST
        else PlaceAboveDesktop(flags, periodic);
    }

    private bool _raised;    // staat tijdelijk bovenaan omdat de shell het bureaublad boven alle vensters haalde
    private int _zTick;

    /// <summary>
    /// Achtergrondstand: onderaan de gewone vensters, maar boven het bureaublad. Bij "Bureaublad weergeven" haalt de shell het
    /// bureaubladvenster (Progman) tijdelijk boven alle gewone vensters; het dashboard verdween dan erachter. Staat het bureaublad
    /// boven ons, dan gaan we er tijdelijk bovenop; is het weer naar beneden, dan zakken we mee.
    /// </summary>
    private void PlaceAboveDesktop(uint flags, bool periodic)
    {
        if ((GetWindowLong(Handle, -20) & 0x8) != 0)                                           // nog TOPMOST (na wisselen van voor- naar achtergrond)
            SetWindowPos(Handle, new IntPtr(-2), 0, 0, 0, 0, flags);                           // NOTOPMOST
        var host = DesktopHost();
        // Bij "Bureaublad weergeven" haalt de shell het bureaublad boven alle gewone vensters: de programmavensters (nu
        // geminimaliseerd/verborgen) staan dan eronder. Normaal staan die erboven. Zo herkennen we de toestand betrouwbaar,
        // ook als het bureaublad lang "weergegeven" blijft (het aantal vensters onder het bureaublad zegt niets: er zitten altijd
        // verborgen hulpvensters onder).
        bool shown = host != IntPtr.Zero && AppWindowBelow(host);
        if (shown)
        {
            if (ZIndex(Handle) > ZIndex(host))
            {
                // HWND_TOP alleen haalt het bureaublad niet in; via topmost en weer gewoon komen we bovenaan de gewone vensters.
                SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, flags);                       // TOPMOST
                SetWindowPos(Handle, new IntPtr(-2), 0, 0, 0, 0, flags);                       // NOTOPMOST
            }
            _raised = true;                                                                    // erbovenop blijven zolang het bureaublad getoond wordt
            return;
        }
        if (_raised || !periodic || ++_zTick % 5 == 0)                                         // anders af en toe (2 s) onderaan zetten
        {
            SetWindowPos(Handle, new IntPtr(1), 0, 0, 0, 0, flags);                            // BOTTOM
            _raised = false;
        }
    }

    /// <summary>Staat er onder dit venster een zichtbaar programmavenster (ook een geminimaliseerd) van een ander proces?</summary>
    private static bool AppWindowBelow(IntPtr h)
    {
        uint me = (uint)Environment.ProcessId;
        int n = 0;
        for (var w = GetWindow(h, 2 /* GW_HWNDNEXT */); w != IntPtr.Zero && n < 5000; w = GetWindow(w, 2), n++)
        {
            if (!IsWindowVisible(w)) continue;
            int ex = GetWindowLong(w, -20);
            if ((ex & 0x8 /* TOPMOST */) != 0 || (ex & 0x80 /* TOOLWINDOW */) != 0) continue;
            GetWindowThreadProcessId(w, out uint pid);
            if (pid == me) continue;
            if (DwmGetWindowAttribute(w, 14 /* DWMWA_CLOAKED */, out int cloaked, 4) == 0 && cloaked != 0) continue;
            return true;
        }
        return false;
    }

    /// <summary>Aantal vensters boven dit venster in de z-volgorde (0 = bovenaan).</summary>
    private static int ZIndex(IntPtr h)
    {
        int n = 0;
        for (var w = GetWindow(h, 3 /* GW_HWNDPREV */); w != IntPtr.Zero && n < 5000; w = GetWindow(w, 3)) n++;
        return n;
    }

    /// <summary>Het venster dat de bureaubladpictogrammen bevat (Progman, of een WorkerW).</summary>
    private static IntPtr DesktopHost()
    {
        var prog = FindWindow("Progman", null);
        if (prog != IntPtr.Zero && FindWindowEx(prog, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero) return prog;
        for (var w = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "WorkerW", null); w != IntPtr.Zero; w = FindWindowEx(IntPtr.Zero, w, "WorkerW", null))
            if (FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero) return w;
        return prog;
    }

    /// <summary>Aan te roepen bij elke tik van het widget; tekent maximaal 1x per seconde.</summary>
    public void Tick()
    {
        if (!Visible || _suppressed) return;
        long now = Environment.TickCount64;
        if (Cfg.DashProcs && now - _lastProc >= 2000) { _lastProc = now; _procs.SampleAsync(); }
        if (now - _lastRender < 1000) return;
        Render();
    }

    // ---------- Tekenen (de tekencode zit in DashboardRenderer) ----------
    public void Render()
    {
        if (!IsHandleCreated || _suppressed) return;
        _lastRender = Environment.TickCount64;
        using var bmp = _renderer.Render(_c.Metrics.Current, _c.Drives(), DeviceDpi);
        if (Width != bmp.Width || Height != bmp.Height) { Width = bmp.Width; Height = bmp.Height; }
        Push(bmp);
    }


    private void Push(Bitmap bmp)
    {
        IntPtr screenDc = GetDC(IntPtr.Zero), memDc = CreateCompatibleDC(screenDc);
        IntPtr hBmp = bmp.GetHbitmap(Color.FromArgb(0)), old = SelectObject(memDc, hBmp);
        try
        {
            var size = new Size(bmp.Width, bmp.Height);
            var src = new Point(0, 0);
            var dst = new Point(Left, Top);
            byte alpha = (byte)Math.Clamp(Cfg.DashOpacity * 255 / 100, 20, 255);
            var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = alpha, AlphaFormat = 1 };
            UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, 2);
        }
        finally
        {
            SelectObject(memDc, old); DeleteObject(hBmp); DeleteDC(memDc); ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    // ---------- Interop ----------
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref Point pptDst, ref Size psize,
        IntPtr hdcSrc, ref Point pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? cls, string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? name);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out int value, int size);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _zTimer.Dispose(); _renderer.Dispose(); }
        base.Dispose(disposing);
    }
}