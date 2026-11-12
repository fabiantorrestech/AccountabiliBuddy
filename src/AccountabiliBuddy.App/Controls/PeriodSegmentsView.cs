using System.Globalization;
using AccountabiliBuddy.Core.Goals;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// The This period tile's segmented bar (round 2, change note §4a: "3 of 7 · Days this week"): one rounded segment per day of the
/// period across the tile's width, the day's letter under it (today's bold). Hit = the goal's colour; today = an accent outline filling
/// with its progress; paused = hatched lightly; anything else = the empty track colour.
/// </summary>
public sealed class PeriodSegmentsView : Control
{
    public static readonly StyledProperty<IReadOnlyList<GoalStripCell>?> CellsProperty =
        AvaloniaProperty.Register<PeriodSegmentsView, IReadOnlyList<GoalStripCell>?>(nameof(Cells));

    public static readonly StyledProperty<IBrush?> FillBrushProperty =
        AvaloniaProperty.Register<PeriodSegmentsView, IBrush?>(nameof(FillBrush));

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<PeriodSegmentsView, IBrush?>(nameof(TrackBrush));

    public static readonly StyledProperty<IBrush?> TodayBrushProperty =
        AvaloniaProperty.Register<PeriodSegmentsView, IBrush?>(nameof(TodayBrush));

    public static readonly StyledProperty<IBrush?> LabelBrushProperty =
        AvaloniaProperty.Register<PeriodSegmentsView, IBrush?>(nameof(LabelBrush));

    public static readonly StyledProperty<IBrush?> TodayLabelBrushProperty =
        AvaloniaProperty.Register<PeriodSegmentsView, IBrush?>(nameof(TodayLabelBrush));

    private const double SegmentHeight = 8;
    private const double Gap = 6;
    private const double LabelGap = 6;
    private const double LabelSize = 11;
    private static readonly Typeface Regular = new(FontFamily.Default);
    private static readonly Typeface Bold = new(FontFamily.Default, FontStyle.Normal, FontWeight.Bold);

    static PeriodSegmentsView()
    {
        AffectsRender<PeriodSegmentsView>(CellsProperty, FillBrushProperty, TrackBrushProperty, TodayBrushProperty, LabelBrushProperty, TodayLabelBrushProperty);
    }

    public IReadOnlyList<GoalStripCell>? Cells
    {
        get => GetValue(CellsProperty);
        set => SetValue(CellsProperty, value);
    }

    public IBrush? FillBrush
    {
        get => GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public IBrush? TodayBrush
    {
        get => GetValue(TodayBrushProperty);
        set => SetValue(TodayBrushProperty, value);
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

    protected override Size MeasureOverride(Size availableSize)
    {
        bool labels = Cells is { Count: <= 7 };
        return new Size(0, SegmentHeight + 4 + (labels ? LabelGap + 14 : 0));
    }

    public override void Render(DrawingContext context)
    {
        if (Cells is not { Count: > 0 } cells || Bounds.Width <= 0)
        {
            return;
        }

        bool labels = cells.Count <= 7;
        double gap = cells.Count > 7 ? 3 : Gap;
        double width = (Bounds.Width - 4 - (cells.Count - 1) * gap) / cells.Count;
        IBrush fill = FillBrush ?? Brushes.Gray;
        IBrush track = TrackBrush ?? Brushes.LightGray;
        for (int i = 0; i < cells.Count; i++)
        {
            GoalStripCell cell = cells[i];
            var rect = new Rect(2 + i * (width + gap), 2, Math.Max(1, width), SegmentHeight);
            var rounded = new RoundedRect(rect, SegmentHeight / 2);
            context.DrawRectangle(cell.Kind == GoalStripCellKind.Hit ? fill : track, null, rounded);
            if (cell.Kind == GoalStripCellKind.Today && cell.Progress > 0)
            {
                using (context.PushClip(rounded))
                {
                    context.DrawRectangle(fill, null, new Rect(rect.X, rect.Y, rect.Width * Math.Clamp(cell.Progress, 0, 1), rect.Height));
                }
            }

            if (cell.Kind == GoalStripCellKind.Paused)
            {
                using (context.PushOpacity(0.4))
                {
                    context.DrawRectangle(null, new Pen(fill, 1, new DashStyle([1, 2], 0)), rounded);
                }
            }

            if (cell.IsToday)
            {
                context.DrawRectangle(null, new Pen(TodayBrush ?? fill, 1.5), new RoundedRect(rect.Inflate(1), SegmentHeight / 2 + 1));
            }

            if (labels)
            {
                string letter = cell.Day.ToString("ddd", CultureInfo.InvariantCulture)[..1];
                var text = new FormattedText(letter, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, cell.IsToday ? Bold : Regular, LabelSize,
                    cell.IsToday ? TodayLabelBrush ?? LabelBrush : LabelBrush);
                context.DrawText(text, new Point(rect.X + (rect.Width - text.Width) / 2, rect.Bottom + LabelGap));
            }
        }
    }
}
