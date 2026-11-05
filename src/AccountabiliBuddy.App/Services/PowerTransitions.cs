using Avalonia.Threading;
using Microsoft.Win32;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Tells the app when the computer goes to sleep and when it wakes, on the UI thread (Windows raises the power events on its
/// own thread). Both events are posted in the order Windows raised them, so a suspend is always handled before its resume.
/// </summary>
public sealed class PowerTransitions : IDisposable
{
    private bool _disposed;

    public PowerTransitions()
    {
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    /// <summary>The computer is about to sleep.</summary>
    public event EventHandler? Suspending;

    /// <summary>The computer woke up.</summary>
    public event EventHandler? Resumed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        // Send priority: the cards are hidden before anything else queued runs, so nothing stale is left on screen.
        if (e.Mode == PowerModes.Suspend)
        {
            Dispatcher.UIThread.Post(() => Suspending?.Invoke(this, EventArgs.Empty), DispatcherPriority.Send);
        }
        else if (e.Mode == PowerModes.Resume)
        {
            Dispatcher.UIThread.Post(() => Resumed?.Invoke(this, EventArgs.Empty), DispatcherPriority.Send);
        }
    }
}
