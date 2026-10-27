using AccountabiliBuddy.Core.Ports;
using Avalonia.Controls;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Keeps an "in front of everything" card (with its <see cref="CardBackdrop"/>) where the user is:
/// <list type="bullet">
/// <item>Focus: every 250 ms, if another program has the foreground (Alt+Tab, the taskbar, the Win key, a window that activates
/// itself), the card takes the foreground and the keyboard focus back. This app's own windows (an alarm, say) are left alone.</item>
/// <item>Virtual desktops: a window lives on one desktop, so after a switch (Win+Ctrl+arrows, a swipe) the card would be left
/// behind; every 500 ms, when any of the windows is on another desktop they are all moved to the current one.</item>
/// </list>
/// The ways out of the card are unchanged: only its own buttons close it.
/// </summary>
public sealed class CardKeeper : IDisposable
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMilliseconds(250);

    private readonly IVirtualDesktops _desktops;
    private readonly IForegroundFocus _focus;
    private readonly CardBackdrop _backdrop;
    private readonly Window _card;
    private readonly DispatcherTimer _timer;
    private bool _desktopCheckDue;

    public CardKeeper(IVirtualDesktops desktops, IForegroundFocus focus, CardBackdrop backdrop, Window card)
    {
        _desktops = desktops;
        _focus = focus;
        _backdrop = backdrop;
        _card = card;
        _timer = new DispatcherTimer { Interval = CheckInterval };
        _timer.Tick += (_, _) => Check();
        _timer.Start();
    }

    public void Dispose()
    {
        _timer.Stop();
    }

    private void Check()
    {
        _backdrop.RefreshScreens();

        // The desktop check runs on every second tick (500 ms).
        _desktopCheckDue = !_desktopCheckDue;
        if (_desktopCheckDue)
        {
            FollowVirtualDesktop();
        }

        TakeFocusBack();
    }

    private void FollowVirtualDesktop()
    {
        List<nint> handles = _backdrop.Windows
            .Select(window => window.TryGetPlatformHandle()?.Handle ?? 0)
            .Where(handle => handle != 0)
            .ToList();
        if (handles.All(_desktops.IsOnCurrentDesktop))
        {
            return;
        }

        // If the new desktop can't be named yet (nothing else on it), the next check tries again.
        if (_desktops.MoveToCurrentDesktop(handles))
        {
            _backdrop.Raise();
        }
    }

    private void TakeFocusBack()
    {
        if (!_focus.IsForegroundForeign())
        {
            return;
        }

        if (_card.WindowState == WindowState.Minimized)
        {
            _card.WindowState = WindowState.Normal;
        }

        _backdrop.Raise();
        nint handle = _card.TryGetPlatformHandle()?.Handle ?? 0;
        if (handle != 0)
        {
            _focus.TakeForeground(handle);
        }
    }
}
