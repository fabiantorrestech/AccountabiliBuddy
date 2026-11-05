using Avalonia.Threading;
using Microsoft.Win32;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Tells the app when the computer goes to sleep and when it wakes, on the UI thread (Windows raises the power events on its
/// own thread). Both events are posted in the order Windows raised them, so a suspend is always handled before its resume.
/// If a suspend is announced but the wake never is (a failed sleep, a missed event), the session ticks give it away: once they
/// have arrived with normal gaps for <see cref="NoSleepAfter"/>, no sleep happened and <see cref="Resumed"/> is raised anyway.
/// </summary>
public sealed class PowerTransitions : IDisposable
{
    /// <summary>How long the ticks must keep their normal rhythm after a suspend before it counts as a sleep that never was.</summary>
    public static readonly TimeSpan NoSleepAfter = TimeSpan.FromSeconds(30);

    // A tick later than this after the previous one means the computer was not running in between.
    private static readonly TimeSpan NormalTickGap = TimeSpan.FromSeconds(2.5);

    private readonly SessionController _sessions;
    private bool _suspended;
    private int _suspendCount;
    private bool _fallbackPosted;
    private DateTimeOffset? _lastTickAt;
    private TimeSpan _steadyFor;
    private bool _disposed;

    public PowerTransitions(SessionController sessions)
    {
        _sessions = sessions;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        _sessions.Ticked += OnTicked;
    }

    /// <summary>The computer is about to sleep.</summary>
    public event EventHandler? Suspending;

    /// <summary>The computer woke up (or it was announced that it would sleep and the ticks show it never did).</summary>
    public event EventHandler? Resumed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _sessions.Ticked -= OnTicked;
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        // Send priority: the cards are hidden before anything else queued runs, so nothing stale is left on screen.
        if (e.Mode == PowerModes.Suspend)
        {
            Dispatcher.UIThread.Post(RaiseSuspending, DispatcherPriority.Send);
        }
        else if (e.Mode == PowerModes.Resume)
        {
            Dispatcher.UIThread.Post(RaiseResumed, DispatcherPriority.Send);
        }
    }

    private void RaiseSuspending()
    {
        _suspended = true;
        _suspendCount++;
        _lastTickAt = null;
        _steadyFor = TimeSpan.Zero;
        Suspending?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseResumed()
    {
        _suspended = false;
        Resumed?.Invoke(this, EventArgs.Empty);
    }

    private void OnTicked(object? sender, EventArgs e)
    {
        if (!_suspended)
        {
            return;
        }

        DateTimeOffset now = _sessions.Now;
        if (_lastTickAt is DateTimeOffset last && now - last >= TimeSpan.Zero && now - last <= NormalTickGap)
        {
            _steadyFor += now - last;
        }
        else
        {
            _steadyFor = TimeSpan.Zero; // The first tick since the suspend, or a gap: the computer did sleep, the wake is coming.
        }

        _lastTickAt = now;
        if (_steadyFor >= NoSleepAfter && !_fallbackPosted)
        {
            // Posted, not raised here: Resumed ticks the sessions again, which must not happen inside this tick.
            _fallbackPosted = true;
            int suspendCount = _suspendCount;
            Dispatcher.UIThread.Post(() => RaiseFallbackResumed(suspendCount), DispatcherPriority.Background);
        }
    }

    private void RaiseFallbackResumed(int suspendCount)
    {
        _fallbackPosted = false;

        // The real wake came first, or another sleep was announced since: that one decides.
        if (!_suspended || suspendCount != _suspendCount || _disposed)
        {
            return;
        }

        RaiseResumed();
    }
}
