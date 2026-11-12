using Avalonia;
using Avalonia.Controls;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// A list pane, a <see cref="PaneSplitter"/> and a detail pane side by side (round 2, change note §4a "Pane split"), laid out by the
/// panel's own width (never the window's): the list gets <see cref="ListWidth"/>; when that would leave the detail under
/// <see cref="KeepDetailWidth"/>, the list shrinks first, down to <see cref="MinListWidth"/>; when even that leaves the detail under
/// <see cref="CollapseDetailBelow"/>, the list collapses to its icons (<see cref="CollapsedListWidth"/>) and the splitter hides.
/// Children, in order: the list, the splitter, the detail. <see cref="ShownListWidth"/> and <see cref="IsListCollapsed"/> say what
/// is shown (the splitter starts its moves from the shown width).
/// </summary>
public sealed class SplitPanesPanel : Panel
{
    public static readonly StyledProperty<double> ListWidthProperty =
        AvaloniaProperty.Register<SplitPanesPanel, double>(nameof(ListWidth), 470);

    public static readonly StyledProperty<double> MinListWidthProperty =
        AvaloniaProperty.Register<SplitPanesPanel, double>(nameof(MinListWidth), 360);

    public static readonly StyledProperty<double> KeepDetailWidthProperty =
        AvaloniaProperty.Register<SplitPanesPanel, double>(nameof(KeepDetailWidth), 760);

    public static readonly StyledProperty<double> CollapseDetailBelowProperty =
        AvaloniaProperty.Register<SplitPanesPanel, double>(nameof(CollapseDetailBelow), 480);

    public static readonly StyledProperty<double> CollapsedListWidthProperty =
        AvaloniaProperty.Register<SplitPanesPanel, double>(nameof(CollapsedListWidth), 64);

    public static readonly DirectProperty<SplitPanesPanel, double> ShownListWidthProperty =
        AvaloniaProperty.RegisterDirect<SplitPanesPanel, double>(nameof(ShownListWidth), panel => panel.ShownListWidth);

    public static readonly DirectProperty<SplitPanesPanel, bool> IsListCollapsedProperty =
        AvaloniaProperty.RegisterDirect<SplitPanesPanel, bool>(nameof(IsListCollapsed), panel => panel.IsListCollapsed);

    private double _shownListWidth = double.NaN;
    private bool _isListCollapsed;

    static SplitPanesPanel()
    {
        AffectsMeasure<SplitPanesPanel>(ListWidthProperty, MinListWidthProperty, KeepDetailWidthProperty, CollapseDetailBelowProperty, CollapsedListWidthProperty);
    }

    public double ListWidth
    {
        get => GetValue(ListWidthProperty);
        set => SetValue(ListWidthProperty, value);
    }

    public double MinListWidth
    {
        get => GetValue(MinListWidthProperty);
        set => SetValue(MinListWidthProperty, value);
    }

    public double KeepDetailWidth
    {
        get => GetValue(KeepDetailWidthProperty);
        set => SetValue(KeepDetailWidthProperty, value);
    }

    public double CollapseDetailBelow
    {
        get => GetValue(CollapseDetailBelowProperty);
        set => SetValue(CollapseDetailBelowProperty, value);
    }

    public double CollapsedListWidth
    {
        get => GetValue(CollapsedListWidthProperty);
        set => SetValue(CollapsedListWidthProperty, value);
    }

    /// <summary>The list's width as laid out.</summary>
    public double ShownListWidth
    {
        get => _shownListWidth;
        private set => SetAndRaise(ShownListWidthProperty, ref _shownListWidth, value);
    }

    /// <summary>True while the list is collapsed to its icons.</summary>
    public bool IsListCollapsed
    {
        get => _isListCollapsed;
        private set => SetAndRaise(IsListCollapsedProperty, ref _isListCollapsed, value);
    }

    /// <summary>
    /// The rule on its own (also for the checks): the list's width and whether it collapses, for a <paramref name="total"/> width
    /// holding a splitter <paramref name="splitter"/> wide.
    /// </summary>
    public static (double List, bool Collapsed) Layout(
        double total, double splitter, double preferred, double minList, double keepDetail, double collapseBelow, double collapsedWidth)
    {
        double room = Math.Max(0, total - splitter);
        double list = Math.Max(minList, preferred);
        if (room - list < keepDetail)
        {
            list = Math.Max(minList, room - keepDetail);
        }

        if (room - list < collapseBelow)
        {
            return (collapsedWidth, true);
        }

        return (list, false);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        (Control? list, Control? splitter, Control? detail) = Parts();
        double total = double.IsInfinity(availableSize.Width) ? ListWidth + KeepDetailWidth : availableSize.Width;
        double splitterWidth = SplitterWidth(splitter);
        (double listWidth, bool collapsed) = Layout(total, splitterWidth, ListWidth, MinListWidth, KeepDetailWidth, CollapseDetailBelow, CollapsedListWidth);
        if (collapsed)
        {
            splitterWidth = 0;
        }

        ShownListWidth = listWidth;
        IsListCollapsed = collapsed;
        if (splitter is not null)
        {
            splitter.IsVisible = !collapsed;
            splitter.Measure(new Size(splitterWidth, availableSize.Height));
            if (splitter is PaneSplitter paneSplitter)
            {
                paneSplitter.ShownValue = listWidth;
            }
        }

        double detailWidth = Math.Max(0, total - listWidth - splitterWidth);
        list?.Measure(new Size(listWidth, availableSize.Height));
        detail?.Measure(new Size(detailWidth, availableSize.Height));
        double height = Math.Max(list?.DesiredSize.Height ?? 0, detail?.DesiredSize.Height ?? 0);
        return new Size(total, double.IsInfinity(availableSize.Height) ? height : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        (Control? list, Control? splitter, Control? detail) = Parts();
        double splitterWidth = IsListCollapsed ? 0 : SplitterWidth(splitter);
        double listWidth = double.IsNaN(ShownListWidth) ? ListWidth : ShownListWidth;
        list?.Arrange(new Rect(0, 0, listWidth, finalSize.Height));
        splitter?.Arrange(new Rect(listWidth, 0, splitterWidth, finalSize.Height));

        double detailLeft = listWidth + splitterWidth;
        detail?.Arrange(new Rect(detailLeft, 0, Math.Max(0, finalSize.Width - detailLeft), finalSize.Height));
        return finalSize;
    }

    // The splitter's own width (set on it), so hiding it never changes the sums.
    private static double SplitterWidth(Control? splitter)
    {
        return splitter is null || double.IsNaN(splitter.Width) ? 0 : splitter.Width;
    }

    private (Control? List, Control? Splitter, Control? Detail) Parts()
    {
        return (Children.Count > 0 ? Children[0] : null, Children.Count > 1 ? Children[1] : null, Children.Count > 2 ? Children[2] : null);
    }
}
