using AccountabiliBuddy.Core.Ports;
using Avalonia.Controls;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Shows the "in front of everything" cards (the work-hours gate, Pre-Work, the start nudge, the session cards): while any is
/// up, every monitor is dimmed behind it, nothing behind can be clicked, and the card follows the user across virtual desktops
/// and takes the focus back (<see cref="CardBackdrop"/>, <see cref="CardKeeper"/>). There is one dim and one keeper however many
/// cards are up: the newest card is the one kept in front, older ones stay above the dim under it. The dim goes with the last card.
/// A card's own ways out (its buttons) are not touched.
/// While the computer sleeps the cards and dims are hidden (<see cref="Suspend"/>), never closed, so no service sees a close; cards
/// asked for meanwhile wait, and <see cref="Resume"/> shows the ones still registered.
/// </summary>
public sealed class FrontCardHost
{
    private readonly IVirtualDesktops _desktops;
    private readonly IForegroundFocus _focus;
    private readonly IOverlayWindowControl _windowControl;
    private readonly List<Window> _cards = new();
    private CardBackdrop? _backdrop;
    private CardKeeper? _keeper;
    private bool _suspended;
    private int _suspendCount;

    public FrontCardHost(IVirtualDesktops desktops, IForegroundFocus focus, IOverlayWindowControl windowControl)
    {
        _desktops = desktops;
        _focus = focus;
        _windowControl = windowControl;
    }

    /// <summary>Shows the card in front of the dim (replaces <c>Show()</c> + <c>Activate()</c>); the dim goes when it closes.</summary>
    public void Show(Window card)
    {
        if (!_cards.Contains(card))
        {
            _cards.Add(card);
            card.Closed += OnCardClosed;
        }

        if (_suspended)
        {
            return; // Registered, shown by Resume if it is still wanted then.
        }

        Present(card);
    }

    /// <summary>
    /// The computer is going to sleep: hides every card and the dims so none is on screen when it wakes. Nothing is closed or
    /// answered, and cards asked for until <see cref="Resume"/> are only registered.
    /// </summary>
    public void Suspend()
    {
        _suspendCount++; // Also when already suspended: a wake whose Resume is still queued is then stale.
        if (_suspended)
        {
            return;
        }

        _suspended = true;
        foreach (Window card in _cards)
        {
            card.Hide();
        }

        _keeper?.Dispose();
        _keeper = null;
        _backdrop?.Dispose(); // Closes the dims; the cards stay, hidden.
        _backdrop = null;
    }

    /// <summary>Counts the suspends; a wake reads it before re-checking and hands it to <see cref="Resume"/>.</summary>
    public int SuspendCount => _suspendCount;

    /// <summary>
    /// The computer woke up: shows the cards still registered (a service that has since closed its card already removed it) with
    /// a fresh dim, each centred again on the monitor under the cursor (the one it was on may have been unplugged meanwhile).
    /// The caller re-checks the services first, so only cards still due are left. <paramref name="suspendCount"/> is
    /// <see cref="SuspendCount"/> as it was at the wake: if the computer went to sleep again since, this Resume is stale and does nothing.
    /// </summary>
    public void Resume(IMonitorLocator monitors, int suspendCount)
    {
        if (!_suspended || suspendCount != _suspendCount)
        {
            return;
        }

        _suspended = false;
        MonitorArea? area = monitors.Find(monitors.FindMonitorUnderCursor());
        foreach (Window card in _cards.ToList())
        {
            // Showing one card can close another, and a closed window can't be shown again.
            if (!_cards.Contains(card) || card.TryGetPlatformHandle() is null)
            {
                continue;
            }

            Present(card);
            if (area is MonitorArea target)
            {
                // After Show: the handlers MonitorPlacement.Center added move the card to its old monitor when it opens.
                MonitorPlacement.Recenter(card, target);
            }
        }
    }

    private void Present(Window card)
    {
        _backdrop ??= new CardBackdrop(_windowControl);
        _backdrop.SetCards(_cards);
        _backdrop.Maintain(); // The dim first, so the card opens on top of it.
        card.Show();
        _backdrop.SetCards(_cards); // The card has a native window now: its handle is read again.
        _backdrop.Maintain(); // If the screens weren't known before the card existed on screen.
        _backdrop.Raise();
        card.Activate();
        _keeper ??= new CardKeeper(_desktops, _focus, _backdrop);
    }

    /// <summary>
    /// Asks again for a card that is already up: puts the windows back in order (the dim never above a card, a newer card or an
    /// alarm kept in front of it) and gives the newest card the focus, but only while it is on the desktop the user is looking at,
    /// since activating it on another one would switch the user back.
    /// </summary>
    public void Raise(Window card)
    {
        if (_suspended || _backdrop is null || !_cards.Contains(card))
        {
            return;
        }

        _backdrop.Raise();
        nint handle = card.TryGetPlatformHandle()?.Handle ?? 0;
        if (ReferenceEquals(card, _backdrop.Newest) && handle != 0 && _desktops.IsOnCurrentDesktop(handle))
        {
            card.Activate();
        }
    }

    private void OnCardClosed(object? sender, EventArgs e)
    {
        if (sender is not Window card)
        {
            return;
        }

        card.Closed -= OnCardClosed;
        _cards.Remove(card);
        _backdrop?.SetCards(_cards);

        // One card often replaces another in the same moment (Pick tasks, then Ready?): the dim only goes if no card is up afterwards.
        Dispatcher.UIThread.Post(ReleaseIfEmpty, DispatcherPriority.Background);
    }

    private void ReleaseIfEmpty()
    {
        if (_cards.Count > 0 || _suspended)
        {
            return;
        }

        _keeper?.Dispose();
        _keeper = null;
        _backdrop?.Dispose();
        _backdrop = null;
    }
}
