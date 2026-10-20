using Avalonia;
using Avalonia.Controls;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// Fills the width its parent gives it but asks for none, so long content (a file name) never widens the parent; the
/// content is measured at the width it was last given, and trims to fit.
/// </summary>
public sealed class FillWidthDecorator : Decorator
{
    private double _arrangedWidth;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child is null)
        {
            return default;
        }

        Child.Measure(new Size(Math.Min(availableSize.Width, _arrangedWidth), availableSize.Height));
        return new Size(0, Child.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Math.Abs(finalSize.Width - _arrangedWidth) > 0.5)
        {
            // The first pass measured at the old width; measure again at the width actually given.
            _arrangedWidth = finalSize.Width;
            InvalidateMeasure();
        }

        Child?.Arrange(new Rect(finalSize));
        return finalSize;
    }
}
