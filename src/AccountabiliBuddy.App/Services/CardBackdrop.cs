using AccountabiliBuddy.App.Views;
using AccountabiliBuddy.Core.Ports;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The dim layer behind "in front of everything" cards: one 55% black window per monitor, under the cards, so the screen is
/// dimmed and clicks can't reach the apps underneath. The dims never take focus and stay out of Alt+Tab. Z-order, bottom to top:
/// the dims, the cards (oldest first, so the newest is in front), then this app's other always-on-top windows (an alarm, a toast…),
/// which stay reachable.
/// </summary>
public sealed class CardBackdrop : IDisposable
{
    private readonly IOverlayWindowControl _windowControl;
    private readonly List<ScreenDimWindow> _dims = new();
    private List<Window> _cards = new();
    private Screens? _watchedScreens;
    private bool _rebuildNeeded;
    private bool _closingDims;
    private bool _disposed;

    public CardBackdrop(IOverlayWindowControl windowControl)
    {
        _windowControl = windowControl;
    }

    /// <summary>The card in front (the last one set), or null when there is none.</summary>
    public Window? Newest => _cards.Count > 0 ? _cards[^1] : null;

    /// <summary>The dim windows followed by the cards: everything that has to be on the user's current virtual desktop.</summary>
    public IReadOnlyList<Window> Windows => _dims.Cast<Window>().Concat(_cards).ToList();

    /// <summary>The cards the dims sit behind, oldest first.</summary>
    public void SetCards(IReadOnlyList<Window> cards)
    {
        _cards = cards.ToList();
        WatchScreens();
    }

    /// <summary>
    /// Covers every monitor if the dims aren't up yet, and again when a monitor was plugged in, unplugged or moved, or a dim was
    /// closed (Alt+F4). Cheap when nothing changed; called on every keeper tick.
    /// </summary>
    public void Maintain()
    {
        Window? newest = Newest;
        if (_disposed || newest is null || (_dims.Count > 0 && !_rebuildNeeded))
        {
            return;
        }

        IReadOnlyList<Screen>? screens = newest.Screens?.All;
        if (screens is null || screens.Count == 0)
        {
            return;
        }

        _rebuildNeeded = false;
        CloseDims();
        foreach (Screen screen in screens)
        {
            PixelRect bounds = screen.Bounds;
            var box = new PixelBox(bounds.X, bounds.Y, bounds.Right, bounds.Bottom);
            var dim = new ScreenDimWindow();
            dim.Pressed += (_, _) => RaiseAndFocus();
            dim.Closed += (_, _) =>
            {
                if (!_closingDims)
                {
                    _dims.Remove(dim);
                    _rebuildNeeded = true; // Closed by someone else: covered again at the next tick.
                }
            };
            MonitorPlacement.Cover(dim, new MonitorArea(box, box));
            _dims.Add(dim);
            dim.Show();
            nint handle = dim.TryGetPlatformHandle()?.Handle ?? 0;
            if (handle != 0)
            {
                _windowControl.SetClickThrough(handle, false); // Takes the mouse, but never the focus, and stays out of Alt+Tab.
            }
        }

        Raise();
    }

    /// <summary>Puts the windows back in order in front of everything, without taking focus.</summary>
    public void Raise()
    {
        var done = new HashSet<Window>();
        foreach (Window window in _dims.Cast<Window>().Concat(_cards))
        {
            KeepOnTop(window);
            done.Add(window);
        }

        // Always-on-top windows of this app that came up before (an alarm, a toast…) go back above the cards.
        foreach (Window window in OwnWindows().Where(window => window.Topmost && window.IsVisible && !done.Contains(window)))
        {
            KeepOnTop(window);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        if (_watchedScreens is not null)
        {
            _watchedScreens.Changed -= OnScreensChanged;
            _watchedScreens = null;
        }

        CloseDims();
    }

    private void RaiseAndFocus()
    {
        Raise();
        Newest?.Activate();
    }

    private void KeepOnTop(Window window)
    {
        nint handle = window.TryGetPlatformHandle()?.Handle ?? 0;
        if (handle != 0)
        {
            _windowControl.KeepOnTop(handle);
        }
    }

    private void WatchScreens()
    {
        Screens? screens = Newest?.Screens;
        if (ReferenceEquals(screens, _watchedScreens))
        {
            return;
        }

        if (_watchedScreens is not null)
        {
            _watchedScreens.Changed -= OnScreensChanged;
        }

        _watchedScreens = screens;
        if (_watchedScreens is not null)
        {
            _watchedScreens.Changed += OnScreensChanged;
        }
    }

    private void OnScreensChanged(object? sender, EventArgs e)
    {
        _rebuildNeeded = true;
    }

    private void CloseDims()
    {
        _closingDims = true;
        foreach (ScreenDimWindow dim in _dims.ToList())
        {
            dim.Close();
        }

        _dims.Clear();
        _closingDims = false;
    }

    private static IReadOnlyList<Window> OwnWindows()
    {
        return (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows ?? [];
    }
}
