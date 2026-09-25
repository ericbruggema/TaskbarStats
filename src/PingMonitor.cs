using System.Net.NetworkInformation;

namespace TaskbarStats;

/// <summary>
/// Meet de ping (ICMP-echo) naar een instelbaar doel op een eigen achtergrondthread en houdt de laatste ~2 minuten bij:
/// laatste, minimum, gemiddelde, maximum, jitter en pakketverlies. Meet alleen als het aan staat.
/// </summary>
public sealed class PingMonitor : IDisposable
{
    private const int Keep = 120;
    private readonly object _lock = new();
    private readonly List<double> _samples = new();   // ms; -1 = geen antwoord
    private readonly ManualResetEventSlim _wake = new(false);
    private Thread? _thread;
    private volatile bool _on, _stop;
    private string _host = "1.1.1.1";
    private string? _error;

    /// <summary>Als niemand kijkt (widget verborgen) minder vaak meten.</summary>
    public volatile bool Idle;

    public string Host { get { lock (_lock) return _host; } }
    public bool Enabled => _on;

    public void Configure(bool on, string? host)
    {
        host = string.IsNullOrWhiteSpace(host) ? "1.1.1.1" : host.Trim();
        lock (_lock)
        {
            if (!host.Equals(_host, StringComparison.OrdinalIgnoreCase)) { _host = host; _samples.Clear(); _error = null; }
        }
        _on = on;
        if (on && _thread is null)
        {
            _thread = new Thread(Loop) { IsBackground = true, Name = "TaskbarStats ping", Priority = ThreadPriority.BelowNormal };
            _thread.Start();
        }
        _wake.Set();
    }

    private void Loop()
    {
        using var ping = new Ping();
        while (!_stop)
        {
            if (_on)
            {
                string host; lock (_lock) host = _host;
                double ms = -1; string? err = null;
                try
                {
                    var r = ping.Send(host, 1500);
                    if (r.Status == IPStatus.Success) ms = Math.Max(0.1, r.RoundtripTime);
                    else err = r.Status.ToString();
                }
                catch (Exception e) { err = e.InnerException?.Message ?? e.Message; }
                lock (_lock)
                {
                    if (host.Equals(_host, StringComparison.OrdinalIgnoreCase))
                    {
                        _samples.Add(ms);
                        if (_samples.Count > Keep) _samples.RemoveAt(0);
                        _error = ms < 0 ? err : null;
                    }
                }
            }
            _wake.Reset();
            _wake.Wait(!_on ? 3000 : Idle ? 10_000 : 2000);
        }
    }

    /// <summary>Samenvatting van de bewaarde metingen (Last = null: nog niets; -1 = geen antwoord).</summary>
    public PingStats Stats()
    {
        lock (_lock)
        {
            var valid = _samples.Where(s => s >= 0).ToList();
            double jitter = 0;
            if (valid.Count > 1)
            {
                double sum = 0;
                for (int i = 1; i < valid.Count; i++) sum += Math.Abs(valid[i] - valid[i - 1]);
                jitter = sum / (valid.Count - 1);
            }
            return new PingStats(
                _host, _samples.Count == 0 ? null : _samples[^1], _samples.Count,
                valid.Count == 0 ? 0 : valid.Min(), valid.Count == 0 ? 0 : valid.Average(), valid.Count == 0 ? 0 : valid.Max(),
                jitter, _samples.Count == 0 ? 0 : 100.0 * (_samples.Count - valid.Count) / _samples.Count, _error);
        }
    }

    /// <summary>Kopie van de metingen (ms, -1 = geen antwoord) voor grafieken.</summary>
    public double[] Samples() { lock (_lock) return _samples.ToArray(); }

    public void Dispose() { _stop = true; _wake.Set(); }
}

public sealed record PingStats(string Host, double? Last, int Count, double Min, double Avg, double Max, double Jitter, double LossPct, string? Error)
{
    public string LastText => Last is null ? "…" : Last < 0 ? "✕" : $"{Last:0} ms";

    public string Details => Count == 0 ? "…" : Last < 0 && Count == 1
        ? Loc.T("no reply")
        : Loc.T("min {0:0} · avg {1:0} · max {2:0} ms  ·  jitter {3:0.#} ms  ·  loss {4:0.#}%  ({5})", Min, Avg, Max, Jitter, LossPct, Count);
}
