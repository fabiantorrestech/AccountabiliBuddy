using Avalonia;
using Avalonia.Controls;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// A row that, when it is given less width than its children want, takes the width back from them in a set order: first the
/// children with <see cref="OrderProperty"/> 1, then 2, and so on, each down to its own MinWidth; children with order 0 never give
/// way. The title bar's left group uses it so the sound name trims first and the app name next, and back / forward and the noise
/// control's buttons keep their size at the narrowest window (R1 review; owner 2026-11-14). A child marked
/// <see cref="IsDividerProperty"/> (a 1 px line between groups) is left out, taking no width and not drawn, when everything between
/// it and the line before it gave way to nothing, so two lines never stand side by side. Children are vertically centred.
/// </summary>
public sealed class GiveWayPanel : Panel
{
    public static readonly AttachedProperty<int> OrderProperty =
        AvaloniaProperty.RegisterAttached<GiveWayPanel, Control, int>("Order");

    public static readonly AttachedProperty<bool> IsDividerProperty =
        AvaloniaProperty.RegisterAttached<GiveWayPanel, Control, bool>("IsDivider");

    // The width each child was given in the last measure (its natural width, or less where it gave way; 0 for a dropped line).
    private readonly Dictionary<Control, double> _given = [];

    // Lines left out in the last measure.
    private readonly HashSet<Control> _dropped = [];

    static GiveWayPanel()
    {
        AffectsParentMeasure<GiveWayPanel>(OrderProperty, IsDividerProperty);
    }

    public static int GetOrder(Control control) => control.GetValue(OrderProperty);

    public static void SetOrder(Control control, int order) => control.SetValue(OrderProperty, order);

    public static bool GetIsDivider(Control control) => control.GetValue(IsDividerProperty);

    public static void SetIsDivider(Control control, bool isDivider) => control.SetValue(IsDividerProperty, isDivider);

    /// <summary>
    /// The width the visible children asked for in the last measure, before any of them gave way (title bar, owner 2026-11-14: the
    /// session control reads its extras' full width from it while they are closed or capped).
    /// </summary>
    public double NaturalWidth { get; private set; }

    /// <summary>
    /// The narrowest this row can get, as of the last measure: every child that gives way at its floor, the others at their natural
    /// width, lines left out where nothing would stand between them (the title bar reads it to know when the start group can give
    /// no more).
    /// </summary>
    public double SmallestWidth { get; private set; }

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

        NaturalWidth = total;
        var floors = Children.ToDictionary(child => child, child => GetOrder(child) > 0 ? Floor(child, _given[child]) : _given[child]);
        SmallestWidth = floors.Values.Sum() - DroppedLines(floors).Sum(line => floors[line]);

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
                double width = Math.Max(Floor(child, natural), natural - excess);
                child.Measure(new Size(width, availableSize.Height));
                _given[child] = width;
                excess -= natural - width;
            }
        }

        _dropped.Clear();
        foreach (Control line in DroppedLines(_given))
        {
            _dropped.Add(line);
            _given[line] = 0;
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
            if (GetIsDivider(child))
            {
                // A line with a fixed Width still draws when given none; a dropped one is hidden without changing the layout.
                child.SetCurrentValue(OpacityProperty, _dropped.Contains(child) ? 0.0 : 1.0);
            }

            left += width;
        }

        return finalSize;
    }

    private static double Floor(Control child, double natural) => Math.Min(natural, child.MinWidth + child.Margin.Left + child.Margin.Right);

    // The lines with nothing shown between them and the line before them (given these widths).
    private List<Control> DroppedLines(IReadOnlyDictionary<Control, double> widths)
    {
        var dropped = new List<Control>();
        bool lineBefore = false;
        bool shownSince = false;
        foreach (Control child in Children)
        {
            if (!child.IsVisible)
            {
                continue;
            }

            if (GetIsDivider(child))
            {
                if (lineBefore && !shownSince)
                {
                    dropped.Add(child);
                    continue;
                }

                lineBefore = true;
                shownSince = false;
            }
            else if (widths[child] > 0.5)
            {
                shownSince = true;
            }
        }

        return dropped;
    }
}
