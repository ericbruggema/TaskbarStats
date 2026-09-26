namespace TaskbarStats;

// Temperatuurgeschiedenis voor de grafiekstijl in het widget; het tekenen zelf zit in WidgetRenderer.Graph.cs.
// Bron: _history (1 Hz, gevuld in Tick; niet tijdens Cadence.Active); temperatuur heeft eigen ringen.
// Tijdens de spaarstand (_idle) wordt niet gemeten: de reeks sluit dan gewoon aan op wat er al was.
public sealed partial class WidgetForm
{
    private readonly Ring _hCpuT = new(300), _hGpuT = new(300);
    private long _graphAt;

    // Temperatuurgeschiedenis bijhouden, 1x per seconde (aangeroepen vanuit Tick).
    private void SampleGraph()
    {
        long now = Environment.TickCount64;
        if (now - _graphAt < 1000 || Cadence.Active) return;
        _graphAt = now;
        if (_snap.CpuTempC is double c) _hCpuT.Add(c);
        if (_snap.GpuTempC is double t) _hGpuT.Add(t);
    }
}
