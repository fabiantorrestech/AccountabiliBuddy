using System.Globalization;
using AccountabiliBuddy.App.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// One day as labelled rows on one hour axis (Calendar, Plan, Focus, Blockers, as chosen in Settings), with a red line
/// at now through every row (pinned to the nearer edge when now is outside the bar). Plan parts and events are drawn
/// in their own colours, drift marks in red on the Focus row and the blockers' blocks in striped amber. With sleep
/// mode on, the night is shaded behind the rows and sleep already lost by staying up is shaded red. Hovering shows
/// what's under the pointer; clicking a session opens its profile (<see cref="OpenProfileCommand"/>) and clicking a
/// plan part opens the day plan (<see cref="OpenDayPlanCommand"/>). <see cref="Compact"/> draws it smaller for the
/// on-screen timeline, without the labels, and leaves out tick labels that would overlap.
/// </summary>
public sealed class DayBarControl : Control
{
    public static readonly StyledProperty<DayBarLayout?> LayoutProperty =
        AvaloniaProperty.Register<DayBarControl, DayBarLayout?>(nameof(Layout));

    public static readonly StyledProperty<System.Windows.Input.ICommand?> OpenProfileCommandProperty =
        AvaloniaProperty.Register<DayBarControl, System.Windows.Input.ICommand?>(nameof(OpenProfileCommand));

    public static readonly StyledProperty<System.Windows.Input.ICommand?> OpenDayPlanCommandProperty =
        AvaloniaProperty.Register<DayBarControl, System.Windows.Input.ICommand?>(nameof(OpenDayPlanCommand));

    public static readonly StyledProperty<System.Windows.Input.ICommand?> AddSessionCommandProperty =
        AvaloniaProperty.Register<DayBarControl, System.Windows.Input.ICommand?>(nameof(AddSessionCommand));

    public static readonly StyledProperty<bool> CompactProperty =
        AvaloniaProperty.Register<DayBarControl, bool>(nameof(Compact));

    public static readonly StyledProperty<bool> QuietProperty =
        AvaloniaProperty.Register<DayBarControl, bool>(nameof(Quiet));

    public static readonly StyledProperty<double> NowFractionProperty =
        AvaloniaProperty.Register<DayBarControl, double>(nameof(NowFraction), double.NaN);

    private static readonly Sizes Normal = new(LabelWidth: 84, RowTop: 3, RowHeight: 14, RowGap: 3, AxisGap: 4, AxisHeight: 14,
        NowOverhang: 3, TextSize: 11, PartTextSize: 10);

    private static readonly Sizes Small = new(LabelWidth: 0, RowTop: 2, RowHeight: 6, RowGap: 2, AxisGap: 2, AxisHeight: 11,
        NowOverhang: 2, TextSize: 9, PartTextSize: 0);

    private const double LabelGap = 4;
    private const double MinPartWidthForName = 28;
    private const double StripeSpacing = 6;

