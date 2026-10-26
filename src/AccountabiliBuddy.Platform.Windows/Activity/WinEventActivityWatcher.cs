using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Ports;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.WindowsAndMessaging;

namespace AccountabiliBuddy.Platform.Windows.Activity;

/// <summary>
/// Watches every monitor using Windows accessibility events (no polling of window contents).
/// Runs its own background thread with a message loop, which out-of-context WinEvent hooks require.
/// Rescans on focus/minimize/move/show/hide/title changes, plus every few seconds as a safety net,
/// and raises <see cref="ActivityChanged"/> only for monitors whose top window or title changed.
/// A new top window is reported once it has stayed on top for <see cref="SettleTime"/>, and nothing is scanned while
/// Alt+Tab or Task view is in front (for up to <see cref="MaxSwitcherHold"/>), so the windows a switch passes over are
/// never judged.
/// <see cref="ActivityChanged"/> is raised on the watcher thread; UI code must marshal to its own thread.
/// </summary>
public sealed class WinEventActivityWatcher : IActivityWatcher, IDisposable
{
    private const int ObjectIdWindow = 0; // OBJID_WINDOW
    private static readonly TimeSpan RecheckInterval = TimeSpan.FromSeconds(5);

    // A new top window is reported only once it has stayed on top this long, so a window that flickers in front
    // during a switch is never judged.
    private static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(400);
    private const uint MinimumTimerMilliseconds = 10; // USER_TIMER_MINIMUM

    // The longest Alt+Tab or Task view in front holds scans back; after that the windows are scanned anyway.
    private static readonly TimeSpan MaxSwitcherHold = TimeSpan.FromSeconds(10);

    // Events that always warrant a rescan.
    private static readonly uint[] LayoutEvents =
    {
        PInvoke.EVENT_SYSTEM_FOREGROUND,
        PInvoke.EVENT_SYSTEM_MOVESIZEEND,
        PInvoke.EVENT_SYSTEM_MINIMIZESTART,
        PInvoke.EVENT_SYSTEM_MINIMIZEEND,
    };

    // Very frequent events; only relevant when they concern a top-level window.
    private static readonly uint[] WindowEvents =
    {
        PInvoke.EVENT_OBJECT_DESTROY,
        PInvoke.EVENT_OBJECT_SHOW,
        PInvoke.EVENT_OBJECT_HIDE,
        PInvoke.EVENT_OBJECT_NAMECHANGE,
        PInvoke.EVENT_OBJECT_CLOAKED,
        PInvoke.EVENT_OBJECT_UNCLOAKED,
    };

    private readonly TimeProvider _timeProvider;
    private readonly DesktopScanner _scanner = new();
    private readonly object _scanLock = new();

    // Kept in a field so the GC does not collect the delegate while native code holds it.
    private readonly WINEVENTPROC _winEventCallback;

    private readonly Dictionary<string, ActivitySnapshot> _lastSnapshotByMonitor = new(StringComparer.Ordinal);
    private readonly HashSet<string> _currentTopWindowIds = new(StringComparer.Ordinal);

    // Per monitor: a new top window waiting out SettleTime before it is reported.
    private readonly Dictionary<string, PendingTopWindow> _pendingByMonitor = new(StringComparer.Ordinal);

    // The one-shot settle timer (0 = not running); only touched on the watcher thread.
    private nuint _settleTimerId;

    // When Alt+Tab or Task view came to the front (Environment.TickCount64), null while it isn't; under _scanLock.
    private long? _switcherSince;

    private Thread? _thread;
    private uint _threadId;

    public WinEventActivityWatcher(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        _winEventCallback = OnWinEvent;
    }

    public event EventHandler<ActivitySnapshot>? ActivityChanged;

    public event EventHandler<string>? MonitorDisconnected;

    public void Start()
    {
        if (_thread is not null)
        {
            return;
        }

        using var hooksInstalled = new ManualResetEventSlim();
        _thread = new Thread(() => RunMessageLoop(hooksInstalled))
        {
            IsBackground = true,
            Name = "AccountabiliBuddy activity watcher",
        };
        _thread.Start();
        hooksInstalled.Wait();
    }

    public void Stop()
    {
        if (_thread is null)
        {
            return;
        }

        PInvoke.PostThreadMessage(_threadId, PInvoke.WM_QUIT, default, default);
        _thread.Join();
        _thread = null;
    }

    public IReadOnlyList<ActivitySnapshot> CaptureCurrent()
    {
        lock (_scanLock)
        {
            return _scanner.Scan(_timeProvider.GetUtcNow());
        }
    }

    public void Dispose()
    {
        Stop();
    }

