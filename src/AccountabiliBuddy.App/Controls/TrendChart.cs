using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// The Stats page's Focus graph (design 19c): the main series as an accent line with a 10% fill under it and a dot on each
/// week, the last one ringed; a second series (drift) as a dashed line; the goal as a dashed line with its label; labels
/// underneath. A missing value (null) breaks a line, it is never drawn as zero. Drawn by hand from the brushes it is given
/// (theme tokens); it redraws only when its data, a brush or its size changes.
/// </summary>
public sealed class TrendChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<double?>?> MainProperty =
        AvaloniaProperty.Register<TrendChart, IReadOnlyList<double?>?>(nameof(Main));

    public static readonly StyledProperty<IReadOnlyList<double?>?> SecondaryProperty =
        AvaloniaProperty.Register<TrendChart, IReadOnlyList<double?>?>(nameof(Secondary));

    /// <summary>A label under each point, or null to leave one out.</summary>
    public static readonly StyledProperty<IReadOnlyList<string?>?> LabelsProperty =
        AvaloniaProperty.Register<TrendChart, IReadOnlyList<string?>?>(nameof(Labels));

    /// <summary>One tip per point, shown on hover.</summary>
    public static readonly StyledProperty<IReadOnlyList<string>?> TipsProperty =
        AvaloniaProperty.Register<TrendChart, IReadOnlyList<string>?>(nameof(Tips));

    /// <summary>The value the chart's top stands for; values and the goal are drawn against it.</summary>
    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<TrendChart, double>(nameof(Maximum), 1);

    /// <summary>The goal as a value on the same scale; NaN draws no goal line.</summary>
    public static readonly StyledProperty<double> GoalProperty =
        AvaloniaProperty.Register<TrendChart, double>(nameof(Goal), double.NaN);

    public static readonly StyledProperty<string?> GoalTextProperty =
        AvaloniaProperty.Register<TrendChart, string?>(nameof(GoalText));

    public static readonly StyledProperty<IBrush?> LineBrushProperty =
        AvaloniaProperty.Register<TrendChart, IBrush?>(nameof(LineBrush));

    public static readonly StyledProperty<IBrush?> SecondaryBrushProperty =
        AvaloniaProperty.Register<TrendChart, IBrush?>(nameof(SecondaryBrush));

    public static readonly StyledProperty<IBrush?> GoalBrushProperty =
        AvaloniaProperty.Register<TrendChart, IBrush?>(nameof(GoalBrush));

    public static readonly StyledProperty<IBrush?> LabelBrushProperty =
        AvaloniaProperty.Register<TrendChart, IBrush?>(nameof(LabelBrush));

    public static readonly StyledProperty<IBrush?> DotBrushProperty =
        AvaloniaProperty.Register<TrendChart, IBrush?>(nameof(DotBrush));

    private const double LabelHeight = 28;
    private const double SidePadding = 12;
    private const double TopPadding = 14;
    private const double MinChartHeight = 170;
    private const double FillOpacity = 0.10;
    private static readonly Typeface LabelTypeface = new(FontFamily.Default);

    private int _hovered = -1;

    static TrendChart()
    {
        AffectsRender<TrendChart>(
            MainProperty, SecondaryProperty, LabelsProperty, MaximumProperty, GoalProperty, GoalTextProperty, LineBrushProperty,
            SecondaryBrushProperty, GoalBrushProperty, LabelBrushProperty, DotBrushProperty);
    }

    public IReadOnlyList<double?>? Main
    {
        get => GetValue(MainProperty);
        set => SetValue(MainProperty, value);
    }

    public IReadOnlyList<double?>? Secondary
    {
        get => GetValue(SecondaryProperty);
        set => SetValue(SecondaryProperty, value);
    }

    public IReadOnlyList<string?>? Labels
    {
        get => GetValue(LabelsProperty);
        set => SetValue(LabelsProperty, value);
    }

    public IReadOnlyList<string>? Tips
    {
        get => GetValue(TipsProperty);
        set => SetValue(TipsProperty, value);
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double Goal
    {
        get => GetValue(GoalProperty);
        set => SetValue(GoalProperty, value);
    }

    public string? GoalText
    {
        get => GetValue(GoalTextProperty);
        set => SetValue(GoalTextProperty, value);
    }

    public IBrush? LineBrush
    {
        get => GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public IBrush? SecondaryBrush
    {
        get => GetValue(SecondaryBrushProperty);
        set => SetValue(SecondaryBrushProperty, value);
    }

    public IBrush? GoalBrush
    {
        get => GetValue(GoalBrushProperty);
        set => SetValue(GoalBrushProperty, value);
    }

    public IBrush? LabelBrush
    {
        get => GetValue(LabelBrushProperty);
        set => SetValue(LabelBrushProperty, value);
    }

    /// <summary>The ring round the last point.</summary>
    public IBrush? DotBrush
    {
        get => GetValue(DotBrushProperty);
        set => SetValue(DotBrushProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        return new Size(width, MinChartHeight);
    }

    public override void Render(DrawingContext context)
    {
        IReadOnlyList<double?>? main = Main;
        double plotHeight = Bounds.Height - LabelHeight - TopPadding;
        if (main is null || main.Count == 0 || Maximum <= 0 || Bounds.Width <= 2 * SidePadding || plotHeight <= 0)
        {
            return;
        }

        double bottom = TopPadding + plotHeight;
        double Y(double value) => bottom - Math.Clamp(value / Maximum, 0, 1) * plotHeight;
        double X(int index) => main.Count == 1 ? Bounds.Width / 2 : SidePadding + index * (Bounds.Width - 2 * SidePadding) / (main.Count - 1);

        if (!double.IsNaN(Goal) && GoalBrush is { } goalBrush)
        {
            double y = Y(Goal);
            context.DrawLine(new Pen(goalBrush, 1, new DashStyle([3, 3], 0)), new Point(0, y), new Point(Bounds.Width, y));
            if (!string.IsNullOrEmpty(GoalText))
            {
                var label = new FormattedText(GoalText, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelTypeface, 12, goalBrush);
                context.DrawText(label, new Point(Bounds.Width - label.Width - SidePadding, y - label.Height - 3));
            }
        }

        if (LineBrush is { } lineBrush)
        {
            DrawRuns(context, main, X, Y, bottom, fillBrush: FillOf(lineBrush), new Pen(lineBrush, 2.5, lineJoin: PenLineJoin.Round, lineCap: PenLineCap.Round), dots: lineBrush);
        }

        if (Secondary is { } secondary && SecondaryBrush is { } secondaryBrush)
        {
            DrawRuns(context, secondary, X, Y, bottom, fillBrush: null, new Pen(secondaryBrush, 1.5, new DashStyle([2, 3], 0), PenLineCap.Round), dots: null);
        }

        int last = main.ToList().FindLastIndex(value => value is not null);
        if (last >= 0 && LineBrush is { } ring && DotBrush is { } dot)
        {
            var center = new Point(X(last), Y(main[last]!.Value));
            context.DrawEllipse(dot, new Pen(ring, 2), center, 5, 5);
        }

        if (Labels is { } labels)
        {
            for (int i = 0; i < labels.Count && i < main.Count; i++)
            {
                if (labels[i] is not { Length: > 0 } text)
                {
                    continue;
                }

                var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelTypeface, 12, LabelBrush);
                double x = Math.Clamp(X(i) - formatted.Width / 2, 0, Bounds.Width - formatted.Width);
                context.DrawText(formatted, new Point(x, bottom + 10));
            }
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        int index = IndexAt(e.GetPosition(this));
        if (index == _hovered)
        {
            return;
        }

        _hovered = index;
        ToolTip.SetTip(this, index >= 0 && Tips is { } tips && index < tips.Count ? tips[index] : null);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hovered = -1;
        ToolTip.SetTip(this, null);
    }

    // Lines and an optional fill for each unbroken run of values.
    private static void DrawRuns(
        DrawingContext context, IReadOnlyList<double?> values, Func<int, double> x, Func<double, double> y, double bottom,
        IBrush? fillBrush, IPen pen, IBrush? dots)
    {
        int index = 0;
        while (index < values.Count)
        {
            if (values[index] is null)
            {
                index++;
                continue;
            }

            var points = new List<Point>();
            while (index < values.Count && values[index] is double value)
            {
                points.Add(new Point(x(index), y(value)));
                index++;
            }

            if (points.Count > 1)
            {
                if (fillBrush is not null)
                {
                    var area = new StreamGeometry();
                    using (StreamGeometryContext path = area.Open())
                    {
                        path.BeginFigure(new Point(points[0].X, bottom), true);
                        foreach (Point point in points)
                        {
                            path.LineTo(point);
                        }

                        path.LineTo(new Point(points[^1].X, bottom));
                        path.EndFigure(true);
                    }

                    context.DrawGeometry(fillBrush, null, area);
                }

                var line = new StreamGeometry();
                using (StreamGeometryContext path = line.Open())
                {
                    path.BeginFigure(points[0], false);
                    foreach (Point point in points.Skip(1))
                    {
                        path.LineTo(point);
                    }

                    path.EndFigure(false);
                }

                context.DrawGeometry(null, pen, line);
            }

            if (dots is not null)
            {
                foreach (Point point in points)
                {
                    context.DrawEllipse(dots, null, point, 3, 3);
                }
            }

        }
    }

    private static IBrush? FillOf(IBrush line)
    {
        return line is ISolidColorBrush solid ? new SolidColorBrush(solid.Color, FillOpacity) : null;
    }

    private int IndexAt(Point point)
    {
        int count = Main?.Count ?? 0;
        if (count == 0 || Bounds.Width <= 2 * SidePadding)
        {
            return -1;
        }

        double step = count == 1 ? Bounds.Width : (Bounds.Width - 2 * SidePadding) / (count - 1);
        int index = (int)Math.Round((point.X - (count == 1 ? Bounds.Width / 2 : SidePadding)) / step);
        return index >= 0 && index < count && Main![index] is not null ? index : -1;
    }
}
