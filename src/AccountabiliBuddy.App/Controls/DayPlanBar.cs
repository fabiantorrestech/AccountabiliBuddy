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
/// The day plan editor's 24-hour bar: each part in its category's colour with its name. Click empty space to add a
/// part, click a part to select it, drag an edge to resize it or its middle to move it (snaps to 15 minutes, never
/// into another part). A part that crosses midnight shows at both ends; its edges resize it, it can't be moved whole.
/// </summary>
public sealed class DayPlanBar : Control
{
    public static readonly StyledProperty<DayPlanEditorViewModel?> EditorProperty =
        AvaloniaProperty.Register<DayPlanBar, DayPlanEditorViewModel?>(nameof(Editor));

    private const double BarTop = 2;
    private const double BarHeight = 30;
    private const double AxisTop = 36;
    private const double TotalHeight = 52;
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
        return new Size(width, TotalHeight);
    }

    public override void Render(DrawingContext context)
    {
        bool isDark = ActualThemeVariant == ThemeVariant.Dark;
        var trackBrush = new SolidColorBrush(isDark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(28, 0, 0, 0));
        IBrush textBrush = isDark ? new SolidColorBrush(Color.FromArgb(190, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(170, 0, 0, 0));
        double width = Bounds.Width;

        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        context.DrawRectangle(trackBrush, null, new Rect(0, BarTop, width, BarHeight), 5, 5);

        foreach (Piece piece in Pieces())
        {
            Rect box = new(X(piece.Start), BarTop, Math.Max(1, X(piece.End) - X(piece.Start)), BarHeight);
            var fill = new SolidColorBrush(Color.Parse(piece.Row.Color), 0.85);
            context.DrawRectangle(fill, piece.Row.IsSelected ? SelectedOutline : null, box.Deflate(0.5), 4, 4);

            var name = new FormattedText(piece.Row.Name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, TextTypeface, 11, Brushes.White)
            {
                MaxTextWidth = Math.Max(0, box.Width - 8),
                MaxLineCount = 1,
                Trimming = TextTrimming.CharacterEllipsis,
            };
            if (box.Width > 24)
            {
                context.DrawText(name, new Point(box.X + 4, BarTop + (BarHeight - name.Height) / 2));
            }
        }

        bool twelveHour = CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains('t');
        var tickPen = new Pen(trackBrush, 1);
        for (int hour = 0; hour <= 24; hour += 3)
        {
            double x = X(hour * 60);
            context.DrawLine(tickPen, new Point(x, BarTop + BarHeight), new Point(x, AxisTop));
            string label = twelveHour ? $"{(hour % 12 == 0 ? 12 : hour % 12)}{(hour % 24 < 12 ? "a" : "p")}" : $"{hour % 24:00}";
            var text = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, TextTypeface, 10, textBrush);
            context.DrawText(text, new Point(Math.Clamp(x - text.Width / 2, 0, Math.Max(0, width - text.Width)), AxisTop + 1));
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
        Piece? piece = PieceAt(at.X);
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
            Piece? piece = PieceAt(at.X);
            Cursor = piece is null ? CrossCursor
                : EdgeAt(piece, at.X) is not null ? ResizeCursor
                : HandCursor;
            ToolTip.SetTip(this, piece is null ? "Click to add a part here" : TipFor(piece));
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

        string times = $"{part.Start.ToString("HH:mm")}–{part.End.ToString("HH:mm")}";
        return part.CrossesMidnight
            ? $"{piece.Row.Name} {times} · the night before this day (it starts the evening before)"
            : $"{piece.Row.Name} {times}";
    }

    private void OnEditorChanged(object? sender, EventArgs e)
    {
        InvalidateVisual();
    }

    /// <summary>What each part looks like on the bar; a midnight-crossing part is two pieces (evening and morning).</summary>
    private IEnumerable<Piece> Pieces()
    {
        if (Editor is null)
        {
            yield break;
        }

        foreach (PlanPartRowViewModel row in Editor.Parts)
        {
            if (row.ToPart() is not PlanPart part)
            {
                continue;
            }

            foreach ((int start, int end) in DayTemplateEditing.Ranges(part))
            {
                yield return new Piece(row, start, end,
                    StartIsEdge: !part.CrossesMidnight || start > 0,
                    EndIsEdge: !part.CrossesMidnight || end < MinutesPerDay);
            }
        }
    }

    private Piece? PieceAt(double x)
    {
        // Selected first, so a part being edited wins where pieces touch.
        return Pieces().OrderByDescending(piece => piece.Row.IsSelected)
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

    private sealed record Piece(PlanPartRowViewModel Row, int Start, int End, bool StartIsEdge, bool EndIsEdge);

    /// <param name="GrabOffset">Minutes from the part's start to where it was grabbed (moving a whole part).</param>
    /// <param name="CrossesMidnight">Whether the part was a night part when the drag started; it stays one.</param>
    private sealed record Drag(Guid PartId, DragKind Kind, int Start, int End, int GrabOffset, bool CrossesMidnight);
}
