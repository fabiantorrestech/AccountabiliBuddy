namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// Spreads requests so no more than <see cref="PerMinute"/> start in any minute (SYNC1b "Re-read everything" on the relay: the relay
/// allows 120 requests a minute per address, a PC and a phone on one home network share them, and a goal history pass may take up to
/// 40). A sliding window of start times; <see cref="WaitAsync"/> returns once the next request may start.
/// </summary>
public sealed class RelayRequestPacer
{
    /// <summary>The most requests that start in any 60 seconds.</summary>
    public const int PerMinute = 30;

    private readonly Func<DateTimeOffset> _now;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Queue<DateTimeOffset> _starts = new();

    /// <param name="now">The clock (a fake in checks).</param>
    /// <param name="delay">Waits for a span (a fake in checks).</param>
    public RelayRequestPacer(Func<DateTimeOffset> now, Func<TimeSpan, CancellationToken, Task> delay)
    {
        _now = now;
        _delay = delay;
    }

    /// <summary>Requests started so far.</summary>
    public int Started { get; private set; }

    /// <summary>Waits until one more request may start, then counts it.</summary>
    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            DateTimeOffset now = _now();
            while (_starts.Count > 0 && now - _starts.Peek() >= TimeSpan.FromMinutes(1))
            {
                _starts.Dequeue();
            }

            if (_starts.Count < PerMinute)
            {
                _starts.Enqueue(now);
                Started++;
                return;
            }

            await _delay(TimeSpan.FromMinutes(1) - (now - _starts.Peek()) + TimeSpan.FromMilliseconds(50), cancellationToken);
        }
    }
}
