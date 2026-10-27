namespace AccountabiliBuddy.Relay;

/// <summary>
/// A simple per-address limit: at most <see cref="RelayLimits.RequestsPerMinutePerIp"/> requests in each clock minute.
/// Only counts are kept, in memory; addresses are never written to the log.
/// </summary>
internal sealed class RequestRateLimiter
{
    private const int PruneAbove = 10_000;

    private readonly object _gate = new();
    private readonly Dictionary<string, Window> _windows = new(StringComparer.Ordinal);

    public bool Allow(string address, DateTimeOffset now)
    {
        long minute = now.ToUnixTimeSeconds() / 60;
        lock (_gate)
        {
            if (_windows.Count > PruneAbove)
            {
                Prune(minute);
            }

            Window window = _windows.TryGetValue(address, out Window? known) && known.Minute == minute
                ? known
                : new Window(minute);
            window.Count++;
            _windows[address] = window;
            return window.Count <= RelayLimits.RequestsPerMinutePerIp;
        }
    }

    private void Prune(long currentMinute)
    {
        foreach (string address in _windows.Where(w => w.Value.Minute < currentMinute).Select(w => w.Key).ToList())
        {
            _windows.Remove(address);
        }
    }

    private sealed class Window
    {
        public Window(long minute)
        {
            Minute = minute;
        }

        public long Minute { get; }

        public int Count { get; set; }
    }
}
