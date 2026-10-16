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
/// <see cref="ActivityChanged"/> is raised on the watcher thread; UI code must marshal to its own thread.
/// </summary>
public sealed class WinEventActivityWatcher : IActivityWatcher, IDisposable
{
    private const int ObjectIdWindow = 0; // OBJID_WINDOW
    private static readonly TimeSpan RecheckInterval = TimeSpan.FromSeconds(5);

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

    private Thread? _thread;
    private uint _threadId;

    public WinEventActivityWatcher(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        _winEventCallback = OnWinEvent;
    }

    public event EventHandler<ActivitySnapshot>? ActivityChanged;

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

        Rescan();

        // GetMessage returns 0 on WM_QUIT and -1 on error.
        while (PInvoke.GetMessage(out MSG message, HWND.Null, 0, 0) > 0)
        {
            if (message.message == PInvoke.WM_TIMER)
            {
                Rescan();
            }

            PInvoke.TranslateMessage(message);
            PInvoke.DispatchMessage(message);
        }

        PInvoke.KillTimer(HWND.Null, timerId);
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

        lock (_scanLock)
        {
            IReadOnlyList<ActivitySnapshot> snapshots = _scanner.Scan(_timeProvider.GetUtcNow());

            _currentTopWindowIds.Clear();
            var connectedMonitors = new HashSet<string>(StringComparer.Ordinal);

            foreach (ActivitySnapshot snapshot in snapshots)
            {
                connectedMonitors.Add(snapshot.MonitorId);
                if (!snapshot.IsEmptyDesktop)
                {
                    _currentTopWindowIds.Add(snapshot.WindowId);
                }

                if (!_lastSnapshotByMonitor.TryGetValue(snapshot.MonitorId, out ActivitySnapshot? previous)
                    || !ShowsSameThing(previous, snapshot))
                {
                    _lastSnapshotByMonitor[snapshot.MonitorId] = snapshot;
                    changed.Add(snapshot);
                }
            }

            // Forget monitors that were disconnected.
            foreach (string monitorId in _lastSnapshotByMonitor.Keys.Where(id => !connectedMonitors.Contains(id)).ToList())
            {
                _lastSnapshotByMonitor.Remove(monitorId);
            }
        }

        // Raise outside the lock so handlers can call CaptureCurrent safely.
        foreach (ActivitySnapshot snapshot in changed)
        {
            ActivityChanged?.Invoke(this, snapshot);
        }
    }

    private static bool ShowsSameThing(ActivitySnapshot previous, ActivitySnapshot current)
    {
        return previous.WindowId == current.WindowId
            && previous.ProcessName == current.ProcessName
            && previous.WindowTitle == current.WindowTitle;
    }
}
