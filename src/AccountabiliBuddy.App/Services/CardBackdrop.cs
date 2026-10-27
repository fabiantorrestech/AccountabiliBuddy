using AccountabiliBuddy.App.Views;
using AccountabiliBuddy.Core.Ports;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The dim layer behind a "in front of everything" card: one 55% black window per monitor under the card, so the screen is
/// dimmed and clicks can't reach the apps underneath. Shown before the card, closed with it.
/// </summary>
public sealed class CardBackdrop : IDisposable
{
    private readonly Window _card;
    private readonly List<ScreenDimWindow> _dims = new();
    private string _screenSignature = string.Empty;

    public CardBackdrop(Window card)
    {
        _card = card;
    }

    /// <summary>The dim windows followed by the card: everything that has to be on the user's current virtual desktop.</summary>
    public IReadOnlyList<Window> Windows => _dims.Cast<Window>().Append(_card).ToList();

    /// <summary>Covers every monitor. Call before the card is shown so the card opens on top.</summary>
    public void Show()
    {
        IReadOnlyList<Screen> screens = _card.Screens?.All ?? [];
        _screenSignature = Signature(screens);
        foreach (Screen screen in screens)
        {
            PixelRect bounds = screen.Bounds;
            var area = new MonitorArea(
                new PixelBox(bounds.X, bounds.Y, bounds.Right, bounds.Bottom),
                new PixelBox(bounds.X, bounds.Y, bounds.Right, bounds.Bottom));
            var dim = new ScreenDimWindow();
            dim.Pressed += (_, _) => Raise();
            MonitorPlacement.Cover(dim, area);
            _dims.Add(dim);
            dim.Show();
        }
    }

    /// <summary>Covers the monitors again when one was plugged in, unplugged or moved while the card is up.</summary>
    public void RefreshScreens()
    {
        IReadOnlyList<Screen> screens = _card.Screens?.All ?? [];
        if (screens.Count == 0 || Signature(screens) == _screenSignature)
        {
            return;
        }

        CloseDims();
        Show();
        Raise();
    }

    /// <summary>Back in front of everything, the dim under the card.</summary>
    public void Raise()
    {
        foreach (ScreenDimWindow dim in _dims)
        {
            dim.Topmost = false;
            dim.Topmost = true;
        }

        _card.Topmost = false;
        _card.Topmost = true;
        _card.Activate();
    }

    public void Dispose()
    {
        CloseDims();
    }

    private void CloseDims()
    {
        foreach (ScreenDimWindow dim in _dims)
        {
            dim.Close();
        }

        _dims.Clear();
    }

    private static string Signature(IReadOnlyList<Screen> screens)
    {
        return string.Join(';', screens.Select(screen => $"{screen.Bounds.X},{screen.Bounds.Y},{screen.Bounds.Width},{screen.Bounds.Height}"));
    }
}
