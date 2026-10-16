using System.Globalization;
using System.Windows.Input;
using AccountabiliBuddy.Core.Domain;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// Cold Turkey-style weekly calendar (Monday–Sunday × 24 h).
/// Drag on empty space to propose a new block (raises <see cref="CreateBlockCommand"/>);
/// click a block to select it (<see cref="SelectedBlockId"/>). Blocks that cross midnight
/// are drawn as two pieces. Times snap to 15 minutes.
/// </summary>
public sealed class WeekScheduleGrid : Control
{
    public static readonly StyledProperty<IReadOnlyList<ScheduleBlockItem>?> ItemsProperty =
        AvaloniaProperty.Register<WeekScheduleGrid, IReadOnlyList<ScheduleBlockItem>?>(nameof(Items));

    public static readonly StyledProperty<Guid?> SelectedBlockIdProperty =
        AvaloniaProperty.Register<WeekScheduleGrid, Guid?>(
            nameof(SelectedBlockId),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<ICommand?> CreateBlockCommandProperty =
        AvaloniaProperty.Register<WeekScheduleGrid, ICommand?>(nameof(CreateBlockCommand));

    private const double HeaderHeight = 28;
    private const double GutterWidth = 48;
    private const double HourHeight = 32;
    private const int SnapMinutes = 15;
    private const int MinutesPerDay = 24 * 60;
    private const double TextSize = 12;

    private static readonly string[] DayNames = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
    private static readonly Typeface TextTypeface = new(FontFamily.Default);
    private static readonly IBrush NowLineBrush = new SolidColorBrush(Color.FromRgb(220, 38, 38));

    private readonly DispatcherTimer _nowLineTimer;

    // Active drag (null when not dragging).
    private int? _dragColumn;
    private int _dragStartMinutes;
    private int _dragCurrentMinutes;

    static WeekScheduleGrid()
    {
        AffectsRender<WeekScheduleGrid>(ItemsProperty, SelectedBlockIdProperty);
    }

    public WeekScheduleGrid()
    {
        Cursor = new Cursor(StandardCursorType.Cross);
        _nowLineTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _nowLineTimer.Tick += (_, _) => InvalidateVisual();
    }

    public IReadOnlyList<ScheduleBlockItem>? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public Guid? SelectedBlockId
    {
        get => GetValue(SelectedBlockIdProperty);
        set => SetValue(SelectedBlockIdProperty, value);
    }

    /// <summary>Executed with a <see cref="ScheduleSlot"/> when the user finishes dragging out a range.</summary>
    public ICommand? CreateBlockCommand
    {
        get => GetValue(CreateBlockCommandProperty);
        set => SetValue(CreateBlockCommandProperty, value);
    }

    private double ColumnWidth => Math.Max(0, (Bounds.Width - GutterWidth) / DayNames.Length);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _nowLineTimer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _nowLineTimer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? 700 : availableSize.Width;
        return new Size(width, HeaderHeight + (24 * HourHeight));
    }

    public override void Render(DrawingContext context)
    {
        bool isDark = ActualThemeVariant == ThemeVariant.Dark;
        IBrush textBrush = isDark ? Brushes.WhiteSmoke : Brushes.Black;
        var lineBrush = new SolidColorBrush(isDark ? Color.FromArgb(50, 255, 255, 255) : Color.FromArgb(40, 0, 0, 0));
        var linePen = new Pen(lineBrush, 1);

        // Transparent fill makes the whole area receive pointer events.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

        DrawGrid(context, textBrush, linePen);

        foreach (ScheduleBlockItem item in Items ?? Array.Empty<ScheduleBlockItem>())
        {
            DrawBlock(context, item);
        }

        DrawDragPreview(context);
        DrawNowLine(context);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        Point position = e.GetPosition(this);
        if (!TryGetColumn(position, out int column))
        {
            return;
        }

        ScheduleBlockItem? hit = HitTestBlock(position);
        if (hit is not null)
        {
            SelectedBlockId = hit.Block.Id;
            e.Handled = true;
            return;
        }

        _dragColumn = column;
        _dragStartMinutes = SnappedMinutesAt(position.Y);
        _dragCurrentMinutes = _dragStartMinutes;
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragColumn is null)
        {
            return;
        }

