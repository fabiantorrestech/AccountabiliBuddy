using System.Globalization;
using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// The day plan editor's 24-hour bar: each part in its category's colour with its name, and parts that overlap on rows of their own
/// (ADR 63), so a covered one can still be picked; the weaker rules win in the time they share. Click empty space to add a part, the
/// strip under the rows to lay a new part over what's there, a part to select it; drag an edge to resize it or its middle to move it
/// (snaps to 15 minutes). A part that crosses midnight shows at both ends; its edges resize it, it can't be moved whole.
/// </summary>
public sealed class DayPlanBar : Control
{
    public static readonly StyledProperty<DayPlanEditorViewModel?> EditorProperty =
        AvaloniaProperty.Register<DayPlanBar, DayPlanEditorViewModel?>(nameof(Editor));

    private const double BarTop = 2;
    private const double SingleRowHeight = 30;
    private const double RowHeight = 22;
    private const double RowGap = 2;
    private const double AddStripHeight = 12;
    private const double AxisGap = 4;
    private const double AxisHeight = 16;
    private const double EdgeGrab = 6;
    private const int MinutesPerDay = 24 * 60;

    private static readonly Typeface TextTypeface = new(FontFamily.Default);
    private static readonly IPen SelectedOutline = new Pen(Brushes.White, 2);

