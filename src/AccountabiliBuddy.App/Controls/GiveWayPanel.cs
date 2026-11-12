using Avalonia;
using Avalonia.Controls;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// A row that, when it is given less width than its children want, takes the width back from them in a set order: first the
/// children with <see cref="OrderProperty"/> 1, then 2, and so on, each down to its own MinWidth; children with order 0 never give
/// way; a child with <see cref="FirstMinWidthProperty"/> stops at that width in its order and gives way the rest of the way to its
/// MinWidth at the end. The title bar's left group uses it so the
/// sound name trims first (to a short name), then the app name, then the rest of the sound name, and back / forward and the noise
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

    /// <summary>
    /// A give-way child that gives way in two steps: in its order only down to this width, then, once every order has given way, the
    /// rest of the way to its MinWidth (NaN: one step, straight to MinWidth). The title bar's noise control keeps a short sound name
    /// (enough for the headphones notice) while the app name gives way, and only then loses it (owner 2026-11-14).
    /// </summary>
    public static readonly AttachedProperty<double> FirstMinWidthProperty =
        AvaloniaProperty.RegisterAttached<GiveWayPanel, Control, double>("FirstMinWidth", double.NaN);

    /// <summary>
    /// A give-way child given less than its natural width and less than this is left out altogether (no width), and the room goes
    /// back to the children that gave way before it: the app name never shows as a lone "…" (owner 2026-11-14). NaN: never.
    /// </summary>
    public static readonly AttachedProperty<double> HideBelowProperty =
        AvaloniaProperty.RegisterAttached<GiveWayPanel, Control, double>("HideBelow", double.NaN);

    // The width each child was given in the last measure (its natural width, or less where it gave way; 0 for a dropped line).
    private readonly Dictionary<Control, double> _given = [];

    // Lines left out in the last measure.
    private readonly HashSet<Control> _dropped = [];

    static GiveWayPanel()
    {
        AffectsParentMeasure<GiveWayPanel>(OrderProperty, IsDividerProperty, FirstMinWidthProperty, HideBelowProperty);
    }

    public static int GetOrder(Control control) => control.GetValue(OrderProperty);

    public static void SetOrder(Control control, int order) => control.SetValue(OrderProperty, order);

    public static bool GetIsDivider(Control control) => control.GetValue(IsDividerProperty);

    public static void SetIsDivider(Control control, bool isDivider) => control.SetValue(IsDividerProperty, isDivider);

    public static double GetFirstMinWidth(Control control) => control.GetValue(FirstMinWidthProperty);

    public static void SetFirstMinWidth(Control control, double width) => control.SetValue(FirstMinWidthProperty, width);

    public static double GetHideBelow(Control control) => control.GetValue(HideBelowProperty);

    public static void SetHideBelow(Control control, double width) => control.SetValue(HideBelowProperty, width);

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
        var naturals = new Dictionary<Control, double>(_given);
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
                double width = Math.Max(FirstFloor(child, natural), natural - excess);
                child.Measure(new Size(width, availableSize.Height));
                _given[child] = width;
                excess -= natural - width;
            }
        }

        // The last step: children that stopped at their first floor give way the rest of the way, down to their MinWidth.
        foreach (Control child in Children)
        {
            if (excess <= 0)
            {
                break;
            }

            if (GetOrder(child) <= 0 || !child.IsVisible || double.IsNaN(GetFirstMinWidth(child)))
            {
                continue;
            }

            double given = _given[child];
            double width = Math.Max(Floor(child, given), given - excess);
            child.Measure(new Size(width, availableSize.Height));
            _given[child] = width;
            excess -= given - width;
        }

        // A child squeezed below its HideBelow is left out; its room (and that of any line left with nothing beside it) goes back
        // to the children that gave way, lowest order first, up to their natural width.
        double freed = 0;
        foreach (Control child in Children)
        {
            double hideBelow = GetHideBelow(child);
            double given = _given[child];
            if (!double.IsNaN(hideBelow) && given > 0 && given < naturals[child] && given < hideBelow)
            {
                child.Measure(new Size(0, availableSize.Height));
                _given[child] = 0;
                freed += given;
            }
        }

        _dropped.Clear();
        foreach (Control line in DroppedLines(_given))
        {
            _dropped.Add(line);
            freed += _given[line];
            _given[line] = 0;
        }

        // What the row was still over by is paid out of the freed room first.
        freed -= Math.Max(0, excess);
        if (freed > 0 && !double.IsInfinity(availableSize.Width))
        {
            foreach (Control child in Children.Where(child => GetOrder(child) > 0 && child.IsVisible).OrderBy(GetOrder))
            {
                double given = _given[child];
                if (freed <= 0 || given <= 0 || given >= naturals[child])
                {
                    continue;
                }

                double width = Math.Min(naturals[child], given + freed);
                child.Measure(new Size(width, availableSize.Height));
                _given[child] = width;
                freed -= width - given;
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

    // Where a give-way child stops in its own order: its first floor where it has one, else its MinWidth.
    private static double FirstFloor(Control child, double natural)
    {
        double first = GetFirstMinWidth(child);
        return double.IsNaN(first) ? Floor(child, natural) : Math.Min(natural, Math.Max(first, child.MinWidth) + child.Margin.Left + child.Margin.Right);
    }

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
