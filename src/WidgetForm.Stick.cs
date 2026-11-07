namespace TaskbarStats;

// Vastplakken aan het systeemvak: de rechterrand van het widget blijft tegen het vak staan.
public sealed partial class WidgetForm
{
    private void SetStickToTray(bool on)
    {
        _cfg.StickToTray = on;
        Diag.Info("position", "Vastplakken aan het systeemvak " + (on ? "aan" : "uit") + " (gekozen)");
        if (on) FollowTray(true);
        Persist();
    }

    // Het systeemvak verschuift door pictogrammen, taakbalkgrootte, schaling of schermresolutie: volg het.
    private int _followLogged;   // per sessie maar de eerste tien verplaatsingen loggen (het systeemvak groeit na het inloggen nog even)

    private void FollowTray(bool force = false)
    {
        if (!_cfg.StickToTray || _dragging || _fxOn || !IsHandleCreated) return;
        if (!TaskbarHost.TryGetTrayRect(out var tray)) return;   // geen systeemvak gevonden (bv. Verkenner herstart): laat staan
        var (x, y) = ComputeDefaultPosition();
        if (!force && Location.X == x && Location.Y == y) return;
        if (_followLogged < 10) { _followLogged++; Diag.Info("position", $"Widget naar het systeemvak verplaatst: {Location.X},{Location.Y} -> {x},{y} (systeemvak {tray.Left},{tray.Top} {tray.Width}x{tray.Height})"); }
        Location = new Point(x, y);
        _cfg.FloatX = x; _cfg.FloatY = y;
        _cfg.Save();   // de positie is veranderd: direct bewaren
    }
}
