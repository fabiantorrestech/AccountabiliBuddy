using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// One day from 12 AM to 12 AM as a pill (design 5p): rest is the track colour, focus, drift and sleep are spans over it,
/// with "12 AM 6 AM 12 PM 6 PM 12 AM" under it. Drawn by hand from the brushes it is given (theme tokens); it redraws only
/// when its spans, a brush or its size changes. Hovering a span shows its tip.
/// </summary>
public sealed class DayStripControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<DayStripSpan>?> SpansProperty =
        AvaloniaProperty.Register<DayStripControl, IReadOnlyList<DayStripSpan>?>(nameof(Spans));

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<DayStripControl, IBrush?>(nameof(TrackBrush));

    public static readonly StyledProperty<IBrush?> FocusBrushProperty =
        AvaloniaProperty.Register<DayStripControl, IBrush?>(nameof(FocusBrush));

    public static readonly StyledProperty<IBrush?> DriftBrushProperty =
        AvaloniaProperty.Register<DayStripControl, IBrush?>(nameof(DriftBrush));

    public static readonly StyledProperty<IBrush?> SleepBrushProperty =
        AvaloniaProperty.Register<DayStripControl, IBrush?>(nameof(SleepBrush));

    public static readonly StyledProperty<IBrush?> LabelBrushProperty =
        AvaloniaProperty.Register<DayStripControl, IBrush?>(nameof(LabelBrush));

    private const double BarHeight = 26;
    private const double TickGap = 6;
    private const double MinSpanWidth = 3;
    private static readonly string[] Ticks = ["12 AM", "6 AM", "12 PM", "6 PM", "12 AM"];
    private static readonly Typeface LabelTypeface = new(FontFamily.Default);

    private int _hovered = -1;

    static DayStripControl()
    {
        AffectsRender<DayStripControl>(SpansProperty, TrackBrushProperty, FocusBrushProperty, DriftBrushProperty, SleepBrushProperty, LabelBrushProperty);
    }

    public IReadOnlyList<DayStripSpan>? Spans
    {
        get => GetValue(SpansProperty);
        set => SetValue(SpansProperty, value);
    }

    /// <summary>The rest of the day (Surface2).</summary>
    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public IBrush? FocusBrush
    {
        get => GetValue(FocusBrushProperty);
        set => SetValue(FocusBrushProperty, value);
    }

    public IBrush? DriftBrush
    {
        get => GetValue(DriftBrushProperty);
        set => SetValue(DriftBrushProperty, value);
    }

    public IBrush? SleepBrush
    {
        get => GetValue(SleepBrushProperty);
        set => SetValue(SleepBrushProperty, value);
    }

    public IBrush? LabelBrush
    {
        get => GetValue(LabelBrushProperty);
        set => SetValue(LabelBrushProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        return new Size(width, BarHeight + TickGap + 16);
    }

    public override void Render(DrawingContext context)
    {
        if (Bounds.Width <= 0)
        {
            return;
        }

        var bar = new Rect(0, 0, Bounds.Width, BarHeight);
        if (TrackBrush is { } track)
        {
            context.DrawRectangle(track, null, new RoundedRect(bar, BarHeight / 2));
        }

        using (context.PushClip(new RoundedRect(bar, BarHeight / 2)))
        {
            // Sleep first, then focus, then drift on top, so a short drift mark isn't hidden by the focus it interrupted.
            foreach (DayStripKind kind in new[] { DayStripKind.Sleep, DayStripKind.Focus, DayStripKind.Drift })
            {
                IBrush? brush = BrushOf(kind);
                if (brush is null)
                {
                    continue;
                }

                foreach (DayStripSpan span in (Spans ?? []).Where(span => span.Kind == kind))
                {
                    double from = Math.Clamp(span.From, 0, 1) * Bounds.Width;
                    double to = Math.Clamp(span.To, 0, 1) * Bounds.Width;
                    context.DrawRectangle(brush, null, new Rect(from, 0, Math.Max(MinSpanWidth, to - from), BarHeight));
                }
            }
        }

        for (int i = 0; i < Ticks.Length; i++)
        {
            var text = new FormattedText(Ticks[i], CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelTypeface, 12, LabelBrush);
            double x = i == 0 ? 0 : i == Ticks.Length - 1 ? Bounds.Width - text.Width : Bounds.Width * i / (Ticks.Length - 1) - text.Width / 2;
            context.DrawText(text, new Point(x, BarHeight + TickGap));
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Point point = e.GetPosition(this);
        int index = -1;
        if (Spans is { } spans && point.Y <= BarHeight && Bounds.Width > 0)
        {
            double at = point.X / Bounds.Width;
            // The topmost span under the pointer: drift over focus over sleep.
            index = spans.Select((span, i) => (span, i))
                .Where(entry => at >= entry.span.From - MinSpanWidth / Bounds.Width && at <= entry.span.To + MinSpanWidth / Bounds.Width)
                .OrderByDescending(entry => entry.span.Kind == DayStripKind.Drift ? 2 : entry.span.Kind == DayStripKind.Focus ? 1 : 0)
                .Select(entry => entry.i)
                .DefaultIfEmpty(-1)
                .First();
        }

        if (index == _hovered)
        {
            return;
        }

        _hovered = index;
        ToolTip.SetTip(this, index >= 0 ? Spans![index].Tip : null);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hovered = -1;
        ToolTip.SetTip(this, null);
    }

    private IBrush? BrushOf(DayStripKind kind) => kind switch
    {
        DayStripKind.Focus => FocusBrush,
        DayStripKind.Drift => DriftBrush,
        _ => SleepBrush,
    };
}
