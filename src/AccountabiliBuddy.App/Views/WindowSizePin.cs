using Avalonia.Controls;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// Tasks redesign 8b.45: while a stack of tasks is dragged the window can't be resized, so the columns a page holds and the column under the
/// pointer never change mid-drag. The window's minimum and maximum size are pinned to its size now and given back by <see cref="Dispose"/>.
/// <c>CanResize</c> is not touched: with the extended client area (<c>ExtendClientAreaToDecorationsHint</c>) changing it can redraw or shift
/// the frame. A maximized or full-screen window is left alone (its edges don't drag; pinning its size could fight the maximize).
/// </summary>
internal sealed class WindowSizePin : IDisposable
{
    private readonly Window _window;
    private readonly double _minWidth;
    private readonly double _minHeight;
    private readonly double _maxWidth;
    private readonly double _maxHeight;
    private bool _released;

    private WindowSizePin(Window window)
    {
        _window = window;
        _minWidth = window.MinWidth;
        _minHeight = window.MinHeight;
        _maxWidth = window.MaxWidth;
        _maxHeight = window.MaxHeight;
        double width = window.ClientSize.Width;
        double height = window.ClientSize.Height;
        window.MinWidth = width;
        window.MaxWidth = width;
        window.MinHeight = height;
        window.MaxHeight = height;
    }

    /// <summary>Pins <paramref name="window"/>'s size; null when it can't be resized by dragging its edges anyway (maximized, full screen).</summary>
    public static WindowSizePin? Pin(Window? window)
    {
        if (window is not { WindowState: WindowState.Normal } || window.ClientSize.Width <= 0 || window.ClientSize.Height <= 0)
        {
            return null;
        }

        return new WindowSizePin(window);
    }

    /// <summary>The window's own minimum and maximum size again (MainWindow: 900 × 560, no maximum).</summary>
    public void Dispose()
    {
        if (_released)
        {
            return;
        }

        _released = true;

        // Widen the limits first, then narrow: the window never sees a minimum above its maximum.
        _window.MaxWidth = _maxWidth;
        _window.MaxHeight = _maxHeight;
        _window.MinWidth = _minWidth;
        _window.MinHeight = _minHeight;
    }
}
