using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AccountabiliBuddy.App.Controls;

/// <summary>The "Less ▪▪▪▪▪ More" key under a heatmap, in the same ramp (<see cref="HeatRamp"/>).</summary>
public sealed class HeatLegend : Control
{
    public static readonly StyledProperty<IBrush?> EmptyBrushProperty =
        AvaloniaProperty.Register<HeatLegend, IBrush?>(nameof(EmptyBrush));

    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<HeatLegend, IBrush?>(nameof(AccentBrush));

    public static readonly StyledProperty<IBrush?> LabelBrushProperty =
        AvaloniaProperty.Register<HeatLegend, IBrush?>(nameof(LabelBrush));

    private const double Square = 12;
    private const double Gap = 4;
    private static readonly Typeface LabelTypeface = new(FontFamily.Default);

    static HeatLegend()
    {
        AffectsRender<HeatLegend>(EmptyBrushProperty, AccentBrushProperty, LabelBrushProperty);
    }

    public IBrush? EmptyBrush
    {
        get => GetValue(EmptyBrushProperty);
        set => SetValue(EmptyBrushProperty, value);
    }

    public IBrush? AccentBrush
    {
        get => GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public IBrush? LabelBrush
    {
        get => GetValue(LabelBrushProperty);
        set => SetValue(LabelBrushProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(Text("Less").Width + Text("More").Width + 5 * Square + 4 * Gap + 2 * 8, 18);
    }

    public override void Render(DrawingContext context)
    {
        IBrush[] ramp = HeatRamp.Brushes(EmptyBrush, AccentBrush);
        FormattedText less = Text("Less");
        FormattedText more = Text("More");
        double x = 0;
        context.DrawText(less, new Point(x, (Bounds.Height - less.Height) / 2));
        x += less.Width + 8;
        foreach (IBrush brush in ramp)
        {
            context.DrawRectangle(brush, null, new RoundedRect(new Rect(x, (Bounds.Height - Square) / 2, Square, Square), 3));
            x += Square + Gap;
        }

        context.DrawText(more, new Point(x + 4, (Bounds.Height - more.Height) / 2));
    }

    private FormattedText Text(string text)
    {
        return new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelTypeface, 12, LabelBrush);
    }
}
