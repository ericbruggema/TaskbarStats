using System.Runtime.InteropServices;

namespace TaskbarStats;

/// <summary>Hulpfuncties om het systeemvak (TrayNotifyWnd) van de Windows-taakbalk te vinden.</summary>
public static class TaskbarHost
{
    /// <summary>Positie/grootte van het systeemvak in schermcoördinaten.</summary>
    public static bool TryGetTrayRect(out Rectangle rect)
    {
        rect = Rectangle.Empty;
        IntPtr tb = FindWindow("Shell_TrayWnd", null);
        if (tb == IntPtr.Zero) return false;
        IntPtr tray = FindWindowEx(tb, IntPtr.Zero, "TrayNotifyWnd", null);
        if (tray == IntPtr.Zero) return false;
        if (!GetWindowRect(tray, out RECT r)) return false;
        rect = new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
        return true;
    }

    /// <summary>Positie/grootte van de taakbalk zelf in schermcoördinaten.</summary>
    public static bool TryGetTaskbarRect(out Rectangle rect)
    {
        rect = Rectangle.Empty;
        IntPtr tb = FindWindow("Shell_TrayWnd", null);
        if (tb == IntPtr.Zero || !GetWindowRect(tb, out RECT r)) return false;
        rect = new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
        return true;
    }

    /// <summary>
    /// True zodra de taakbalk op dit moment weggeschoven is door "Taakbalk automatisch verbergen" (staat die instelling uit,
    /// dan is dit altijd false). Gebaseerd op <c>SHAppBarMessage</c>: <c>ABM_GETSTATE</c> voor de instelling zelf, daarna de
    /// live rand (<c>ABM_GETTASKBARPOS</c>) - verstopt laat Windows maar een paar pixels van de taakbalk staan (om te
    /// kunnen hoveren), dus de dikte loodrecht op de rand waar hij aan vastzit wordt dan heel klein.
    /// </summary>
    public static bool IsAutoHiddenNow()
    {
        var abd = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>() };
        if ((SHAppBarMessage(ABM_GETSTATE, ref abd) & ABS_AUTOHIDE) == 0) return false;
        abd = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>() };
        if (SHAppBarMessage(ABM_GETTASKBARPOS, ref abd) == 0) return false;
        int w = abd.rc.Right - abd.rc.Left, h = abd.rc.Bottom - abd.rc.Top;
        bool vertical = abd.uEdge is ABE_LEFT or ABE_RIGHT;
        return vertical ? w <= 8 : h <= 8;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize; public IntPtr hWnd; public uint uCallbackMessage; public uint uEdge; public RECT rc; public IntPtr lParam;
    }

    private const uint ABM_GETSTATE = 0x4, ABM_GETTASKBARPOS = 0x5, ABS_AUTOHIDE = 0x1;
    private const uint ABE_LEFT = 0, ABE_RIGHT = 2;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? cls, string? title);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("shell32.dll")] private static extern uint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);
}