    private void RunMessageLoop(ManualResetEventSlim hooksInstalled)
    {
        _threadId = PInvoke.GetCurrentThreadId();
        List<HWINEVENTHOOK> hooks = InstallHooks();
        // A timer without a window posts WM_TIMER to this thread's queue.
        nuint timerId = PInvoke.SetTimer(HWND.Null, 0, (uint)RecheckInterval.TotalMilliseconds, null);
        hooksInstalled.Set();

#if DEBUG
        _diagTrigger = "start"; // DIAG (Task 4, remove)
#endif
        Rescan();

        // GetMessage returns 0 on WM_QUIT and -1 on error.
        while (PInvoke.GetMessage(out MSG message, HWND.Null, 0, 0) > 0)
        {
            if (message.message == PInvoke.WM_TIMER)
            {
                bool isSettleTimer = _settleTimerId != 0 && message.wParam.Value == _settleTimerId;
                if (isSettleTimer)
                {
                    CancelSettleTimer(); // one-shot
                }
#if DEBUG
                _diagTrigger = isSettleTimer ? "settle" : "timer"; // DIAG (Task 4, remove)
#endif
                Rescan();
            }

            PInvoke.TranslateMessage(message);
            PInvoke.DispatchMessage(message);
        }

        PInvoke.KillTimer(HWND.Null, timerId);
        CancelSettleTimer();
        foreach (HWINEVENTHOOK hook in hooks)
        {
            PInvoke.UnhookWinEvent(hook);
        }
    }

    private List<HWINEVENTHOOK> InstallHooks()
    {
        var hooks = new List<HWINEVENTHOOK>();
        uint flags = PInvoke.WINEVENT_OUTOFCONTEXT | PInvoke.WINEVENT_SKIPOWNPROCESS;

        foreach (uint eventId in LayoutEvents.Concat(WindowEvents))
        {
            HWINEVENTHOOK hook = PInvoke.SetWinEventHook(eventId, eventId, HMODULE.Null, _winEventCallback, 0, 0, flags);
            if (!hook.IsNull)
            {
                hooks.Add(hook);
            }
        }

        return hooks;
    }

    private void OnWinEvent(
        HWINEVENTHOOK hook, uint eventId, HWND window, int objectId, int childId, uint eventThread, uint eventTime)
    {
        // Ignore events about controls inside windows (buttons, text fields, ...).
        if (objectId != ObjectIdWindow || childId != PInvoke.CHILDID_SELF || window.IsNull)
        {
            return;
        }

        if (LayoutEvents.Contains(eventId) || IsRelevantWindowEvent(eventId, window))
        {
#if DEBUG
            _diagTrigger = $"0x{eventId:X4}"; // DIAG (Task 4, remove)
#endif
            Rescan();
        }
    }

    private bool IsRelevantWindowEvent(uint eventId, HWND window)
    {
        if (eventId == PInvoke.EVENT_OBJECT_SHOW || eventId == PInvoke.EVENT_OBJECT_UNCLOAKED)
        {
            // A newly shown top-level window may now be in front.
            return PInvoke.GetAncestor(window, GET_ANCESTOR_FLAGS.GA_ROOT) == window;
        }

        // Title change, hide, destroy, cloak: only matters for a window currently on top of a monitor.
        lock (_scanLock)
        {
            return _currentTopWindowIds.Contains(WindowIds.Format(window));
        }
    }

