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

    public static readonly StyledProperty<IBrush?> EmptyBrushProperty =
        AvaloniaProperty.Register<HeatmapControl, IBrush?>(nameof(EmptyBrush));

    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<HeatmapControl, IBrush?>(nameof(AccentBrush));

    public static readonly StyledProperty<IBrush?> LabelBrushProperty =
        AvaloniaProperty.Register<HeatmapControl, IBrush?>(nameof(LabelBrush));

    public static readonly StyledProperty<double> MaxCellSizeProperty =
        AvaloniaProperty.Register<HeatmapControl, double>(nameof(MaxCellSize), 36);

    public static readonly StyledProperty<double> CellGapProperty =
        AvaloniaProperty.Register<HeatmapControl, double>(nameof(CellGap), 4);

    public static readonly StyledProperty<System.Windows.Input.ICommand?> CellCommandProperty =
        AvaloniaProperty.Register<HeatmapControl, System.Windows.Input.ICommand?>(nameof(CellCommand));

    private const double ColumnLabelHeight = 22;
    private const double RowLabelGap = 10;
    private static readonly Typeface LabelTypeface = new(FontFamily.Default);

    private IBrush[]? _ramp;
    private double _labelWidth = -1;
    private Dictionary<(int Row, int Column), HeatCell> _byPlace = new();
    private (int Row, int Column) _hovered = (-1, -1);

    static HeatmapControl()
    {
        AffectsRender<HeatmapControl>(
            CellsProperty, RowCountProperty, ColumnCountProperty, RowLabelsProperty, ColumnLabelsProperty, EmptyBrushProperty,
            AccentBrushProperty, LabelBrushProperty, MaxCellSizeProperty, CellGapProperty);
        AffectsMeasure<HeatmapControl>(RowCountProperty, ColumnCountProperty, RowLabelsProperty, ColumnLabelsProperty, MaxCellSizeProperty, CellGapProperty);
        CellsProperty.Changed.AddClassHandler<HeatmapControl>((control, _) => control.IndexCells());
        RowLabelsProperty.Changed.AddClassHandler<HeatmapControl>((control, _) => control._labelWidth = -1);
        EmptyBrushProperty.Changed.AddClassHandler<HeatmapControl>((control, _) => control._ramp = null);
        AccentBrushProperty.Changed.AddClassHandler<HeatmapControl>((control, _) => control._ramp = null);
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

    public double MaxCellSize
    {
        get => GetValue(MaxCellSizeProperty);
        set => SetValue(MaxCellSizeProperty, value);
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

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        (double _, double _, double cell) = Layout(width);
        double top = ColumnLabels is null ? 0 : ColumnLabelHeight;
        return new Size(width, top + RowCount * cell + Math.Max(0, RowCount - 1) * CellGap);
    }

    public override void Render(DrawingContext context)
    {
        if (Cells is null || RowCount <= 0 || ColumnCount <= 0 || Bounds.Width <= 0)
        {
            return;
        }

        (double left, double top, double cell) = Layout(Bounds.Width);
        _ramp ??= HeatRamp.Brushes(EmptyBrush, AccentBrush);
        double radius = cell * 0.28;
        if (RowLabels is { } rows)
        {
            for (int row = 0; row < rows.Count && row < RowCount; row++)
            {
                var text = new FormattedText(rows[row], CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelTypeface, 12, LabelBrush);
                context.DrawText(text, new Point(0, top + row * (cell + CellGap) + (cell - text.Height) / 2));
            }
        }

        if (ColumnLabels is { } columns)
        {
            double lastRight = double.NegativeInfinity;
            for (int column = 0; column < columns.Count && column < ColumnCount; column++)
            {
                if (columns[column] is not { Length: > 0 } label)
                {
                    continue;
                }

                var text = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelTypeface, 12, LabelBrush);
                double x = left + column * (cell + CellGap);
                if (x < lastRight + 6 || x + text.Width > Bounds.Width)
                {
                    continue; // Too close to the label before it, or past the edge.
                }

                context.DrawText(text, new Point(x, 0));
                lastRight = x + text.Width;
            }
        }

        foreach (HeatCell cell0 in Cells)
        {
            if (cell0.Level < 0 || cell0.Row >= RowCount || cell0.Column >= ColumnCount)
            {
                continue;
            }

            var rect = new Rect(left + cell0.Column * (cell + CellGap), top + cell0.Row * (cell + CellGap), cell, cell);
            context.DrawRectangle(_ramp[Math.Clamp(cell0.Level, 0, _ramp.Length - 1)], null, new RoundedRect(rect, radius));
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
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hovered = (-1, -1);
        ToolTip.SetTip(this, null);
        Cursor = null;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_byPlace.TryGetValue(CellAt(e.GetPosition(this)), out HeatCell hit) && hit is { Level: >= 0, Date: DateOnly date } && CellCommand?.CanExecute(date) == true)
        {
            CellCommand.Execute(date);
        }
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
            _labelWidth = RowLabels is { Count: > 0 } rows ? rows.Max(label => LabelWidth(label)) + RowLabelGap : 0;
        }

        double left = _labelWidth;
        double top = ColumnLabels is null ? 0 : ColumnLabelHeight;
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
