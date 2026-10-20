using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// One day as a bar, like the calendar widget's Density view: a thin calendar-event band on top, the planned focus
/// track, a slimmer lane for what actually ran, an hour axis, and a red line at now (pinned to the nearer edge when
/// now is outside the bar). With sleep mode on, the night is shaded behind the lanes and sleep already lost by
/// staying up is shaded red. Hovering a stretch shows what it is; a planned block or session also lights up and
/// opens its profile when clicked (<see cref="OpenProfileCommand"/>). <see cref="Compact"/> draws it smaller for the
/// on-screen timeline, with tick labels that would overlap left out.
/// </summary>
public sealed class DayBarControl : Control
{
    public static readonly StyledProperty<DayBarLayout?> LayoutProperty =
        AvaloniaProperty.Register<DayBarControl, DayBarLayout?>(nameof(Layout));

    public static readonly StyledProperty<System.Windows.Input.ICommand?> OpenProfileCommandProperty =
        AvaloniaProperty.Register<DayBarControl, System.Windows.Input.ICommand?>(nameof(OpenProfileCommand));

    public static readonly StyledProperty<bool> CompactProperty =
        AvaloniaProperty.Register<DayBarControl, bool>(nameof(Compact));

    private static readonly Lanes Normal = new(EventHeight: 4, PlannedTop: 7, PlannedHeight: 16, ActualTop: 26, ActualHeight: 8,
        AxisTop: 38, TotalHeight: 54, NowOverhang: 3, TextSize: 11);

    private static readonly Lanes Small = new(EventHeight: 3, PlannedTop: 5, PlannedHeight: 10, ActualTop: 17, ActualHeight: 5,
        AxisTop: 24, TotalHeight: 36, NowOverhang: 2, TextSize: 9);

    private const double LabelGap = 4;

