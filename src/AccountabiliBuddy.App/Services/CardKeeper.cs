using AccountabiliBuddy.Core.Ports;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Keeps the newest "in front of everything" card (with its <see cref="CardBackdrop"/>) where the user is. Every 250 ms:
/// <list type="number">
/// <item>Virtual desktops: a window lives on one desktop, so after a switch (Win+Ctrl+arrows, a swipe) the cards would be left
/// behind; when any of them is on another desktop they are all moved to the current one. Nothing below happens while they can't
/// be (a desktop with no other program's window on it to read it from), since activating a card on another desktop would switch
/// the user back to it.</item>
/// <item>Focus: if another program has the foreground (Alt+Tab, the taskbar, the Win key, a window that activates itself) or this
/// app's main window does (it is behind the dim), the card takes the foreground and the keyboard focus back. This app's other
/// always-on-top windows (an alarm, say) are left alone. When Windows refuses (a locked session), it waits 2 s before trying again.</item>
/// </list>
/// The ways out of a card are unchanged: only its own buttons close it.
/// </summary>
public sealed class CardKeeper : IDisposable
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMilliseconds(250);
    private const long RefusedBackOffMilliseconds = 2000;

    private readonly IVirtualDesktops _desktops;
    private readonly IForegroundFocus _focus;
    private readonly CardBackdrop _backdrop;
    private readonly DispatcherTimer _timer;
    private long _noFocusBeforeTick;

    public CardKeeper(IVirtualDesktops desktops, IForegroundFocus focus, CardBackdrop backdrop)
    {
        _desktops = desktops;
        _focus = focus;
        _backdrop = backdrop;
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
        _backdrop.Maintain();
        Window? card = _backdrop.Newest;
        if (card is null)
        {
            return;
        }

        if (FollowVirtualDesktop())
        {
            TakeFocusBack(card);
        }
    }

    /// <returns>True when the cards are on the current desktop (already, or moved there now).</returns>
    private bool FollowVirtualDesktop()
    {
        IReadOnlyList<nint> handles = _backdrop.Handles;
        if (AllOnCurrentDesktop(handles))
        {
            return true;
        }

        if (!_desktops.MoveToCurrentDesktop(handles))
        {
            return false; // Tried again at the next tick.
        }

        // A move can succeed and still leave a window behind (the desktop id came from a window that isn't on the one the user is
        // looking at): checked again, and focus waits for the next tick if not.
        if (!AllOnCurrentDesktop(handles))
        {
            return false;
        }

        _backdrop.Raise();
        return true;
    }

    private bool AllOnCurrentDesktop(IReadOnlyList<nint> handles)
    {
        foreach (nint handle in handles)
        {
            if (!_desktops.IsOnCurrentDesktop(handle))
            {
                return false;
            }
        }

        return true;
    }

    private void TakeFocusBack(Window card)
    {
        if (Environment.TickCount64 < _noFocusBeforeTick || !NeedsFocusBack())
        {
            return;
        }

        if (card.WindowState == WindowState.Minimized)
        {
            card.WindowState = WindowState.Normal;
        }

        _backdrop.Raise();
        nint handle = card.TryGetPlatformHandle()?.Handle ?? 0;
        if (handle != 0 && !_focus.TakeForeground(handle))
        {
            _noFocusBeforeTick = Environment.TickCount64 + RefusedBackOffMilliseconds;
        }
    }

    private bool NeedsFocusBack()
    {
        if (_focus.IsForegroundForeign())
        {
            return true;
        }

        // This app's own window in front: fine for the cards and the always-on-top windows, but not for the main window behind the dim.
        IReadOnlyList<Window> windows = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows ?? [];
        Window? active = windows.FirstOrDefault(window => window.IsActive);
        return active is not null && !active.Topmost;
    }
}
