using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// History's deep-work chart: one bar per day (teal when the day met the goal, slate otherwise, today outlined), the
/// goal as a dashed line, and the days' labels underneath (every one for a week, every fifth for a month).
/// </summary>
public sealed class DeepWorkChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<DeepWorkBar>?> BarsProperty =
        AvaloniaProperty.Register<DeepWorkChart, IReadOnlyList<DeepWorkBar>?>(nameof(Bars));

    public static readonly StyledProperty<double> GoalLineProperty =
        AvaloniaProperty.Register<DeepWorkChart, double>(nameof(GoalLine));

    private const double LabelHeight = 18;
    private static readonly IBrush MetBrush = new SolidColorBrush(Color.Parse("#2A9D8F"));
    private static readonly IBrush UnderBrush = new SolidColorBrush(Color.Parse("#64748B"));
    private static readonly IBrush TrackBrush = new SolidColorBrush(Color.Parse("#1AFFFFFF"));
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.Parse("#B3FFFFFF"));
    private static readonly IPen TodayPen = new Pen(Brushes.White, 1.5);
    private static readonly IPen GoalPen = new Pen(new SolidColorBrush(Color.Parse("#F4A261")), 1.5, new DashStyle([4, 3], 0));

    static DeepWorkChart()
    {
        AffectsRender<DeepWorkChart>(BarsProperty, GoalLineProperty);
    }

    public IReadOnlyList<DeepWorkBar>? Bars
    {
        get => GetValue(BarsProperty);
        set => SetValue(BarsProperty, value);
    }

    public double GoalLine
    {
        get => GetValue(GoalLineProperty);
        set => SetValue(GoalLineProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        IReadOnlyList<DeepWorkBar>? bars = Bars;
        if (bars is null || bars.Count == 0 || Bounds.Width <= 0 || Bounds.Height <= LabelHeight)
        {
            return;
        }

        double plot = Bounds.Height - LabelHeight;
        double slot = Bounds.Width / bars.Count;
        double width = Math.Max(2, slot * 0.7);
        int labelEvery = bars.Count <= 7 ? 1 : 5;
        for (int i = 0; i < bars.Count; i++)
        {
            DeepWorkBar bar = bars[i];
            double x = i * slot + (slot - width) / 2;
            context.FillRectangle(TrackBrush, new Rect(x, 0, width, plot), 3);
            double height = Math.Clamp(bar.Fraction, 0, 1) * plot;
            if (height > 0)
            {
                context.FillRectangle(bar.Met ? MetBrush : UnderBrush, new Rect(x, plot - height, width, height), 3);
            }

            if (bar.IsToday)
            {
                context.DrawRectangle(null, TodayPen, new Rect(x, 0, width, plot), 3, 3);
            }

            if (i % labelEvery == 0 || bar.IsToday)
            {
                var text = new FormattedText(bar.Label, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    Typeface.Default, 11, LabelBrush);
                context.DrawText(text, new Point(i * slot + (slot - text.Width) / 2, plot + 3));
            }
        }

        double goalY = plot - Math.Clamp(GoalLine, 0, 1) * plot;
        context.DrawLine(GoalPen, new Point(0, goalY), new Point(Bounds.Width, goalY));
    }
}
