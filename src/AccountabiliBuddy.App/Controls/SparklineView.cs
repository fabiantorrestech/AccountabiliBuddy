using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// A small trend line of a goal's recent day values (round 2, change note §4a: an amount goal's row, e.g. weight): one point per day
/// that has a value (days without one are skipped, the line joins across them), scaled to the values' own range, with a dot on the
/// latest value. No axes.
/// </summary>
public sealed class SparklineView : Control
{
    /// <summary>The days' values, oldest first; null for a day without one.</summary>
    public static readonly StyledProperty<IReadOnlyList<double?>?> ValuesProperty =
        AvaloniaProperty.Register<SparklineView, IReadOnlyList<double?>?>(nameof(Values));

    public static readonly StyledProperty<IBrush?> StrokeBrushProperty =
        AvaloniaProperty.Register<SparklineView, IBrush?>(nameof(StrokeBrush));

    static SparklineView()
    {
        AffectsRender<SparklineView>(ValuesProperty, StrokeBrushProperty);
    }

    public IReadOnlyList<double?>? Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public IBrush? StrokeBrush
    {
        get => GetValue(StrokeBrushProperty);
        set => SetValue(StrokeBrushProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(double.IsInfinity(availableSize.Width) ? 64 : Math.Min(availableSize.Width, 64), 22);
    }

    public override void Render(DrawingContext context)
    {
        if (Values is not { Count: > 1 } values || StrokeBrush is null)
        {
            return;
        }

        List<(int Index, double Value)> points = values
            .Select((value, index) => (index, value))
            .Where(point => point.value is not null)
            .Select(point => (point.index, point.value!.Value))
            .ToList();
        if (points.Count == 0)
        {
            return;
        }

        double min = points.Min(point => point.Value);
        double max = points.Max(point => point.Value);
        double span = max - min < 1e-9 ? 1 : max - min;
        double inset = 3;
        double width = Math.Max(1, Bounds.Width - 2 * inset);
        double height = Math.Max(1, Bounds.Height - 2 * inset);
        Point At((int Index, double Value) point) => new(
            inset + width * point.Index / Math.Max(1, values.Count - 1),
            inset + height * (1 - ((point.Value - min) / span)));

        var pen = new Pen(StrokeBrush, 1.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        for (int i = 1; i < points.Count; i++)
        {
            context.DrawLine(pen, At(points[i - 1]), At(points[i]));
        }

        context.DrawEllipse(StrokeBrush, null, At(points[^1]), 2.5, 2.5);
    }
}
