using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.App.Services.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// Lays the Shelf's columns side by side (round 2 R3, change note §2; paged in PG1, owner 2026-11-14). The columns share the room the
/// panel is given evenly, never narrower than <see cref="MinColumnWidth"/>. The three bins may shrink to <see cref="SmallestColumnWidth"/>
/// first (so they fit the smallest window, 901 × 567); when the columns still do not fit, only as many whole columns as fit are shown
/// (<see cref="ColumnsPerPage"/>) and the rest are on other pages: <see cref="FirstIndex"/> is the column the page was turned to, the
/// page holding it is the one drawn. There is no sideways scrolling. Hidden columns are neither measured nor focusable. Every column is
/// as tall as the panel, so each one scrolls on its own and its header stays put. Turning the page slides the columns in (Settings ›
/// Animations; Off = no slide). <see cref="TaskColumnPaging"/> holds the numbers.
/// </summary>
public sealed class TaskColumnsPanel : Panel
{
    private const double SlideDistance = 36;
    private static readonly TimeSpan SlideTime = TimeSpan.FromMilliseconds(170);

    public static readonly StyledProperty<double> ViewportWidthProperty =
        AvaloniaProperty.Register<TaskColumnsPanel, double>(nameof(ViewportWidth), double.NaN);

    public static readonly StyledProperty<double> MinColumnWidthProperty =
        AvaloniaProperty.Register<TaskColumnsPanel, double>(nameof(MinColumnWidth), 300);

    public static readonly StyledProperty<double> SmallestColumnWidthProperty =
        AvaloniaProperty.Register<TaskColumnsPanel, double>(nameof(SmallestColumnWidth), 220);

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<TaskColumnsPanel, double>(nameof(Spacing), 14);

    /// <summary>The index of a column of the page to show (the page that holds it is drawn).</summary>
    public static readonly StyledProperty<int> FirstIndexProperty =
        AvaloniaProperty.Register<TaskColumnsPanel, int>(nameof(FirstIndex));

    public static readonly DirectProperty<TaskColumnsPanel, int> ColumnsPerPageProperty =
        AvaloniaProperty.RegisterDirect<TaskColumnsPanel, int>(nameof(ColumnsPerPage), panel => panel.ColumnsPerPage);

    private int _columnsPerPage = 1;

    static TaskColumnsPanel()
    {
        AffectsMeasure<TaskColumnsPanel>(ViewportWidthProperty, MinColumnWidthProperty, SmallestColumnWidthProperty, SpacingProperty, FirstIndexProperty);
    }

    /// <summary>The width the columns share; NaN uses the width offered, when it is finite.</summary>
    public double ViewportWidth
    {
        get => GetValue(ViewportWidthProperty);
        set => SetValue(ViewportWidthProperty, value);
    }

    public double MinColumnWidth
    {
        get => GetValue(MinColumnWidthProperty);
        set => SetValue(MinColumnWidthProperty, value);
    }

    /// <summary>How narrow the (up to three) bins may get before paging starts instead; never above <see cref="MinColumnWidth"/>.</summary>
    public double SmallestColumnWidth
    {
        get => GetValue(SmallestColumnWidthProperty);
        set => SetValue(SmallestColumnWidthProperty, value);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public int FirstIndex
    {
        get => GetValue(FirstIndexProperty);
        set => SetValue(FirstIndexProperty, value);
    }

    /// <summary>How many columns one page shows now (all of them when they fit); set while measuring.</summary>
    public int ColumnsPerPage
    {
        get => _columnsPerPage;
        private set => SetAndRaise(ColumnsPerPageProperty, ref _columnsPerPage, value);
    }

    /// <summary>The index of the first column of the page drawn now.</summary>
    public int ShownFirst { get; private set; }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FirstIndexProperty && IsEffectivelyVisible && change.OldValue is int old && change.NewValue is int now)
        {
            int perPage = Math.Max(1, _columnsPerPage);
            int count = Children.Count;
            int from = TaskColumnPaging.PageOf(old, count, perPage);
            int to = TaskColumnPaging.PageOf(now, count, perPage);
            if (from != to)
            {
                Slide(to > from ? 1 : -1);
            }
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        int count = Children.Count;
        double room = Room(availableSize.Width);
        int perPage = TaskColumnPaging.ColumnsPerPage(room, count, MinColumnWidth, SmallestColumnWidth, Spacing);
        ColumnsPerPage = perPage;
        double width = TaskColumnPaging.ColumnWidth(room, perPage, Spacing);
        int first = TaskColumnPaging.FirstOfPage(TaskColumnPaging.PageOf(FirstIndex, count, perPage), count, perPage);
        ShownFirst = first;
        double height = double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height;
        for (int index = 0; index < count; index++)
        {
            Control child = Children[index];
            bool shown = index >= first && index < first + perPage;
            if (child.IsVisible != shown)
            {
                child.IsVisible = shown;
            }

            if (shown)
            {
                child.Measure(new Size(width, availableSize.Height));
                if (double.IsInfinity(availableSize.Height))
                {
                    height = Math.Max(height, child.DesiredSize.Height);
                }
            }
        }

        return new Size(double.IsFinite(room) ? room : perPage * width + (perPage - 1) * Spacing, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int count = Children.Count;
        double room = Room(finalSize.Width);
        int perPage = TaskColumnPaging.ColumnsPerPage(room, count, MinColumnWidth, SmallestColumnWidth, Spacing);
        double width = TaskColumnPaging.ColumnWidth(room, perPage, Spacing);
        int first = TaskColumnPaging.FirstOfPage(TaskColumnPaging.PageOf(FirstIndex, count, perPage), count, perPage);
        double x = 0;
        for (int index = first; index < Math.Min(count, first + perPage); index++)
        {
            Children[index].Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width + Spacing;
        }

        return finalSize;
    }

    // The room the columns share: the viewport width when one is given, else the width offered.
    private double Room(double offered)
    {
        if (!double.IsNaN(ViewportWidth) && ViewportWidth > 0)
        {
            return ViewportWidth;
        }

        return double.IsFinite(offered) ? offered : Children.Count * MinColumnWidth + Math.Max(0, Children.Count - 1) * Spacing;
    }

    // A page turn: the columns slide in a few pixels from the side the page came from while they fade in (Animations Off: nothing).
    private async void Slide(int direction)
    {
        if (Motion.Current is not { IsOn: true } motion)
        {
            return;
        }

        var shift = RenderTransform as TranslateTransform ?? new TranslateTransform();
        RenderTransform = shift;
        var easing = new CubicEaseOut();
        TimeSpan duration = motion.Scale(SlideTime);
        try
        {
            await Task.WhenAll(
                Frames(duration, easing, Visual.OpacityProperty, 0.0, 1.0).RunAsync(this),
                Frames(duration, easing, TranslateTransform.XProperty, direction * SlideDistance, 0.0).RunAsync(shift));
        }
        catch (Exception)
        {
            // Never leave the columns half-faded.
            Opacity = 1;
            shift.X = 0;
        }
    }

    private static Animation Frames(TimeSpan duration, Easing easing, AvaloniaProperty property, double from, double to) => new()
    {
        Duration = duration,
        Easing = easing,
        FillMode = FillMode.None,
        Children =
        {
            new KeyFrame { Cue = new Cue(0), Setters = { new Setter(property, from) } },
            new KeyFrame { Cue = new Cue(1), Setters = { new Setter(property, to) } },
        },
    };
}
