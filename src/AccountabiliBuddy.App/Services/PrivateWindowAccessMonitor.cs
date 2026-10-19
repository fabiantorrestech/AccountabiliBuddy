using AccountabiliBuddy.Core.Ports;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Keeps track of watched browsers whose extension isn't allowed in private windows, and asks for a reminder
/// (<see cref="NagRequested"/>) until it is: at launch, as soon as such a browser connects, and every minute once
/// the last reminder's snooze is over. There is no way to turn the reminder off other than allowing it. The
/// extension's own report wins while the browser runs; otherwise the browser's settings files are read.
/// During focus those browsers count as distracting (see <c>BrowserWithoutPrivateAccess</c>). Use from the UI thread.
/// </summary>
public sealed class PrivateWindowAccessMonitor : IDisposable
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan SnoozeLength = TimeSpan.FromMinutes(5);

    private readonly IPrivateWindowAccessReader _reader;
    private readonly IBrowserActivitySource _browsers;
    private readonly WatchedBrowsersService _watched;
    private readonly TimeProvider _time;
    private readonly DispatcherTimer _timer;
    private Dictionary<BrowserKind, PrivateWindowAccess> _statuses = new();
    private DateTimeOffset _snoozedUntil = DateTimeOffset.MinValue;

    public PrivateWindowAccessMonitor(
        IPrivateWindowAccessReader reader, IBrowserActivitySource browsers, WatchedBrowsersService watched, TimeProvider time)
    {
        _reader = reader;
        _browsers = browsers;
        _watched = watched;
        _time = time;
        _timer = new DispatcherTimer { Interval = CheckInterval };
        _timer.Tick += (_, _) => Refresh(forceReminder: false);
    }

    /// <summary>Watched browsers whose extension isn't allowed in private windows. Replaced, never changed in place.</summary>
    public IReadOnlySet<BrowserKind> Missing { get; private set; } = new HashSet<BrowserKind>();

    /// <summary>Raised when <see cref="Missing"/> changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when the reminder should be shown.</summary>
    public event EventHandler? NagRequested;

    public PrivateWindowAccess StatusOf(BrowserKind browser)
    {
        return _statuses.TryGetValue(browser, out PrivateWindowAccess status) ? status : _reader.Read(browser);
    }

    public void Start()
    {
        _browsers.ConnectionsChanged += OnConnectionsChanged;
        _watched.Changed += OnWatchedChanged;
        _timer.Start();
        Refresh(forceReminder: true);
    }

    /// <summary>Checks again now (e.g. after the user says they turned it on).</summary>
    public void RefreshNow()
    {
        Refresh(forceReminder: false);
    }

    /// <summary>The reminder was closed: ask again in a few minutes if it's still needed.</summary>
    public void Snooze()
    {
        _snoozedUntil = _time.GetUtcNow() + SnoozeLength;
    }

    public void Dispose()
    {
        _timer.Stop();
        _browsers.ConnectionsChanged -= OnConnectionsChanged;
        _watched.Changed -= OnWatchedChanged;
    }

    private void OnConnectionsChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() => Refresh(forceReminder: false));
    }

    private void OnWatchedChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() => Refresh(forceReminder: false));
    }

    /// <param name="forceReminder">Remind even while snoozed (at launch).</param>
    private void Refresh(bool forceReminder)
    {
        IReadOnlyList<BrowserConnection> connections = _browsers.Connections;
        var statuses = new Dictionary<BrowserKind, PrivateWindowAccess>();
        foreach (BrowserKind browser in _watched.Watched)
        {
            BrowserConnection? live = connections.FirstOrDefault(connection => connection.Browser == browser);
            statuses[browser] = live?.PrivateWindowsAllowed switch
            {
                true => PrivateWindowAccess.Allowed,
                false => PrivateWindowAccess.NotAllowed,
                null => _reader.Read(browser),
            };
        }

        _statuses = statuses;
        HashSet<BrowserKind> missing = statuses.Where(pair => pair.Value == PrivateWindowAccess.NotAllowed).Select(pair => pair.Key).ToHashSet();
        bool newlyMissing = missing.Except(Missing).Any();
        if (!missing.SetEquals(Missing))
        {
            Missing = missing;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        // A browser that just turned up without access is reminded at once, snooze or not.
        if (missing.Count > 0 && (forceReminder || newlyMissing || _time.GetUtcNow() >= _snoozedUntil))
        {
            NagRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
