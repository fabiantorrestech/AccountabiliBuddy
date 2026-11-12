using AccountabiliBuddy.Core.Goals;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// A goal's days as small cells in one row (round 2, brief §3.8, change note §4): the list's 14-day strip (thin bars), the week dots
/// of a weekly-count habit (<see cref="Dots"/>, Sun first or the user's week start, today as a ring) and the 7-day bars of a daily
/// amount (<see cref="Heights"/> give each bar its share of the day's target). Hit = solid; missed = a hollow outline; some under the
/// target = a faint fill; today = an accent outline filling from the bottom with progress; paused = hatched; not due = a faint dot.
/// The cells come from the same day states as the streak (<see cref="GoalStrip14"/>), so the strip agrees with it.
/// </summary>
public sealed class DayStateCellsView : Control
{
    public static readonly StyledProperty<IReadOnlyList<GoalStripCell>?> CellsProperty =
        AvaloniaProperty.Register<DayStateCellsView, IReadOnlyList<GoalStripCell>?>(nameof(Cells));

    /// <summary>For 7-day bars: each cell's height as a share of the day's target (0 to 1); null draws equal cells.</summary>
    public static readonly StyledProperty<IReadOnlyList<double>?> HeightsProperty =
        AvaloniaProperty.Register<DayStateCellsView, IReadOnlyList<double>?>(nameof(Heights));

    /// <summary>Round dots (the week dots) instead of bars.</summary>
    public static readonly StyledProperty<bool> DotsProperty =
        AvaloniaProperty.Register<DayStateCellsView, bool>(nameof(Dots));

    public static readonly StyledProperty<double> CellWidthProperty =
        AvaloniaProperty.Register<DayStateCellsView, double>(nameof(CellWidth), 5);

    public static readonly StyledProperty<double> CellGapProperty =
        AvaloniaProperty.Register<DayStateCellsView, double>(nameof(CellGap), 2);

    public static readonly StyledProperty<double> CellHeightProperty =
        AvaloniaProperty.Register<DayStateCellsView, double>(nameof(CellHeight), 18);

    /// <summary>Hit cells (the goal's colour).</summary>
    public static readonly StyledProperty<IBrush?> FillBrushProperty =
        AvaloniaProperty.Register<DayStateCellsView, IBrush?>(nameof(FillBrush));

    /// <summary>Today's ring (the theme's accent).</summary>
    public static readonly StyledProperty<IBrush?> TodayBrushProperty =
        AvaloniaProperty.Register<DayStateCellsView, IBrush?>(nameof(TodayBrush));

    /// <summary>Outlines and not-due cells (the theme's line).</summary>
    public static readonly StyledProperty<IBrush?> LineBrushProperty =
        AvaloniaProperty.Register<DayStateCellsView, IBrush?>(nameof(LineBrush));

    static DayStateCellsView()
    {
        AffectsRender<DayStateCellsView>(CellsProperty, HeightsProperty, DotsProperty, FillBrushProperty, TodayBrushProperty, LineBrushProperty);
        AffectsMeasure<DayStateCellsView>(CellsProperty, CellWidthProperty, CellGapProperty, CellHeightProperty, DotsProperty);
    }

    public IReadOnlyList<GoalStripCell>? Cells
    {
        get => GetValue(CellsProperty);
        set => SetValue(CellsProperty, value);
    }

    public IReadOnlyList<double>? Heights
    {
        get => GetValue(HeightsProperty);
        set => SetValue(HeightsProperty, value);
    }

    public bool Dots
    {
        get => GetValue(DotsProperty);
        set => SetValue(DotsProperty, value);
    }

    public double CellWidth
    {
        get => GetValue(CellWidthProperty);
        set => SetValue(CellWidthProperty, value);
    }

    public double CellGap
    {
        get => GetValue(CellGapProperty);
        set => SetValue(CellGapProperty, value);
    }

    public double CellHeight
    {
        get => GetValue(CellHeightProperty);
        set => SetValue(CellHeightProperty, value);
    }

    public IBrush? FillBrush
    {
        get => GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    public IBrush? TodayBrush
    {
        get => GetValue(TodayBrushProperty);
        set => SetValue(TodayBrushProperty, value);
    }

    public IBrush? LineBrush
    {
        get => GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        int count = Cells?.Count ?? 0;
        double height = Dots ? CellWidth : CellHeight;
        return new Size(count * CellWidth + Math.Max(0, count - 1) * CellGap + 2, height + 2);
    }

    public override void Render(DrawingContext context)
    {
        if (Cells is not { Count: > 0 } cells)
        {
            return;
        }

        IBrush fill = FillBrush ?? Brushes.Gray;
        IBrush line = LineBrush ?? Brushes.LightGray;
        IBrush faint = Faint(fill);
        var outline = new Pen(line, 1);
        double height = Dots ? CellWidth : CellHeight;
        for (int i = 0; i < cells.Count; i++)
        {
            GoalStripCell cell = cells[i];
            double share = Heights is { } heights && i < heights.Count ? Math.Clamp(heights[i], 0.15, 1) : 1;
            double cellHeight = height * share;
            var rect = new Rect(1 + i * (CellWidth + CellGap), 1 + height - cellHeight, CellWidth, cellHeight);
            double radius = Dots ? CellWidth / 2 : Math.Min(2, CellWidth / 2);
            var rounded = new RoundedRect(rect, radius);
            switch (cell.Kind)
            {
                case GoalStripCellKind.Hit:
                    context.DrawRectangle(fill, null, rounded);
                    break;
                case GoalStripCellKind.Partial:
                    context.DrawRectangle(faint, outline, new RoundedRect(rect.Deflate(0.5), radius));
                    break;
                case GoalStripCellKind.Missed:
                    context.DrawRectangle(null, outline, new RoundedRect(rect.Deflate(0.5), radius));
                    break;
                case GoalStripCellKind.Paused:
                    using (context.PushOpacity(0.5))
                    {
                        context.DrawRectangle(null, new Pen(line, 1, new DashStyle([1, 2], 0)), new RoundedRect(rect.Deflate(0.5), radius));
                    }

                    break;
                case GoalStripCellKind.Today:
                {
                    double progress = Math.Clamp(cell.Progress, 0, 1);
                    if (progress > 0)
                    {
                        double filled = rect.Height * progress;
                        using (context.PushClip(rounded))
                        {
                            context.DrawRectangle(fill, null, new Rect(rect.X, rect.Bottom - filled, rect.Width, filled));
                        }
                    }

                    break;
                }

                default:
                    context.DrawRectangle(line, null, Dots ? rounded : new RoundedRect(rect.Deflate(new Thickness(Math.Max(0, CellWidth / 2 - 1), 0)), 1));
                    break;
            }

            if (cell.IsToday)
            {
                context.DrawRectangle(null, new Pen(TodayBrush ?? fill, 1.5), new RoundedRect(rect.Inflate(0.25), radius + 0.25));
            }
        }
    }

    private static IBrush Faint(IBrush fill)
    {
        return fill is ISolidColorBrush solid ? new SolidColorBrush(solid.Color, 0.3) : fill;
    }
}
