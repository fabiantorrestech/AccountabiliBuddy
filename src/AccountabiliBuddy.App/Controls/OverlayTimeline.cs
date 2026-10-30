using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// The overlay's 4 px timeline (14a): what is done in Accent, what is left in Upcoming, the break in Sleep, what comes next in Plan, with a
/// thin ink tick where now is. Fractions are shares of the whole line; it redraws only when one changes.
/// </summary>
public sealed class OverlayTimeline : Control
{
    public static readonly StyledProperty<double> DoneProperty = AvaloniaProperty.Register<OverlayTimeline, double>(nameof(Done));
    public static readonly StyledProperty<double> LeftProperty = AvaloniaProperty.Register<OverlayTimeline, double>(nameof(Left));
    public static readonly StyledProperty<double> BreakProperty = AvaloniaProperty.Register<OverlayTimeline, double>(nameof(Break));
    public static readonly StyledProperty<double> NextProperty = AvaloniaProperty.Register<OverlayTimeline, double>(nameof(Next));
    public static readonly StyledProperty<IBrush?> DoneBrushProperty = AvaloniaProperty.Register<OverlayTimeline, IBrush?>(nameof(DoneBrush));
    public static readonly StyledProperty<IBrush?> LeftBrushProperty = AvaloniaProperty.Register<OverlayTimeline, IBrush?>(nameof(LeftBrush));
    public static readonly StyledProperty<IBrush?> BreakBrushProperty = AvaloniaProperty.Register<OverlayTimeline, IBrush?>(nameof(BreakBrush));
    public static readonly StyledProperty<IBrush?> NextBrushProperty = AvaloniaProperty.Register<OverlayTimeline, IBrush?>(nameof(NextBrush));
    public static readonly StyledProperty<IBrush?> TickBrushProperty = AvaloniaProperty.Register<OverlayTimeline, IBrush?>(nameof(TickBrush));

    static OverlayTimeline()
    {
        AffectsRender<OverlayTimeline>(DoneProperty, LeftProperty, BreakProperty, NextProperty, DoneBrushProperty, LeftBrushProperty, BreakBrushProperty,
            NextBrushProperty, TickBrushProperty);
    }

    public double Done { get => GetValue(DoneProperty); set => SetValue(DoneProperty, value); }

    public double Left { get => GetValue(LeftProperty); set => SetValue(LeftProperty, value); }

    public double Break { get => GetValue(BreakProperty); set => SetValue(BreakProperty, value); }

    public double Next { get => GetValue(NextProperty); set => SetValue(NextProperty, value); }

    public IBrush? DoneBrush { get => GetValue(DoneBrushProperty); set => SetValue(DoneBrushProperty, value); }

    public IBrush? LeftBrush { get => GetValue(LeftBrushProperty); set => SetValue(LeftBrushProperty, value); }

    public IBrush? BreakBrush { get => GetValue(BreakBrushProperty); set => SetValue(BreakBrushProperty, value); }

    public IBrush? NextBrush { get => GetValue(NextBrushProperty); set => SetValue(NextBrushProperty, value); }

    public IBrush? TickBrush { get => GetValue(TickBrushProperty); set => SetValue(TickBrushProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? 80 : availableSize.Width;
        return new Size(width, 4);
    }

    public override void Render(DrawingContext context)
    {
        double total = Done + Left + Break + Next;
        if (total <= 0 || Bounds.Width <= 0)
        {
            return;
        }

        double width = Bounds.Width;
        double top = (Bounds.Height - 4) / 2;
        double x = 0;
        x = Segment(context, x, Done, total, width, top, DoneBrush);
        double nowX = x;
        x = Segment(context, x, Left, total, width, top, LeftBrush);
        x = Segment(context, x, Break, total, width, top, BreakBrush);
        Segment(context, x, Next, total, width, top, NextBrush);
        if (TickBrush is { } tick && Done > 0)
        {
            context.FillRectangle(tick, new Rect(Math.Max(0, nowX - 1), top - 2, 2, 8));
        }
    }

    private static double Segment(DrawingContext context, double x, double share, double total, double width, double top, IBrush? brush)
    {
        double length = share / total * width;
        if (brush is not null && length > 0)
        {
            // A 1 px gap between segments keeps them apart; each is a rounded 4 px bar.
            context.DrawRectangle(brush, null, new RoundedRect(new Rect(x, top, Math.Max(1, length - 1), 4), 2));
        }

        return x + length;
    }
}
