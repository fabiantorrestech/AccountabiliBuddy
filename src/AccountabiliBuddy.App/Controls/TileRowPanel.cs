using Avalonia;
using Avalonia.Controls;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// Tiles side by side, as many per row as fit at <see cref="MinTileWidth"/> each, by the panel's own width (round 2, change note §4a
/// "Detail panel reflow"): all in one row when they fit; else rows of that many, left to right, so the last tile (the goal's Target)
/// drops to its own full-width row first; then two per row; then one. Tiles in a row share its width and its height. Also the bottom
/// cards' "two columns, then stacked". Nothing overlaps or scrolls sideways.
/// </summary>
public sealed class TileRowPanel : Panel
{
    public static readonly StyledProperty<double> MinTileWidthProperty =
        AvaloniaProperty.Register<TileRowPanel, double>(nameof(MinTileWidth), 230);

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<TileRowPanel, double>(nameof(Spacing), 12);

    private readonly List<double> _rowHeights = [];

    static TileRowPanel()
    {
        AffectsMeasure<TileRowPanel>(MinTileWidthProperty, SpacingProperty);
    }

    public double MinTileWidth
    {
        get => GetValue(MinTileWidthProperty);
        set => SetValue(MinTileWidthProperty, value);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>The rule on its own (also for the checks): how many tiles go in each row, top to bottom.</summary>
    public static IReadOnlyList<int> Rows(double width, int count, double minTile, double spacing)
    {
        if (count <= 0)
        {
            return [];
        }

        int perRow = Math.Clamp((int)Math.Floor((width + spacing) / (minTile + spacing)), 1, count);
        var rows = new List<int>();
        for (int left = count; left > 0; left -= perRow)
        {
            rows.Add(Math.Min(perRow, left));
        }

        return rows;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        List<Control> tiles = Children.Where(child => child.IsVisible).ToList();
        double width = double.IsInfinity(availableSize.Width) ? tiles.Count * (MinTileWidth + Spacing) : availableSize.Width;
        _rowHeights.Clear();
        int index = 0;
        foreach (int inRow in Rows(width, tiles.Count, MinTileWidth, Spacing))
        {
            double tileWidth = TileWidth(width, inRow);
            double rowHeight = 0;
            for (int i = 0; i < inRow; i++, index++)
            {
                tiles[index].Measure(new Size(tileWidth, double.PositiveInfinity));
                rowHeight = Math.Max(rowHeight, tiles[index].DesiredSize.Height);
            }

            _rowHeights.Add(rowHeight);
        }

        double height = _rowHeights.Sum() + Math.Max(0, _rowHeights.Count - 1) * Spacing;
        return new Size(double.IsInfinity(availableSize.Width) ? width : availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        List<Control> tiles = Children.Where(child => child.IsVisible).ToList();
        IReadOnlyList<int> rows = Rows(finalSize.Width, tiles.Count, MinTileWidth, Spacing);
        int index = 0;
        double y = 0;
        for (int row = 0; row < rows.Count; row++)
        {
            double tileWidth = TileWidth(finalSize.Width, rows[row]);
            double rowHeight = row < _rowHeights.Count ? _rowHeights[row] : 0;
            for (int i = 0; i < rows[row]; i++, index++)
            {
                tiles[index].Arrange(new Rect(i * (tileWidth + Spacing), y, tileWidth, rowHeight));
            }

            y += rowHeight + Spacing;
        }

        return finalSize;
    }

    private double TileWidth(double width, int inRow)
    {
        return Math.Max(0, (width - Math.Max(0, inRow - 1) * Spacing) / Math.Max(1, inRow));
    }
}
