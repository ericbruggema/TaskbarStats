namespace TaskbarStats;

// Vastplakken aan het systeemvak: de rechterrand van het widget blijft tegen het vak staan.
public sealed partial class WidgetForm
{
    private void SetStickToTray(bool on)
    {
        _cfg.StickToTray = on;
        if (on) FollowTray(true);
        Persist();
    }

    // Het systeemvak verschuift door pictogrammen, taakbalkgrootte, schaling of schermresolutie: volg het.
    private void FollowTray(bool force = false)
    {
        if (!_cfg.StickToTray || _dragging || _fxOn || !IsHandleCreated) return;
        if (!TaskbarHost.TryGetTrayRect(out _)) return;   // geen systeemvak gevonden (bv. Verkenner herstart): laat staan
        var (x, y) = ComputeDefaultPosition();
        if (!force && Location.X == x && Location.Y == y) return;
        Location = new Point(x, y);
        _cfg.FloatX = x; _cfg.FloatY = y;
        _cfg.Save();   // de positie is veranderd: direct bewaren
    }
}
