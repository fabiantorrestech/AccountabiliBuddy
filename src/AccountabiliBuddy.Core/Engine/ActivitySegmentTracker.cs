using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// Turns a stream of per-monitor observations into <see cref="ActivitySegment"/>s for the log.
/// A segment stays open while a monitor keeps showing the same thing (same app, title, address, classification
/// and session context) and is closed when that changes, the monitor goes away, or the tracker is paused
/// (e.g. the user is idle). Empty desktops are not logged, and blips shorter than <see cref="MinimumDuration"/>
/// (alt-tabbing through windows) are dropped.
/// Not thread-safe: call from a single thread.
/// </summary>
public sealed class ActivitySegmentTracker
{
    public static TimeSpan MinimumDuration { get; } = TimeSpan.FromSeconds(2);

    private readonly Dictionary<string, OpenSegment> _openByMonitor = new(StringComparer.Ordinal);

    /// <summary>Feeds the latest observation of every connected monitor.</summary>
    /// <returns>Segments that just closed.</returns>
    public IReadOnlyList<ActivitySegment> Observe(IEnumerable<ObservedActivity> observations, DateTimeOffset now)
    {
        var closed = new List<ActivitySegment>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (ObservedActivity observation in observations)
        {
            string monitorId = observation.Snapshot.MonitorId;
            seen.Add(monitorId);

            if (_openByMonitor.TryGetValue(monitorId, out OpenSegment? open))
            {
                if (open.Matches(observation))
                {
                    open.Update(observation);
                    continue;
                }

                Close(monitorId, open, now, closed);
            }

            if (!observation.Snapshot.IsEmptyDesktop)
            {
                _openByMonitor[monitorId] = new OpenSegment(observation, now);
            }
        }

        // Monitors that disappeared from the observations were disconnected.
        foreach (string monitorId in _openByMonitor.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            Close(monitorId, _openByMonitor[monitorId], now, closed);
        }

        return closed;
    }

    /// <summary>Closes every open segment, e.g. when the user goes idle, logging is turned off, or the app exits.</summary>
    public IReadOnlyList<ActivitySegment> CloseAll(DateTimeOffset now)
    {
        var closed = new List<ActivitySegment>();
        foreach (string monitorId in _openByMonitor.Keys.ToList())
        {
            Close(monitorId, _openByMonitor[monitorId], now, closed);
        }

        return closed;
    }

    private void Close(string monitorId, OpenSegment open, DateTimeOffset now, List<ActivitySegment> closed)
    {
        _openByMonitor.Remove(monitorId);
        if (now - open.Start >= MinimumDuration)
        {
            closed.Add(open.ToSegment(now));
        }
    }

    private sealed class OpenSegment(ObservedActivity first, DateTimeOffset start)
    {
        private ObservedActivity _latest = first;

        public DateTimeOffset Start { get; } = start;

        public bool Matches(ObservedActivity other)
        {
            ActivitySnapshot a = _latest.Snapshot;
            ActivitySnapshot b = other.Snapshot;
            return a.WindowId == b.WindowId
                && a.ProcessName == b.ProcessName
                && a.WindowTitle == b.WindowTitle
                && a.Url == b.Url
                && _latest.Classification == other.Classification
                && _latest.ProfileId == other.ProfileId
                && _latest.InFocusSession == other.InFocusSession;
        }

        public void Update(ObservedActivity observation)
        {
            // Screen text can arrive a little after the segment opened; keep the newest.
            if (observation.Snapshot.OcrText is not null)
            {
                _latest = observation;
            }
        }

        public ActivitySegment ToSegment(DateTimeOffset end)
        {
            ActivitySnapshot snapshot = _latest.Snapshot;
            return new ActivitySegment(
                Start,
                end,
                snapshot.MonitorId,
                snapshot.ProcessName,
                snapshot.WindowTitle,
                snapshot.Url,
                _latest.Classification,
                _latest.ProfileId,
                _latest.InFocusSession,
                snapshot.OcrText);
        }
    }
}