    private static readonly Cursor CrossCursor = new(StandardCursorType.Cross);
    private static readonly Cursor ResizeCursor = new(StandardCursorType.SizeWestEast);
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);

    private Drag? _drag;

    static DayPlanBar()
    {
        AffectsRender<DayPlanBar>(EditorProperty);
    }

    public DayPlanEditorViewModel? Editor
    {
        get => GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == EditorProperty)
        {
            if (change.OldValue is DayPlanEditorViewModel old)
            {
                old.Changed -= OnEditorChanged;
            }

            if (change.NewValue is DayPlanEditorViewModel editor)
            {
                editor.Changed += OnEditorChanged;
            }
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? 600 : availableSize.Width;
        return new Size(width, AxisTop + AxisHeight);
    }

    private int RowCount => Math.Max(1, Pieces().Where(piece => !piece.Row.IsFlexible).Select(piece => piece.Lane + 1).DefaultIfEmpty(1).Max());

    private double LaneHeight => RowCount == 1 ? SingleRowHeight : RowHeight;

    private double BarHeight => RowCount * LaneHeight + (RowCount - 1) * RowGap;

    private double StripTop => BarTop + BarHeight + RowGap;

    private double AxisTop => StripTop + AddStripHeight + AxisGap;

    private double LaneTop(int lane) => BarTop + lane * (LaneHeight + RowGap);

    public override void Render(DrawingContext context)
    {
        bool isDark = ActualThemeVariant == ThemeVariant.Dark;
        var trackBrush = new SolidColorBrush(isDark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(28, 0, 0, 0));
        IBrush textBrush = isDark ? new SolidColorBrush(Color.FromArgb(190, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(170, 0, 0, 0));
        double width = Bounds.Width;

        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        double barHeight = BarHeight;
        double laneHeight = LaneHeight;
        double axisTop = AxisTop;
        context.DrawRectangle(trackBrush, null, new Rect(0, BarTop, width, barHeight), 5, 5);
        context.DrawRectangle(trackBrush, null, new Rect(0, StripTop, width, AddStripHeight), 3, 3);

        foreach (Piece piece in Pieces())
        {
            Rect box = new(X(piece.Start), LaneTop(piece.Lane), Math.Max(1, X(piece.End) - X(piece.Start)), laneHeight);
            // A flexible break's window lies over the work around it: see-through with a dashed edge.
            var fill = new SolidColorBrush(Color.Parse(piece.Row.Color), piece.Row.IsFlexible ? 0.35 : 0.85);
            IPen? edge = piece.Row.IsSelected ? SelectedOutline
                : piece.Row.IsFlexible ? new Pen(new SolidColorBrush(Color.Parse(piece.Row.Color)), 1.5, new DashStyle([3, 2], 0)) : null;
            context.DrawRectangle(fill, edge, box.Deflate(0.5), 4, 4);

            var name = new FormattedText(piece.Row.Name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, TextTypeface, 11, Brushes.White)
            {
                MaxTextWidth = Math.Max(0, box.Width - 8),
                MaxLineCount = 1,
                Trimming = TextTrimming.CharacterEllipsis,
            };
            if (box.Width > 24)
            {
                context.DrawText(name, new Point(box.X + 4, box.Y + (box.Height - name.Height) / 2));
            }
        }

        bool twelveHour = CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains('t');
        var tickPen = new Pen(trackBrush, 1);
        for (int hour = 0; hour <= 24; hour += 3)
        {
            double x = X(hour * 60);
            context.DrawLine(tickPen, new Point(x, StripTop + AddStripHeight), new Point(x, axisTop));
            string label = twelveHour ? $"{(hour % 12 == 0 ? 12 : hour % 12)}{(hour % 24 < 12 ? "a" : "p")}" : $"{hour % 24:00}";
            var text = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, TextTypeface, 10, textBrush);
            context.DrawText(text, new Point(Math.Clamp(x - text.Width / 2, 0, Math.Max(0, width - text.Width)), axisTop + 1));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Editor is not { } editor || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        Point at = e.GetPosition(this);
        Piece? piece = PieceAt(at);
        if (piece is null)
        {
            editor.AddAt(MinuteAt(at.X));
            return;
        }

        editor.Selected = piece.Row;
        if (piece.Row.ToPart() is not PlanPart part)
        {
            return;
        }

        int start = (int)part.Start.ToTimeSpan().TotalMinutes;
        int end = part.EndMinutes;
        DragKind? kind = EdgeAt(piece, at.X) ?? (part.CrossesMidnight ? null : DragKind.Whole);
        if (kind is DragKind dragKind)
        {
            _drag = new Drag(part.Id, dragKind, start, end, MinuteAt(at.X) - start, part.CrossesMidnight);
            e.Pointer.Capture(this);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Point at = e.GetPosition(this);
        if (_drag is not { } drag || Editor is not { } editor)
        {
            Piece? piece = PieceAt(at);
            Cursor = piece is null ? CrossCursor
                : EdgeAt(piece, at.X) is not null ? ResizeCursor
                : HandCursor;
            ToolTip.SetTip(this, piece is not null ? TipFor(piece)
                : at.Y >= StripTop ? "Click to lay a new part over what's here. Where parts overlap, the weaker rules win."
                : "Click to add a part here");
            return;
        }

        int minute = MinuteAt(at.X);
        switch (drag.Kind)
        {
            case DragKind.Start:
                editor.Move(drag.PartId, minute, drag.End, drag.CrossesMidnight);
                break;
            case DragKind.End:
                editor.Move(drag.PartId, drag.Start, minute, drag.CrossesMidnight);
                break;
            case DragKind.Whole:
                editor.MoveWhole(drag.PartId, minute - drag.GrabOffset, drag.End - drag.Start);
                break;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag is not null)
        {
            _drag = null;
            e.Pointer.Capture(null);
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        // E.g. Alt+Tab mid-drag: stop dragging, so moving the mouse later doesn't keep changing the part.
        _drag = null;
    }

    /// <summary>E.g. "Sleep 23:45–06:45 · the night before this day (from the evening before)".</summary>
    private static string TipFor(Piece piece)
    {
        if (piece.Row.ToPart() is not PlanPart part)
        {
            return piece.Row.Name;
        }

        string times = $"{part.Start.ToString("t")} – {part.End.ToString("t")}";
        return part.CrossesMidnight
            ? $"{piece.Row.Name} {times} · the night before this day (it starts the evening before)"
            : $"{piece.Row.Name} {times}";
    }

    private void OnEditorChanged(object? sender, EventArgs e)
    {
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>
    /// What each part looks like on the bar; a midnight-crossing part is two pieces (evening and morning). Each part takes the first row
    /// with room; a flexible break's window stays on the first row, over the work around it, and takes no room.
    /// </summary>
    private IEnumerable<Piece> Pieces()
    {
        if (Editor is null)
        {
            yield break;
        }

        var lanes = new List<List<(int Start, int End)>>();
        foreach (PlanPartRowViewModel row in Editor.Parts)
        {
            if (row.ToPart() is not PlanPart part)
            {
                continue;
            }

            List<(int Start, int End)> ranges = DayTemplateEditing.Ranges(part).ToList();
            int lane = 0;
            if (!part.IsFlexible)
            {
                lane = lanes.FindIndex(taken => !taken.Any(used => ranges.Any(range => range.Start < used.End && used.Start < range.End)));
                if (lane < 0)
                {
                    lane = lanes.Count;
                    lanes.Add([]);
                }

                lanes[lane].AddRange(ranges);
            }

            foreach ((int start, int end) in ranges)
            {
                yield return new Piece(row, lane, start, end,
                    StartIsEdge: !part.CrossesMidnight || start > 0,
                    EndIsEdge: !part.CrossesMidnight || end < MinutesPerDay);
            }
        }
    }

    private Piece? PieceAt(Point at)
    {
        // The strip under the rows only adds, and the axis below it does too.
        if (at.Y >= StripTop)
        {
            return null;
        }

        double x = at.X;
        double laneHeight = LaneHeight;
        // Selected first, so a part being edited wins where pieces touch.
        return Pieces().Where(piece => at.Y >= LaneTop(piece.Lane) - RowGap && at.Y <= LaneTop(piece.Lane) + laneHeight + RowGap)
            .OrderByDescending(piece => piece.Row.IsSelected)
            .FirstOrDefault(piece => x >= X(piece.Start) - EdgeGrab && x <= X(piece.End) + EdgeGrab
                                     && (x >= X(piece.Start) && x <= X(piece.End) || EdgeAt(piece, x) is not null));
    }

    private DragKind? EdgeAt(Piece piece, double x)
    {
        if (piece.StartIsEdge && Math.Abs(x - X(piece.Start)) <= EdgeGrab)
        {
            return DragKind.Start;
        }

        if (piece.EndIsEdge && Math.Abs(x - X(piece.End)) <= EdgeGrab)
        {
            return DragKind.End;
        }

        return null;
    }

    private double X(int minute)
    {
        return minute / (double)MinutesPerDay * Bounds.Width;
    }

    private int MinuteAt(double x)
    {
        return (int)Math.Clamp(x / Math.Max(1, Bounds.Width) * MinutesPerDay, 0, MinutesPerDay);
    }

    private enum DragKind
    {
        Start,
        End,
        Whole,
    }

    private sealed record Piece(PlanPartRowViewModel Row, int Lane, int Start, int End, bool StartIsEdge, bool EndIsEdge);

    /// <param name="GrabOffset">Minutes from the part's start to where it was grabbed (moving a whole part).</param>
    /// <param name="CrossesMidnight">Whether the part was a night part when the drag started; it stays one.</param>
    private sealed record Drag(Guid PartId, DragKind Kind, int Start, int End, int GrabOffset, bool CrossesMidnight);
}
