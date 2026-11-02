using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// The overlay tile's row of parts. Laid out like a StackPanel, but it never lets the end of the row run off the edge: when the parts do not
/// fit the width it was given, it gives up room in a fixed order and never slices a part in half.
/// <list type="number">
/// <item>Parts marked <see cref="ShrinksProperty"/> (the task name, the second line) trim their text, down to a small floor.</item>
/// <item>Then whole parts marked with a <see cref="DropOrderProperty"/> go, lowest number first (sound, then batteries, then time and date).</item>
/// <item>Then the shrinking parts trim all the way down (the task name is gone, the minutes stay).</item>
/// </list>
/// A part with a drop order of 0 is never dropped. If even that does not fit (a very narrow band), the tile's own clip is the last guard.
/// Vertical orientation (the side tile) is a plain stack: nothing to give up there.
/// </summary>
public sealed class PriorityRowPanel : Panel
{
    /// <summary>Drop this part whole when the row does not fit; parts with a lower number go first. 0 = never.</summary>
    public static readonly AttachedProperty<int> DropOrderProperty = AvaloniaProperty.RegisterAttached<PriorityRowPanel, Control, int>("DropOrder");

    /// <summary>This part may trim its text (it measures narrower than it wants) before anything is dropped.</summary>
    public static readonly AttachedProperty<bool> ShrinksProperty = AvaloniaProperty.RegisterAttached<PriorityRowPanel, Control, bool>("Shrinks");

    public static readonly StyledProperty<double> SpacingProperty = AvaloniaProperty.Register<PriorityRowPanel, double>(nameof(Spacing));

    public static readonly StyledProperty<Orientation> OrientationProperty = AvaloniaProperty.Register<PriorityRowPanel, Orientation>(nameof(Orientation), Orientation.Horizontal);

    // How much of a trimmed text part is kept before parts start to go: about a short task name.
    private const double TextFloor = 60;

    // What the last measure decided, for arranging: each child's width, and whether it was dropped.
    private double[] _widths = [];
    private bool[] _dropped = [];

