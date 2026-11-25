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
/// Lays the Shelf's columns side by side (round 2 R3, change note §2; paged in PG1, owner 2026-11-14; Tasks redesign 4c). The columns
/// share the room the panel is given by their <see cref="WeightProperty"/> (Next and To sort by <see cref="Split"/>: the
/// <see cref="SplitRoleProperty"/> "lead" takes Split, "trail" the rest), never narrower than <see cref="MinColumnWidth"/>; a rail
/// (<see cref="IsRailProperty"/>: a folded column, the empty Later) is <see cref="RailWidth"/> wide. The three bins may shrink to
/// <see cref="SmallestColumnWidth"/> first (so they fit the smallest window, 901 × 567); when the columns still do not fit, only as many
/// whole columns as fit are shown (<see cref="ColumnsPerPage"/>, at most <see cref="MaxPerPage"/> when set: Group by Labels' 4, all as
/// wide) and the rest are on other pages: <see cref="FirstIndex"/> is the column the page was turned to, the page holding it is the one
/// drawn. There is no sideways scrolling. Hidden columns are neither measured nor focusable. Every column is as tall as the panel, so each
/// one scrolls on its own and its header stays put. Turning the page slides the columns in (Settings › Animations; Off = no slide).
/// <see cref="TaskColumnPaging"/> holds the paging numbers. The handle between Next and To sort is drawn over the gap by the Shelf
/// (<see cref="SplitHandle"/> says where).
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

    /// <summary>The most columns a page shows, all as wide (Group by Labels: 4); 0 = as many as fit, by weight.</summary>
    public static readonly StyledProperty<int> MaxPerPageProperty =
        AvaloniaProperty.Register<TaskColumnsPanel, int>(nameof(MaxPerPage));

    /// <summary>The "lead" column's share of the width it shares with the "trail" one (Next's of Next and To sort).</summary>
    public static readonly StyledProperty<double> SplitProperty =
        AvaloniaProperty.Register<TaskColumnsPanel, double>(nameof(Split), 0.58);

    public static readonly StyledProperty<double> RailWidthProperty =
        AvaloniaProperty.Register<TaskColumnsPanel, double>(nameof(RailWidth), 62);

    /// <summary>A column's share of the width among the open ones (default 1; Later 0.8).</summary>
    public static readonly AttachedProperty<double> WeightProperty =
        AvaloniaProperty.RegisterAttached<TaskColumnsPanel, Control, double>("Weight", 1);

    /// <summary>The column is a rail (folded, or the empty Later): <see cref="RailWidth"/> wide.</summary>
    public static readonly AttachedProperty<bool> IsRailProperty =
        AvaloniaProperty.RegisterAttached<TaskColumnsPanel, Control, bool>("IsRail");

    /// <summary>"lead" (Next), "trail" (To sort) or empty: the two whose widths follow <see cref="Split"/>.</summary>
    public static readonly AttachedProperty<string> SplitRoleProperty =
        AvaloniaProperty.RegisterAttached<TaskColumnsPanel, Control, string>("SplitRole", string.Empty);

    public static readonly DirectProperty<TaskColumnsPanel, int> ColumnsPerPageProperty =
        AvaloniaProperty.RegisterDirect<TaskColumnsPanel, int>(nameof(ColumnsPerPage), panel => panel.ColumnsPerPage);

    public static readonly DirectProperty<TaskColumnsPanel, double> ColumnWidthProperty =
        AvaloniaProperty.RegisterDirect<TaskColumnsPanel, double>(nameof(ColumnWidth), panel => panel.ColumnWidth);

    private int _columnsPerPage = 1;
    private double _columnWidth;

    static TaskColumnsPanel()
    {
        AffectsMeasure<TaskColumnsPanel>(
            ViewportWidthProperty, MinColumnWidthProperty, SmallestColumnWidthProperty, SpacingProperty, FirstIndexProperty, MaxPerPageProperty,
            SplitProperty, RailWidthProperty);
        AffectsParentMeasure<TaskColumnsPanel>(WeightProperty, IsRailProperty, SplitRoleProperty);
    }

    public static double GetWeight(Control control) => control.GetValue(WeightProperty);

    public static void SetWeight(Control control, double value) => control.SetValue(WeightProperty, value);

    public static bool GetIsRail(Control control) => control.GetValue(IsRailProperty);

    public static void SetIsRail(Control control, bool value) => control.SetValue(IsRailProperty, value);

    public static string GetSplitRole(Control control) => control.GetValue(SplitRoleProperty);

    public static void SetSplitRole(Control control, string value) => control.SetValue(SplitRoleProperty, value);

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

    public int MaxPerPage
    {
        get => GetValue(MaxPerPageProperty);
        set => SetValue(MaxPerPageProperty, value);
    }

    public double Split
    {
        get => GetValue(SplitProperty);
        set => SetValue(SplitProperty, value);
    }

    public double RailWidth
    {
        get => GetValue(RailWidthProperty);
        set => SetValue(RailWidthProperty, value);
    }

    /// <summary>How many columns one page shows now (all of them when they fit); set while measuring.</summary>
    public int ColumnsPerPage
    {
        get => _columnsPerPage;
        private set => SetAndRaise(ColumnsPerPageProperty, ref _columnsPerPage, value);
    }

    /// <summary>One column slot's width as last laid out (Group by Labels' columns are all this wide; the bands span them).</summary>
    public double ColumnWidth
    {
        get => _columnWidth;
        private set => SetAndRaise(ColumnWidthProperty, ref _columnWidth, value);
    }

    /// <summary>The index of the first column of the page drawn now.</summary>
    public int ShownFirst { get; private set; }

    /// <summary>
    /// Where the handle between the "lead" and "trail" columns goes, as last arranged; null when the two are not side by side and open on
    /// this page.
    /// </summary>
    public TaskColumnSplitPair? SplitHandle { get; private set; }

    /// <summary>The handle's place changed (a resize, a fold, a page turn).</summary>
    public event EventHandler? SplitHandleChanged;

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
        double room = Room(availableSize.Width);
        PagePlan plan = Plan(room);
        ColumnsPerPage = plan.PerPage;
        ColumnWidth = plan.SlotWidth;
        ShownFirst = plan.First;
        double height = double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height;
        for (int index = 0; index < Children.Count; index++)
        {
            Control child = Children[index];
            bool shown = index >= plan.First && index < plan.First + plan.Widths.Length;
            if (child.IsVisible != shown)
            {
                child.IsVisible = shown;
            }

            if (shown)
            {
                child.Measure(new Size(plan.Widths[index - plan.First], availableSize.Height));
                if (double.IsInfinity(availableSize.Height))
                {
                    height = Math.Max(height, child.DesiredSize.Height);
                }
            }
        }

        double used = plan.Widths.Sum() + Math.Max(0, plan.Widths.Length - 1) * Spacing;
        return new Size(double.IsFinite(room) ? room : used, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        PagePlan plan = Plan(Room(finalSize.Width));
        double x = 0;
        TaskColumnSplitPair? handle = null;
        for (int slot = 0; slot < plan.Widths.Length; slot++)
        {
            Control child = Children[plan.First + slot];
            double width = plan.Widths[slot];
            child.Arrange(new Rect(x, 0, width, finalSize.Height));
            if (slot + 1 < plan.Widths.Length && PairsWith(child, Children[plan.First + slot + 1]))
            {
                handle = new TaskColumnSplitPair(x + width + Spacing / 2, x, width + plan.Widths[slot + 1], GetSplitRole(child) == "lead");
            }

            x += width + Spacing;
        }

        if (handle != SplitHandle)
        {
            SplitHandle = handle;
            SplitHandleChanged?.Invoke(this, EventArgs.Empty);
        }

        return finalSize;
    }

    // The lead and trail columns side by side, both open (not rails).
    private static bool PairsWith(Control left, Control right)
    {
        string a = GetSplitRole(left);
        string b = GetSplitRole(right);
        return !GetIsRail(left) && !GetIsRail(right) && ((a == "lead" && b == "trail") || (a == "trail" && b == "lead"));
    }

    // The page's columns and their widths for this much room.
    private PagePlan Plan(double room)
    {
        int count = Children.Count;
        if (count == 0)
        {
            return new PagePlan(1, 0, [], 0);
        }

        if (MaxPerPage > 0)
        {
            // Group by Labels: as many columns of at least the minimum as fit, at most MaxPerPage, all as wide (the last page keeps that width).
            int fit = double.IsFinite(room) ? (int)Math.Floor((room + Spacing) / (MinColumnWidth + Spacing)) : MaxPerPage;
            int perPage = Math.Clamp(Math.Min(fit, count), 1, MaxPerPage);
            double slot = TaskColumnPaging.ColumnWidth(room, perPage, Spacing);
            int firstOfPage = TaskColumnPaging.FirstOfPage(TaskColumnPaging.PageOf(FirstIndex, count, perPage), count, perPage);
            int shown = Math.Min(perPage, count - firstOfPage);
            return new PagePlan(perPage, firstOfPage, Enumerable.Repeat(slot, shown).ToArray(), slot);
        }

        int per = PerPageByWeight(room, count);
        int first = TaskColumnPaging.FirstOfPage(TaskColumnPaging.PageOf(FirstIndex, count, per), count, per);
        int n = Math.Min(per, count - first);
        double min = count <= TaskColumnPaging.ShrinkUpTo ? Math.Min(MinColumnWidth, SmallestColumnWidth) : MinColumnWidth;
        double[] widths = WeightedWidths(room, first, n, min);
        return new PagePlan(per, first, widths, widths.Length > 0 ? widths.Max() : 0);
    }

    // All the columns when they fit at their minimum (rails at their width; the three bins may shrink to the smallest width); else as many
    // columns of the minimum width as fit.
    private int PerPageByWeight(double room, int count)
    {
        if (count <= 1 || !double.IsFinite(room))
        {
            return Math.Max(1, count);
        }

        double min = count <= TaskColumnPaging.ShrinkUpTo ? Math.Min(MinColumnWidth, SmallestColumnWidth) : MinColumnWidth;
        double need = Children.Sum(child => GetIsRail(child) ? RailWidth : min) + (count - 1) * Spacing;
        if (need <= room + 0.01)
        {
            return count;
        }

        return Math.Clamp((int)Math.Floor((room + Spacing) / (MinColumnWidth + Spacing)), 1, count);
    }

    // Rails at their width; the other columns share what is left by weight, none narrower than the minimum (those that would be take the
    // minimum and the rest share again).
    private double[] WeightedWidths(double room, int first, int n, double min)
    {
        var widths = new double[n];
        if (n == 0)
        {
            return widths;
        }

        if (!double.IsFinite(room))
        {
            Array.Fill(widths, MinColumnWidth);
            return widths;
        }

        double left = room - (n - 1) * Spacing;
        // Next and To sort share their 1.0 by the split only while both are open on this page; alone, the open one takes the whole 1.0.
        bool paired = Enumerable.Range(first, n).Any(index => GetSplitRole(Children[index]) == "lead" && !GetIsRail(Children[index]))
            && Enumerable.Range(first, n).Any(index => GetSplitRole(Children[index]) == "trail" && !GetIsRail(Children[index]));
        var free = new List<int>();
        for (int slot = 0; slot < n; slot++)
        {
            if (GetIsRail(Children[first + slot]))
            {
                widths[slot] = RailWidth;
                left -= RailWidth;
            }
            else
            {
                free.Add(slot);
            }
        }

        double freeTotal = free.Sum(slot => WeightOf(Children[first + slot], paired));
        bool minimumsBind = free.Any(slot => left * WeightOf(Children[first + slot], paired) / freeTotal < min);
        if (minimumsBind && free.Any(slot => GetSplitRole(Children[first + slot]).Length > 0))
        {
            return LeadFirstWidths(widths, free, first, left, min, freeTotal, paired);
        }

        while (free.Count > 0)
        {
            double total = free.Sum(slot => WeightOf(Children[first + slot], paired));
            List<int> tooNarrow = free.Where(slot => left * WeightOf(Children[first + slot], paired) / total < min).ToList();
            if (tooNarrow.Count == 0 || tooNarrow.Count == free.Count)
            {
                foreach (int slot in free)
                {
                    widths[slot] = Math.Max(0, Math.Floor(left * WeightOf(Children[first + slot], paired) / total));
                }

                break;
            }

            foreach (int slot in tooNarrow)
            {
                widths[slot] = min;
                left -= min;
                free.Remove(slot);
            }
        }

        return widths;
    }

    // Group by Bins when the minimums bind (a narrow window): every open bin starts at the minimum and the room left goes to Next first,
    // then To sort, then Later (each up to its weighted width; Later never wider than Next); anything still left goes to Next, so Next leads.
    private double[] LeadFirstWidths(double[] widths, List<int> free, int first, double left, double min, double total, bool paired)
    {
        int Rank(int slot) => GetSplitRole(Children[first + slot]) switch
        {
            "lead" => 0,
            "trail" => 1,
            _ => 2,
        };

        List<int> order = free.OrderBy(Rank).ThenBy(slot => slot).ToList();
        double spare = left - order.Count * min;
        foreach (int slot in order)
        {
            widths[slot] = min;
        }

        int leadSlot = order[0];
        foreach (int slot in order)
        {
            double cap = left * WeightOf(Children[first + slot], paired) / total;
            if (Rank(slot) == 2)
            {
                cap = Math.Min(cap, widths[leadSlot]);
            }

            double add = Math.Clamp(cap - min, 0, Math.Max(0, spare));
            widths[slot] += add;
            spare -= add;
        }

        widths[leadSlot] += Math.Max(0, spare);
        foreach (int slot in order)
        {
            widths[slot] = Math.Max(0, Math.Floor(widths[slot]));
        }

        return widths;
    }

    private double WeightOf(Control child, bool paired)
    {
        double split = Math.Clamp(Split, 0.05, 0.95);
        return GetSplitRole(child) switch
        {
            "lead" => paired ? split : 1,
            "trail" => paired ? 1 - split : 1,
            _ => Math.Max(0.01, GetWeight(child)),
        };
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

    private readonly record struct PagePlan(int PerPage, int First, double[] Widths, double SlotWidth);

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
