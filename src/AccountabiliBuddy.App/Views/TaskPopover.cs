using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The shared rules of the Tasks page's menus and popovers (spec 03 §C): the theme's popover radius ("pr", <c>Ab.PopoverRadius</c>),
/// a 1 px line, the shadow 0 16 40 rgba(0,0,0,.22) (<c>Ab.PopoverShadow</c>), the surface colour; shown in the window's overlay layer,
/// so a menu always stays inside the window (near the bottom it shifts up), and never taller than the window less 16 (it scrolls).
/// <see cref="TaskMenuFlyout"/> and <see cref="TaskPopoverFlyout"/> apply them, submenus included. Every value follows the theme live.
/// </summary>
public static class TaskPopover
{
    /// <summary>The space kept between a popover and the window's top and bottom edges together.</summary>
    public const double WindowMargin = 16;

    // Never shrink a popover below this, however small the window.
    private const double MinMaxHeight = 120;

    /// <summary>Gives a popover's presenter the shared radius, line and surface (its template passes them to its outer border).</summary>
    public static void StylePresenter(TemplatedControl presenter)
    {
        presenter.Bind(TemplatedControl.CornerRadiusProperty, presenter.GetResourceObservable("Ab.PopoverRadius"));
        presenter.Bind(TemplatedControl.BorderBrushProperty, presenter.GetResourceObservable("Ab.Line"));
        presenter.Bind(TemplatedControl.BackgroundProperty, presenter.GetResourceObservable("Ab.Surface"));
        presenter.BorderThickness = new Thickness(1);
        presenter.TemplateApplied += (_, e) =>
        {
            // The shadow is the outer border's own (no template binding carries it).
            Border? root = e.NameScope.Find<Border>("LayoutRoot") ?? presenter.GetVisualChildren().OfType<Border>().FirstOrDefault();
            if (root is not null)
            {
                root.Bind(Border.BoxShadowProperty, root.GetResourceObservable("Ab.PopoverShadow"));
            }
        };
    }

    /// <summary>Gives a submenu's outer border the shared look (a submenu's popup holds a plain border).</summary>
    public static void StyleBorder(Border border)
    {
        border.Bind(Border.CornerRadiusProperty, border.GetResourceObservable("Ab.PopoverRadius"));
        border.Bind(Border.BorderBrushProperty, border.GetResourceObservable("Ab.Line"));
        border.Bind(Border.BackgroundProperty, border.GetResourceObservable("Ab.Surface"));
        border.Bind(Border.BoxShadowProperty, border.GetResourceObservable("Ab.PopoverShadow"));
        border.BorderThickness = new Thickness(1);
    }

    /// <summary>The tallest a popover over <paramref name="anchor"/>'s window may be: the window's height less <see cref="WindowMargin"/>.</summary>
    public static double MaxHeightFor(Visual? anchor)
    {
        return anchor is not null && TopLevel.GetTopLevel(anchor) is { } top
            ? Math.Max(MinMaxHeight, top.ClientSize.Height - WindowMargin)
            : double.PositiveInfinity;
    }

    /// <summary>
    /// A menu item's submenu follows the same rules: in the overlay layer (inside the window), the shared look, and at most the
    /// window's height less 16 (its own scroll takes the rest).
    /// </summary>
    public static void FollowRules(MenuItem item)
    {
        item.TemplateApplied += (_, e) =>
        {
            if (e.NameScope.Find<Popup>("PART_Popup") is not Popup popup)
            {
                return;
            }

            popup.ShouldUseOverlayLayer = true;
            bool styled = false;
            popup.Opened += (_, _) =>
            {
                if (popup.Child is not Border border)
                {
                    return;
                }

                if (!styled)
                {
                    StyleBorder(border);
                    styled = true;
                }

                // The window may have been resized since the last time it opened.
                border.MaxHeight = MaxHeightFor(item);
            };
        };
    }
}
