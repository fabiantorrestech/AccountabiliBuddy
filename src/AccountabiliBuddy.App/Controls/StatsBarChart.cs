using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// The Stats page's bar chart (design 5p): one bar per day (or hour), coloured by <see cref="StatsBarKind"/>, an optional
/// dashed goal line with its label, and the labels underneath, today's in bold. Days that haven't happened get no bar. Drawn
/// by hand from the brushes it is given (theme tokens); it redraws only when its data, a brush or its size changes. Hovering
/// a bar shows its tip, and a click on a bar with a date runs <see cref="BarCommand"/> with that date.
/// </summary>
public sealed class StatsBarChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<StatsBar>?> BarsProperty =
        AvaloniaProperty.Register<StatsBarChart, IReadOnlyList<StatsBar>?>(nameof(Bars));

    /// <summary>The goal's height, 0 to 1 of the scale; NaN draws no goal line.</summary>
    public static readonly StyledProperty<double> GoalFractionProperty =
        AvaloniaProperty.Register<StatsBarChart, double>(nameof(GoalFraction), double.NaN);

    public static readonly StyledProperty<string?> GoalTextProperty =
        AvaloniaProperty.Register<StatsBarChart, string?>(nameof(GoalText));

    public static readonly StyledProperty<IBrush?> MetBrushProperty =
        AvaloniaProperty.Register<StatsBarChart, IBrush?>(nameof(MetBrush));

    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<StatsBarChart, IBrush?>(nameof(AccentBrush));

    public static readonly StyledProperty<IBrush?> PlanBrushProperty =
        AvaloniaProperty.Register<StatsBarChart, IBrush?>(nameof(PlanBrush));

    public static readonly StyledProperty<IBrush?> DriftBrushProperty =
        AvaloniaProperty.Register<StatsBarChart, IBrush?>(nameof(DriftBrush));

    public static readonly StyledProperty<IBrush?> LabelBrushProperty =
        AvaloniaProperty.Register<StatsBarChart, IBrush?>(nameof(LabelBrush));

    public static readonly StyledProperty<IBrush?> TodayLabelBrushProperty =
        AvaloniaProperty.Register<StatsBarChart, IBrush?>(nameof(TodayLabelBrush));

    public static readonly StyledProperty<IBrush?> GoalBrushProperty =
        AvaloniaProperty.Register<StatsBarChart, IBrush?>(nameof(GoalBrush));

    public static readonly StyledProperty<System.Windows.Input.ICommand?> BarCommandProperty =
        AvaloniaProperty.Register<StatsBarChart, System.Windows.Input.ICommand?>(nameof(BarCommand));

    private const double LabelHeight = 26;
    private const double GoalLabelHeight = 20;
    private const double MinLabelSlot = 26;
    private const double MinChartHeight = 150;
    private const double BarRadius = 8;
    private const double SmallRadius = 2;
    private static readonly Typeface LabelTypeface = new(FontFamily.Default);
    private static readonly Typeface TodayTypeface = new(FontFamily.Default, weight: FontWeight.Bold);

    private int _hovered = -1;

    static StatsBarChart()
    {
        AffectsRender<StatsBarChart>(
            BarsProperty, GoalFractionProperty, GoalTextProperty, MetBrushProperty, AccentBrushProperty, PlanBrushProperty,
            DriftBrushProperty, LabelBrushProperty, TodayLabelBrushProperty, GoalBrushProperty);
    }

    public IReadOnlyList<StatsBar>? Bars
    {
        get => GetValue(BarsProperty);
        set => SetValue(BarsProperty, value);
    }

    public double GoalFraction
    {
        get => GetValue(GoalFractionProperty);
        set => SetValue(GoalFractionProperty, value);
    }

    public string? GoalText
    {
        get => GetValue(GoalTextProperty);
        set => SetValue(GoalTextProperty, value);
    }

    public IBrush? MetBrush
    {
        get => GetValue(MetBrushProperty);
        set => SetValue(MetBrushProperty, value);
    }

    public IBrush? AccentBrush
    {
        get => GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public IBrush? PlanBrush
    {
        get => GetValue(PlanBrushProperty);
        set => SetValue(PlanBrushProperty, value);
    }

    public IBrush? DriftBrush
    {
        get => GetValue(DriftBrushProperty);
        set => SetValue(DriftBrushProperty, value);
    }

    public IBrush? LabelBrush
    {
        get => GetValue(LabelBrushProperty);
        set => SetValue(LabelBrushProperty, value);
    }

    public IBrush? TodayLabelBrush
    {
        get => GetValue(TodayLabelBrushProperty);
        set => SetValue(TodayLabelBrushProperty, value);
    }

    public IBrush? GoalBrush
    {
        get => GetValue(GoalBrushProperty);
        set => SetValue(GoalBrushProperty, value);
    }

    /// <summary>Run with the bar's date when a bar that has one is clicked.</summary>
    public System.Windows.Input.ICommand? BarCommand
    {
        get => GetValue(BarCommandProperty);
        set => SetValue(BarCommandProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        return new Size(width, MinChartHeight);
    }

    public override void Render(DrawingContext context)
    {
        IReadOnlyList<StatsBar>? bars = Bars;
        double top = double.IsNaN(GoalFraction) ? 0 : GoalLabelHeight;
        double plot = Bounds.Height - LabelHeight - top;
        if (bars is null || bars.Count == 0 || Bounds.Width <= 0 || plot <= 0)
        {
            return;
        }

        double slot = Bounds.Width / bars.Count;
        double width = Math.Max(2, slot * 0.86);
        int labelEvery = LabelEvery(bars.Count, slot);
        for (int i = 0; i < bars.Count; i++)
        {
            StatsBar bar = bars[i];
            double x = i * slot + (slot - width) / 2;
            if (!bar.IsFuture)
            {
                double height = Math.Clamp(bar.Fraction, 0, 1) * plot;
                height = height > 0 ? Math.Max(height, 3) : 2;
                IBrush? brush = BrushOf(bar.Kind);
                if (brush is not null)
                {
                    double radius = Math.Min(BarRadius, Math.Min(width / 2, height));
                    var rect = new Rect(x, top + plot - height, width, height);
                    context.DrawRectangle(brush, null, new RoundedRect(rect, radius, radius, SmallRadius, SmallRadius));
                }
            }

            if ((i % labelEvery == 0 || bar.IsToday) && !string.IsNullOrEmpty(bar.Label))
            {
                var text = new FormattedText(bar.Label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    bar.IsToday ? TodayTypeface : LabelTypeface, 12, bar.IsToday && !bar.IsFuture ? TodayLabelBrush : LabelBrush);
                context.DrawText(text, new Point(i * slot + (slot - text.Width) / 2, top + plot + 8));
            }
        }

        if (!double.IsNaN(GoalFraction) && GoalBrush is { } goalBrush)
        {
            double y = top + plot - Math.Clamp(GoalFraction, 0, 1) * plot;
            context.DrawLine(new Pen(goalBrush, 1, new DashStyle([3, 3], 0)), new Point(0, y), new Point(Bounds.Width, y));
            if (!string.IsNullOrEmpty(GoalText))
            {
                var label = new FormattedText(GoalText, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelTypeface, 12, goalBrush);
                context.DrawText(label, new Point(Bounds.Width - label.Width, y - label.Height - 3));
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
        ToolTip.SetTip(this, index >= 0 ? Bars![index].Tip : null);
        Cursor = index >= 0 && Bars![index].Date is not null && BarCommand is not null ? new Cursor(StandardCursorType.Hand) : null;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hovered = -1;
        ToolTip.SetTip(this, null);
        Cursor = null;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        int index = IndexAt(e.GetPosition(this));
        if (index >= 0 && Bars![index] is { Date: DateOnly date } && BarCommand?.CanExecute(date) == true)
        {
            BarCommand.Execute(date);
        }
    }

    private int IndexAt(Point point)
    {
        IReadOnlyList<StatsBar>? bars = Bars;
        if (bars is null || bars.Count == 0 || Bounds.Width <= 0 || point.X < 0 || point.X >= Bounds.Width)
        {
            return -1;
        }

        int index = (int)(point.X / (Bounds.Width / bars.Count));
        return index < bars.Count && !bars[index].IsFuture ? index : -1;
    }

    private IBrush? BrushOf(StatsBarKind kind) => kind switch
    {
        StatsBarKind.Met => MetBrush,
        StatsBarKind.Accent => AccentBrush,
        StatsBarKind.Drift => DriftBrush,
        _ => PlanBrush,
    };

    // Every label while they fit, else every few so none touch.
    private static int LabelEvery(int count, double slot)
    {
        int every = 1;
        while (slot * every < MinLabelSlot && every < count)
        {
            every++;
        }

        return every;
    }
}