        _dragCurrentMinutes = SnappedMinutesAt(e.GetPosition(this).Y);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragColumn is not int column)
        {
            return;
        }

        e.Pointer.Capture(null);
        int start = Math.Min(_dragStartMinutes, _dragCurrentMinutes);
        int end = Math.Max(_dragStartMinutes, _dragCurrentMinutes);
        _dragColumn = null;
        InvalidateVisual();

        if (end - start < SnapMinutes)
        {
            // A click on empty space clears the selection.
            SelectedBlockId = null;
            return;
        }

        var slot = new ScheduleSlot(DayFromColumn(column), ToTime(start), ToTime(end));
        if (CreateBlockCommand?.CanExecute(slot) == true)
        {
            CreateBlockCommand.Execute(slot);
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragColumn = null;
        InvalidateVisual();
    }

    private void DrawGrid(DrawingContext context, IBrush textBrush, Pen linePen)
    {
        double columnWidth = ColumnWidth;

        for (int column = 0; column < DayNames.Length; column++)
        {
            double x = GutterWidth + (column * columnWidth);
            DrawText(context, DayNames[column], new Point(x + 6, 6), textBrush);
            context.DrawLine(linePen, new Point(x, HeaderHeight), new Point(x, Bounds.Height));
        }

        for (int hour = 0; hour <= 24; hour++)
        {
            double y = HeaderHeight + (hour * HourHeight);
            context.DrawLine(linePen, new Point(GutterWidth, y), new Point(Bounds.Width, y));
            if (hour < 24)
            {
                DrawText(context, $"{hour:00}:00", new Point(4, y + 2), textBrush);
            }
        }
    }

    private void DrawBlock(DrawingContext context, ScheduleBlockItem item)
    {
        bool isSelected = item.Block.Id == SelectedBlockId;
        var fill = new SolidColorBrush(item.Color, 0.85);
        Pen? outline = isSelected ? new Pen(Brushes.White, 2) : null;
        string label = $"{item.ProfileName}\n{item.Block.Start:HH\\:mm}–{item.Block.End:HH\\:mm}";

        foreach (Rect rect in SegmentRects(item.Block))
        {
            Rect inset = rect.Deflate(1);
            context.DrawRectangle(fill, outline, inset, 4, 4);

            using (context.PushClip(inset))
            {
                DrawText(context, label, new Point(inset.X + 4, inset.Y + 2), Brushes.White);
            }
        }
    }

    private void DrawDragPreview(DrawingContext context)
    {
        if (_dragColumn is not int column)
        {
            return;
        }

        int start = Math.Min(_dragStartMinutes, _dragCurrentMinutes);
        int end = Math.Max(_dragStartMinutes, _dragCurrentMinutes);
        Rect rect = RectFor(column, start, end);
        var fill = new SolidColorBrush(Color.FromArgb(90, 15, 118, 110));
        context.DrawRectangle(fill, new Pen(Brushes.Teal, 1, dashStyle: DashStyle.Dash), rect);
    }

    private void DrawNowLine(DrawingContext context)
    {
        DateTime now = DateTime.Now;
        int column = ColumnFromDay(now.DayOfWeek);
        double y = YForMinutes((int)now.TimeOfDay.TotalMinutes);
        double x = GutterWidth + (column * ColumnWidth);
        context.DrawLine(new Pen(NowLineBrush, 2), new Point(x, y), new Point(x + ColumnWidth, y));
    }

    private IEnumerable<Rect> SegmentRects(ScheduleBlock block)
    {
        int column = ColumnFromDay(block.Day);
        int start = MinutesOf(block.Start);
        int end = MinutesOf(block.End);

        if (!block.CrossesMidnight)
        {
            yield return RectFor(column, start, end);
            yield break;
        }

        yield return RectFor(column, start, MinutesPerDay);
        if (end > 0)
        {
            yield return RectFor((column + 1) % DayNames.Length, 0, end);
        }
    }

    private ScheduleBlockItem? HitTestBlock(Point position)
    {
        // Last drawn is on top, so search in reverse.
        IReadOnlyList<ScheduleBlockItem> items = Items ?? Array.Empty<ScheduleBlockItem>();
        for (int i = items.Count - 1; i >= 0; i--)
        {
            if (SegmentRects(items[i].Block).Any(rect => rect.Contains(position)))
            {
                return items[i];
            }
        }

        return null;
    }

    private Rect RectFor(int column, int startMinutes, int endMinutes)
    {
        double x = GutterWidth + (column * ColumnWidth);
        double top = YForMinutes(startMinutes);
        double bottom = YForMinutes(endMinutes);
        return new Rect(x, top, ColumnWidth, Math.Max(0, bottom - top));
    }

    private bool TryGetColumn(Point position, out int column)
    {
        column = -1;
        if (position.X < GutterWidth || position.Y < HeaderHeight || ColumnWidth <= 0)
        {
            return false;
        }

        column = Math.Clamp((int)((position.X - GutterWidth) / ColumnWidth), 0, DayNames.Length - 1);
        return true;
    }

    private static int SnappedMinutesAt(double y)
    {
        double minutes = (y - HeaderHeight) / HourHeight * 60;
        int snapped = (int)Math.Round(minutes / SnapMinutes) * SnapMinutes;
        return Math.Clamp(snapped, 0, MinutesPerDay);
    }

    private static double YForMinutes(int minutes)
    {
        return HeaderHeight + (minutes / 60.0 * HourHeight);
    }

    private static int MinutesOf(TimeOnly time)
    {
        return (time.Hour * 60) + time.Minute;
    }

    private static TimeOnly ToTime(int minutes)
    {
        // 24:00 is represented as 00:00 ("until midnight").
        return minutes >= MinutesPerDay ? TimeOnly.MinValue : new TimeOnly(minutes / 60, minutes % 60);
    }

    /// <summary>Monday is column 0, Sunday column 6.</summary>
    private static int ColumnFromDay(DayOfWeek day)
    {
        return ((int)day + 6) % 7;
    }

    private static DayOfWeek DayFromColumn(int column)
    {
        return (DayOfWeek)((column + 1) % 7);
    }

    private static void DrawText(DrawingContext context, string text, Point origin, IBrush brush)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            TextTypeface,
            TextSize,
            brush);
        context.DrawText(formatted, origin);
    }
}
