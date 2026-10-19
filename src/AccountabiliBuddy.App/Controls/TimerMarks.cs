using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// Division marks over the on-screen timer's bar or ring (e.g. quarters), drawn in the panel's colour so they read
/// as notches in the bar. 0 or 1 divisions draws nothing.
/// </summary>
public sealed class TimerMarks : Control
{
    public static readonly StyledProperty<int> DivisionsProperty =
        AvaloniaProperty.Register<TimerMarks, int>(nameof(Divisions));

    public static readonly StyledProperty<bool> IsRingProperty =
        AvaloniaProperty.Register<TimerMarks, bool>(nameof(IsRing));

    /// <summary>Width of the ring's stroke and its gap from the edge (see the timer window).</summary>
    public static readonly StyledProperty<double> RingStrokeProperty =
        AvaloniaProperty.Register<TimerMarks, double>(nameof(RingStroke), 6);

    public static readonly StyledProperty<double> RingInsetProperty =
        AvaloniaProperty.Register<TimerMarks, double>(nameof(RingInset), 3);

    private const double MarkWidth = 2;
    private static readonly IBrush MarkBrush = new SolidColorBrush(Color.FromRgb(0x15, 0x19, 0x1E));

    static TimerMarks()
    {
        AffectsRender<TimerMarks>(DivisionsProperty, IsRingProperty, RingStrokeProperty, RingInsetProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<TimerMarks>(false);
    }

    public int Divisions
    {
        get => GetValue(DivisionsProperty);
        set => SetValue(DivisionsProperty, value);
    }

    public bool IsRing
    {
        get => GetValue(IsRingProperty);
        set => SetValue(IsRingProperty, value);
    }

    public double RingStroke
    {
        get => GetValue(RingStrokeProperty);
        set => SetValue(RingStrokeProperty, value);
    }

    public double RingInset
    {
        get => GetValue(RingInsetProperty);
        set => SetValue(RingInsetProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Divisions < 2)
        {
            return;
        }

        var pen = new Pen(MarkBrush, MarkWidth);
        for (int i = 1; i < Divisions; i++)
        {
            double fraction = (double)i / Divisions;
            if (IsRing)
            {
                DrawRingTick(context, pen, fraction);
            }
            else
            {
                double x = fraction * Bounds.Width;
                context.DrawLine(pen, new Point(x, 0), new Point(x, Bounds.Height));
            }
        }
    }

    private void DrawRingTick(DrawingContext context, Pen pen, double fraction)
    {
        // Same circle as the ring: centred, starting at the top and going clockwise.
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        double outer = (Math.Min(Bounds.Width, Bounds.Height) / 2) - RingInset;
        double inner = outer - RingStroke;
        double angle = (fraction * 2 * Math.PI) - (Math.PI / 2);
        var direction = new Vector(Math.Cos(angle), Math.Sin(angle));
        context.DrawLine(pen, center + (direction * (inner - 1)), center + (direction * (outer + 1)));
    }
}
