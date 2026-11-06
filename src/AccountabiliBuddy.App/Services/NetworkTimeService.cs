using AccountabiliBuddy.Core.Engine;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Anchors the <see cref="TrustedClock"/> to network time (ADR 70): asks the time servers through <see cref="CalendarNetwork"/> at
/// start, every 6 hours and when the PC wakes, and takes their time as the truth when it differs from the trusted time by more than
/// the clock's jump tolerance. With no answer, the trusted clock works as before. Use from the UI thread only.
/// </summary>
public sealed class NetworkTimeService : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private readonly TrustedClock _clock;
    private readonly CalendarNetwork _network;
    private readonly DispatcherTimer _timer;
    private bool _asking;

    public NetworkTimeService(TrustedClock clock, CalendarNetwork network)
    {
        _clock = clock;
        _network = network;
        _timer = new DispatcherTimer { Interval = Interval };
        _timer.Tick += (_, _) => _ = CheckAsync();
    }

    /// <summary>The last answer: network time minus the trusted time when it was asked; null before any answer.</summary>
    public TimeSpan? LastOffset { get; private set; }

    /// <summary>When a server last answered; null before any answer.</summary>
    public DateTimeOffset? LastAnsweredAt { get; private set; }

    public void Start()
    {
        _timer.Start();
        _ = CheckAsync();
    }

    /// <summary>Asks now (start, the 6-hour timer, waking from sleep); a check already running is not doubled.</summary>
    public async Task CheckAsync()
    {
        if (_asking)
        {
            return;
        }

        _asking = true;
        try
        {
            TimeSpan? offset = await _network.QueryNetworkTimeOffsetAsync(_clock.GetUtcNow, CancellationToken.None);
            if (offset is TimeSpan found)
            {
                LastOffset = found;
                LastAnsweredAt = _clock.GetUtcNow() + found;
                _clock.CorrectTo(_clock.GetUtcNow() + found);
            }
        }
        finally
        {
            _asking = false;
        }
    }

    public void Dispose() => _timer.Stop();
}