    private static readonly Color FocusColor = Color.FromRgb(15, 118, 110);
    private static readonly IBrush PlannedBrush = new SolidColorBrush(FocusColor, 0.45);
    private static readonly IBrush ActualBrush = new SolidColorBrush(FocusColor);
    private static readonly IBrush EventBrush = new SolidColorBrush(Color.FromRgb(100, 116, 139), 0.85);
    private static readonly IBrush NowBrush = new SolidColorBrush(Color.FromRgb(220, 38, 38));
    private static readonly IBrush NightBrush = new SolidColorBrush(Color.FromRgb(79, 70, 229), 0.28);
    private static readonly IBrush MissedBrush = new SolidColorBrush(Color.FromRgb(220, 38, 38), 0.35);
    private static readonly IBrush PlannedHoverBrush = new SolidColorBrush(Color.FromRgb(45, 212, 191), 0.85);
    private static readonly IBrush ActualHoverBrush = new SolidColorBrush(Color.FromRgb(45, 212, 191));
    private static readonly IPen HoverOutline = new Pen(new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), 1);

    // The planned block or session under the pointer, drawn brighter; null when none.
    private DayBarSpan? _hovered;
    private bool _hoveredIsPlanned;
    private static readonly Typeface TextTypeface = new(FontFamily.Default);

    static DayBarControl()
    {
        AffectsRender<DayBarControl>(LayoutProperty, CompactProperty);
        AffectsMeasure<DayBarControl>(CompactProperty);
    }

    public DayBarLayout? Layout
    {
        get => GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    /// <summary>Run with the profile's id when a planned block or session is clicked.</summary>
    public System.Windows.Input.ICommand? OpenProfileCommand
    {
        get => GetValue(OpenProfileCommandProperty);
        set => SetValue(OpenProfileCommandProperty, value);
    }

    public bool Compact
    {
        get => GetValue(CompactProperty);
        set => SetValue(CompactProperty, value);
    }

    private Lanes Sizes => Compact ? Small : Normal;

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? 600 : availableSize.Width;
        return new Size(width, Sizes.TotalHeight);
    }

    public override void Render(DrawingContext context)
    {
        DayBarLayout layout = Layout ?? DayBarLayout.Empty;
        Lanes lanes = Sizes;
        bool isDark = ActualThemeVariant == ThemeVariant.Dark;
        var trackBrush = new SolidColorBrush(isDark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(28, 0, 0, 0));
        IBrush textBrush = isDark ? new SolidColorBrush(Color.FromArgb(190, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(170, 0, 0, 0));
        double width = Bounds.Width;

        // Transparent fill makes the whole area show tooltips.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

        context.DrawRectangle(trackBrush, null, new Rect(0, lanes.PlannedTop, width, lanes.PlannedHeight), 4, 4);
        context.DrawRectangle(trackBrush, null, new Rect(0, lanes.ActualTop, width, lanes.ActualHeight), 3, 3);

        // Night behind the lanes, from the top of the planned track to the bottom of the actual lane.
        double nightHeight = lanes.ActualTop + lanes.ActualHeight - lanes.PlannedTop;
        DrawSpans(context, layout.Night ?? Array.Empty<DayBarSpan>(), NightBrush, lanes.PlannedTop, nightHeight, 3);
        DrawSpans(context, layout.Missed ?? Array.Empty<DayBarSpan>(), MissedBrush, lanes.PlannedTop, nightHeight, 3);

        DrawSpans(context, layout.Events, EventBrush, 0, lanes.EventHeight, 1);
        DrawSpans(context, layout.Planned, PlannedBrush, lanes.PlannedTop, lanes.PlannedHeight, 4);
        DrawSpans(context, layout.Actual, ActualBrush, lanes.ActualTop, lanes.ActualHeight, 3);

        if (_hovered is DayBarSpan hovered)
        {
            double left = Math.Clamp(hovered.Start, 0, 1) * width;
            double right = Math.Max(left + 1, Math.Clamp(hovered.End, 0, 1) * width);
            Rect box = _hoveredIsPlanned
                ? new Rect(left, lanes.PlannedTop, right - left, lanes.PlannedHeight)
                : new Rect(left, lanes.ActualTop, right - left, lanes.ActualHeight);
            context.DrawRectangle(_hoveredIsPlanned ? PlannedHoverBrush : ActualHoverBrush, HoverOutline, box, 4, 4);
        }

        var tickPen = new Pen(trackBrush, 1);
        double labelsEnd = double.NegativeInfinity;
        foreach (DayBarTick tick in layout.Ticks)
        {
            double x = tick.Position * width;
            context.DrawLine(tickPen, new Point(x, lanes.ActualTop + lanes.ActualHeight), new Point(x, lanes.AxisTop));
            var text = new FormattedText(tick.Label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, TextTypeface, lanes.TextSize, textBrush);
            double left = Math.Clamp(x - (text.Width / 2), 0, Math.Max(0, width - text.Width));
            if (left < labelsEnd + LabelGap)
            {
                continue;
            }

            context.DrawText(text, new Point(left, lanes.AxisTop + 1));
            labelsEnd = left + text.Width;
        }

        double nowX = Math.Clamp(layout.Now, 0, 1) * width;
        nowX = Math.Clamp(nowX, 1, Math.Max(1, width - 1));
        context.DrawLine(new Pen(NowBrush, 2), new Point(nowX, lanes.PlannedTop - lanes.NowOverhang),
            new Point(nowX, lanes.ActualTop + lanes.ActualHeight + lanes.NowOverhang));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Point position = e.GetPosition(this);
        ToolTip.SetTip(this, TipAt(position));
        SetHovered(ProfileSpanAt(position));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        ToolTip.SetTip(this, null);
        SetHovered((null, false));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            && ProfileSpanAt(e.GetPosition(this)).Span is { ProfileId: Guid profileId }
            && OpenProfileCommand?.CanExecute(profileId) == true)
        {
            OpenProfileCommand.Execute(profileId);
            e.Handled = true;
        }
    }

    /// <summary>The planned block (top lane) or session (lower lane) with a profile under the pointer.</summary>
    private (DayBarSpan? Span, bool IsPlanned) ProfileSpanAt(Point position)
    {
        DayBarLayout layout = Layout ?? DayBarLayout.Empty;
        if (Bounds.Width <= 0 || OpenProfileCommand is null)
        {
            return (null, false);
        }

        double at = position.X / Bounds.Width;
        Lanes lanes = Sizes;
        bool planned = position.Y >= lanes.PlannedTop && position.Y < lanes.ActualTop;
        bool actual = position.Y >= lanes.ActualTop && position.Y < lanes.ActualTop + lanes.ActualHeight + 2;
        IReadOnlyList<DayBarSpan> lane = planned ? layout.Planned : actual ? layout.Actual : Array.Empty<DayBarSpan>();
        DayBarSpan? span = lane.LastOrDefault(candidate => candidate.ProfileId is not null && at >= candidate.Start && at <= candidate.End);
        return (span, planned);
    }

    private void SetHovered((DayBarSpan? Span, bool IsPlanned) hovered)
    {
        if (ReferenceEquals(hovered.Span, _hovered) && hovered.IsPlanned == _hoveredIsPlanned)
        {
            return;
        }

        _hovered = hovered.Span;
        _hoveredIsPlanned = hovered.IsPlanned;
        Cursor = _hovered is null ? Cursor.Default : new Cursor(StandardCursorType.Hand);
        InvalidateVisual();
    }

    private string? TipAt(Point position)
    {
        DayBarLayout layout = Layout ?? DayBarLayout.Empty;
        if (Bounds.Width <= 0)
        {
            return null;
        }

        double at = position.X / Bounds.Width;
        IReadOnlyList<DayBarSpan> lane = position.Y < Sizes.PlannedTop ? layout.Events
            : position.Y < Sizes.ActualTop ? layout.Planned
            : layout.Actual;
        IEnumerable<DayBarSpan> sleep = (layout.Night ?? Array.Empty<DayBarSpan>()).Concat(layout.Missed ?? Array.Empty<DayBarSpan>());
        List<string> tips = lane.Concat(sleep).Where(span => at >= span.Start && at <= span.End).Select(span => span.Tip).ToList();
        return tips.Count == 0 ? null : string.Join(Environment.NewLine, tips);
    }

    /// <summary>Draws the merged spans, so overlapping ones (two blocks at once) don't show darker.</summary>
    private void DrawSpans(DrawingContext context, IReadOnlyList<DayBarSpan> spans, IBrush brush, double top, double height, double radius)
    {
        double width = Bounds.Width;
        foreach ((double start, double end) in Merge(spans))
        {
            double left = Math.Clamp(start, 0, 1) * width;
            double right = Math.Clamp(end, 0, 1) * width;
            if (right - left < 1)
            {
                right = left + 1;
            }

            context.DrawRectangle(brush, null, new Rect(left, top, right - left, height), radius, radius);
        }
    }

    private static List<(double Start, double End)> Merge(IEnumerable<DayBarSpan> spans)
    {
        var merged = new List<(double Start, double End)>();
        foreach (DayBarSpan span in spans.OrderBy(span => span.Start))
        {
            if (merged.Count > 0 && span.Start <= merged[^1].End)
            {
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, span.End));
            }
            else
            {
                merged.Add((span.Start, span.End));
            }
        }

        return merged;
    }

    /// <summary>Where each lane sits, top to bottom (the event band starts at the top).</summary>
    private sealed record Lanes(
        double EventHeight,
        double PlannedTop,
        double PlannedHeight,
        double ActualTop,
        double ActualHeight,
        double AxisTop,
        double TotalHeight,
        double NowOverhang,
        double TextSize);
}