    private static readonly Color FocusColor = Color.FromRgb(15, 118, 110);
    /// <summary>The Focus row's colour, also used by the legend under the day bar.</summary>
    public static readonly IBrush FocusBrush = new SolidColorBrush(FocusColor);
    private static readonly IBrush CalendarBrush = new SolidColorBrush(Color.FromRgb(100, 116, 139), 0.85);
    private static readonly IBrush DriftBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68));
    private static readonly IPen StripePen = new Pen(new SolidColorBrush(Color.FromRgb(180, 83, 9), 0.85), 2);
    private static readonly IBrush AddedBrush = new SolidColorBrush(FocusColor, 0.45);
    private static readonly IPen AddedStripePen = new Pen(new SolidColorBrush(Color.FromRgb(45, 212, 191), 0.55), 2);
    private static readonly IBrush NowBrush = new SolidColorBrush(Color.FromRgb(220, 38, 38));

    /// <summary>Every span in <see cref="Quiet"/>: one muted slate (drift and task marks keep their colours).</summary>
    public static readonly IBrush QuietBrush = new SolidColorBrush(Color.FromRgb(100, 116, 139), 0.55);
    private static readonly IBrush NightBrush = new SolidColorBrush(Color.FromRgb(79, 70, 229), 0.28);
    private static readonly IBrush MissedBrush = new SolidColorBrush(Color.FromRgb(220, 38, 38), 0.35);
    private static readonly IPen HoverOutline = new Pen(new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)), 1.5);
    private static readonly Typeface TextTypeface = new(FontFamily.Default);

    // Colours from the plan and calendars, parsed once each ("#RRGGBB"); a colour that doesn't parse uses the row's own.
    private static readonly Dictionary<string, IBrush?> SpanBrushes = new(StringComparer.OrdinalIgnoreCase);

    // The clickable span under the pointer (a session or a plan part), outlined; null when none.
    private (DayBarRow Row, DayBarSpan Span)? _hovered;

    static DayBarControl()
    {
        AffectsRender<DayBarControl>(LayoutProperty, CompactProperty, QuietProperty, NowFractionProperty);
        AffectsMeasure<DayBarControl>(LayoutProperty, CompactProperty);
    }

    public DayBarLayout? Layout
    {
        get => GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    /// <summary>Run with the profile's id when a session on the Focus row is clicked.</summary>
    public System.Windows.Input.ICommand? OpenProfileCommand
    {
        get => GetValue(OpenProfileCommandProperty);
        set => SetValue(OpenProfileCommandProperty, value);
    }

    /// <summary>Run when a part on the Plan row is clicked.</summary>
    public System.Windows.Input.ICommand? OpenDayPlanCommand
    {
        get => GetValue(OpenDayPlanCommandProperty);
        set => SetValue(OpenDayPlanCommandProperty, value);
    }

    /// <summary>Run with the local time when empty time on the Focus row is clicked: add a past session there (ADR 20).</summary>
    public System.Windows.Input.ICommand? AddSessionCommand
    {
        get => GetValue(AddSessionCommandProperty);
        set => SetValue(AddSessionCommandProperty, value);
    }

    public bool Compact
    {
        get => GetValue(CompactProperty);
        set => SetValue(CompactProperty, value);
    }

    /// <summary>The on-screen window at rest: every span in one muted slate, drift and task marks in their colours.</summary>
    public bool Quiet
    {
        get => GetValue(QuietProperty);
        set => SetValue(QuietProperty, value);
    }

    /// <summary>Where the now line is drawn (0–1), moved every second so it glides; NaN = the layout's own.</summary>
    public double NowFraction
    {
        get => GetValue(NowFractionProperty);
        set => SetValue(NowFractionProperty, value);
    }

    private Sizes Size => Compact ? Small : Normal;

    private IReadOnlyList<DayBarRow> Rows => (Layout ?? DayBarLayout.Empty).Rows;

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? 600 : availableSize.Width;
        Sizes size = Size;
        return new Size(width, RowsBottom(size) + size.AxisGap + size.AxisHeight);
    }

    public override void Render(DrawingContext context)
    {
        DayBarLayout layout = Layout ?? DayBarLayout.Empty;
        Sizes size = Size;
        bool isDark = ActualThemeVariant == ThemeVariant.Dark;
        var trackBrush = new SolidColorBrush(isDark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(28, 0, 0, 0));
        IBrush textBrush = isDark ? new SolidColorBrush(Color.FromArgb(190, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(170, 0, 0, 0));
        double rowsBottom = RowsBottom(size);

        // Transparent fill makes the whole area show tooltips.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

        for (int i = 0; i < layout.Rows.Count; i++)
        {
            double top = RowTop(size, i);
            context.DrawRectangle(trackBrush, null, new Rect(BarLeft(size), top, BarWidth(size), size.RowHeight), 3, 3);
            if (size.LabelWidth > 0)
            {
                var label = new FormattedText(layout.Rows[i].Label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, TextTypeface, size.TextSize, textBrush)
                {
                    MaxTextWidth = size.LabelWidth - 6,
                    MaxLineCount = 1,
                    Trimming = TextTrimming.CharacterEllipsis,
                };
                context.DrawText(label, new Point(0, top + ((size.RowHeight - label.Height) / 2)));
            }
        }

        // Night behind all rows.
        if (layout.Rows.Count > 0)
        {
            double nightTop = size.RowTop;
            double nightHeight = rowsBottom - size.RowTop;
            DrawPlain(context, size, layout.Night ?? Array.Empty<DayBarSpan>(), NightBrush, nightTop, nightHeight);
            DrawPlain(context, size, layout.Missed ?? Array.Empty<DayBarSpan>(), MissedBrush, nightTop, nightHeight);
        }

        for (int i = 0; i < layout.Rows.Count; i++)
        {
            DrawRow(context, size, layout.Rows[i], RowTop(size, i));
        }

        if (_hovered is (DayBarRow hoveredRow, DayBarSpan hoveredSpan) && IndexOf(hoveredRow) is int hoveredIndex)
        {
            Rect box = SpanRect(size, hoveredSpan, RowTop(size, hoveredIndex));
            context.DrawRectangle(null, HoverOutline, box, 3, 3);
        }

        DrawAxis(context, size, layout, trackBrush, textBrush, rowsBottom);

        double now = double.IsNaN(NowFraction) ? layout.Now : NowFraction;
        double nowX = BarLeft(size) + (Math.Clamp(now, 0, 1) * BarWidth(size));
        nowX = Math.Clamp(nowX, BarLeft(size) + 1, Math.Max(BarLeft(size) + 1, Bounds.Width - 1));
        context.DrawLine(new Pen(NowBrush, 2), new Point(nowX, size.RowTop - size.NowOverhang), new Point(nowX, rowsBottom + size.NowOverhang));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Point position = e.GetPosition(this);
        ToolTip.SetTip(this, TipAt(position));
        SetHovered(ClickableAt(position));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        ToolTip.SetTip(this, null);
        SetHovered(null);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (EmptyFocusTimeAt(e.GetPosition(this)) is DateTime at && AddSessionCommand?.CanExecute(at) == true)
        {
            AddSessionCommand.Execute(at);
            e.Handled = true;
            return;
        }

        if (ClickableAt(e.GetPosition(this)) is not (DayBarRow row, DayBarSpan span))
        {
            return;
        }

        if (row.Kind == TimelineRow.Focus && span.ProfileId is Guid profileId && OpenProfileCommand?.CanExecute(profileId) == true)
        {
            OpenProfileCommand.Execute(profileId);
            e.Handled = true;
        }
        else if (row.Kind == TimelineRow.Plan && OpenDayPlanCommand?.CanExecute(null) == true)
        {
            OpenDayPlanCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void DrawRow(DrawingContext context, Sizes size, DayBarRow row, double top)
    {
        foreach (DayBarSpan span in row.Spans)
        {
            Rect box = SpanRect(size, span, top);
            IBrush brush = Quiet ? QuietBrush : span.Hatched ? AddedBrush : BrushFor(span.Color) ?? DefaultBrush(row.Kind);
            context.DrawRectangle(brush, null, box, 3, 3);

            if (Quiet)
            {
                continue; // No stripes or names at rest.
            }

            if (row.Kind == TimelineRow.Blockers)
            {
                DrawStripes(context, box, StripePen);
            }
            else if (span.Hatched)
            {
                // Added by hand (ADR 20): lighter, with stripes.
                DrawStripes(context, box, AddedStripePen);
            }

            if (span.Label is string name && size.PartTextSize > 0 && box.Width >= MinPartWidthForName)
            {
                var text = new FormattedText(name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, TextTypeface, size.PartTextSize, Brushes.White)
                {
                    MaxTextWidth = Math.Max(0, box.Width - 6),
                    MaxLineCount = 1,
                    Trimming = TextTrimming.CharacterEllipsis,
                };
                context.DrawText(text, new Point(box.X + 3, top + ((size.RowHeight - text.Height) / 2)));
            }
        }

        foreach (DayBarSpan mark in row.Marks)
        {
            Rect box = SpanRect(size, mark, top);
            if (box.Width < DayBarHitTest.MinMarkWidth)
            {
                box = box.WithWidth(DayBarHitTest.MinMarkWidth);
            }

            // Drift in red; a ticked task (ADR 24) in its own colour.
            context.DrawRectangle(BrushFor(mark.Color) ?? DriftBrush, null, box, 1, 1);
        }
    }

    private static void DrawStripes(DrawingContext context, Rect box, IPen pen)
    {
        using (context.PushClip(box))
        {
            for (double x = box.Left - box.Height; x < box.Right; x += StripeSpacing)
            {
                context.DrawLine(pen, new Point(x, box.Bottom), new Point(x + box.Height, box.Top));
            }
        }
    }

    /// <summary>The local time under the pointer when it's on empty time in the Focus row (no session there); else null.</summary>
    private DateTime? EmptyFocusTimeAt(Point position)
    {
        DayBarLayout layout = Layout ?? DayBarLayout.Empty;
        if (AddSessionCommand is null || layout.RangeStart is not DateTime start || layout.RangeEnd is not DateTime end
            || RowAt(position) is not { Kind: TimelineRow.Focus } row || FractionAt(position) is not double at || at < 0 || at > 1)
        {
            return null;
        }

        // Work time with no session (a gap) is still empty: a session can be added there.
        if (row.Spans.Any(span => !span.IsGap && at >= span.Start && at <= span.End))
        {
            return null;
        }

        return start + ((end - start) * at);
    }

    /// <summary>Draws the merged spans, so overlapping ones don't show darker (night and lost sleep).</summary>
    private void DrawPlain(DrawingContext context, Sizes size, IReadOnlyList<DayBarSpan> spans, IBrush brush, double top, double height)
    {
        foreach ((double start, double end) in Merge(spans))
        {
            double left = BarLeft(size) + (Math.Clamp(start, 0, 1) * BarWidth(size));
            double right = BarLeft(size) + (Math.Clamp(end, 0, 1) * BarWidth(size));
            context.DrawRectangle(brush, null, new Rect(left, top, Math.Max(1, right - left), height), 3, 3);
        }
    }

    private void DrawAxis(DrawingContext context, Sizes size, DayBarLayout layout, IBrush trackBrush, IBrush textBrush, double rowsBottom)
    {
        var tickPen = new Pen(trackBrush, 1);
        double axisTop = rowsBottom + size.AxisGap;
        double labelsEnd = double.NegativeInfinity;
        foreach (DayBarTick tick in layout.Ticks)
        {
            double x = BarLeft(size) + (tick.Position * BarWidth(size));
            context.DrawLine(tickPen, new Point(x, rowsBottom), new Point(x, axisTop));
            var text = new FormattedText(tick.Label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, TextTypeface, size.TextSize, textBrush);
            double left = Math.Clamp(x - (text.Width / 2), BarLeft(size), Math.Max(BarLeft(size), Bounds.Width - text.Width));
            if (left < labelsEnd + LabelGap)
            {
                continue;
            }

            context.DrawText(text, new Point(left, axisTop));
            labelsEnd = left + text.Width;
        }
    }

    /// <summary>A session (Focus row, with a profile) or a plan part under the pointer: the things a click opens.</summary>
    private (DayBarRow Row, DayBarSpan Span)? ClickableAt(Point position)
    {
        if (RowAt(position) is not DayBarRow row || FractionAt(position) is not double at)
        {
            return null;
        }

        bool clickable = row.Kind == TimelineRow.Focus ? OpenProfileCommand is not null
            : row.Kind == TimelineRow.Plan && OpenDayPlanCommand is not null;
        if (!clickable)
        {
            return null;
        }

        DayBarSpan? span = row.Spans.LastOrDefault(candidate => at >= candidate.Start && at <= candidate.End
                                                                && (row.Kind != TimelineRow.Focus || candidate.ProfileId is not null));
        return span is null ? null : (row, span);
    }

    private void SetHovered((DayBarRow Row, DayBarSpan Span)? hovered)
    {
        if (Equals(hovered, _hovered))
        {
            return;
        }

        _hovered = hovered;
        Cursor = hovered is null ? Cursor.Default : new Cursor(StandardCursorType.Hand);
        InvalidateVisual();
    }

    private string? TipAt(Point position)
    {
        if (EmptyFocusTimeAt(position) is DateTime emptyTime && RowAt(position) is DayBarRow focusRow
            && DayBarHitTest.MarksAt(focusRow.Marks, position.X, BarLeft(Size), BarWidth(Size)).Count == 0)
        {
            return $"Click to add a past focus session at {emptyTime:t}";
        }

        DayBarLayout layout = Layout ?? DayBarLayout.Empty;
        if (FractionAt(position) is not double at)
        {
            return null;
        }

        // Drift marks by their drawn width in pixels (a short drift is thinner than a pixel), the rest by time.
        Sizes size = Size;
        DayBarRow? under = RowAt(position);
        IEnumerable<DayBarSpan> marks = under is null ? Array.Empty<DayBarSpan>() : DayBarHitTest.MarksAt(under.Marks, position.X, BarLeft(size), BarWidth(size));
        IEnumerable<DayBarSpan> spans = under?.Spans ?? (IEnumerable<DayBarSpan>)Array.Empty<DayBarSpan>();
        IEnumerable<DayBarSpan> sleep = (layout.Night ?? Array.Empty<DayBarSpan>()).Concat(layout.Missed ?? Array.Empty<DayBarSpan>());
        IEnumerable<string> spanTips = spans.Concat(sleep).Where(span => at >= span.Start && at <= span.End).Select(span => span.Tip);
        List<string> tips = marks.Select(mark => mark.Tip).Concat(spanTips).Distinct().ToList();
        return tips.Count == 0 ? null : string.Join(Environment.NewLine, tips);
    }

    /// <summary>The row whose band (row plus the gap under it) holds the pointer.</summary>
    private DayBarRow? RowAt(Point position)
    {
        Sizes size = Size;
        IReadOnlyList<DayBarRow> rows = Rows;
        for (int i = 0; i < rows.Count; i++)
        {
            double top = RowTop(size, i);
            if (position.Y >= top - (size.RowGap / 2) && position.Y < top + size.RowHeight + (size.RowGap / 2))
            {
                return rows[i];
            }
        }

        return null;
    }

    private double? FractionAt(Point position)
    {
        Sizes size = Size;
        double width = BarWidth(size);
        if (width <= 0 || position.X < BarLeft(size))
        {
            return null;
        }

        return (position.X - BarLeft(size)) / width;
    }

    private int? IndexOf(DayBarRow row)
    {
        IReadOnlyList<DayBarRow> rows = Rows;
        for (int i = 0; i < rows.Count; i++)
        {
            if (ReferenceEquals(rows[i], row))
            {
                return i;
            }
        }

        return null;
    }

    private Rect SpanRect(Sizes size, DayBarSpan span, double top)
    {
        double left = BarLeft(size) + (Math.Clamp(span.Start, 0, 1) * BarWidth(size));
        double right = BarLeft(size) + (Math.Clamp(span.End, 0, 1) * BarWidth(size));
        return new Rect(left, top, Math.Max(1, right - left), size.RowHeight);
    }

    private double RowsBottom(Sizes size)
    {
        int count = Rows.Count;
        return count == 0 ? size.RowTop : RowTop(size, count - 1) + size.RowHeight;
    }

    private static double RowTop(Sizes size, int index)
    {
        return size.RowTop + (index * (size.RowHeight + size.RowGap));
    }

    private static double BarLeft(Sizes size) => size.LabelWidth;

    private double BarWidth(Sizes size) => Math.Max(0, Bounds.Width - size.LabelWidth);

    private static IBrush DefaultBrush(TimelineRow row)
    {
        return row switch
        {
            TimelineRow.Focus => FocusBrush,
            TimelineRow.Calendar => CalendarBrush,
            _ => CalendarBrush,
        };
    }

    private static IBrush? BrushFor(string? hex)
    {
        if (hex is null)
        {
            return null;
        }

        if (!SpanBrushes.TryGetValue(hex, out IBrush? brush))
        {
            brush = Color.TryParse(hex, out Color color) ? new SolidColorBrush(color, 0.85) : null;
            SpanBrushes[hex] = brush;
        }

        return brush;
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

    /// <summary>Where things sit: the label column, the rows top to bottom, then the hour axis.</summary>
    private sealed record Sizes(
        double LabelWidth,
        double RowTop,
        double RowHeight,
        double RowGap,
        double AxisGap,
        double AxisHeight,
        double NowOverhang,
        double TextSize,
        double PartTextSize);
}
