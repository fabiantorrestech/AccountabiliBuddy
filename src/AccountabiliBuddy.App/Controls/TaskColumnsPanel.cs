using Avalonia;
using Avalonia.Controls;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// Lays the Shelf's columns side by side (round 2 R3, change note §2): each column gets an equal share of <see cref="ViewportWidth"/>
/// (the room the page gives them), but never less than <see cref="MinColumnWidth"/>. When they don't fit at that width the columns
/// first shrink, down to <see cref="SmallestColumnWidth"/> (so the three bins fit the smallest window, 901 × 567, with no sideways
/// scrolling: the repo's smallest-window rule); only when they still don't fit (many labels or tags) is the row of columns wider than
/// the room, and its host scrolls sideways. Every column is as tall as the panel, so each one
/// scrolls on its own and its header stays put. One column (Group by None) takes the whole width.
/// </summary>
public sealed class TaskColumnsPanel : Panel
{
    public static readonly StyledProperty<double> ViewportWidthProperty =
        AvaloniaProperty.Register<TaskColumnsPanel, double>(nameof(ViewportWidth), double.NaN);

    public static readonly StyledProperty<double> MinColumnWidthProperty =
        AvaloniaProperty.Register<TaskColumnsPanel, double>(nameof(MinColumnWidth), 300);

    public static readonly StyledProperty<double> SmallestColumnWidthProperty =
        AvaloniaProperty.Register<TaskColumnsPanel, double>(nameof(SmallestColumnWidth), 220);

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<TaskColumnsPanel, double>(nameof(Spacing), 14);

    static TaskColumnsPanel()
    {
        AffectsMeasure<TaskColumnsPanel>(ViewportWidthProperty, MinColumnWidthProperty, SmallestColumnWidthProperty, SpacingProperty);
    }

    /// <summary>The width the columns share (the scroll host's viewport); NaN uses the width offered, when it is finite.</summary>
    public double ViewportWidth
    {
        get => GetValue(ViewportWidthProperty);
        set => SetValue(ViewportWidthProperty, value);
    }

    public double MinColumnWidth
    {
        get => GetValue(MinColumnWidthProperty);
        set => SetValue(MinColumnWidthProperty, value);
    }

    /// <summary>How narrow a column may get before the row scrolls sideways instead; never above <see cref="MinColumnWidth"/>.</summary>
    public double SmallestColumnWidth
    {
        get => GetValue(SmallestColumnWidthProperty);
        set => SetValue(SmallestColumnWidthProperty, value);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = ColumnWidth(availableSize.Width);
        double height = double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height;
        foreach (Control child in Children)
        {
            child.Measure(new Size(width, double.IsInfinity(availableSize.Height) ? double.PositiveInfinity : availableSize.Height));
            height = Math.Max(height, double.IsInfinity(availableSize.Height) ? child.DesiredSize.Height : 0);
        }

        return new Size(TotalWidth(width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double width = ColumnWidth(finalSize.Width);
        double x = 0;
        foreach (Control child in Children)
        {
            child.Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width + Spacing;
        }

        return new Size(Math.Max(finalSize.Width, TotalWidth(width)), finalSize.Height);
    }

    // The share each column gets of the room: at least the minimum while that fits, else down to the smallest width.
    private double ColumnWidth(double offered)
    {
        int count = Children.Count;
        if (count == 0)
        {
            return 0;
        }

        double room = !double.IsNaN(ViewportWidth) && ViewportWidth > 0 ? ViewportWidth
            : double.IsFinite(offered) ? offered
            : count * MinColumnWidth + (count - 1) * Spacing;
        double share = (room - (count - 1) * Spacing) / count;
        return Math.Max(Math.Min(MinColumnWidth, SmallestColumnWidth), Math.Floor(share));
    }

    private double TotalWidth(double columnWidth)
    {
        int count = Children.Count;
        return count == 0 ? 0 : count * columnWidth + (count - 1) * Spacing;
    }
}
