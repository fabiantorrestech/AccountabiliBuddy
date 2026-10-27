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
/// </summary>
public sealed class FrontCardHost
{
    private readonly IVirtualDesktops _desktops;
    private readonly IForegroundFocus _focus;
    private readonly IOverlayWindowControl _windowControl;
    private readonly List<Window> _cards = new();
    private CardBackdrop? _backdrop;
    private CardKeeper? _keeper;

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
        if (_backdrop is null || !_cards.Contains(card))
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
        if (_cards.Count > 0)
        {
            return;
        }

        _keeper?.Dispose();
        _keeper = null;
        _backdrop?.Dispose();
        _backdrop = null;
    }
}