    private void Rescan()
    {
        var changed = new List<ActivitySnapshot>();
        var disconnected = new List<string>();

        lock (_scanLock)
        {
            // While Alt+Tab or Task view is in front the user is passing over windows: keep the last snapshots and
            // start the settle wait over once a window is picked (that change of foreground rescans). Only for
            // MaxSwitcherHold, though: a switcher left open (or stuck) doesn't hide what's behind it for longer.
            bool switcherInFront = _scanner.IsSwitcherInFront();
            long switcherNow = Environment.TickCount64;
            if (!switcherInFront)
            {
                _switcherSince = null;
            }
            else
            {
                _switcherSince ??= switcherNow;
            }

            if (switcherInFront && switcherNow - _switcherSince < (long)MaxSwitcherHold.TotalMilliseconds)
            {
                _pendingByMonitor.Clear();
                CancelSettleTimer();
#if DEBUG
                DiagWrite(_scanner.LastDiagScan); // DIAG (Task 4, remove)
#endif
                return;
            }

            IReadOnlyList<ActivitySnapshot> snapshots = _scanner.Scan(_timeProvider.GetUtcNow());
#if DEBUG
            var diagOutcomes = new List<string>(snapshots.Count); // DIAG (Task 4, remove)
#endif

            _currentTopWindowIds.Clear();
            var connectedMonitors = new HashSet<string>(StringComparer.Ordinal);
            long now = Environment.TickCount64;
            long? nextSettleIn = null;

            foreach (ActivitySnapshot snapshot in snapshots)
            {
                connectedMonitors.Add(snapshot.MonitorId);
                if (!snapshot.IsEmptyDesktop)
                {
                    _currentTopWindowIds.Add(snapshot.WindowId);
                }

                long? waitLeft = SettleWaitLeft(snapshot, now);
                if (waitLeft is long left)
                {
                    nextSettleIn = Math.Min(nextSettleIn ?? left, left);
                }
                else if (!_lastSnapshotByMonitor.TryGetValue(snapshot.MonitorId, out ActivitySnapshot? previous)
                    || !ShowsSameThing(previous, snapshot))
                {
                    _lastSnapshotByMonitor[snapshot.MonitorId] = snapshot;
                    changed.Add(snapshot);
                }
#if DEBUG
                // DIAG (Task 4, remove)
                diagOutcomes.Add(waitLeft is long l ? $"{snapshot.MonitorId}:pending {l}ms"
                    : changed.Contains(snapshot) ? $"{snapshot.MonitorId}:reported" : $"{snapshot.MonitorId}:same");
#endif
            }

            if (nextSettleIn is long settleIn)
            {
                StartSettleTimer(settleIn);
            }
            else
            {
                CancelSettleTimer();
            }

#if DEBUG
            DiagWrite($"{_scanner.LastDiagScan} | {string.Join(", ", diagOutcomes)}"); // DIAG (Task 4, remove)
#endif

            // Forget monitors that were disconnected.
            foreach (string monitorId in _lastSnapshotByMonitor.Keys.Where(id => !connectedMonitors.Contains(id)).ToList())
            {
                _lastSnapshotByMonitor.Remove(monitorId);
                disconnected.Add(monitorId);
            }

            foreach (string monitorId in _pendingByMonitor.Keys.Where(id => !connectedMonitors.Contains(id)).ToList())
            {
                _pendingByMonitor.Remove(monitorId);
            }
        }

        // Raise outside the lock so handlers can call CaptureCurrent safely.
        foreach (string monitorId in disconnected)
        {
            MonitorDisconnected?.Invoke(this, monitorId);
        }

        foreach (ActivitySnapshot snapshot in changed)
        {
            ActivityChanged?.Invoke(this, snapshot);
        }
    }

#if DEBUG
    // DIAG (Task 4, remove): one line per Rescan, no titles.
    private static readonly string DiagLogPath = Path.Combine(
        Environment.GetEnvironmentVariable("ACCOUNTABILIBUDDY_DATA_DIR")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AccountabiliBuddy-Dev"),
        "alt-tab-diag.log");

    // DIAG (Task 4, remove): what triggered the next Rescan (WinEvent id in hex, "timer" or "start").
    private string _diagTrigger = "?";

    // DIAG (Task 4, remove)
    private void DiagWrite(string? scan)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DiagLogPath)!);
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} trigger={_diagTrigger} {scan}{Environment.NewLine}";
            File.AppendAllText(DiagLogPath, line);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        _diagTrigger = "?";
    }
#endif

    /// <summary>
    /// How long a monitor's new top window still has to stay on top before it is reported, or null when it can be
    /// reported now (the monitor's first scan, the same window as reported last, or a new one that has settled).
    /// A window that flickers in front for a moment during a switch is replaced before its wait ends.
    /// </summary>
    private long? SettleWaitLeft(ActivitySnapshot snapshot, long now)
    {
        if (!_lastSnapshotByMonitor.TryGetValue(snapshot.MonitorId, out ActivitySnapshot? reported)
            || reported.WindowId == snapshot.WindowId)
        {
            _pendingByMonitor.Remove(snapshot.MonitorId);
            return null;
        }

        if (!_pendingByMonitor.TryGetValue(snapshot.MonitorId, out PendingTopWindow? pending)
            || pending.WindowId != snapshot.WindowId)
        {
            pending = new PendingTopWindow(snapshot.WindowId, now);
            _pendingByMonitor[snapshot.MonitorId] = pending;
        }

        long waited = now - pending.FirstSeenAt;
        if (waited >= (long)SettleTime.TotalMilliseconds)
        {
            _pendingByMonitor.Remove(snapshot.MonitorId);
            return null;
        }

        return (long)SettleTime.TotalMilliseconds - waited;
    }

    /// <summary>One-shot: (re)starts the settle timer, which rescans when it fires. Watcher thread only.</summary>
    private void StartSettleTimer(long milliseconds)
    {
        CancelSettleTimer();
        uint delay = (uint)Math.Max(MinimumTimerMilliseconds, milliseconds);
        _settleTimerId = PInvoke.SetTimer(HWND.Null, 0, delay, null);
    }

    private void CancelSettleTimer()
    {
        if (_settleTimerId != 0)
        {
            PInvoke.KillTimer(HWND.Null, _settleTimerId);
            _settleTimerId = 0;
        }
    }

    private static bool ShowsSameThing(ActivitySnapshot previous, ActivitySnapshot current)
    {
        return previous.WindowId == current.WindowId
            && previous.ProcessName == current.ProcessName
            && previous.WindowTitle == current.WindowTitle;
    }

    /// <param name="FirstSeenAt">Environment.TickCount64 when the window was first seen on top (immune to clock changes).</param>
    private sealed record PendingTopWindow(string WindowId, long FirstSeenAt);
}
