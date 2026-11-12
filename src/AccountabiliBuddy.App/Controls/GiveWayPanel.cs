using Avalonia;
using Avalonia.Controls;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// A row that, when it is given less width than its children want, takes the width back from them in a set order: first the
/// children with <see cref="OrderProperty"/> 1, then 2, and so on, each down to its own MinWidth; children with order 0 never give
/// way. The title bar's left group uses it so the sound name trims first and the app name next, and the noise control's buttons
/// keep their size at the narrowest window (R1 review). Children are vertically centred.
/// </summary>
public sealed class GiveWayPanel : Panel
{
    public static readonly AttachedProperty<int> OrderProperty =
        AvaloniaProperty.RegisterAttached<GiveWayPanel, Control, int>("Order");

    // The width each child was given in the last measure (its natural width, or less where it gave way).
    private readonly Dictionary<Control, double> _given = [];

    static GiveWayPanel()
    {
        AffectsParentMeasure<GiveWayPanel>(OrderProperty);
    }

    public static int GetOrder(Control control) => control.GetValue(OrderProperty);

    public static void SetOrder(Control control, int order) => control.SetValue(OrderProperty, order);

    protected override Size MeasureOverride(Size availableSize)
    {
        _given.Clear();
        var unbounded = new Size(double.PositiveInfinity, availableSize.Height);
        double total = 0;
        double height = 0;
        foreach (Control child in Children)
        {
            child.Measure(unbounded);
            double width = child.IsVisible ? child.DesiredSize.Width : 0;
            _given[child] = width;
            total += width;
            height = Math.Max(height, child.DesiredSize.Height);
        }

        double excess = double.IsInfinity(availableSize.Width) ? 0 : total - availableSize.Width;
        foreach (int order in Children.Select(GetOrder).Where(order => order > 0).Distinct().OrderBy(order => order))
        {
            foreach (Control child in Children)
            {
                if (excess <= 0)
                {
                    break;
                }

                if (GetOrder(child) != order || !child.IsVisible)
                {
                    continue;
                }

                double natural = _given[child];
                double floor = Math.Min(natural, child.MinWidth + child.Margin.Left + child.Margin.Right);
                double width = Math.Max(floor, natural - excess);
                child.Measure(new Size(width, availableSize.Height));
                _given[child] = width;
                excess -= natural - width;
            }
        }

        return new Size(_given.Values.Sum(), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double left = 0;
        foreach (Control child in Children)
        {
            double width = _given.GetValueOrDefault(child, child.DesiredSize.Width);
            double childHeight = Math.Min(child.DesiredSize.Height, finalSize.Height);
            child.Arrange(new Rect(left, (finalSize.Height - childHeight) / 2, width, childHeight));
            left += width;
        }

        return finalSize;
    }
}
