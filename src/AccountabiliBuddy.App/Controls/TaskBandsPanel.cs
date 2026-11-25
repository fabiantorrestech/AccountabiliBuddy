using Avalonia;
using Avalonia.Controls;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// Group by Labels' band row (Tasks redesign 4c.43): each band spans exactly the label columns under it (<see cref="SpanProperty"/>
/// columns of <see cref="ColumnWidth"/>, <see cref="Spacing"/> apart, the bands 14 px apart like the columns). The pager sits at the row's
/// right, so the last band ends where the room it is given ends.
/// </summary>
public sealed class TaskBandsPanel : Panel
{
    public static readonly StyledProperty<double> ColumnWidthProperty =
        AvaloniaProperty.Register<TaskBandsPanel, double>(nameof(ColumnWidth));

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<TaskBandsPanel, double>(nameof(Spacing), 14);

    /// <summary>
    /// 0.1.154: the page's column widths left to right (a folded label column is a 62 px rail); when it has them, a band spans those
    /// widths instead of <see cref="ColumnWidth"/> each.
    /// </summary>
    public static readonly StyledProperty<IReadOnlyList<double>?> ColumnWidthsProperty =
        AvaloniaProperty.Register<TaskBandsPanel, IReadOnlyList<double>?>(nameof(ColumnWidths));

    /// <summary>How many columns a band spans.</summary>
    public static readonly AttachedProperty<int> SpanProperty =
        AvaloniaProperty.RegisterAttached<TaskBandsPanel, Control, int>("Span", 1);

    static TaskBandsPanel()
    {
        AffectsMeasure<TaskBandsPanel>(ColumnWidthProperty, SpacingProperty, ColumnWidthsProperty);
        AffectsParentMeasure<TaskBandsPanel>(SpanProperty);
    }

    public double ColumnWidth
    {
        get => GetValue(ColumnWidthProperty);
        set => SetValue(ColumnWidthProperty, value);
    }

    public IReadOnlyList<double>? ColumnWidths
    {
        get => GetValue(ColumnWidthsProperty);
        set => SetValue(ColumnWidthsProperty, value);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    // Narrower than this, a band could show no more than a cut icon.
    private const double MinShownWidth = 40;

    public static int GetSpan(Control control) => control.GetValue(SpanProperty);

    public static void SetSpan(Control control, int value) => control.SetValue(SpanProperty, value);

    protected override Size MeasureOverride(Size availableSize)
    {
        double height = 0;
        foreach ((Control child, double width) in Widths(availableSize.Width))
        {
            child.Measure(new Size(width, availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : 0, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0;
        foreach ((Control child, double width) in Widths(finalSize.Width))
        {
            // 0.1.154: a band cut down to a sliver by the pager (the rails' band at a page's right end) is not drawn at all.
            child.Opacity = width < MinShownWidth ? 0 : 1;
            child.Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width + Spacing;
        }

        return finalSize;
    }

    // Each band's width: its columns and the gaps between them, the last one cut to the room left.
    private List<(Control Child, double Width)> Widths(double room)
    {
        var widths = new List<(Control, double)>();
        IReadOnlyList<double>? columns = ColumnWidths;
        double x = 0;
        int column = 0;
        foreach (Control child in Children.Where(item => item.IsVisible))
        {
            int span = Math.Max(1, GetSpan(child));
            double width = (span - 1) * Spacing;
            for (int index = 0; index < span; index++, column++)
            {
                width += columns is not null && column < columns.Count ? columns[column] : ColumnWidth;
            }

            if (double.IsFinite(room))
            {
                width = Math.Max(0, Math.Min(width, room - x));
            }

            widths.Add((child, width));
            x += width + Spacing;
        }

        return widths;
    }
}
