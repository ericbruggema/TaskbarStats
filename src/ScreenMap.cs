namespace TaskbarStats;

/// <summary>Een beeldscherm zoals het er op een bepaald moment uitzag.</summary>
internal sealed record ScreenInfo(string Name, bool Primary, Rectangle Bounds, Rectangle Work);

/// <summary>
/// Houdt een venster op een zinnige plek als de schermen veranderen (inloggen via VNC/RDP: eerst 1024x768, dan opeens de echte
/// resolutie; monitor erbij of eraf; schaling). Een vaste pixelpositie van het vorige scherm klopt dan niet meer: het widget bleef
/// op de plek van 1024x768 staan en schaalde niet mee. Het venster behoudt nu dezelfde afstand tot dezelfde rand (rechtsonder blijft
/// rechtsonder) en blijft binnen het beeld.
/// </summary>
internal static class ScreenMap
{
    public static List<ScreenInfo> Capture()
        => Screen.AllScreens.Select(s => new ScreenInfo(s.DeviceName, s.Primary, s.Bounds, s.WorkingArea)).ToList();

    /// <summary>Zijn de twee momentopnames gelijk (zelfde schermen, zelfde afmetingen en posities)?</summary>
    public static bool Same(IReadOnlyList<ScreenInfo> a, IReadOnlyList<ScreenInfo> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var x in a)
        {
            var y = b.FirstOrDefault(s => s.Name == x.Name);
            if (y is null || y.Bounds != x.Bounds || y.Work != x.Work) return false;
        }
        return true;
    }

    public static string Describe(IReadOnlyList<ScreenInfo> s)
        => string.Join(", ", s.Select(x => $"{x.Bounds.Width}x{x.Bounds.Height}@{x.Bounds.Left},{x.Bounds.Top}{(x.Primary ? "*" : "")}"));

    /// <summary>
    /// Nieuwe linkerbovenhoek voor <paramref name="win"/> nadat de schermen van <paramref name="before"/> naar <paramref name="after"/> gingen.
    /// <paramref name="workArea"/>: rekenen met het werkgebied (zonder taakbalk, voor het dashboard) in plaats van het hele scherm
    /// (voor het widget, dat juist op de taakbalk staat).
    /// </summary>
    public static Point Remap(Rectangle win, IReadOnlyList<ScreenInfo> before, IReadOnlyList<ScreenInfo> after, bool workArea)
    {
        if (after.Count == 0) return win.Location;
        Rectangle Area(ScreenInfo s) => workArea ? s.Work : s.Bounds;

        // Op welk scherm stond het venster? (midden van het venster; anders het dichtstbijzijnde)
        var center = new Point(win.Left + win.Width / 2, win.Top + win.Height / 2);
        ScreenInfo? old = before.FirstOrDefault(s => Area(s).Contains(center)) ?? before.OrderBy(s => Distance(Area(s), center)).FirstOrDefault();
        // Hetzelfde scherm nu (op naam); is dat weg, dan het hoofdscherm.
        ScreenInfo target = (old is null ? null : after.FirstOrDefault(s => s.Name == old.Name)) ?? after.FirstOrDefault(s => s.Primary) ?? after[0];
        Rectangle a = old is null ? Area(target) : Area(old), b = Area(target);

        int x = win.Left, y = win.Top;
        if (a != b)
        {
            // dezelfde afstand tot de dichtstbijzijnde rand (links of rechts, boven of onder)
            int dl = win.Left - a.Left, dr = a.Right - win.Right;
            x = dl <= dr ? b.Left + dl : b.Right - dr - win.Width;
            int dt = win.Top - a.Top, db = a.Bottom - win.Bottom;
            y = dt <= db ? b.Top + dt : b.Bottom - db - win.Height;
        }
        // altijd binnen het beeld (ook als het scherm niet is veranderd maar het venster er al buiten stond)
        if (!after.Any(s => Area(s).IntersectsWith(new Rectangle(x, y, win.Width, win.Height))) || a != b)
        {
            x = Math.Clamp(x, b.Left, Math.Max(b.Left, b.Right - win.Width));
            y = Math.Clamp(y, b.Top, Math.Max(b.Top, b.Bottom - win.Height));
        }
        return new Point(x, y);
    }

    private static double Distance(Rectangle r, Point p)
    {
        int dx = Math.Max(Math.Max(r.Left - p.X, 0), p.X - r.Right), dy = Math.Max(Math.Max(r.Top - p.Y, 0), p.Y - r.Bottom);
        return Math.Sqrt((double)dx * dx + (double)dy * dy);
    }
}