    static PriorityRowPanel()
    {
        AffectsMeasure<PriorityRowPanel>(SpacingProperty, OrientationProperty);
        AffectsParentMeasure<PriorityRowPanel>(DropOrderProperty, ShrinksProperty);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public Orientation Orientation
    {
        get => GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    public static int GetDropOrder(Control control) => control.GetValue(DropOrderProperty);

    public static void SetDropOrder(Control control, int value) => control.SetValue(DropOrderProperty, value);

    public static bool GetShrinks(Control control) => control.GetValue(ShrinksProperty);

    public static void SetShrinks(Control control, bool value) => control.SetValue(ShrinksProperty, value);

    private static void AffectsParentMeasure<TPanel>(params AvaloniaProperty[] properties)
        where TPanel : Panel
    {
        foreach (AvaloniaProperty property in properties)
        {
            property.Changed.AddClassHandler<Control>((control, _) => (control.Parent as TPanel)?.InvalidateMeasure());
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return Orientation == Orientation.Vertical ? MeasureVertical(availableSize) : MeasureHorizontal(availableSize);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Orientation == Orientation.Vertical)
        {
            return ArrangeVertical(finalSize);
        }

        double x = 0;
        bool first = true;
        for (int i = 0; i < Children.Count; i++)
        {
            Control child = Children[i];
            if (!child.IsVisible)
            {
                continue;
            }

            bool dropped = i < _dropped.Length && _dropped[i];
            // A dropped part is arranged with no size and clipped, so nothing of it is drawn.
            child.ClipToBounds = dropped;
            if (dropped)
            {
                child.Arrange(new Rect(0, 0, 0, 0));
                continue;
            }

            if (!first)
            {
                x += Spacing;
            }

            double width = i < _widths.Length ? _widths[i] : child.DesiredSize.Width;
            child.Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width;
            first = false;
        }

        return new Size(Math.Max(finalSize.Width, x), finalSize.Height);
    }

    private Size MeasureHorizontal(Size availableSize)
    {
        int count = Children.Count;
        _widths = new double[count];
        _dropped = new bool[count];
        double height = availableSize.Height;
        double[] natural = new double[count];
        double[] floor = new double[count];
        double[] hardFloor = new double[count];
        for (int i = 0; i < count; i++)
        {
            Control child = Children[i];
            if (!child.IsVisible)
            {
                continue;
            }

            child.Measure(new Size(double.PositiveInfinity, height));
            natural[i] = child.DesiredSize.Width;
            hardFloor[i] = natural[i];
            floor[i] = natural[i];
            if (GetShrinks(child))
            {
                // What it needs with its text trimmed away, and that plus a short task name.
                child.Measure(new Size(0, height));
                hardFloor[i] = Math.Min(natural[i], child.DesiredSize.Width);
                floor[i] = Math.Min(natural[i], hardFloor[i] + TextFloor);
            }
        }

        List<int> shown = Enumerable.Range(0, count).Where(i => Children[i].IsVisible).ToList();
        double available = availableSize.Width;
        if (!double.IsInfinity(available))
        {
            Fit(shown, natural, floor, hardFloor, available);
        }
        else
        {
            foreach (int i in shown)
            {
                _widths[i] = natural[i];
            }
        }

        double total = 0;
        double tallest = 0;
        int placed = 0;
        foreach (int i in shown)
        {
            Control child = Children[i];
            if (_dropped[i])
            {
                child.Measure(new Size(0, 0));
                continue;
            }

            child.Measure(new Size(_widths[i], height));
            total += _widths[i];
            tallest = Math.Max(tallest, child.DesiredSize.Height);
            placed++;
        }

        return new Size(total + (Spacing * Math.Max(0, placed - 1)), tallest);
    }

    private void Fit(List<int> shown, double[] natural, double[] floor, double[] hardFloor, double available)
    {
        // Parts that can go, in the order they go.
        List<int> droppable = shown.Where(i => GetDropOrder(Children[i]) > 0).OrderBy(i => GetDropOrder(Children[i])).ToList();
        var dropped = new HashSet<int>();
        int step = 0;
        while (true)
        {
            List<int> kept = shown.Where(i => !dropped.Contains(i)).ToList();
            double gaps = Spacing * Math.Max(0, kept.Count - 1);
            double deficit = kept.Sum(i => natural[i]) + gaps - available;
            double capacity = kept.Sum(i => natural[i] - floor[i]);
            if (deficit <= 0 || deficit <= capacity || step >= droppable.Count)
            {
                Assign(kept, natural, floor, hardFloor, deficit);
                break;
            }

            dropped.Add(droppable[step]);
            step++;
        }

        foreach (int i in dropped)
        {
            _dropped[i] = true;
        }
    }

    // Takes the missing width out of the parts that can trim, to their floor; past that, to their hard floor (no task name at all).
    private void Assign(List<int> kept, double[] natural, double[] floor, double[] hardFloor, double deficit)
    {
        foreach (int i in kept)
        {
            _widths[i] = natural[i];
        }

        foreach (double[] limit in new[] { floor, hardFloor })
        {
            foreach (int i in kept)
            {
                if (deficit <= 0)
                {
                    return;
                }

                double take = Math.Min(deficit, _widths[i] - limit[i]);
                if (take > 0)
                {
                    _widths[i] -= take;
                    deficit -= take;
                }
            }
        }
    }

    private Size MeasureVertical(Size availableSize)
    {
        double width = 0;
        double height = 0;
        int placed = 0;
        foreach (Control child in Children.Where(child => child.IsVisible))
        {
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            width = Math.Max(width, child.DesiredSize.Width);
            height += child.DesiredSize.Height;
            placed++;
        }

        return new Size(width, height + (Spacing * Math.Max(0, placed - 1)));
    }

    private Size ArrangeVertical(Size finalSize)
    {
        double y = 0;
        bool first = true;
        foreach (Control child in Children.Where(child => child.IsVisible))
        {
            if (!first)
            {
                y += Spacing;
            }

            child.Arrange(new Rect(0, y, finalSize.Width, child.DesiredSize.Height));
            y += child.DesiredSize.Height;
            first = false;
        }

        return new Size(finalSize.Width, Math.Max(finalSize.Height, y));
    }
}
