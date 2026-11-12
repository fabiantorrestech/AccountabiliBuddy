using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// A grid of rounded cells for the Stats page's Consistency (a day per cell, weeks across) and Intensity (an hour per
/// cell, weekdays down) views: row labels on the left, optional column labels on top, the cells sized to the width they get
/// (never above <see cref="MaxCellSize"/>), coloured by <see cref="HeatRamp"/> from the empty and accent brushes it is given.
/// Redraws only when its data, a brush or its size changes. Hovering a cell shows its tip; a click on one with a date runs
/// <see cref="CellCommand"/>.
/// <para>
/// Round 2 (the goal heatmap, change note §4a) adds, all off unless set: a <see cref="FixedCellSize"/> (never squashed: the caller
/// crops weeks instead), month labels centred over their columns (<see cref="ColumnSpans"/>), continuous staircase month dividers
/// (<see cref="Dividers"/>), a today ring in the accent and a dark ring on the selected or hovered day
/// (<see cref="TodayDate"/>, <see cref="SelectedDate"/>), and per-cell marks (<see cref="HeatCellMark"/>: hollow missed days, faint
/// under-target days, today filling from the bottom, hatched paused days, dashed days still to come).
/// </para>
/// </summary>
public sealed class HeatmapControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<HeatCell>?> CellsProperty =
        AvaloniaProperty.Register<HeatmapControl, IReadOnlyList<HeatCell>?>(nameof(Cells));

    public static readonly StyledProperty<int> RowCountProperty =
        AvaloniaProperty.Register<HeatmapControl, int>(nameof(RowCount), 7);

    public static readonly StyledProperty<int> ColumnCountProperty =
        AvaloniaProperty.Register<HeatmapControl, int>(nameof(ColumnCount), 1);

    public static readonly StyledProperty<IReadOnlyList<string>?> RowLabelsProperty =
        AvaloniaProperty.Register<HeatmapControl, IReadOnlyList<string>?>(nameof(RowLabels));

    /// <summary>A label above each column, or null to leave one out; the list may be shorter than the columns.</summary>
    public static readonly StyledProperty<IReadOnlyList<string?>?> ColumnLabelsProperty =
        AvaloniaProperty.Register<HeatmapControl, IReadOnlyList<string?>?>(nameof(ColumnLabels));

    /// <summary>Labels centred over runs of columns (months over their weeks); used instead of <see cref="ColumnLabels"/> when set.</summary>
    public static readonly StyledProperty<IReadOnlyList<HeatmapLabelSpan>?> ColumnSpansProperty =
        AvaloniaProperty.Register<HeatmapControl, IReadOnlyList<HeatmapLabelSpan>?>(nameof(ColumnSpans));

    /// <summary>Month dividers, each one continuous staircase line.</summary>
    public static readonly StyledProperty<IReadOnlyList<HeatmapDivider>?> DividersProperty =
        AvaloniaProperty.Register<HeatmapControl, IReadOnlyList<HeatmapDivider>?>(nameof(Dividers));

    public static readonly StyledProperty<IBrush?> EmptyBrushProperty =
        AvaloniaProperty.Register<HeatmapControl, IBrush?>(nameof(EmptyBrush));

    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<HeatmapControl, IBrush?>(nameof(AccentBrush));

    public static readonly StyledProperty<IBrush?> LabelBrushProperty =
        AvaloniaProperty.Register<HeatmapControl, IBrush?>(nameof(LabelBrush));

    /// <summary>The month dividers' and hollow cells' line colour.</summary>
    public static readonly StyledProperty<IBrush?> LineBrushProperty =
        AvaloniaProperty.Register<HeatmapControl, IBrush?>(nameof(LineBrush));

    /// <summary>The today ring (the theme's accent).</summary>
    public static readonly StyledProperty<IBrush?> TodayRingBrushProperty =
        AvaloniaProperty.Register<HeatmapControl, IBrush?>(nameof(TodayRingBrush));

    /// <summary>The selected and hovered ring (the theme's ink).</summary>
    public static readonly StyledProperty<IBrush?> SelectedRingBrushProperty =
        AvaloniaProperty.Register<HeatmapControl, IBrush?>(nameof(SelectedRingBrush));

    public static readonly StyledProperty<DateOnly?> TodayDateProperty =
        AvaloniaProperty.Register<HeatmapControl, DateOnly?>(nameof(TodayDate));

    public static readonly StyledProperty<DateOnly?> SelectedDateProperty =
        AvaloniaProperty.Register<HeatmapControl, DateOnly?>(nameof(SelectedDate));

    public static readonly StyledProperty<double> MaxCellSizeProperty =
        AvaloniaProperty.Register<HeatmapControl, double>(nameof(MaxCellSize), 36);

    /// <summary>A fixed cell size (never squashed or grown); NaN (the default) sizes cells to the width.</summary>
    public static readonly StyledProperty<double> FixedCellSizeProperty =
        AvaloniaProperty.Register<HeatmapControl, double>(nameof(FixedCellSize), double.NaN);

    /// <summary>A fixed row-label column width (so the grid's width is known before drawing); NaN measures the labels.</summary>
    public static readonly StyledProperty<double> RowLabelWidthProperty =
        AvaloniaProperty.Register<HeatmapControl, double>(nameof(RowLabelWidth), double.NaN);

    public static readonly StyledProperty<double> CellGapProperty =
        AvaloniaProperty.Register<HeatmapControl, double>(nameof(CellGap), 4);

    public static readonly StyledProperty<System.Windows.Input.ICommand?> CellCommandProperty =
        AvaloniaProperty.Register<HeatmapControl, System.Windows.Input.ICommand?>(nameof(CellCommand));

    private const double ColumnLabelHeight = 22;
    private const double RowLabelGap = 10;
    private const double DividerThickness = 2;
    private const double DividerCornerRadius = 5;
    private static readonly Typeface LabelTypeface = new(FontFamily.Default);

    private double _labelWidth = -1;
    private Dictionary<(int Row, int Column), HeatCell> _byPlace = new();
    private (int Row, int Column) _hovered = (-1, -1);

    static HeatmapControl()
    {
        AffectsRender<HeatmapControl>(
            CellsProperty, RowCountProperty, ColumnCountProperty, RowLabelsProperty, ColumnLabelsProperty, ColumnSpansProperty, DividersProperty,
            EmptyBrushProperty, AccentBrushProperty, LabelBrushProperty, LineBrushProperty, TodayRingBrushProperty, SelectedRingBrushProperty,
            TodayDateProperty, SelectedDateProperty, MaxCellSizeProperty, FixedCellSizeProperty, RowLabelWidthProperty, CellGapProperty);
        AffectsMeasure<HeatmapControl>(
            RowCountProperty, ColumnCountProperty, RowLabelsProperty, ColumnLabelsProperty, ColumnSpansProperty, MaxCellSizeProperty,
            FixedCellSizeProperty, RowLabelWidthProperty, CellGapProperty);
        CellsProperty.Changed.AddClassHandler<HeatmapControl>((control, _) => control.IndexCells());
        RowLabelsProperty.Changed.AddClassHandler<HeatmapControl>((control, _) => control._labelWidth = -1);
        RowLabelWidthProperty.Changed.AddClassHandler<HeatmapControl>((control, _) => control._labelWidth = -1);
    }

    public IReadOnlyList<HeatCell>? Cells
    {
        get => GetValue(CellsProperty);
        set => SetValue(CellsProperty, value);
    }

    public int RowCount
    {
        get => GetValue(RowCountProperty);
        set => SetValue(RowCountProperty, value);
    }

    public int ColumnCount
    {
        get => GetValue(ColumnCountProperty);
        set => SetValue(ColumnCountProperty, value);
    }

    public IReadOnlyList<string>? RowLabels
    {
        get => GetValue(RowLabelsProperty);
        set => SetValue(RowLabelsProperty, value);
    }

    public IReadOnlyList<string?>? ColumnLabels
    {
        get => GetValue(ColumnLabelsProperty);
        set => SetValue(ColumnLabelsProperty, value);
    }

    public IReadOnlyList<HeatmapLabelSpan>? ColumnSpans
    {
        get => GetValue(ColumnSpansProperty);
        set => SetValue(ColumnSpansProperty, value);
    }

    public IReadOnlyList<HeatmapDivider>? Dividers
    {
        get => GetValue(DividersProperty);
        set => SetValue(DividersProperty, value);
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

    public IBrush? LineBrush
    {
        get => GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public IBrush? TodayRingBrush
    {
        get => GetValue(TodayRingBrushProperty);
        set => SetValue(TodayRingBrushProperty, value);
    }

    public IBrush? SelectedRingBrush
    {
        get => GetValue(SelectedRingBrushProperty);
        set => SetValue(SelectedRingBrushProperty, value);
    }

    public DateOnly? TodayDate
    {
        get => GetValue(TodayDateProperty);
        set => SetValue(TodayDateProperty, value);
    }

    public DateOnly? SelectedDate
    {
        get => GetValue(SelectedDateProperty);
        set => SetValue(SelectedDateProperty, value);
    }

    public double MaxCellSize
    {
        get => GetValue(MaxCellSizeProperty);
        set => SetValue(MaxCellSizeProperty, value);
    }

    public double FixedCellSize
    {
        get => GetValue(FixedCellSizeProperty);
        set => SetValue(FixedCellSizeProperty, value);
    }

    public double RowLabelWidth
    {
        get => GetValue(RowLabelWidthProperty);
        set => SetValue(RowLabelWidthProperty, value);
    }

    public double CellGap
    {
        get => GetValue(CellGapProperty);
        set => SetValue(CellGapProperty, value);
    }

    /// <summary>Run with the cell's date when a cell that has one is clicked.</summary>
    public System.Windows.Input.ICommand? CellCommand
    {
        get => GetValue(CellCommandProperty);
        set => SetValue(CellCommandProperty, value);
    }

    private bool HasColumnLabels => ColumnLabels is not null || ColumnSpans is not null;

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        (double left, double _, double cell) = Layout(width);
        double top = HasColumnLabels ? ColumnLabelHeight : 0;
        double rings = 0;
        if (!double.IsNaN(FixedCellSize))
        {
            // A fixed grid asks for exactly its own width (the caller crops weeks to fit), with room for the rings at its edges.
            rings = 2;
            width = left + ColumnCount * cell + Math.Max(0, ColumnCount - 1) * CellGap + rings;
        }

        return new Size(width, top + RowCount * cell + Math.Max(0, RowCount - 1) * CellGap + rings);
    }

    public override void Render(DrawingContext context)
    {
        if (Cells is null || RowCount <= 0 || ColumnCount <= 0 || Bounds.Width <= 0)
        {
            return;
        }

        (double left, double top, double cell) = Layout(Bounds.Width);
        // Made on each draw, so a theme that changes a brush in place still gets its colours (a draw is rare: data or size changes).
        IBrush[] ramp = HeatRamp.Brushes(EmptyBrush, AccentBrush);
        double radius = cell * 0.28;
        DrawRowLabels(context, top, cell);
        DrawColumnLabels(context, left, cell);

        var outline = new Pen(LineBrush ?? EmptyBrush, 1);
        var dashed = new Pen(LineBrush ?? EmptyBrush, 1, new DashStyle([2, 2], 0));
        HeatCell? todayCell = null;
        HeatCell? selectedCell = null;
        foreach (HeatCell cell0 in Cells)
        {
            if (cell0.Level < 0 || cell0.Row >= RowCount || cell0.Column >= ColumnCount)
            {
                continue;
            }

            Rect rect = CellRect(cell0.Row, cell0.Column, left, top, cell);
            DrawCell(context, cell0, rect, radius, ramp, outline, dashed);
            if (cell0.Date is DateOnly date && date == TodayDate)
            {
                todayCell = cell0;
            }

            if (cell0.Date is DateOnly picked && picked == SelectedDate)
            {
                selectedCell = cell0;
            }
        }

        DrawDividers(context, left, top, cell);

        // Rings: the selected (or hovered) day dark, today in the accent; when today is selected only the accent ring shows.
        if (_byPlace.TryGetValue(_hovered, out HeatCell hovered) && hovered.Level >= 0 && hovered.Date != TodayDate && SelectedRingBrush is not null)
        {
            DrawRing(context, CellRect(hovered.Row, hovered.Column, left, top, cell), radius, SelectedRingBrush);
        }

        if (selectedCell is HeatCell selected && selected.Date != TodayDate && SelectedRingBrush is not null)
        {
            DrawRing(context, CellRect(selected.Row, selected.Column, left, top, cell), radius, SelectedRingBrush);
        }

        if (todayCell is HeatCell today && TodayRingBrush is not null)
        {
            DrawRing(context, CellRect(today.Row, today.Column, left, top, cell), radius, TodayRingBrush);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        (int row, int column) = CellAt(e.GetPosition(this));
        if ((row, column) == _hovered)
        {
            return;
        }

        _hovered = (row, column);
        bool found = _byPlace.TryGetValue((row, column), out HeatCell hit) && hit.Level >= 0;
        ToolTip.SetTip(this, found ? hit.Tip : null);
        Cursor = found && hit.Date is not null && CellCommand is not null ? new Cursor(StandardCursorType.Hand) : null;
        if (SelectedRingBrush is not null)
        {
            InvalidateVisual(); // The hovered ring moves.
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hovered = (-1, -1);
        ToolTip.SetTip(this, null);
        Cursor = null;
        if (SelectedRingBrush is not null)
        {
            InvalidateVisual();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_byPlace.TryGetValue(CellAt(e.GetPosition(this)), out HeatCell hit) && hit is { Level: >= 0, Date: DateOnly date } && CellCommand?.CanExecute(date) == true)
        {
            CellCommand.Execute(date);
        }
    }

    private void DrawRowLabels(DrawingContext context, double top, double cell)
    {
        if (RowLabels is not { } rows)
        {
            return;
        }

        for (int row = 0; row < rows.Count && row < RowCount; row++)
        {
            if (string.IsNullOrEmpty(rows[row]))
            {
                continue;
            }

            var text = new FormattedText(rows[row], CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelTypeface, 12, LabelBrush);
            context.DrawText(text, new Point(0, top + row * (cell + CellGap) + (cell - text.Height) / 2));
        }
    }

    private void DrawColumnLabels(DrawingContext context, double left, double cell)
    {
        double stride = cell + CellGap;
        double gridRight = left + ColumnCount * stride - CellGap;
        if (ColumnSpans is { } spans)
        {
            double lastRight = double.NegativeInfinity;
            foreach (HeatmapLabelSpan span in spans)
            {
                if (span.Label.Length == 0)
                {
                    continue;
                }

                var text = new FormattedText(span.Label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelTypeface, 12, LabelBrush);
                double centre = left + span.Centre * stride - CellGap / 2;
                // Centred over its columns, then kept inside the grid (clipped to the visible part).
                double x = Math.Clamp(centre - text.Width / 2, left, Math.Max(left, gridRight - text.Width));
                if (x < lastRight + 6)
                {
                    continue; // It would touch the label before it.
                }

                context.DrawText(text, new Point(x, 0));
                lastRight = x + text.Width;
            }

            return;
        }

        if (ColumnLabels is not { } columns)
        {
            return;
        }

        double previousRight = double.NegativeInfinity;
        for (int column = 0; column < columns.Count && column < ColumnCount; column++)
        {
            if (columns[column] is not { Length: > 0 } label)
            {
                continue;
            }

            var text = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelTypeface, 12, LabelBrush);
            double x = left + column * stride;
            if (x < previousRight + 6 || x + text.Width > Bounds.Width)
            {
                continue; // Too close to the label before it, or past the edge.
            }

            context.DrawText(text, new Point(x, 0));
            previousRight = x + text.Width;
        }
    }

    private void DrawCell(DrawingContext context, HeatCell cell, Rect rect, double radius, IBrush[] ramp, Pen outline, Pen dashed)
    {
        IBrush fill = ramp[Math.Clamp(cell.Level, 0, ramp.Length - 1)];
        var rounded = new RoundedRect(rect, radius);
        switch (cell.Mark)
        {
            case HeatCellMark.Hollow:
                context.DrawRectangle(null, outline, new RoundedRect(rect.Deflate(0.5), radius));
                break;
            case HeatCellMark.Faint:
                context.DrawRectangle(ramp[1], outline, new RoundedRect(rect.Deflate(0.5), radius));
                break;
            case HeatCellMark.Today:
            {
                context.DrawRectangle(ramp[0], null, rounded);
                double share = Math.Clamp(cell.Progress, 0, 1);
                if (share > 0)
                {
                    double height = rect.Height * share;
                    using (context.PushClip(rounded))
                    {
                        context.DrawRectangle(ramp[Math.Clamp(Math.Max(cell.Level, 2), 0, ramp.Length - 1)], null, new Rect(rect.X, rect.Bottom - height, rect.Width, height));
                    }
                }

                break;
            }

            case HeatCellMark.Paused:
            {
                context.DrawRectangle(ramp[0], null, rounded);
                var hatch = new Pen(LineBrush ?? EmptyBrush, 1);
                using (context.PushClip(rounded))
                {
                    for (double offset = -rect.Height; offset < rect.Width; offset += 4)
                    {
                        context.DrawLine(hatch, new Point(rect.X + offset, rect.Bottom), new Point(rect.X + offset + rect.Height, rect.Y));
                    }
                }

                break;
            }

            case HeatCellMark.Future:
                context.DrawRectangle(null, dashed, new RoundedRect(rect.Deflate(0.5), radius));
                break;
            default:
                context.DrawRectangle(fill, null, rounded);
                break;
        }
    }

    // One continuous line per month: down the right side of the new month's first column to its first row, across, down its left side.
    private void DrawDividers(DrawingContext context, double left, double top, double cell)
    {
        if (Dividers is not { Count: > 0 } dividers || LineBrush is null)
        {
            return;
        }

        double stride = cell + CellGap;
        double half = CellGap / 2;
        double gridTop = top - half;
        double gridBottom = top + RowCount * stride - half;
        double corner = Math.Min(DividerCornerRadius, stride / 2);
        var pen = new Pen(LineBrush, DividerThickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        foreach (HeatmapDivider divider in dividers)
        {
            if (divider.Column < 0 || divider.Column >= ColumnCount)
            {
                continue;
            }

            double xLeft = left + divider.Column * stride - half;
            double xRight = xLeft + stride;
            if (divider.Row <= 0 || divider.Column + 1 > ColumnCount)
            {
                context.DrawLine(pen, new Point(xLeft, gridTop), new Point(xLeft, gridBottom));
                continue;
            }

            double yStep = top + divider.Row * stride - half;
            var geometry = new StreamGeometry();
            using (StreamGeometryContext path = geometry.Open())
            {
                path.BeginFigure(new Point(xRight, gridTop), false);
                path.LineTo(new Point(xRight, yStep - corner));
                path.ArcTo(new Point(xRight - corner, yStep), new Size(corner, corner), 0, false, SweepDirection.Clockwise);
                path.LineTo(new Point(xLeft + corner, yStep));
                path.ArcTo(new Point(xLeft, yStep + corner), new Size(corner, corner), 0, false, SweepDirection.CounterClockwise);
                path.LineTo(new Point(xLeft, gridBottom));
                path.EndFigure(false);
            }

            context.DrawGeometry(null, pen, geometry);
        }
    }

    private static void DrawRing(DrawingContext context, Rect rect, double radius, IBrush brush)
    {
        context.DrawRectangle(null, new Pen(brush, 2), new RoundedRect(rect.Inflate(1.5), radius + 1.5));
    }

    private Rect CellRect(int row, int column, double left, double top, double cell)
    {
        return new Rect(left + column * (cell + CellGap), top + row * (cell + CellGap), cell, cell);
    }

    private void IndexCells()
    {
        _byPlace = new Dictionary<(int Row, int Column), HeatCell>();
        _hovered = (-1, -1);
        foreach (HeatCell cell in Cells ?? [])
        {
            _byPlace[(cell.Row, cell.Column)] = cell;
        }
    }

    // Where the first cell starts and how big a cell is, for a given width.
    private (double Left, double Top, double Cell) Layout(double width)
    {
        if (_labelWidth < 0)
        {
            _labelWidth = !double.IsNaN(RowLabelWidth) ? RowLabelWidth
                : RowLabels is { Count: > 0 } rows ? rows.Max(label => LabelWidth(label)) + RowLabelGap : 0;
        }

        double left = _labelWidth;
        double top = HasColumnLabels ? ColumnLabelHeight : 0;
        if (!double.IsNaN(FixedCellSize))
        {
            return (left + 1, top + 1, FixedCellSize);
        }

        int columns = Math.Max(1, ColumnCount);
        double fit = (width - left - Math.Max(0, columns - 1) * CellGap) / columns;
        return (left, top, Math.Max(4, Math.Min(MaxCellSize, fit)));
    }

    private static double LabelWidth(string label)
    {
        return new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelTypeface, 12, Brushes.Black).Width;
    }

    private (int Row, int Column) CellAt(Point point)
    {
        (double left, double top, double cell) = Layout(Bounds.Width);
        double stride = cell + CellGap;
        int column = (int)Math.Floor((point.X - left) / stride);
        int row = (int)Math.Floor((point.Y - top) / stride);
        bool insideX = point.X >= left && point.X - left - column * stride <= cell;
        bool insideY = point.Y >= top && point.Y - top - row * stride <= cell;
        return insideX && insideY && column < ColumnCount && row < RowCount ? (row, column) : (-1, -1);
    }
}
