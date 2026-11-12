using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// A thin progress bar with an optional mark (round 2, change note §4a: the Target tile's "where you should be today" mark; the goal
/// rows' bars without one). Width follows its slot; the fill and the mark are shares of it (0 to 1).
/// </summary>
public sealed class MarkedBar : Control
{
    public static readonly StyledProperty<double> FractionProperty =
        AvaloniaProperty.Register<MarkedBar, double>(nameof(Fraction));

    /// <summary>Where the mark goes (0 to 1); NaN for none.</summary>
    public static readonly StyledProperty<double> MarkProperty =
        AvaloniaProperty.Register<MarkedBar, double>(nameof(Mark), double.NaN);

    public static readonly StyledProperty<double> BarHeightProperty =
        AvaloniaProperty.Register<MarkedBar, double>(nameof(BarHeight), 6);

    public static readonly StyledProperty<IBrush?> FillBrushProperty =
        AvaloniaProperty.Register<MarkedBar, IBrush?>(nameof(FillBrush));

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<MarkedBar, IBrush?>(nameof(TrackBrush));

    public static readonly StyledProperty<IBrush?> MarkBrushProperty =
        AvaloniaProperty.Register<MarkedBar, IBrush?>(nameof(MarkBrush));

    static MarkedBar()
    {
        AffectsRender<MarkedBar>(FractionProperty, MarkProperty, FillBrushProperty, TrackBrushProperty, MarkBrushProperty);
        AffectsMeasure<MarkedBar>(BarHeightProperty);
    }

    public double Fraction
    {
        get => GetValue(FractionProperty);
        set => SetValue(FractionProperty, value);
    }

    public double Mark
    {
        get => GetValue(MarkProperty);
        set => SetValue(MarkProperty, value);
    }

    public double BarHeight
    {
        get => GetValue(BarHeightProperty);
        set => SetValue(BarHeightProperty, value);
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

    public IBrush? MarkBrush
    {
        get => GetValue(MarkBrushProperty);
        set => SetValue(MarkBrushProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double markRoom = double.IsNaN(Mark) ? 0 : 6;
        return new Size(0, BarHeight + markRoom);
    }

    public override void Render(DrawingContext context)
    {
        double width = Bounds.Width;
        if (width <= 0)
        {
            return;
        }

        double top = (Bounds.Height - BarHeight) / 2;
        double radius = BarHeight / 2;
        var track = new Rect(0, top, width, BarHeight);
        if (TrackBrush is not null)
        {
            context.DrawRectangle(TrackBrush, null, new RoundedRect(track, radius));
        }

        double share = double.IsNaN(Fraction) ? 0 : Math.Clamp(Fraction, 0, 1);
        if (share > 0 && FillBrush is not null)
        {
            context.DrawRectangle(FillBrush, null, new RoundedRect(new Rect(0, top, Math.Max(BarHeight, width * share), BarHeight), radius));
        }

        if (!double.IsNaN(Mark) && MarkBrush is not null)
        {
            double x = Math.Clamp(Mark, 0, 1) * width;
            context.DrawLine(new Pen(MarkBrush, 2), new Point(x, 0), new Point(x, Bounds.Height));
        }
    }
}
