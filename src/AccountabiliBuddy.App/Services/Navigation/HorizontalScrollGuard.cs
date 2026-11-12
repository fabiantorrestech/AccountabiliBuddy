using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace AccountabiliBuddy.App.Services.Navigation;

/// <summary>
/// Where a swipe must not navigate (owner 2026-11-14): over anything that scrolls sideways, the scrolling wins. That is any
/// <see cref="ScrollViewer"/> above the pointer whose content is wider than what it shows, even when it already sits at its end
/// (a swipe then must not be mistaken for the end of the scroll). A scroller that only scrolls up and down doesn't count.
/// </summary>
public static class HorizontalScrollGuard
{
    // Layout rounding can leave an extent a fraction wider than the viewport: that is not something to scroll.
    private const double Tolerance = 0.5;

    /// <summary>True when the control or one of its ancestors is a ScrollViewer that can scroll sideways at all.</summary>
    public static bool IsInHorizontalScroller(Visual? source)
    {
        if (source is null)
        {
            return false;
        }

        foreach (Visual visual in source.GetSelfAndVisualAncestors())
        {
            if (visual is ScrollViewer scroller && CanScrollSideways(scroller.HorizontalScrollBarVisibility, scroller.Extent, scroller.Viewport))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A scroller scrolls sideways when its bar isn't disabled and its content is wider than its view.</summary>
    public static bool CanScrollSideways(Avalonia.Controls.Primitives.ScrollBarVisibility visibility, Size extent, Size viewport)
    {
        return visibility != Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled && extent.Width > viewport.Width + Tolerance;
    }
}
