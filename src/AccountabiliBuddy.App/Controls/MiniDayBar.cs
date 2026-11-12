using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// The Today sidebar's small day bar (round 2, brief §4.2 "Next up"): today's planned parts as coloured pieces on a thin track, with
/// a short ink mark at the current time. Drawn by hand; it redraws only when its segments, the time mark or a brush changes.
/// </summary>
public sealed class MiniDayBar : Control
{
    public static readonly StyledProperty<IReadOnlyList<MiniDayBarSegment>?> SegmentsProperty =
        AvaloniaProperty.Register<MiniDayBar, IReadOnlyList<MiniDayBarSegment>?>(nameof(Segments));

    public static readonly StyledProperty<double> NowFractionProperty =
        AvaloniaProperty.Register<MiniDayBar, double>(nameof(NowFraction), -1);

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<MiniDayBar, IBrush?>(nameof(TrackBrush));

    public static readonly StyledProperty<IBrush?> NowBrushProperty =
        AvaloniaProperty.Register<MiniDayBar, IBrush?>(nameof(NowBrush));

    private const double BarHeight = 8;
    private const double Gap = 2;

    static MiniDayBar()
    {
        AffectsRender<MiniDayBar>(SegmentsProperty, NowFractionProperty, TrackBrushProperty, NowBrushProperty);
    }

    public IReadOnlyList<MiniDayBarSegment>? Segments
    {
        get => GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    /// <summary>Where now sits on the bar, 0 to 1; outside that range (the default -1) no mark is drawn.</summary>
    public double NowFraction
    {
        get => GetValue(NowFractionProperty);
        set => SetValue(NowFractionProperty, value);
    }

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public IBrush? NowBrush
    {
        get => GetValue(NowBrushProperty);
        set => SetValue(NowBrushProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(0, 14);

    public override void Render(DrawingContext context)
    {
        double width = Bounds.Width;
        double top = (Bounds.Height - BarHeight) / 2;
        if (width <= 0)
        {
            return;
        }

        if (TrackBrush is { } track)
        {
            context.DrawRectangle(track, null, new Rect(0, top, width, BarHeight), 3, 3);
        }

        foreach (MiniDayBarSegment segment in Segments ?? [])
        {
            double from = Math.Clamp(segment.From, 0, 1) * width;
            double to = Math.Clamp(segment.To, 0, 1) * width;
            double pieceWidth = to - from - Gap;
            if (pieceWidth > 1)
            {
                context.DrawRectangle(segment.Brush, null, new Rect(from, top, pieceWidth, BarHeight), 2, 2);
            }
        }

        if (NowFraction is >= 0 and <= 1 && NowBrush is { } now)
        {
            double x = Math.Round(NowFraction * width);
            context.DrawRectangle(now, null, new Rect(x - 1, 0, 2, Bounds.Height), 1, 1);
        }
    }
}
