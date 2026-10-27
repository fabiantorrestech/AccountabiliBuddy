using AccountabiliBuddy.App.Services;
using Avalonia;
using Avalonia.Platform;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// One cell of a 3×3 grid laid over a monitor's work area. The on-screen window is anchored by the point of that cell:
/// a corner for the four corner cells, the middle of an edge for the four edge cells, and its centre for the middle cell
/// (so the window grows evenly both ways there). Column 0 is the left third, 2 the right; row 0 the top third, 2 the
/// bottom. All maths is in physical pixels.
/// </summary>
public readonly record struct OverlayAnchorZone(int Column, int Row)
{
    /// <summary>The right column: the window grows leftward and its contents hug the right edge.</summary>
    public bool AnchorsRight => Column == 2;

    /// <summary>The bottom row: the window grows upward and its Ctrl+Alt details open above the bar.</summary>
    public bool AnchorsBottom => Row == 2;

    /// <summary>The cell of <paramref name="area"/> that holds <paramref name="centre"/> (outside it counts as the nearest cell).</summary>
    public static OverlayAnchorZone Of(PixelRect area, PixelPoint centre)
    {
        return new OverlayAnchorZone(ThirdOf(centre.X, area.X, area.Width), ThirdOf(centre.Y, area.Y, area.Height));
    }

    /// <summary>The cell of a default corner.</summary>
    public static OverlayAnchorZone ForCorner(OverlayCorner corner)
    {
        bool right = corner is OverlayCorner.TopRight or OverlayCorner.BottomRight;
        bool bottom = corner is OverlayCorner.BottomRight or OverlayCorner.BottomLeft;
        return new OverlayAnchorZone(right ? 2 : 0, bottom ? 2 : 0);
    }

    /// <summary>The anchor point on a rectangle: its left/centre/right and top/middle/bottom, by this cell.</summary>
    public PixelPoint PointOn(PixelRect rect)
    {
        return new PixelPoint(rect.X + (Column * rect.Width / 2), rect.Y + (Row * rect.Height / 2));
    }

    /// <summary>The rectangle of the given size whose anchor point (see <see cref="PointOn"/>) is <paramref name="point"/>.</summary>
    public PixelRect RectAt(PixelPoint point, PixelSize size)
    {
        return new PixelRect(point.X - (Column * size.Width / 2), point.Y - (Row * size.Height / 2), size.Width, size.Height);
    }

    /// <summary>
    /// Where the folded bar sits inside the open window: against the right edge in the right column, the bottom edge in
    /// the bottom row (the details open above it there), otherwise against the left and top edges.
    /// </summary>
    public PixelRect BarIn(PixelRect window, PixelSize folded)
    {
        int x = AnchorsRight ? window.Right - folded.Width : window.X;
        int y = AnchorsBottom ? window.Bottom - folded.Height : window.Y;
        return new PixelRect(x, y, folded.Width, folded.Height);
    }

    /// <summary>The window of the given size that holds <paramref name="bar"/> where <see cref="BarIn"/> puts it; the anchor point is where this cell anchors it.</summary>
    public PixelRect WindowFor(PixelRect bar, PixelSize size)
    {
        int x = AnchorsRight ? bar.Right - size.Width : bar.X;
        int y = AnchorsBottom ? bar.Bottom - size.Height : bar.Y;
        return new PixelRect(x, y, size.Width, size.Height);
    }

    /// <summary>Moves a rectangle the least it takes to lie inside the area (pinned to the top-left when it is bigger).</summary>
    public static PixelRect Clamp(PixelRect rect, PixelRect area)
    {
        int x = Math.Max(area.X, Math.Min(rect.X, area.Right - rect.Width));
        int y = Math.Max(area.Y, Math.Min(rect.Y, area.Bottom - rect.Height));
        return new PixelRect(x, y, rect.Width, rect.Height);
    }

    private static int ThirdOf(int value, int start, int length)
    {
        long offset = (long)(value - start) * 3;
        return offset < length ? 0 : offset < 2L * length ? 1 : 2;
    }
}
