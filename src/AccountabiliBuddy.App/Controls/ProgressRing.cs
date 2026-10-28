using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// A round progress meter: a full track circle and, over it, an arc from twelve o'clock clockwise for <see cref="Value"/>
/// (0 to 1), with round caps. Drawn by hand like the deep-work chart; it redraws only when one of its properties changes.
/// </summary>
public sealed class ProgressRing : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<ProgressRing, double>(nameof(Value));

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<ProgressRing, IBrush?>(nameof(TrackBrush));

    public static readonly StyledProperty<IBrush?> ArcBrushProperty =
        AvaloniaProperty.Register<ProgressRing, IBrush?>(nameof(ArcBrush));

    public static readonly StyledProperty<double> ThicknessProperty =
        AvaloniaProperty.Register<ProgressRing, double>(nameof(Thickness), 8);

    // Below this the arc is a speck under its round caps; above the other bound it would be a closed circle.
    private const double MinDrawn = 0.001;
    private const double FullCircle = 0.9999;

    static ProgressRing()
    {
        AffectsRender<ProgressRing>(ValueProperty, TrackBrushProperty, ArcBrushProperty, ThicknessProperty);
    }

    /// <summary>How much of the ring is filled, 0 to 1 (values outside are cut to it).</summary>
    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public IBrush? ArcBrush
    {
        get => GetValue(ArcBrushProperty);
        set => SetValue(ArcBrushProperty, value);
    }

    /// <summary>The stroke width of the track and the arc.</summary>
    public double Thickness
    {
        get => GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        double thickness = Math.Max(0, Thickness);
        double diameter = Math.Min(Bounds.Width, Bounds.Height);
        double radius = (diameter - thickness) / 2;
        if (radius <= 0)
        {
            return;
        }

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        if (TrackBrush is { } track)
        {
            context.DrawEllipse(null, new Pen(track, thickness), center, radius, radius);
        }

        double value = Math.Clamp(Value, 0, 1);
        if (ArcBrush is not { } arcBrush || value < MinDrawn)
        {
            return;
        }

        var arcPen = new Pen(arcBrush, thickness, lineCap: PenLineCap.Round);
        if (value >= FullCircle)
        {
            context.DrawEllipse(null, arcPen, center, radius, radius);
            return;
        }

        double angle = value * 2 * Math.PI;
        var start = new Point(center.X, center.Y - radius);
        var end = new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle));
        var geometry = new StreamGeometry();
        using (StreamGeometryContext path = geometry.Open())
        {
            path.BeginFigure(start, false);
            path.ArcTo(end, new Size(radius, radius), 0, value > 0.5, SweepDirection.Clockwise);
            path.EndFigure(false);
        }

        context.DrawGeometry(null, arcPen, geometry);
    }
}
