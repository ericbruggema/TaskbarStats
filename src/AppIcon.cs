using System.Runtime.InteropServices;

namespace TaskbarStats;

/// <summary>Het programma-icoon (ingebed, meerdere formaten) voor venster-, taakbalk- en systeemvak-pictogrammen.</summary>
public static class AppIcon
{
    private static Icon? _small, _large;
    private static readonly List<Icon> Keep = new();   // iconen moeten in leven blijven zolang een venster ze gebruikt

    private static Icon Load(Size size)
    {
        try
        {
            using var s = typeof(AppIcon).Assembly.GetManifestResourceStream("app.ico");
            if (s is not null) return new Icon(s, size);
        }
        catch (Exception dex) { Diag.Swallow(dex); }
        return SystemIcons.Application;
    }

    /// <summary>Klein formaat (systeemvak, titelbalk).</summary>
    public static Icon Small => _small ??= Load(SystemInformation.SmallIconSize);

    /// <summary>Groot formaat (taakbalkknop, Alt+Tab).</summary>
    public static Icon Large => _large ??= Load(SystemInformation.IconSize);

    /// <summary>
    /// Zet het icoon op een gewoon venster, op de maat die bij de schaling (DPI) van het scherm hoort, zodat de
    /// taakbalkknop scherp is in plaats van opgeschaald.
    /// </summary>
    public static void Apply(Form f)
    {
        try { f.Icon = Large; } catch (Exception dex) { Diag.Swallow(dex); }
        f.HandleCreated += (_, _) => f.BeginInvoke(new Action(() => SetSized(f)));
        f.DpiChanged += (_, _) => SetSized(f);
    }

    private static void SetSized(Form f)
    {
        try
        {
            if (!f.IsHandleCreated) return;
            int dpi = f.DeviceDpi;
            int bw = GetSystemMetricsForDpi(11, dpi), bh = GetSystemMetricsForDpi(12, dpi);
            int sw = GetSystemMetricsForDpi(49, dpi), sh = GetSystemMetricsForDpi(50, dpi);
            if (bw <= 0 || bh <= 0 || sw <= 0 || sh <= 0) return;   // onbekende maat: het standaard-icoon van het venster blijft
            var big = Load(new Size(bw, bh));
            var small = Load(new Size(sw, sh));
            if (ReferenceEquals(big, SystemIcons.Application) || ReferenceEquals(small, SystemIcons.Application)) return;
            Keep.Add(big); Keep.Add(small);
            SendMessage(f.Handle, 0x0080 /* WM_SETICON */, (IntPtr)1 /* ICON_BIG */, big.Handle);
            SendMessage(f.Handle, 0x0080, IntPtr.Zero /* ICON_SMALL */, small.Handle);
        }
        catch (Exception dex) { Diag.Swallow(dex); }
    }

    [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int index, int dpi);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
