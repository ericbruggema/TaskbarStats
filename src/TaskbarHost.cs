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

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? cls, string? title);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
}
